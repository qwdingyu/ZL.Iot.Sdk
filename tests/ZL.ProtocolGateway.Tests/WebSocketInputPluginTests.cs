using System;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// WebSocketInputPlugin 单元测试 — 验证配置和状态转换
    /// 注意：WebSocket 连接测试需要服务器，e2e 场景不在本测试类覆盖
    /// </summary>
    public class WebSocketInputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig { Url = "ws://10.0.0.1:8080/ws" });
            Assert.Equal("WebSocketInput-ws://10.0.0.1:8080/ws", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig { Name = "my-ws-in", Url = "ws://10.0.0.1:8080/ws" });
            Assert.Equal("my-ws-in", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig());
            Assert.Equal("WebSocket", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new WebSocketInputPlugin(null));
        }

        [Fact]
        public void Constructor_Defaults_Url()
        {
            var config = new WebSocketInputConfig();
            Assert.Equal("ws://127.0.0.1:8080/ws", config.Url);
        }

        [Fact]
        public void Constructor_Defaults_ReconnectIntervalTo3000ms()
        {
            var config = new WebSocketInputConfig();
            Assert.Equal(3000, config.ReconnectIntervalMs);
        }

        [Fact]
        public void Constructor_Defaults_BufferSizeTo4096()
        {
            var config = new WebSocketInputConfig();
            Assert.Equal(4096, config.BufferSize);
        }

        [Fact]
        public async Task StartAsync_WithNullHandler_ThrowsArgumentNullException()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig());
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => plugin.StartAsync(null));
            Assert.Equal("messageHandler", ex.ParamName);
        }

        [Fact]
        public async Task StartAsync_TransitionsToStarting()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig { Url = "ws://127.0.0.1:19999" });
            await plugin.StartAsync(_ => Task.CompletedTask);
            Assert.Equal(PluginStatus.Starting, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig { Url = "ws://127.0.0.1:19999" });
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task Dispose_CallsStopAsync()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig { Url = "ws://127.0.0.1:19999" });
            await plugin.StartAsync(_ => Task.CompletedTask);
            plugin.Dispose();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }
    }
}
