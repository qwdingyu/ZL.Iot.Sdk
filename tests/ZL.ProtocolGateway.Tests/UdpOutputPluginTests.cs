using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// UdpOutputPlugin 单元测试 — 验证配置、状态转换和 UDP 发送
    /// </summary>
    public class UdpOutputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new UdpOutputPlugin(new UdpOutputConfig { ServerIp = "10.0.0.1", Port = 9000 });
            Assert.Equal("Udp-10.0.0.1:9000", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new UdpOutputPlugin(new UdpOutputConfig { Name = "my-udp", ServerIp = "10.0.0.1", Port = 9000 });
            Assert.Equal("my-udp", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new UdpOutputPlugin(new UdpOutputConfig { Port = 9000 });
            Assert.Equal("Udp", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new UdpOutputPlugin(null));
        }

        [Fact]
        public async Task StartAsync_TransitionsToRunning()
        {
            var receiver = new UdpClient(0);
            var port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;

            var plugin = new UdpOutputPlugin(new UdpOutputConfig { ServerIp = "127.0.0.1", Port = port });
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
            receiver.Close();
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsNoOp()
        {
            var receiver = new UdpClient(0);
            var port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;

            var plugin = new UdpOutputPlugin(new UdpOutputConfig { ServerIp = "127.0.0.1", Port = port });
            await plugin.StartAsync();
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
            receiver.Close();
        }

        [Fact]
        public async Task SendAsync_WhenNotRunning_ThrowsInvalidOperationException()
        {
            var plugin = new UdpOutputPlugin(new UdpOutputConfig { ServerIp = "127.0.0.1", Port = 9000 });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.Contains("not running", ex.Message);
        }

        [Fact]
        public async Task SendAsync_NullMessage_IsNoOp()
        {
            var receiver = new UdpClient(0);
            var port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;

            var plugin = new UdpOutputPlugin(new UdpOutputConfig { ServerIp = "127.0.0.1", Port = port });
            await plugin.StartAsync();
            await plugin.SendAsync(null); // 不应抛异常
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
            receiver.Close();
        }

        [Fact]
        public async Task SendAsync_EndToEnd_DeliversPayloadToReceiver()
        {
            // 创建接收端
            var receiver = new UdpClient(0);
            var localEp = (IPEndPoint)receiver.Client.LocalEndPoint;
            var receiveTask = receiver.ReceiveAsync();

            // 创建发送端
            var plugin = new UdpOutputPlugin(new UdpOutputConfig
            {
                ServerIp = "127.0.0.1",
                Port = localEp.Port
            });
            await plugin.StartAsync();

            var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            await plugin.SendAsync(new Message { Topic = "test", Payload = payload });
            await plugin.StopAsync();

            var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(payload.Length, result.Buffer.Length);
            Assert.Equal(0xDE, result.Buffer[0]);
            Assert.Equal(0xAD, result.Buffer[1]);
            Assert.Equal(0xBE, result.Buffer[2]);
            Assert.Equal(0xEF, result.Buffer[3]);

            receiver.Close();
        }

        [Fact]
        public async Task SendAsync_EmptyPayload_SendsEmptyDatagram()
        {
            var receiver = new UdpClient(0);
            var localEp = (IPEndPoint)receiver.Client.LocalEndPoint;
            var receiveTask = receiver.ReceiveAsync();

            var plugin = new UdpOutputPlugin(new UdpOutputConfig
            {
                ServerIp = "127.0.0.1",
                Port = localEp.Port
            });
            await plugin.StartAsync();

            await plugin.SendAsync(new Message { Topic = "test", Payload = Array.Empty<byte>() });
            await plugin.StopAsync();

            var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(result.Buffer);

            receiver.Close();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var receiver = new UdpClient(0);
            var port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;

            var plugin = new UdpOutputPlugin(new UdpOutputConfig { ServerIp = "127.0.0.1", Port = port });
            await plugin.StartAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
            receiver.Close();
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoOp()
        {
            var receiver = new UdpClient(0);
            var port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;

            var plugin = new UdpOutputPlugin(new UdpOutputConfig { ServerIp = "127.0.0.1", Port = port });
            await plugin.StartAsync();
            await plugin.StopAsync();
            await plugin.StopAsync(); // 不应抛异常
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
            receiver.Close();
        }

        [Fact]
        public async Task StopAsync_RaisesDetailedStatusStopped()
        {
            var receiver = new UdpClient(0);
            var port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;

            var plugin = new UdpOutputPlugin(new UdpOutputConfig { ServerIp = "127.0.0.1", Port = port });
            await plugin.StartAsync();

            OutputPluginStatusArgs capturedArgs = null;
            plugin.DetailedStatusChanged += args => capturedArgs = args;

            await plugin.StopAsync();

            Assert.NotNull(capturedArgs);
            Assert.Contains("stopped", capturedArgs.Message);
            receiver.Close();
        }
    }
}
