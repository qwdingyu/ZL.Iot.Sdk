using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// FileOutputPlugin 单元测试 — 验证文件写入功能
    /// </summary>
    public class FileOutputPluginTests : IDisposable
    {
        private readonly string _testDir;

        public FileOutputPluginTests()
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
            Assert.Throws<ArgumentNullException>(() => new FileOutputPlugin(null));
        }

        [Fact]
        public void Constructor_AutoGeneratesName()
        {
            var plugin = new FileOutputPlugin(new FileOutputConfig { FilePath = "test.log" });
            Assert.Equal("File-test.log", plugin.Name);
        }

        [Fact]
        public void Constructor_UsesConfigName()
        {
            var plugin = new FileOutputPlugin(new FileOutputConfig { Name = "my-file", FilePath = "test.log" });
            Assert.Equal("my-file", plugin.Name);
        }

        [Fact]
        public void Constructor_SetsProtocolType()
        {
            var plugin = new FileOutputPlugin(new FileOutputConfig());
            Assert.Equal("File", plugin.ProtocolType);
        }

        [Fact]
        public async Task SendAsync_WritesToFile()
        {
            var filePath = Path.Combine(_testDir, "output.txt");
            var plugin = new FileOutputPlugin(new FileOutputConfig { FilePath = filePath, Append = false });
            await plugin.StartAsync();

            var payload = Encoding.UTF8.GetBytes("hello-file");
            await plugin.SendAsync(new Message { Payload = payload });

            await plugin.StopAsync();

            // FileOutputPlugin 以 hex 格式写入字节
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            Assert.Equal("68656C6C6F2D66696C65", content.Trim());
        }

        [Fact]
        public async Task SendAsync_AppendsWhenConfigured()
        {
            var filePath = Path.Combine(_testDir, "append.txt");
            File.WriteAllText(filePath, "existing\n", Encoding.UTF8);

            var plugin = new FileOutputPlugin(new FileOutputConfig { FilePath = filePath, Append = true });
            await plugin.StartAsync();

            var payload = Encoding.UTF8.GetBytes("appended");
            await plugin.SendAsync(new Message { Payload = payload });

            await plugin.StopAsync();

            var content = File.ReadAllText(filePath, Encoding.UTF8);
            Assert.Contains("existing", content);
            Assert.Contains("617070656E646564", content); // "appended" in hex
        }

        [Fact]
        public async Task SendAsync_OverwritesWhenNotAppend()
        {
            var filePath = Path.Combine(_testDir, "overwrite.txt");
            File.WriteAllText(filePath, "old-content\n", Encoding.UTF8);

            var plugin = new FileOutputPlugin(new FileOutputConfig { FilePath = filePath, Append = false });
            await plugin.StartAsync();

            var payload = Encoding.UTF8.GetBytes("new");
            await plugin.SendAsync(new Message { Payload = payload });

            await plugin.StopAsync();

            var content = File.ReadAllText(filePath, Encoding.UTF8);
            Assert.Equal("6E6577", content.Trim()); // "new" in hex
            Assert.DoesNotContain("old-content", content);
        }

        [Fact]
        public async Task SendAsync_WhenNotRunning_ThrowsInvalidOperationException()
        {
            var plugin = new FileOutputPlugin(new FileOutputConfig());
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.SendAsync(new Message { Payload = Array.Empty<byte>() }));
            Assert.Contains("not running", ex.Message);
        }

        [Fact]
        public async Task StartAsync_TransitionsToRunning()
        {
            var filePath = Path.Combine(_testDir, "status.txt");
            var plugin = new FileOutputPlugin(new FileOutputConfig { FilePath = filePath });
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var filePath = Path.Combine(_testDir, "stop.txt");
            var plugin = new FileOutputPlugin(new FileOutputConfig { FilePath = filePath });
            await plugin.StartAsync();
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }
    }
}
