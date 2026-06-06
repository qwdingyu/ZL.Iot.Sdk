using System;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// ModbusTcpOutputPlugin 单元测试 — 验证配置、状态转换和连接行为
    /// </summary>
    public class ModbusTcpOutputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { ServerIp = "10.0.0.1", Port = 502 });
            Assert.Equal("ModbusTcp-10.0.0.1:502", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { Name = "my-mb", ServerIp = "10.0.0.1", Port = 502 });
            Assert.Equal("my-mb", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { Port = 502 });
            Assert.Equal("ModbusTcp", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new ModbusTcpOutputPlugin(null));
        }

        [Fact]
        public void Constructor_Defaults_PortTo502()
        {
            var config = new ModbusTcpOutputConfig();
            Assert.Equal(502, config.Port);
        }

        [Fact]
        public void Constructor_Defaults_UnitIdTo1()
        {
            var config = new ModbusTcpOutputConfig();
            Assert.Equal((byte)1, config.UnitId);
        }

        [Fact]
        public void Constructor_Defaults_TimeoutTo3000ms()
        {
            var config = new ModbusTcpOutputConfig();
            Assert.Equal(3000, config.TimeoutMs);
        }

        [Fact]
        public async Task StartAsync_AttemptsConnection_AndTransitions()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { ServerIp = "127.0.0.1", Port = 502 });
            await plugin.StartAsync();
            // OnStartAsync spawns a background ConnectionLoopAsync Task and returns immediately,
            // so OutputPluginBase sets Status=Running; the connection loop retries in the background.
            await Task.Delay(200);
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsNoOp()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { ServerIp = "127.0.0.1", Port = 502 });
            await plugin.StartAsync();
            // Second call: base class StartAsync checks _started flag, returns early
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_WhenNotRunning_ThrowsInvalidOperationException()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { ServerIp = "127.0.0.1", Port = 502 });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.Contains("not running", ex.Message);
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { ServerIp = "127.0.0.1", Port = 502 });
            await plugin.StartAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoOp()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { ServerIp = "127.0.0.1", Port = 502 });
            await plugin.StartAsync();
            await plugin.StopAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_RaisesDetailedStatusStopped()
        {
            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig { ServerIp = "127.0.0.1", Port = 502 });
            await plugin.StartAsync();

            OutputPluginStatusArgs capturedArgs = null;
            plugin.DetailedStatusChanged += args => capturedArgs = args;

            await plugin.StopAsync();

            Assert.NotNull(capturedArgs);
            Assert.Contains("stopped", capturedArgs.Message);
        }
    }
}
