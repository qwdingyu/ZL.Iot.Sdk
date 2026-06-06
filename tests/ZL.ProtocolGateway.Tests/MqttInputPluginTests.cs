using System;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// MqttInputPlugin 单元测试 — 验证配置和状态转换
    /// 注意：MQTT 连接测试需要 Broker，e2e 场景不在本测试类覆盖
    /// </summary>
    public class MqttInputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new MqttInputPlugin(new MqttInputConfig { Server = "broker.example.com" });
            Assert.Equal("MqttInput-broker.example.com", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new MqttInputPlugin(new MqttInputConfig { Name = "my-mqtt-in", Server = "broker.example.com" });
            Assert.Equal("my-mqtt-in", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new MqttInputPlugin(new MqttInputConfig { Server = "broker.example.com" });
            Assert.Equal("Mqtt", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new MqttInputPlugin(null));
        }

        [Fact]
        public void Constructor_Defaults_ServerTo127()
        {
            var config = new MqttInputConfig();
            Assert.Equal("127.0.0.1", config.Server);
        }

        [Fact]
        public void Constructor_Defaults_PortTo1883()
        {
            var config = new MqttInputConfig();
            Assert.Equal(1883, config.Port);
        }

        [Fact]
        public void Constructor_Defaults_TopicsToWildcard()
        {
            var config = new MqttInputConfig();
            Assert.Single(config.Topics);
            Assert.Equal("#", config.Topics[0]);
        }

        [Fact]
        public async Task StartAsync_WithNullHandler_ThrowsArgumentNullException()
        {
            var plugin = new MqttInputPlugin(new MqttInputConfig { Server = "127.0.0.1" });
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => plugin.StartAsync(null));
            Assert.Equal("messageHandler", ex.ParamName);
        }

        [Fact]
        public async Task StartAsync_TransitionsToStarting()
        {
            var plugin = new MqttInputPlugin(new MqttInputConfig { Server = "127.0.0.1", Port = 18839 });
            await plugin.StartAsync(_ => Task.CompletedTask);
            // 启动后立即为 Starting（后台重连循环运行中）
            Assert.NotEqual(PluginStatus.Stopped, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new MqttInputPlugin(new MqttInputConfig { Server = "127.0.0.1", Port = 18839 });
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task DisposeAsync_CallsStopAsync()
        {
            var plugin = new MqttInputPlugin(new MqttInputConfig { Server = "127.0.0.1", Port = 18839 });
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.DisposeAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }
    }
}
