using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// HttpOutputPlugin 单元测试 — 验证配置、状态转换和请求构建
    /// 注意：端到端 HTTP 转发已在 HttpForwardingScenarioTests 中覆盖
    /// </summary>
    public class HttpOutputPluginTests
    {
        [Fact]
        public void Constructor_AutoGeneratesName_WhenNameIsNull()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            Assert.Equal("HTTP-example.com", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName_WhenProvided()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Name = "my-http", Url = "http://example.com/api" });
            Assert.Equal("my-http", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            Assert.Equal("HTTP", plugin.ProtocolType);
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new HttpOutputPlugin(null));
        }

        [Fact]
        public void Constructor_Defaults_MethodToPost()
        {
            var config = new HttpOutputConfig();
            Assert.Equal("POST", config.Method);
        }

        [Fact]
        public void Constructor_Defaults_ContentTypeToJson()
        {
            var config = new HttpOutputConfig();
            Assert.Equal("application/json", config.ContentType);
        }

        [Fact]
        public void Constructor_Defaults_TimeoutTo30s()
        {
            var config = new HttpOutputConfig();
            Assert.Equal(30000, config.Timeout);
        }

        [Fact]
        public async Task StartAsync_TransitionsToRunning()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsNoOp()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            await plugin.StartAsync();
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_WhenNotStarted_ThrowsInvalidOperationException()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.Contains("not running", ex.Message);
        }

        [Fact]
        public async Task SendAsync_NullMessage_ThrowsArgumentNullException()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            await plugin.StartAsync();
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(
                () => plugin.SendAsync(null));
            Assert.Equal("message", ex.ParamName);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_ConnectionRefused_ThrowsHttpRequestException()
        {
            // 向一个没有监听的端口发送请求，应抛出连接拒绝异常
            var plugin = new HttpOutputPlugin(new HttpOutputConfig
            {
                Url = "http://127.0.0.1:19999/api",
                Timeout = 2000
            });
            await plugin.StartAsync();

            var msg = new Message { Topic = "test", ContentType = "json" };
            msg.Payload = Encoding.UTF8.GetBytes("{}");

            var ex = await Assert.ThrowsAsync<HttpRequestException>(
                () => plugin.SendAsync(msg));
            // 连接拒绝会包装为 HttpRequestException
            Assert.NotNull(ex.Message);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            await plugin.StartAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoOp()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            await plugin.StartAsync();
            await plugin.StopAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_RaisesDetailedStatusStopped()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            await plugin.StartAsync();

            OutputPluginStatusArgs capturedArgs = null;
            plugin.DetailedStatusChanged += args => capturedArgs = args;

            await plugin.StopAsync();

            Assert.NotNull(capturedArgs);
            Assert.Contains("stopped", capturedArgs.Message);
        }

        [Fact]
        public async Task SendAsync_AfterStop_ThrowsInvalidOperationException()
        {
            var plugin = new HttpOutputPlugin(new HttpOutputConfig { Url = "http://example.com/api" });
            await plugin.StartAsync();
            await plugin.StopAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.Contains("not running", ex.Message);
        }
    }
}
