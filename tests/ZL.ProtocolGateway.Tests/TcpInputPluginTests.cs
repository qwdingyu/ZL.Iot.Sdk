using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// TcpInputPlugin 单元测试 — 验证配置、状态转换和 TCP 监听
    /// TcpInputPlugin 实现 IInputPlugin（不是 InputPluginBase），API 不同
    /// </summary>
    public class TcpInputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new TcpInputPlugin(new TcpInputConfig { Port = 9000 });
            Assert.Equal("TcpInput-9000", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new TcpInputPlugin(new TcpInputConfig { Name = "my-tcp-in", Port = 9000 });
            Assert.Equal("my-tcp-in", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new TcpInputPlugin(new TcpInputConfig { Port = 9000 });
            Assert.Equal("Tcp", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new TcpInputPlugin(null));
        }

        [Fact]
        public void Constructor_Defaults_DelimiterToNewline()
        {
            var config = new TcpInputConfig();
            Assert.Equal("\n", Encoding.ASCII.GetString(config.Delimiter));
        }

        [Fact]
        public void Constructor_Defaults_MaxMessageSizeTo8192()
        {
            var config = new TcpInputConfig();
            Assert.Equal(8192, config.MaxMessageSize);
        }

        [Fact]
        public void Constructor_Defaults_LocalIpToAny()
        {
            var config = new TcpInputConfig();
            Assert.Equal("0.0.0.0", config.LocalIp);
        }

        [Fact]
        public async Task StartAsync_BindsAndTransitionsToRunning()
        {
            var testPort = FindAvailableTcpPort();
            var plugin = new TcpInputPlugin(new TcpInputConfig { LocalIp = "127.0.0.1", Port = testPort });
            var tcs = new TaskCompletionSource<Message>();
            await plugin.StartAsync(msg => tcs.TrySetResult(msg) ? Task.CompletedTask : Task.Delay(100));
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_WithNullHandler_ThrowsArgumentNullException()
        {
            var plugin = new TcpInputPlugin(new TcpInputConfig { Port = 9000 });
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => plugin.StartAsync(null));
            Assert.Equal("messageHandler", ex.ParamName);
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var testPort = FindAvailableTcpPort();
            var plugin = new TcpInputPlugin(new TcpInputConfig { LocalIp = "127.0.0.1", Port = testPort });
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoOp()
        {
            var testPort = FindAvailableTcpPort();
            var plugin = new TcpInputPlugin(new TcpInputConfig { LocalIp = "127.0.0.1", Port = testPort });
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task EndToEnd_AcceptsTcpClient_DeliversMessageToHandler()
        {
            var tcs = new TaskCompletionSource<Message>();
            var plugin = new TcpInputPlugin(new TcpInputConfig { LocalIp = "127.0.0.1", Port = 0 });
            await plugin.StartAsync(msg => tcs.TrySetResult(msg) ? Task.CompletedTask : Task.Delay(100));

            // 通过反射获取实际监听端口（Port=0 时系统分配）
            var port = GetActualListeningPort(plugin);
            Assert.True(port > 0, "Could not determine listening port");

            // 连接并发送数据
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("hello-tcp\n"));
            await stream.FlushAsync();

            // 验证接收 — DelimiterSplitter 保留分隔符在帧内
            var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(msg);
            Assert.Equal("hello-tcp\n", Encoding.UTF8.GetString(msg.Payload));
            Assert.Equal("text", msg.ContentType);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task EndToEnd_CustomDelimiter_SplitsCorrectly()
        {
            var tcs = new TaskCompletionSource<Message>();
            var plugin = new TcpInputPlugin(new TcpInputConfig
            {
                LocalIp = "127.0.0.1",
                Port = 0,
                Delimiter = Encoding.ASCII.GetBytes("|")
            });
            await plugin.StartAsync(msg => tcs.TrySetResult(msg) ? Task.CompletedTask : Task.Delay(100));

            var port = GetActualListeningPort(plugin);
            Assert.True(port > 0, "Could not determine listening port");

            // 发送以 | 分隔的数据
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("custom|delim|"));
            await stream.FlushAsync();

            // 应该收到 "custom|"（DelimiterSplitter 保留分隔符在帧内）
            var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(msg);
            Assert.Equal("custom|", Encoding.UTF8.GetString(msg.Payload));

            await plugin.StopAsync();
        }

        [Fact]
        public async Task ConnectionChanged_FiresOnStartAndStop()
        {
            var testPort = FindAvailableTcpPort();
            var plugin = new TcpInputPlugin(new TcpInputConfig { LocalIp = "127.0.0.1", Port = testPort });

            bool connected = false, disconnected = false;
            plugin.ConnectionChanged += (name, isConn) =>
            {
                if (isConn) connected = true;
                else disconnected = true;
            };

            await plugin.StartAsync(_ => Task.CompletedTask);
            Assert.True(connected);

            await plugin.StopAsync();
            Assert.True(disconnected);
        }

        [Fact]
        public async Task Dispose_CallsStopAsync()
        {
            var testPort = FindAvailableTcpPort();
            var plugin = new TcpInputPlugin(new TcpInputConfig { LocalIp = "127.0.0.1", Port = testPort });
            await plugin.StartAsync(_ => Task.CompletedTask);
            plugin.Dispose();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        private static int FindAvailableTcpPort()
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// 通过反射获取 TcpInputPlugin 实际监听的端口（Port=0 时系统分配）
        /// </summary>
        private static int GetActualListeningPort(TcpInputPlugin plugin)
        {
            var listenerField = typeof(TcpInputPlugin).GetField("_listener",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var listener = listenerField?.GetValue(plugin) as TcpListener;
            if (listener != null)
            {
                return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            }
            return 0;
        }
    }
}
