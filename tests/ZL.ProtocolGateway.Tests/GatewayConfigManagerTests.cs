// ============================================================
// 文件：GatewayConfigManagerTests.cs
// 描述：GatewayConfigManager 单元测试 — 配置重载、文件监听
// ============================================================

using System;
using System.IO;
using System.Threading.Tasks;
using NSubstitute;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class GatewayConfigManagerTests
    {
        private ResilientMessagePipeline CreateMockPipeline()
        {
            var pipeline = Substitute.ForPartsOf<ResilientMessagePipeline>();
            pipeline.SendTimeoutMs = 30000;
            pipeline.MaxRetryAttempts = 3;
            pipeline.RetryBaseDelayMs = 100;
            pipeline.CircuitBreakerFailureThreshold = 5;
            pipeline.CircuitBreakerRecoveryTimeMs = 60000;
            pipeline.QueueCapacity = 10000;
            return pipeline;
        }

        private GatewayManagerOptions CreateDefaultOptions()
        {
            return new GatewayManagerOptions
            {
                SendTimeoutMs = 30000,
                MaxRetryAttempts = 3,
                RetryBaseDelayMs = 100,
                QueueCapacity = 10000,
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerRecoveryTimeMs = 60000
            };
        }

        [Fact]
        public async Task ReloadPipelineConfigAsync_ValidOptions_UpdatesPipeline()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            var newOptions = new GatewayManagerOptions
            {
                SendTimeoutMs = 60000,
                MaxRetryAttempts = 5,
                RetryBaseDelayMs = 200,
                QueueCapacity = 10000,
                CircuitBreakerFailureThreshold = 10,
                CircuitBreakerRecoveryTimeMs = 120000
            };

            await config.ReloadPipelineConfigAsync(newOptions);

            Assert.Equal(60000, pipeline.SendTimeoutMs);
            Assert.Equal(5, pipeline.MaxRetryAttempts);
            Assert.Equal(200, pipeline.RetryBaseDelayMs);
            Assert.Equal(10, pipeline.CircuitBreakerFailureThreshold);
            Assert.Equal(120000, pipeline.CircuitBreakerRecoveryTimeMs);
        }

        [Fact]
        public async Task ReloadPipelineConfigAsync_NullOptions_ThrowsArgumentNullException()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            await Assert.ThrowsAsync<ArgumentNullException>(() => config.ReloadPipelineConfigAsync(null));
        }

        [Fact]
        public async Task ReloadPipelineConfigAsync_UpdatesOptionsProperty()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            var newOptions = new GatewayManagerOptions
            {
                SendTimeoutMs = 45000,
                MaxRetryAttempts = 4,
                RetryBaseDelayMs = 150,
                QueueCapacity = 10000,
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerRecoveryTimeMs = 60000
            };

            await config.ReloadPipelineConfigAsync(newOptions);

            Assert.Equal(45000, config.Options.SendTimeoutMs);
            Assert.Equal(4, config.Options.MaxRetryAttempts);
        }

        [Fact]
        public async Task ReloadPipelineConfigAsync_InvalidConfig_Throws()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            var invalidOptions = new GatewayManagerOptions
            {
                SendTimeoutMs = -1,
                MaxRetryAttempts = -5,
                RetryBaseDelayMs = 100,
                QueueCapacity = 50, // below minimum 100
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerRecoveryTimeMs = 60000
            };

            await Assert.ThrowsAnyAsync<Exception>(() => config.ReloadPipelineConfigAsync(invalidOptions));
        }

        [Fact]
        public async Task ReloadPipelineConfigAsync_QueueCapacityChange_DoesNotThrow()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            var newOptions = new GatewayManagerOptions
            {
                SendTimeoutMs = 30000,
                MaxRetryAttempts = 3,
                RetryBaseDelayMs = 100,
                QueueCapacity = 20000, // different from original 10000
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerRecoveryTimeMs = 60000
            };

            // Should succeed (QueueCapacity change is just a warning, not an error)
            await config.ReloadPipelineConfigAsync(newOptions);

            // Other properties should still be updated
            Assert.Equal(30000, pipeline.SendTimeoutMs);
        }

        [Fact]
        public void WatchConfigurationFile_NullPath_ThrowsArgumentNullException()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            Assert.Throws<ArgumentNullException>(() => config.WatchConfigurationFile(null));
        }

        [Fact]
        public void WatchConfigurationFile_EmptyPath_ThrowsArgumentNullException()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            Assert.Throws<ArgumentNullException>(() => config.WatchConfigurationFile(""));
        }

        [Fact]
        public void WatchConfigurationFile_NonExistentFile_ThrowsFileNotFoundException()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            Assert.Throws<FileNotFoundException>(() => config.WatchConfigurationFile("/nonexistent/path/config.json"));
        }

        [Fact]
        public void WatchConfigurationFile_ValidFile_ReturnsDisposable()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            var tempFile = Path.Combine(Path.GetTempPath(), $"config_test_{Guid.NewGuid():N}.json");
            File.WriteAllText(tempFile, "{}");

            try
            {
                var watcher = config.WatchConfigurationFile(tempFile);
                Assert.NotNull(watcher);
                watcher.Dispose(); // Should not throw
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void Options_ReturnsCurrentConfig()
        {
            var pipeline = CreateMockPipeline();
            var options = CreateDefaultOptions();
            var config = new GatewayConfigManager(pipeline, options);

            Assert.Same(options, config.Options);
        }
    }
}
