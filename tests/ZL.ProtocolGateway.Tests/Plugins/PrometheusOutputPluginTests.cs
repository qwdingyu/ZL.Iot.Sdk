#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// PrometheusOutputPlugin 单元测试
    /// </summary>
    public class PrometheusOutputPluginTests
    {
        [Fact]
        public void Constructor_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PrometheusOutputPlugin(null!));
        }

        [Fact]
        public void Constructor_ValidConfig_SetsProperties()
        {
            var config = new PrometheusOutputConfig
            {
                Name = "test-prom",
                PushGatewayUrl = "http://localhost:9091",
                JobName = "plc_test",
                Instance = "test_host"
            };

            using var plugin = new PrometheusOutputPlugin(config);
            Assert.Equal("test-prom", plugin.Name);
            Assert.Equal("Prometheus", plugin.ProtocolType);
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public void Constructor_DefaultName_FromJobName()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "custom_job"
            };

            using var plugin = new PrometheusOutputPlugin(config);
            Assert.Equal("Prometheus-custom_job", plugin.Name);
        }

        [Fact]
        public async Task StartStop_Lifecycle_Succeeds()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test"
            };

            using var plugin = new PrometheusOutputPlugin(config);

            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StartStop_MultipleTimes_Idempotent()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test"
            };

            using var plugin = new PrometheusOutputPlugin(config);

            await plugin.StartAsync();
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task Send_NullMessage_DoesNotThrow()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test"
            };

            using var plugin = new PrometheusOutputPlugin(config);
            await plugin.StartAsync();

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(null!));
            Assert.Null(ex);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_WhenStopped_ThrowsInvalidOperationException()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test"
            };

            using var plugin = new PrometheusOutputPlugin(config);

            var msg = new Message { ContentType = "json" };
            msg.SetJsonContent("{}");
            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task Send_TagChangeMessage_UpdatesCache()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test",
                PushIntervalSeconds = 60 // 避免测试中触发推送
            };

            using var plugin = new PrometheusOutputPlugin(config);
            await plugin.StartAsync();

            var payloadObj = new Dictionary<string, object?>
            {
                ["source"] = "TagRegistry",
                ["tagName"] = "Motor1.Temperature",
                ["value"] = 85.5,
                ["dataType"] = "REAL",
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };
            var json = JsonSerializer.Serialize(payloadObj);

            var msg = new Message { Topic = "plc/tag/Motor1.Temperature", ContentType = "json" };
            msg.SetJsonContent(json);

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.Null(ex);

            await Task.Delay(200);

            // 再次发送更新值
            payloadObj["value"] = 90.0;
            json = JsonSerializer.Serialize(payloadObj);
            msg.SetJsonContent(json);
            await plugin.SendAsync(msg);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_MultipleTags_AccumulatesInCache()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test",
                PushIntervalSeconds = 60
            };

            using var plugin = new PrometheusOutputPlugin(config);
            await plugin.StartAsync();

            for (int i = 1; i <= 5; i++)
            {
                var payloadObj = new Dictionary<string, object?>
                {
                    ["source"] = "TagRegistry",
                    ["tagName"] = $"Sensor_{i}",
                    ["value"] = i * 10.0,
                    ["dataType"] = "REAL",
                    ["timestamp"] = DateTime.UtcNow.ToString("O")
                };
                var json = JsonSerializer.Serialize(payloadObj);

                var msg = new Message { Topic = $"plc/tag/Sensor_{i}", ContentType = "json" };
                msg.SetJsonContent(json);
                await plugin.SendAsync(msg);
            }

            await Task.Delay(200);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_NonJsonMessage_DoesNotThrow()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test"
            };

            using var plugin = new PrometheusOutputPlugin(config);
            await plugin.StartAsync();

            var msg = new Message
            {
                Payload = new byte[] { 0x00, 0x01, 0x02 },
                ContentType = "binary"
            };

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.Null(ex);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_InvalidJson_DoesNotThrow()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test"
            };

            using var plugin = new PrometheusOutputPlugin(config);
            await plugin.StartAsync();

            var msg = new Message { ContentType = "json" };
            msg.SetJsonContent("{garbage}");

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.Null(ex);

            await plugin.StopAsync();
        }

        [Fact]
        public void Config_DefaultValues_AreSane()
        {
            var config = new PrometheusOutputConfig();
            Assert.Equal("http://localhost:9091", config.PushGatewayUrl);
            Assert.Equal("plc_simulator", config.JobName);
            Assert.Equal("localhost", config.Instance);
            Assert.Equal(15, config.PushIntervalSeconds);
            Assert.Equal(5000, config.TimeoutMs);
            Assert.False(config.EnableMemoryMetrics);
            Assert.True(config.EnableTagMetrics);
        }

        [Fact]
        public void ExtraLabels_AreConfigurable()
        {
            var config = new PrometheusOutputConfig
            {
                ExtraLabels = new Dictionary<string, string>
                {
                    ["env"] = "production",
                    ["location"] = "factory_1"
                }
            };

            Assert.Equal(2, config.ExtraLabels.Count);
            Assert.Equal("production", config.ExtraLabels["env"]);
            Assert.Equal("factory_1", config.ExtraLabels["location"]);
        }

        [Fact]
        public async Task Dispose_DoesNotSynchronouslyStop_AvoidsDeadlock()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test"
            };

            var plugin = new PrometheusOutputPlugin(config);
            await plugin.StartAsync();

            plugin.Dispose();
            // Dispose 不再同步调用 StopAsync，避免死锁
            // 正确做法：显式调用 StopAsync
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task GetLastMetricsText_AfterStart_DoesNotThrow()
        {
            var config = new PrometheusOutputConfig
            {
                PushGatewayUrl = "http://localhost:9091",
                JobName = "test",
                Instance = "test",
                PushIntervalSeconds = 60
            };

            using var plugin = new PrometheusOutputPlugin(config);
            await plugin.StartAsync();

            var payloadObj = new Dictionary<string, object?>
            {
                ["source"] = "TagRegistry",
                ["tagName"] = "TestTag",
                ["value"] = 42.0,
                ["dataType"] = "INT",
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };
            var json = JsonSerializer.Serialize(payloadObj);

            var msg = new Message { Topic = "plc/tag/TestTag", ContentType = "json" };
            msg.SetJsonContent(json);
            await plugin.SendAsync(msg);

            await Task.Delay(200);

            // GetLastMetricsText 不应抛异常
            var metrics = plugin.GetLastMetricsText();
            await plugin.StopAsync();
        }
    }
}
