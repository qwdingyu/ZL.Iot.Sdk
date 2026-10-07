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
        public async Task StartAsync_TransitionsToStartingOrRunning()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig { Url = "ws://127.0.0.1:19999" });
            await plugin.StartAsync(_ => Task.CompletedTask);
            // StartAsync 返回时基类已置 Running，但后台连接循环对不可达地址会很快改回
            // Starting/Recovering——断言精确的中间态是竞态（旧写法恒断言 Starting 会随机挂）。
            // 确定性判据：启动有实际效果（既非 Stopped 也非 Error）。
            Assert.True(plugin.Status is PluginStatus.Starting or PluginStatus.Running or PluginStatus.Recovering,
                $"unexpected status after start: {plugin.Status}");
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
        public async Task DisposeAsync_TransitionsToStopped()
        {
            var plugin = new WebSocketInputPlugin(new WebSocketInputConfig { Url = "ws://127.0.0.1:19999" });
            await plugin.StartAsync(_ => Task.CompletedTask);
            // 基类 P0-3 契约：同步 Dispose 只取消/释放 CTS、不等待停机（避免 ThreadPool 耗尽死锁），
            // 停机应走 DisposeAsync（见 InputPluginBase.Dispose 注释）。此处按契约改用异步释放。
            await plugin.DisposeAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }
    }
}
