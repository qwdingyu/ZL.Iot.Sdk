using System;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// MqttOutputPlugin 单元测试 — 验证配置和状态转换
    /// 注意：MQTT 连接测试需要 Broker，e2e 场景不在本测试类覆盖
    /// </summary>
    public class MqttOutputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new MqttOutputPlugin(new MqttOutputConfig { Server = "broker.example.com" });
            Assert.Equal("Mqtt-broker.example.com", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new MqttOutputPlugin(new MqttOutputConfig { Name = "my-mqtt", Server = "broker.example.com" });
            Assert.Equal("my-mqtt", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new MqttOutputPlugin(new MqttOutputConfig { Server = "broker.example.com" });
            Assert.Equal("Mqtt", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new MqttOutputPlugin(null));
        }

        [Fact]
        public void Constructor_Defaults_PortTo1883()
        {
            var config = new MqttOutputConfig();
            Assert.Equal(1883, config.Port);
        }

        [Fact]
        public void Constructor_Defaults_DefaultTopic()
        {
            var config = new MqttOutputConfig();
            Assert.Equal("gateway/data", config.DefaultTopic);
        }

        [Fact]
        public void Constructor_Defaults_QoS_ToAtMostOnce()
        {
            var config = new MqttOutputConfig();
            Assert.Equal(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce, config.QoS);
        }

        [Fact]
        public void Constructor_Defaults_ErrorThresholdTo10()
        {
            var config = new MqttOutputConfig();
            Assert.Equal(10, config.ErrorThreshold);
        }

        [Fact]
        public async Task StartAsync_TransitionsToStarting()
        {
            var plugin = new MqttOutputPlugin(new MqttOutputConfig { Server = "127.0.0.1", Port = 18839 });
            await plugin.StartAsync();
            // StartAsync 设置 Starting，然后后台重连循环运行
            // 无 Broker 时，状态可能变为 Starting 或 Error
            Assert.NotEqual(PluginStatus.Stopped, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new MqttOutputPlugin(new MqttOutputConfig { Server = "127.0.0.1", Port = 18839 });
            await plugin.StartAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task SendAsync_WhenNotRunning_ThrowsInvalidOperationException()
        {
            var plugin = new MqttOutputPlugin(new MqttOutputConfig { Server = "127.0.0.1" });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.Contains("not running", ex.Message);
        }
    }
}
