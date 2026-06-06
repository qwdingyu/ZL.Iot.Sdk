using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// UdpInputPlugin 单元测试 — 验证配置、状态转换和 UDP 接收
    /// </summary>
    public class UdpInputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { Port = 9000 });
            Assert.Equal("UdpInput-9000", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { Name = "my-udp-in", Port = 9000 });
            Assert.Equal("my-udp-in", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { Port = 9000 });
            Assert.Equal("Udp", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new UdpInputPlugin(null));
        }

        [Fact]
        public async Task StartAsync_BindsAndTransitionsToRunning()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { LocalIp = "127.0.0.1", Port = 0 });
            var tcs = new TaskCompletionSource<Message>();
            await plugin.StartAsync(async msg => await tcs.SetTaskCompleted(msg));
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_WithNullHandler_ThrowsArgumentNullException()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { Port = 9000 });
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => plugin.StartAsync(null));
            Assert.Equal("messageHandler", ex.ParamName);
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsNoOp()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { LocalIp = "127.0.0.1", Port = 0 });
            var tcs = new TaskCompletionSource<Message>();
            await plugin.StartAsync(async msg => await tcs.SetTaskCompleted(msg));
            await plugin.StartAsync(async msg => await tcs.SetTaskCompleted(msg)); // 不应抛异常
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_InvalidIP_ThrowsInvalidOperationException()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { LocalIp = "not-an-ip", Port = 9000 });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.StartAsync(_ => Task.CompletedTask));
            Assert.Contains("Failed to start UDP input", ex.Message);
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { LocalIp = "127.0.0.1", Port = 0 });
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoOp()
        {
            var plugin = new UdpInputPlugin(new UdpInputConfig { LocalIp = "127.0.0.1", Port = 0 });
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();
            await plugin.StopAsync(); // 不应抛异常
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task EndToEnd_ReceiveUdpDatagram_DeliversMessageToHandler()
        {
            // 创建监听端
            var tcs = new TaskCompletionSource<Message>();
            var plugin = new UdpInputPlugin(new UdpInputConfig { LocalIp = "127.0.0.1", Port = 0 });
            await plugin.StartAsync(async msg => await tcs.SetTaskCompleted(msg));

            // 获取监听端口 — UdpInputPlugin 不暴露端口，通过发一个探测包
            // 使用固定端口来简化测试
            await plugin.StopAsync();

            // 重新用固定端口
            var testPort = FindAvailableUdpPort();
            plugin = new UdpInputPlugin(new UdpInputConfig { LocalIp = "127.0.0.1", Port = testPort });
            tcs = new TaskCompletionSource<Message>();
            await plugin.StartAsync(async msg => await tcs.SetTaskCompleted(msg));

            // 发送 UDP 数据包
            var sender = new UdpClient();
            var data = Encoding.UTF8.GetBytes("hello-udp");
            await sender.SendAsync(data, data.Length, "127.0.0.1", testPort);

            // 验证接收
            var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(msg);
            Assert.Equal("hello-udp", Encoding.UTF8.GetString(msg.Payload));
            Assert.Equal("binary", msg.ContentType);
            Assert.Equal(testPort.ToString(), msg.Topic);
            Assert.Equal("Udp", msg.Metadata["Protocol"]);
            Assert.Equal(testPort.ToString(), msg.Metadata["LocalPort"]);
            Assert.Contains("127.0.0.1", msg.Metadata["Source"]);

            await plugin.StopAsync();
            sender.Close();
        }

        private static int FindAvailableUdpPort()
        {
            var listener = new UdpClient(0);
            var port = ((IPEndPoint)listener.Client.LocalEndPoint).Port;
            listener.Close();
            return port;
        }
    }

    /// <summary>
    /// TaskCompletionSource 扩展
    /// </summary>
    public static class TcsExtensions
    {
        public static async Task SetTaskCompleted<T>(this TaskCompletionSource<T> tcs, T result)
        {
            tcs.TrySetResult(result);
            // 避免 fire-and-forget
            await Task.CompletedTask;
        }
    }
}
