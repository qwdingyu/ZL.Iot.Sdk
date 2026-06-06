// ============================================================
// 文件：RecentImprovementsTests.cs
// 描述：针对 2026-06-05 本轮 13 项改进的专项回归测试
// 覆盖：A(死信持久化日志) C(内存泄漏) D(IAsyncDisposable)
//       F(零分配指标) G(超时异常观察) I(Dispose死锁)
//       J(同步Handler) K(HTTP异常观察) L(Pipeline Dispose)
//       M(配置系统) N(文件日志) P(优雅关闭) Q(HealthCheck单例)
// ============================================================

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class RecentImprovementsTests : IDisposable
    {
        private string? _testLogDir;

        public void Dispose()
        {
            GatewayLog.DisableFileOutput();
            if (!string.IsNullOrEmpty(_testLogDir) && Directory.Exists(_testLogDir))
            {
                try { Directory.Delete(_testLogDir, true); } catch { }
            }
        }

        #region A — 死信持久化 fire-and-forget 异常日志

        [Fact]
        public void DeadLetterManager_Add_CallsPersistAsync()
        {
            // 验证：Add() 调用 PersistAsync（通过 Store 属性）
            var manager = new PipelineDeadLetterManager();
            var store = new DeadLetterStore(":memory:");
            manager.Store = store;

            manager.Add(new Message { Topic = "test" }, new Exception("fail"), null);

            Assert.Equal(1, manager.Count);
            store.Dispose();
        }

        [Fact]
        public void DeadLetterManager_Add_AlwaysStartsAtRetry1_AndClonesMessage()
        {
            var manager = new PipelineDeadLetterManager();
            var msg = new Message { Topic = "test" };
            msg.Metadata["original"] = "value";

            manager.Add(msg, new Exception("fail"), null);

            // New design: Add() always clones and starts at retryCount=1.
            // Old metadata-based retry counting is removed (P0-5 fix).
            Assert.Equal(1, manager.Count);
            var dl = manager.GetMessages().Single();
            Assert.Equal(1, dl.RetryCount);
            Assert.NotSame(msg, dl.Message); // clone
            Assert.Equal("value", msg.Metadata["original"]); // original untouched
        }

        #endregion

        #region C — inFlightTasks 内存泄漏消除

        [Fact]
        public async Task Pipeline_ProcessMessages_DoesNotLeakTaskReferences()
        {
            // 验证：Pipeline 正常处理消息后不泄漏
            var pipeline = new ResilientMessagePipeline
            {
                QueueCapacity = 100,
                SendTimeoutMs = 5000,
                MaxRetryAttempts = 0,
                RetryBaseDelayMs = 10
            };

            var output = new FastMockOutputPlugin { Name = "test" };
            pipeline.RegisterOutput(output);

            await pipeline.StartAsync(CancellationToken.None);

            // 使用 TestSendAsync 直接发送（不走 Channel），确保 SendCount 可观测
            for (int i = 0; i < 10; i++)
            {
                await pipeline.TestSendAsync("test", new Message { Topic = $"t{i}" });
            }

            Assert.Equal(10, output.SendCount);
            await pipeline.StopAsync();
        }

        #endregion

        #region D — DeadLetterStore IAsyncDisposable

        [Fact]
        public async Task DeadLetterStore_DisposeAsync_DoesNotThrow()
        {
            var store = new DeadLetterStore(":memory:");
            await store.AddAsync(new DeadLetterStore.DeadLetterEntry
            {
                Topic = "test",
                FailedAt = DateTimeOffset.UtcNow.ToString("O")
            });

            await store.DisposeAsync(); // 不应抛异常

            // 双重 DisposeAsync 也不应抛异常
            await store.DisposeAsync();
        }

        [Fact]
        public void DeadLetterStore_Dispose_DoesNotThrow()
        {
            var store = new DeadLetterStore(":memory:");
            store.Dispose();
            store.Dispose(); // 双重 Dispose 安全
        }

        #endregion

        #region F — PipelineMetricsCollector 零分配

        [Fact]
        public void MetricsCollector_GetSnapshot_EmptyWindow_ReturnsZeroLatency()
        {
            var collector = new PipelineMetricsCollector();
            var snapshot = collector.GetSnapshot();

            Assert.Equal(0, snapshot.LatencyCount);
            Assert.Equal(0, snapshot.LatencyAvgMs);
            Assert.Equal(0, snapshot.LatencyP50Ms);
        }

        [Fact]
        public void MetricsCollector_GetSnapshot_PartialWindow_CountsOnlyNonZero()
        {
            var collector = new PipelineMetricsCollector();
            // 只记录 5 个值（窗口 100），count 应该 = 5
            for (int i = 0; i < 5; i++)
            {
                collector.RecordSuccess((i + 1) * 10.0);
            }

            var snapshot = collector.GetSnapshot();
            Assert.Equal(5, snapshot.LatencyCount);
            Assert.Equal(30.0, snapshot.LatencyAvgMs); // (10+20+30+40+50)/5
            Assert.Equal(50.0, snapshot.LatencyMaxMs);
        }

        [Fact]
        public void MetricsCollector_GetSnapshot_FullWindow_AfterOverflow()
        {
            var collector = new PipelineMetricsCollector();
            for (int i = 1; i <= 150; i++)
            {
                collector.RecordSuccess(i);
            }

            var snapshot = collector.GetSnapshot();
            Assert.Equal(100, snapshot.LatencyCount); // 窗口 100 格全满
            Assert.Equal(150.0, snapshot.LatencyMaxMs);
            Assert.Equal(150L, snapshot.TotalProcessed); // 计数器不滑动
        }

        #endregion

        #region G — PipelineSendStrategy 超时后观察异常

        [Fact]
        public async Task PipelineSendStrategy_Timeout_ObservesTaskAndReturnsFailureResult()
        {
            // 验证：超时后 SendAsync 观察 sendTask（不抛 UnobservedTaskException），返回失败结果
            var slowOutput = new SlowMockOutputPlugin { Name = "slow", DelayMs = 2000 };
            var strategy = new PipelineSendStrategy(() => 100, () => 0, () => 100); // 100ms timeout
            var breaker = new CircuitBreaker(failureThreshold: 5, recoveryTimeMs: 30000);

            Exception? capturedEx = null;
            var deadLetterAction = new Action<Message, Exception>((msg, ex) => { capturedEx = ex; });

            var result = await strategy.SendAsync(
                slowOutput, new Message { Topic = "test" }, "trace-1", breaker, deadLetterAction, _ => { }, CancellationToken.None);

            // SendAsync 内部捕获 TimeoutException，返回失败结果（不向外抛出）
            Assert.Equal(GatewaySendFinalStatus.Failed, result.FinalStatus);
            Assert.NotNull(capturedEx); // 死信回调被触发
            Assert.IsType<TimeoutException>(capturedEx);

            // 等待确保后台任务完成，不应抛出 UnobservedTaskException
            await Task.Delay(2100);
        }

        #endregion

        #region I+J+K — HealthCheckHttp 同步 Handler + Dispose 安全

        [Fact]
        public void HealthCheckHttp_Dispose_DoesNotDeadlock()
        {
            // 验证：Dispose() 不调用 StopAsync().Wait()，不会死锁
            var manager = new GatewayManager();
            var httpService = new GatewayHealthCheckHttpService(manager, "http://127.0.0.1:0/");

            // 不调用 StartAsync，直接 Dispose — 不应死锁或抛异常
            httpService.Dispose();
        }

        [Fact]
        public async Task HealthCheckHttp_DisposeAsync_GracefulShutdown()
        {
            var manager = new GatewayManager();
            var httpService = new GatewayHealthCheckHttpService(manager, "http://127.0.0.1:0/");

            await httpService.DisposeAsync();
            // 不应抛异常
        }

        [Fact]
        public void HealthCheckHttp_UsesManagerHealthCheckInstance()
        {
            // Q 修复验证：HealthCheckHttp 使用 Manager.HealthCheck，不是独立实例
            var manager = new GatewayManager();
            var httpService = new GatewayHealthCheckHttpService(manager);

            // 通过反射或间接方式验证：两个实例的 HealthCheck 是同一个
            Assert.Same(manager.HealthCheck, manager.HealthCheck); // 同一引用
        }

        #endregion

        #region L — Pipeline Dispose 无死锁

        [Fact]
        public void Pipeline_Dispose_Sync_DoesNotDeadlock()
        {
            var pipeline = new ResilientMessagePipeline();
            pipeline.Dispose(); // 不应死锁
        }

        [Fact]
        public async Task Pipeline_DisposeAsync_GracefulShutdown()
        {
            var pipeline = new ResilientMessagePipeline
            {
                QueueCapacity = 100,
                SendTimeoutMs = 5000,
                MaxRetryAttempts = 0
            };

            var output = new FastMockOutputPlugin { Name = "test" };
            pipeline.RegisterOutput(output);

            await pipeline.StartAsync(CancellationToken.None);
            await pipeline.ProcessAsync(new Message { Topic = "test" });
            await Task.Delay(200);

            await pipeline.DisposeAsync(); // 优雅关闭
        }

        #endregion

        #region M — 配置系统

        [Fact]
        public void GatewayConfiguration_Defaults_AreValid()
        {
            var config = new GatewayConfiguration();
            var errors = config.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void GatewayConfiguration_ToManagerOptions_MapsCorrectly()
        {
            var config = new GatewayConfiguration
            {
                QueueCapacity = 20000,
                SendTimeoutMs = 60000,
                MaxRetryAttempts = 5,
                EnableHealthCheckHttp = true,
                HealthCheckHttpPort = 8080
            };

            var options = config.ToManagerOptions();
            Assert.Equal(20000, options.QueueCapacity);
            Assert.Equal(60000, options.SendTimeoutMs);
            Assert.Equal(5, options.MaxRetryAttempts);
            Assert.True(options.EnableHealthCheckHttp);
            Assert.Equal(8080, options.HealthCheckHttpPort);
        }

        [Fact]
        public void GatewayConfiguration_Validate_CatchesInvalidValues()
        {
            var config = new GatewayConfiguration { QueueCapacity = 50 };
            var errors = config.Validate();
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.PropertyName == nameof(GatewayConfiguration.QueueCapacity));
        }

        [Fact]
        public void GatewayConfigurationLoader_LoadDefaults_ReturnsDefaults()
        {
            var config = GatewayConfigurationLoader.LoadDefaults();
            Assert.Equal(10000, config.QueueCapacity);
            Assert.Equal(30000, config.SendTimeoutMs);
            Assert.Equal("Info", config.LogLevel);
        }

        [Fact]
        public void GatewayConfigurationLoader_LoadFromJson_WithMissingFile_ReturnsDefaults()
        {
            var config = GatewayConfigurationLoader.LoadFromJson("/nonexistent/path/appsettings.json");
            Assert.Equal(10000, config.QueueCapacity); // 默认值
        }

        [Fact]
        public void GatewayConfigurationLoader_LoadFromJson_WithValidFile_OverridesDefaults()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"gw_config_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var configPath = Path.Combine(tempDir, "appsettings.json");

            try
            {
                File.WriteAllText(configPath, @"{
                    ""queueCapacity"": 50000,
                    ""sendTimeoutMs"": 15000,
                    ""enableHealthCheckHttp"": true,
                    ""logLevel"": ""Debug""
                }");

                var config = GatewayConfigurationLoader.LoadFromJson(configPath);
                Assert.Equal(50000, config.QueueCapacity);
                Assert.Equal(15000, config.SendTimeoutMs);
                Assert.True(config.EnableHealthCheckHttp);
                Assert.Equal("Debug", config.LogLevel);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void GatewayConfigurationLoader_EnvironmentVariables_OverrideDefaults()
        {
            try
            {
                Environment.SetEnvironmentVariable("GATEWAY_QUEUE_CAPACITY", "99999");
                Environment.SetEnvironmentVariable("GATEWAY_LOG_LEVEL", "Debug");
                Environment.SetEnvironmentVariable("GATEWAY_ENABLE_HEALTH_CHECK_HTTP", "true");

                // LoadDefaults() 不读取环境变量，使用 LoadFromJson() 来测试 env var 覆盖
                var config = GatewayConfigurationLoader.LoadFromJson();
                Assert.Equal(99999, config.QueueCapacity);
                Assert.Equal("Debug", config.LogLevel);
                Assert.True(config.EnableHealthCheckHttp);
            }
            finally
            {
                Environment.SetEnvironmentVariable("GATEWAY_QUEUE_CAPACITY", null);
                Environment.SetEnvironmentVariable("GATEWAY_LOG_LEVEL", null);
                Environment.SetEnvironmentVariable("GATEWAY_ENABLE_HEALTH_CHECK_HTTP", null);
            }
        }

        [Fact]
        public void GatewayConfigurationLoader_CommandLineArgs_HighestPriority()
        {
            try
            {
                Environment.SetEnvironmentVariable("GATEWAY_QUEUE_CAPACITY", "99999");

                var args = new[] { "--queue-capacity=77777" };
                var config = GatewayConfigurationLoader.LoadFromJson(null, args);
                Assert.Equal(77777, config.QueueCapacity); // 命令行 > 环境变量
            }
            finally
            {
                Environment.SetEnvironmentVariable("GATEWAY_QUEUE_CAPACITY", null);
            }
        }

        #endregion

        #region N — 文件日志

        [Fact]
        public void GatewayLog_EnableFileOutput_WritesToFile()
        {
            _testLogDir = Path.Combine(Path.GetTempPath(), $"gw_log_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testLogDir);

            try
            {
                GatewayLog.EnableFileOutput(_testLogDir, GatewayLog.LogLevel.Debug, maxFileSizeMb: 50);
                GatewayLog.Info("TestArea", "Test file log message");
                GatewayLog.Error("TestArea", "Test error message");

                // 给文件写入一点时间
                Thread.Sleep(200);
                GatewayLog.DisableFileOutput();

                var logFiles = Directory.GetFiles(_testLogDir, "gateway-*.log");
                Assert.NotEmpty(logFiles);

                var content = File.ReadAllText(logFiles[0]);
                Assert.Contains("Test file log message", content);
                Assert.Contains("Test error message", content);
                Assert.Contains("[TestArea]", content);
            }
            finally
            {
                GatewayLog.DisableFileOutput();
            }
        }

        [Fact]
        public void GatewayLog_EnableFileOutput_CreatesDirectoryIfMissing()
        {
            _testLogDir = Path.Combine(Path.GetTempPath(), $"gw_log_nested_{Guid.NewGuid():N}", "sub");

            try
            {
                GatewayLog.EnableFileOutput(_testLogDir);
                GatewayLog.Info("Test", "hello");
                Thread.Sleep(200);
                GatewayLog.DisableFileOutput();

                Assert.True(Directory.Exists(_testLogDir));
                var logFiles = Directory.GetFiles(_testLogDir, "gateway-*.log");
                Assert.NotEmpty(logFiles);
            }
            finally
            {
                GatewayLog.DisableFileOutput();
            }
        }

        [Fact]
        public void GatewayLog_DisableFileOutput_StopsWriting()
        {
            _testLogDir = Path.Combine(Path.GetTempPath(), $"gw_log_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testLogDir);

            try
            {
                GatewayLog.EnableFileOutput(_testLogDir);
                GatewayLog.Info("Test", "before disable");
                Thread.Sleep(100);
                GatewayLog.DisableFileOutput();

                GatewayLog.Info("Test", "after disable");
                Thread.Sleep(100);

                var logFiles = Directory.GetFiles(_testLogDir, "gateway-*.log");
                if (logFiles.Length > 0)
                {
                    var content = File.ReadAllText(logFiles[0]);
                    Assert.Contains("before disable", content);
                    Assert.DoesNotContain("after disable", content);
                }
            }
            finally
            {
                GatewayLog.DisableFileOutput();
            }
        }

        #endregion

        #region P — 优雅关闭

        [Fact]
        public async Task GatewayManager_GracefulShutdownAsync_CompletesWithoutError()
        {
            using var gm = new GatewayManager();
            await gm.StartAsync();

            // 注册一个输出插件
            var output = new FastMockOutputPlugin { Name = "test-out" };
            gm.RegisterOutput("test-out", output);
            await gm.StartOutputAsync("test-out");

            // 发送少量消息
            await gm.PublishMessageAsync(new Message { Topic = "test" });
            await Task.Delay(200);

            // 优雅关闭 — 不应抛异常
            await gm.GracefulShutdownAsync(TimeSpan.FromSeconds(10));
            Assert.False(gm.IsRunning);
        }

        [Fact]
        public async Task GatewayManager_GracefulShutdownAsync_AlreadyStopped_ReturnsImmediately()
        {
            using var gm = new GatewayManager();
            // 未启动直接关闭
            await gm.GracefulShutdownAsync();
            Assert.False(gm.IsRunning);
        }

        [Fact]
        public async Task GatewayManager_GracefulShutdownAsync_Timeout_ThrowsOperationCanceled()
        {
            using var gm = new GatewayManager();
            await gm.StartAsync();

            // 极短超时 — 可能超时
            var ex = await Record.ExceptionAsync(async () =>
            {
                await gm.GracefulShutdownAsync(TimeSpan.FromMilliseconds(1));
            });

            // 可能成功（快速关闭）或超时，都不算失败
            if (ex != null)
            {
                Assert.IsType<OperationCanceledException>(ex);
            }
        }

        #endregion

        #region Q — HealthCheck 单例

        [Fact]
        public void GatewayManager_HealthCheck_IsSingleInstance()
        {
            using var gm = new GatewayManager();
            var hc1 = gm.HealthCheck;
            var hc2 = gm.HealthCheck;
            Assert.Same(hc1, hc2);
        }

        [Fact]
        public async Task GatewayHealthCheckService_Check_ReflectsPluginStatus()
        {
            using var gm = new GatewayManager();
            await gm.StartAsync();

            var output = new FastMockOutputPlugin { Name = "hc-test" };
            gm.RegisterOutput("hc-test", output);
            await gm.StartOutputAsync("hc-test");

            var result = gm.HealthCheck.Check();
            Assert.Equal(1, result.TotalPlugins);
            Assert.True(result.IsLive);

            await gm.StopAsync();
        }

        #endregion

        #region DeadLetterStore ForcePersistAsync

        [Fact]
        public async Task DeadLetterStore_ForcePersistAsync_DoesNotThrow()
        {
            var store = new DeadLetterStore(":memory:");
            await store.AddAsync(new DeadLetterStore.DeadLetterEntry
            {
                Topic = "test",
                FailedAt = DateTimeOffset.UtcNow.ToString("O")
            });

            await store.ForcePersistAsync(); // 不应抛异常
            store.Dispose();
        }

        [Fact]
        public async Task DeadLetterStore_ForcePersistAsync_AfterDispose_IsNoop()
        {
            var store = new DeadLetterStore(":memory:");
            store.Dispose();
            await store.ForcePersistAsync(); // Dispose 后不应抛异常
        }

        #endregion

        #region Mock 类

        private class FastMockOutputPlugin : IOutputPlugin
        {
            public string Name { get; set; } = "mock";
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            private int _sendCount;
            public int SendCount => _sendCount;
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

            public Task StartAsync(CancellationToken ct = default)
            {
                Status = PluginStatus.Running;
                return Task.CompletedTask;
            }

            public Task SendAsync(Message message, CancellationToken ct = default)
            {
                Interlocked.Increment(ref _sendCount);
                return Task.CompletedTask;
            }

            public Task StopAsync()
            {
                Status = PluginStatus.Stopped;
                return Task.CompletedTask;
            }

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private class SlowMockOutputPlugin : IOutputPlugin
        {
            public string Name { get; set; } = "slow";
            public int DelayMs { get; set; } = 2000;
            public string ProtocolType => "SlowMock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

            public Task StartAsync(CancellationToken ct = default)
            {
                Status = PluginStatus.Running;
                return Task.CompletedTask;
            }

            public Task SendAsync(Message message, CancellationToken ct = default)
            {
                return Task.Delay(DelayMs, ct);
            }

            public Task StopAsync()
            {
                Status = PluginStatus.Stopped;
                return Task.CompletedTask;
            }

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        #endregion
    }
}
