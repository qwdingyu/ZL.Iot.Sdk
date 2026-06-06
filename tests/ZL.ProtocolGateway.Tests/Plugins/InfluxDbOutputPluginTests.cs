#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// InfluxDbOutputPlugin 单元测试
    /// </summary>
    public class InfluxDbOutputPluginTests
    {
        [Fact]
        public void Constructor_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new InfluxDbOutputPlugin(null!));
        }

        [Fact]
        public void Constructor_ValidConfig_SetsProperties()
        {
            var config = new InfluxDbOutputConfig
            {
                Name = "test-influx",
                Url = "http://localhost:8086",
                Token = "my-token",
                Organization = "my-org",
                Bucket = "my-bucket"
            };

            using var plugin = new InfluxDbOutputPlugin(config);
            Assert.Equal("test-influx", plugin.Name);
            Assert.Equal("InfluxDB", plugin.ProtocolType);
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public void Constructor_DefaultName_FromUrl()
        {
            var config = new InfluxDbOutputConfig
            {
                Url = "http://influx-prod:8086",
                Token = "token"
            };

            using var plugin = new InfluxDbOutputPlugin(config);
            Assert.Equal("InfluxDB-influx-prod", plugin.Name);
        }

        [Fact]
        public async Task StartStop_Lifecycle_Succeeds()
        {
            var config = new InfluxDbOutputConfig
            {
                Token = "test-token",
                Url = "http://localhost:8086"
            };

            using var plugin = new InfluxDbOutputPlugin(config);

            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StartStop_MultipleTimes_Idempotent()
        {
            var config = new InfluxDbOutputConfig { Token = "t", Url = "http://localhost:8086" };
            using var plugin = new InfluxDbOutputPlugin(config);

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
            var config = new InfluxDbOutputConfig { Token = "t", Url = "http://localhost:8086" };
            using var plugin = new InfluxDbOutputPlugin(config);
            await plugin.StartAsync();

            await plugin.SendAsync(null!);
            // 没有异常即通过
            Assert.Equal(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_WhenStopped_ThrowsInvalidOperationException()
        {
            var config = new InfluxDbOutputConfig { Token = "t", Url = "http://localhost:8086" };
            using var plugin = new InfluxDbOutputPlugin(config);

            var msg = new Message { ContentType = "json" };
            msg.SetJsonContent("{}");
            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task Send_TagChangeMessage_ConvertsCorrectly()
        {
            var config = new InfluxDbOutputConfig
            {
                Token = "test-token",
                Url = "http://localhost:8086"
            };

            using var plugin = new InfluxDbOutputPlugin(config);
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

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_MemoryChangeMessage_ConvertsCorrectly()
        {
            var config = new InfluxDbOutputConfig
            {
                Token = "test-token",
                Url = "http://localhost:8086"
            };

            using var plugin = new InfluxDbOutputPlugin(config);
            await plugin.StartAsync();

            var payloadObj = new Dictionary<string, object?>
            {
                ["source"] = "PlcMemory",
                ["areaCode"] = 0x84,
                ["dbNumber"] = 0,
                ["address"] = 100,
                ["length"] = 4,
                ["hex"] = "0000C842",
                ["intValue"] = 100,
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };
            var json = JsonSerializer.Serialize(payloadObj);

            var msg = new Message { Topic = "plc/memory/84/0", ContentType = "json" };
            msg.SetJsonContent(json);

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.Null(ex);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_NonJsonMessage_DoesNotThrow()
        {
            var config = new InfluxDbOutputConfig { Token = "t", Url = "http://localhost:8086" };
            using var plugin = new InfluxDbOutputPlugin(config);
            await plugin.StartAsync();

            var msg = new Message
            {
                Payload = Encoding.UTF8.GetBytes("plain text"),
                ContentType = "text"
            };

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.Null(ex);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Dispose_DoesNotSynchronouslyStop_AvoidsDeadlock()
        {
            var config = new InfluxDbOutputConfig { Token = "t", Url = "http://localhost:8086" };
            var plugin = new InfluxDbOutputPlugin(config);
            await plugin.StartAsync();

            plugin.Dispose();
            // Dispose 不再同步调用 StopAsync，避免死锁
            // 正确做法：显式调用 StopAsync
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task Send_InvalidJsonMessage_DoesNotThrow()
        {
            var config = new InfluxDbOutputConfig { Token = "t", Url = "http://localhost:8086" };
            using var plugin = new InfluxDbOutputPlugin(config);
            await plugin.StartAsync();

            var msg = new Message { ContentType = "json" };
            msg.SetJsonContent("{invalid json}");

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.Null(ex);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task BatchWrite_AccumulatesMessages()
        {
            var config = new InfluxDbOutputConfig
            {
                Token = "t",
                Url = "http://localhost:8086",
                EnableBatchWrite = true
            };

            using var plugin = new InfluxDbOutputPlugin(config);
            await plugin.StartAsync();

            for (int i = 0; i < 5; i++)
            {
                var payloadObj = new Dictionary<string, object?>
                {
                    ["source"] = "TagRegistry",
                    ["tagName"] = $"Tag{i}",
                    ["value"] = i * 10.0,
                    ["dataType"] = "REAL",
                    ["timestamp"] = DateTime.UtcNow.ToString("O")
                };
                var json = JsonSerializer.Serialize(payloadObj);

                var msg = new Message { Topic = $"plc/tag/Tag{i}", ContentType = "json" };
                msg.SetJsonContent(json);
                await plugin.SendAsync(msg);
            }

            // 等待批量定时器触发
            await Task.Delay(800);
            await plugin.StopAsync();
        }

        [Fact]
        public void Config_Properties_DefaultValues()
        {
            var config = new InfluxDbOutputConfig();
            Assert.Equal("http://localhost:8086", config.Url);
            Assert.Equal("plc-simulator", config.Organization);
            Assert.Equal("plc-data", config.Bucket);
            Assert.Equal("plc_memory", config.DefaultMeasurement);
            Assert.Equal("plc_tag", config.TagMeasurement);
            Assert.True(config.EnableBatchWrite);
            Assert.True(config.WriteMemoryChanges);
            Assert.True(config.WriteTagChanges);
            Assert.Equal(5000, config.MaxBatchLines);
            Assert.Equal(5000, config.TimeoutMs);
        }
    }
}
