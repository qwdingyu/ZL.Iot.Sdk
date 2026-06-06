using System;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// TcpOutputPlugin 单元测试 — 验证配置、状态转换和连接行为
    /// 端到端 TCP 转发已在 TcpForwardingScenarioTests 中覆盖
    /// </summary>
    public class TcpOutputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "10.0.0.1", Port = 8000 });
            Assert.Equal("Tcp-10.0.0.1:8000", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { Name = "my-tcp", ServerIp = "10.0.0.1", Port = 8000 });
            Assert.Equal("my-tcp", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "10.0.0.1", Port = 8000 });
            Assert.Equal("Tcp", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new TcpOutputPlugin(null));
        }

        [Fact]
        public void Constructor_Defaults_ReconnectIntervalTo3000ms()
        {
            var config = new TcpOutputConfig();
            Assert.Equal(3000, config.ReconnectIntervalMs);
        }

        [Fact]
        public void Constructor_Defaults_ErrorThresholdTo10()
        {
            var config = new TcpOutputConfig();
            Assert.Equal(10, config.ErrorThreshold);
        }

        [Fact]
        public void Constructor_Defaults_LocalStartupQuietPeriodTo5000ms()
        {
            var config = new TcpOutputConfig();
            Assert.Equal(5000, config.LocalStartupQuietPeriodMs);
        }

        [Fact]
        public async Task StartAsync_TransitionsToStarting_ThenRunningOrError()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "127.0.0.1", Port = 19999, ReconnectIntervalMs = 5000 });
            await plugin.StartAsync();
            // Base class sets Starting, then connection loop runs in background.
            // With no server, it quickly transitions to Error.
            await Task.Delay(200);
            Assert.NotEqual(PluginStatus.Starting, plugin.Status);
            Assert.NotEqual(PluginStatus.Stopped, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsNoOp()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "127.0.0.1", Port = 19999 });
            await plugin.StartAsync();
            await Task.Delay(100);
            await plugin.StartAsync();
            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_WhenNotRunning_ThrowsInvalidOperationException()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "127.0.0.1", Port = 8000 });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.Contains("not running", ex.Message);
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "127.0.0.1", Port = 19999 });
            await plugin.StartAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoOp()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "127.0.0.1", Port = 19999 });
            await plugin.StartAsync();
            await plugin.StopAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_RaisesDetailedStatusStopped()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "127.0.0.1", Port = 19999 });
            await plugin.StartAsync();

            OutputPluginStatusArgs capturedArgs = null;
            plugin.DetailedStatusChanged += args => capturedArgs = args;

            await plugin.StopAsync();

            Assert.NotNull(capturedArgs);
            Assert.Contains("stopped", capturedArgs.Message);
        }

        [Fact]
        public async Task ConnectionRefused_SetsErrorStatus()
        {
            var plugin = new TcpOutputPlugin(new TcpOutputConfig { ServerIp = "127.0.0.1", Port = 19999, ReconnectIntervalMs = 5000 });
            await plugin.StartAsync();
            await Task.Delay(800);

            Assert.Equal(PluginStatus.Error, plugin.Status);
            await plugin.StopAsync();
        }
    }
}
