using System;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// WebSocketOutputPlugin 单元测试 — 验证配置和状态转换
    /// 注意：WebSocket 连接测试需要服务器，e2e 场景不在本测试类覆盖
    /// </summary>
    public class WebSocketOutputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new WebSocketOutputPlugin(new WebSocketOutputConfig { Url = "ws://10.0.0.1:8080/ws" });
            Assert.Equal("WebSocket-ws://10.0.0.1:8080/ws", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new WebSocketOutputPlugin(new WebSocketOutputConfig { Name = "my-ws", Url = "ws://10.0.0.1:8080/ws" });
            Assert.Equal("my-ws", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new WebSocketOutputPlugin(new WebSocketOutputConfig());
            Assert.Equal("WebSocket", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new WebSocketOutputPlugin(null));
        }

        [Fact]
        public void Constructor_Defaults_Url()
        {
            var config = new WebSocketOutputConfig();
            Assert.Equal("ws://127.0.0.1:8080/ws", config.Url);
        }

        [Fact]
        public void Constructor_Defaults_ReconnectIntervalTo3000ms()
        {
            var config = new WebSocketOutputConfig();
            Assert.Equal(3000, config.ReconnectIntervalMs);
        }

        [Fact]
        public async Task StartAsync_TransitionsToStarting()
        {
            var plugin = new WebSocketOutputPlugin(new WebSocketOutputConfig { Url = "ws://127.0.0.1:19999" });
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Starting, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new WebSocketOutputPlugin(new WebSocketOutputConfig { Url = "ws://127.0.0.1:19999" });
            await plugin.StartAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task SendAsync_WhenNotRunning_ThrowsInvalidOperationException()
        {
            var plugin = new WebSocketOutputPlugin(new WebSocketOutputConfig());
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.Contains("not running", ex.Message);
        }
    }
}
