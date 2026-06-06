using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// FileInputPlugin 单元测试 — 验证文件轮询读取功能
    /// </summary>
    public class FileInputPluginTests : IDisposable
    {
        private readonly string _testDir;

        public FileInputPluginTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"pg-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new FileInputPlugin(null));
        }

        [Fact]
        public void Constructor_AutoGeneratesName()
        {
            var plugin = new FileInputPlugin(new FileInputConfig { FilePath = "test.log" });
            Assert.Equal("FileInput-test.log", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName()
        {
            var plugin = new FileInputPlugin(new FileInputConfig { Name = "my-file-in", FilePath = "test.log" });
            Assert.Equal("my-file-in", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new FileInputPlugin(new FileInputConfig());
            Assert.Equal("File", plugin.ProtocolType);
        }

        [Fact]
        public async Task StartAsync_DetectsExistingContent()
        {
            var filePath = Path.Combine(_testDir, "input.txt");
            // 使用无 BOM 的 UTF-8 编码写入，避免 File.WriteAllText 默认带 BOM
            File.WriteAllText(filePath, "line1\nline2\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var tcs = new TaskCompletionSource<Message>();
            var plugin = new FileInputPlugin(new FileInputConfig { FilePath = filePath, PollIntervalMs = 50 });
            await plugin.StartAsync(msg =>
            {
                if (tcs.Task.IsCompleted) return Task.CompletedTask;
                tcs.TrySetResult(msg);
                return Task.CompletedTask;
            });

            Assert.Equal(PluginStatus.Running, plugin.Status);

            var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(msg);
            Assert.Equal("line1\n", Encoding.UTF8.GetString(msg.Payload));

            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_DetectsNewContentAppended()
        {
            var filePath = Path.Combine(_testDir, "tail.txt");
            // 使用无 BOM 的 UTF-8 编码
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(filePath, "", utf8NoBom);

            var tcs = new TaskCompletionSource<Message>();
            var plugin = new FileInputPlugin(new FileInputConfig { FilePath = filePath, PollIntervalMs = 50 });
            await plugin.StartAsync(msg =>
            {
                if (tcs.Task.IsCompleted) return Task.CompletedTask;
                tcs.TrySetResult(msg);
                return Task.CompletedTask;
            });

            // 等待开始轮询后追加内容
            await Task.Delay(100);
            File.AppendAllText(filePath, "new-line\n", utf8NoBom);

            // 应该收到 "new-line\n"（DelimiterSplitter 保留分隔符在帧内）
            var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(msg);
            Assert.Equal("new-line\n", Encoding.UTF8.GetString(msg.Payload));

            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_WithNullHandler_ThrowsArgumentNullException()
        {
            var filePath = Path.Combine(_testDir, "null-handler.txt");
            var plugin = new FileInputPlugin(new FileInputConfig { FilePath = filePath });
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => plugin.StartAsync(null));
            Assert.Equal("messageHandler", ex.ParamName);
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var filePath = Path.Combine(_testDir, "stop.txt");
            File.WriteAllText(filePath, "", Encoding.UTF8);
            var plugin = new FileInputPlugin(new FileInputConfig { FilePath = filePath, PollIntervalMs = 50 });
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task ConnectionChanged_FiresOnStartAndStop()
        {
            var filePath = Path.Combine(_testDir, "conn.txt");
            File.WriteAllText(filePath, "", Encoding.UTF8);
            var plugin = new FileInputPlugin(new FileInputConfig { FilePath = filePath, PollIntervalMs = 50 });

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
            var filePath = Path.Combine(_testDir, "dispose.txt");
            File.WriteAllText(filePath, "", Encoding.UTF8);
            var plugin = new FileInputPlugin(new FileInputConfig { FilePath = filePath, PollIntervalMs = 50 });
            await plugin.StartAsync(_ => Task.CompletedTask);
            plugin.Dispose();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }
    }
}
