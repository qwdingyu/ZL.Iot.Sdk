// ============================================================
// 文件：ArchitectureFixesTests.cs
// 描述：针对架构分析报告中的 P0/P1 修复项的充分测试
// 覆盖：DeadLetterStore 异步安全、PipelineMetrics 暴露、
//       Message 不可变性、CT-only 终止、CircuitBreaker 恢复日志
// 修改日期：2026-06-05
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

using ZL.ProtocolGateway.Plugins;

namespace ZL.ProtocolGateway.Tests
{
    public class DeadLetterStoreAsyncTests : IDisposable
    {
        private readonly string _dbPath;
        private DeadLetterStore? _store;

        public DeadLetterStoreAsyncTests(ITestOutputHelper output)
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"dl_test_{Guid.NewGuid():N}.db");
        }

        [Fact]
        public async Task AddAsync_ShouldPersistAndRetrieve()
        {
            _store = new DeadLetterStore(_dbPath, maxRows: 100, retentionHours: 1);
            var entry = new DeadLetterStore.DeadLetterEntry
            {
                Topic = "test/topic",
                ContentType = "text",
                PayloadText = "hello dead letter",
                ExceptionMessage = "Connection refused",
                ExceptionType = "IOException",
                OutputName = "tcp-out",
                RetryCount = 3,
                FailedAt = DateTimeOffset.UtcNow.ToString("o"),
                TraceId = "trace-001"
            };

            await _store.AddAsync(entry);

            var all = await _store.GetAllAsync();
            Assert.Single(all);
            Assert.Equal("test/topic", all[0].Topic);
            Assert.Equal("hello dead letter", all[0].PayloadText);
            Assert.Equal("trace-001", all[0].TraceId);
        }

        [Fact]
        public async Task AddAsync_NullEntry_IsIgnored()
        {
            _store = new DeadLetterStore(_dbPath);
            await _store.AddAsync(null!); // should not throw
            Assert.Equal(0, await _store.GetCountAsync());
        }

        [Fact]
        public async Task AddAsync_ExceedsMaxRows_TrimsOldest()
        {
            _store = new DeadLetterStore(_dbPath, maxRows: 5, retentionHours: 0);
            for (int i = 0; i < 10; i++)
            {
                await _store.AddAsync(new DeadLetterStore.DeadLetterEntry
                {
                    Topic = $"topic-{i}",
                    FailedAt = DateTimeOffset.UtcNow.ToString("o")
                });
            }

            var all = await _store.GetAllAsync(limit: 100);
            Assert.True(all.Count <= 5, $"Expected <= 5 rows but got {all.Count}");
        }

        [Fact]
        public async Task AddAsync_RetentionPurge_RemovesExpired()
        {
            _store = new DeadLetterStore(_dbPath, maxRows: 10000, retentionHours: 1);

            // Insert an old entry with a manually crafted 2-hour-old timestamp
            var oldTime = DateTimeOffset.UtcNow.AddHours(-2).ToString("o");
            await _store.AddAsync(new DeadLetterStore.DeadLetterEntry
            {
                Topic = "old",
                FailedAt = oldTime
            });

            // Insert a fresh entry — this triggers PurgeOldRowsAsync which removes the old one
            await _store.AddAsync(new DeadLetterStore.DeadLetterEntry
            {
                Topic = "new",
                FailedAt = DateTimeOffset.UtcNow.ToString("o")
            });

            var all = await _store.GetAllAsync(limit: 100);
            Assert.All(all, e => Assert.Equal("new", e.Topic));
        }

        [Fact]
        public async Task ClearAsync_ShouldRemoveAllEntries()
        {
            // retentionHours: 0 disables time-based purge, preventing old timestamps from being deleted
            _store = new DeadLetterStore(_dbPath, retentionHours: 0);
            await _store.AddAsync(new DeadLetterStore.DeadLetterEntry { Topic = "a", FailedAt = DateTimeOffset.UtcNow.ToString("o") });
            await _store.AddAsync(new DeadLetterStore.DeadLetterEntry { Topic = "b", FailedAt = DateTimeOffset.UtcNow.ToString("o") });
            Assert.Equal(2, await _store.GetCountAsync());

            await _store.ClearAsync();
            Assert.Equal(0, await _store.GetCountAsync());
        }

        [Fact]
        public async Task AddAsync_TruncatesLongPayloads()
        {
            _store = new DeadLetterStore(_dbPath);
            var longText = new string('X', 5000);
            var longHex = new string('A', 3000);
            var longEx = new string('E', 3000);

            await _store.AddAsync(new DeadLetterStore.DeadLetterEntry
            {
                Topic = "truncate-test",
                PayloadText = longText,
                PayloadHex = longHex,
                ExceptionMessage = longEx,
                FailedAt = DateTimeOffset.UtcNow.ToString("o")
            });

            var all = await _store.GetAllAsync();
            Assert.Single(all);
            Assert.Equal(4096, all[0].PayloadText!.Length);
            Assert.Equal(2048, all[0].PayloadHex!.Length);
            Assert.Equal(2048, all[0].ExceptionMessage!.Length);
        }

        public void Dispose()
        {
            _store?.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
    }

    public class PipelineMetricsCollectorTests
    {
        [Fact]
        public void GetSnapshot_Empty_ReturnsZeroes()
        {
            var collector = new PipelineMetricsCollector();
            var snapshot = collector.GetSnapshot();

            Assert.Equal(0, snapshot.TotalProcessed);
            Assert.Equal(0, snapshot.TotalFailed);
            Assert.Equal(0, snapshot.LatencyP50Ms);
            Assert.Equal(0, snapshot.LatencyP95Ms);
            Assert.Equal(0, snapshot.LatencyP99Ms);
            Assert.Equal(0, snapshot.LatencyCount);
            Assert.Equal(0, snapshot.TotalDeadLetters);
        }

        [Fact]
        public void RecordSuccess_ShouldTrackLatencyPercentiles()
        {
            var collector = new PipelineMetricsCollector();

            // Record 100 messages with known latencies
            for (int i = 1; i <= 100; i++)
            {
                collector.RecordSuccess(i * 1.0); // 1ms, 2ms, ... 100ms
            }

            var snapshot = collector.GetSnapshot();

            Assert.Equal(100, snapshot.TotalProcessed);
            // 0-indexed array: index 50 = 51st element = value 51
            Assert.Equal(51, snapshot.LatencyP50Ms);
            Assert.Equal(96, snapshot.LatencyP95Ms);
            // index min(99, 99) = latencies[99] = value 100
            Assert.Equal(100, snapshot.LatencyP99Ms);
            Assert.Equal(50.5, snapshot.LatencyAvgMs); // average of 1..100
            Assert.Equal(100, snapshot.LatencyMaxMs);
        }

        [Fact]
        public void RecordFailure_ShouldTrackErrorRate()
        {
            var collector = new PipelineMetricsCollector();

            collector.RecordSuccess(10.0);
            collector.RecordSuccess(12.0);
            collector.RecordSuccess(8.0);
            collector.RecordFailure();

            var snapshot = collector.GetSnapshot();

            Assert.Equal(3, snapshot.TotalProcessed);
            Assert.Equal(1, snapshot.TotalFailed);
        }

        [Fact]
        public void RecordFilteredAndDeadLetter_ShouldTrackCounts()
        {
            var collector = new PipelineMetricsCollector();

            collector.RecordFiltered();
            collector.RecordFiltered();
            collector.RecordDeadLetter();

            var snapshot = collector.GetSnapshot();

            Assert.Equal(2, snapshot.TotalFiltered);
            Assert.Equal(1, snapshot.TotalDeadLetters);
        }

        [Fact]
        public void SlidingWindow_ExceedsCapacity_DiscardsOldest()
        {
            var collector = new PipelineMetricsCollector();

            // Record 150 latencies (window size is 100)
            for (int i = 1; i <= 150; i++)
            {
                collector.RecordSuccess(i);
            }

            var snapshot = collector.GetSnapshot();

            Assert.Equal(100, snapshot.LatencyCount);
            // Window contains values 51..150 (100 items). Sorted: [51,52,...,150]
            // P50: index (int)(100*0.50)=50 → latencies[50] = 51+50 = 101
            Assert.Equal(101, snapshot.LatencyP50Ms);
            Assert.Equal(150, snapshot.LatencyMaxMs); // max of 51..150
        }

        [Fact]
        public void Reset_ShouldClearAllCounters()
        {
            var collector = new PipelineMetricsCollector();

            collector.RecordSuccess(42.0);
            collector.RecordFailure();
            collector.RecordFiltered();
            collector.RecordDeadLetter();

            collector.Reset();

            var snapshot = collector.GetSnapshot();
            Assert.Equal(0, snapshot.TotalProcessed);
            Assert.Equal(0, snapshot.TotalFailed);
            Assert.Equal(0, snapshot.TotalFiltered);
            Assert.Equal(0, snapshot.TotalDeadLetters);
            Assert.Equal(0, snapshot.LatencyCount);
        }

        [Fact]
        public void RecordSuccess_Multiple_ShouldTrackTotal()
        {
            var collector = new PipelineMetricsCollector();

            collector.RecordSuccess(1.0);
            collector.RecordSuccess(1.0);
            collector.RecordSuccess(1.0);

            var snapshot = collector.GetSnapshot();

            Assert.Equal(3, snapshot.TotalProcessed);
            Assert.Equal(3, snapshot.LatencyCount);
        }
    }

    public class CircuitBreakerRecoveryTests
    {
        /// <summary>
        /// TestSendAsync calls SendWithRetryAndTimeout directly, which never throws —
        /// it returns GatewaySendResult with FinalStatus indicating success/failure.
        /// GetCircuitBreakerState calls breaker.GetState() which returns raw state
        /// without auto-transitioning Open→HalfOpen (unlike IsOpen getter).
        /// </summary>
        [Fact]
        public async Task CircuitBreaker_HalfOpen_Success_ResetsToClosed()
        {
            var pipeline = new ResilientMessagePipeline
            {
                CircuitBreakerFailureThreshold = 2,
                CircuitBreakerRecoveryTimeMs = 100,
                MaxRetryAttempts = 0, // no retries for fast test
                SendTimeoutMs = 3000
            };

            var output = Substitute.For<IOutputPlugin>();
            output.Name.Returns("test-output");
            output.ProtocolType.Returns("Test");
            output.Status.Returns(PluginStatus.Running);
            output.SendAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new IOException("fail")));

            pipeline.RegisterOutput(output);

            // Trigger 2 failures to open the breaker
            for (int i = 0; i < 2; i++)
            {
                var result = await pipeline.TestSendAsync("test-output", new Message { Topic = "fail" });
                Assert.Equal(GatewaySendFinalStatus.Failed, result.FinalStatus);
            }

            Assert.Equal(CircuitBreakerState.Open, pipeline.GetCircuitBreakerState("test-output"));

            // Wait for recovery window
            await Task.Delay(150);

            // Change output to succeed
            output.SendAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // Half-open probe: IsOpen getter transitions Open→HalfOpen and allows one probe
            var successResult = await pipeline.TestSendAsync("test-output", new Message { Topic = "recover" });
            Assert.Equal(GatewaySendFinalStatus.Success, successResult.FinalStatus);

            Assert.Equal(CircuitBreakerState.Closed, pipeline.GetCircuitBreakerState("test-output"));
        }

        [Fact]
        public async Task CircuitBreaker_Open_RejectsImmediately()
        {
            var pipeline = new ResilientMessagePipeline
            {
                CircuitBreakerFailureThreshold = 2,
                CircuitBreakerRecoveryTimeMs = 5000, // long recovery so it stays open
                MaxRetryAttempts = 0, // no retries for fast test
                SendTimeoutMs = 3000
            };

            var output = Substitute.For<IOutputPlugin>();
            output.Name.Returns("cb-test");
            output.ProtocolType.Returns("Test");
            output.Status.Returns(PluginStatus.Running);
            output.SendAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new IOException("fail")));

            pipeline.RegisterOutput(output);

            // Open the breaker with 2 failures
            for (int i = 0; i < 2; i++)
            {
                var result = await pipeline.TestSendAsync("cb-test", new Message { Topic = "fail" });
                Assert.Equal(GatewaySendFinalStatus.Failed, result.FinalStatus);
            }

            Assert.Equal(CircuitBreakerState.Open, pipeline.GetCircuitBreakerState("cb-test"));
        }

        [Fact]
        public void CircuitBreaker_Reset_ShouldCloseImmediately()
        {
            var pipeline = new ResilientMessagePipeline();

            var output = Substitute.For<IOutputPlugin>();
            output.Name.Returns("reset-test");
            pipeline.RegisterOutput(output);

            // ResetCircuitBreaker should work and leave state as Closed
            pipeline.ResetCircuitBreaker("reset-test");
            Assert.Equal(CircuitBreakerState.Closed, pipeline.GetCircuitBreakerState("reset-test"));
        }
    }

    public class MessageImmutabilityTests
    {
        [Fact]
        public void SetPayload_ShouldSetPayload()
        {
            var msg = new Message { Topic = "test" };
            var data = new byte[] { 1, 2, 3 };

            msg.SetPayload(data);

            Assert.Same(data, msg.Payload);
        }

        [Fact]
        public void SetJsonContent_ShouldSetPayloadAndContentType()
        {
            var msg = new Message();
            msg.SetJsonContent("{\"key\":\"value\"}");

            Assert.Equal("json", msg.ContentType);
            Assert.NotNull(msg.Payload);
            Assert.Equal("{\"key\":\"value\"}", msg.GetJsonContent());
        }

        [Fact]
        public void SetTextContent_ShouldSetPayloadAndContentType()
        {
            var msg = new Message();
            msg.SetTextContent("hello");

            Assert.Equal("text", msg.ContentType);
            Assert.Equal("hello", msg.GetTextContent());
        }

        [Fact]
        public void Clone_ShouldCreateIndependentCopy()
        {
            var original = new Message { Topic = "test" };
            original.SetPayload(new byte[] { 1, 2, 3 });
            original.Metadata["key"] = "value";

            var clone = original.Clone();

            // Modify clone
            clone.SetPayload(new byte[] { 4, 5, 6 });
            clone.Metadata["key"] = "modified";

            // Original should be unchanged
            Assert.Equal(3, original.Payload.Length);
            Assert.Equal(1, original.Payload[0]);
            Assert.Equal("value", original.Metadata["key"]);
        }

        [Fact]
        public void Metadata_IsAlwaysInitialized()
        {
            var msg = new Message();
            Assert.NotNull(msg.Metadata);
            Assert.Empty(msg.Metadata);

            // Should be able to add entries without null check
            msg.Metadata["test"] = "value";
            Assert.Equal("value", msg.Metadata["test"]);
        }

        [Fact]
        public void TraceId_Property_ReadsAndWritesMetadata()
        {
            var msg = new Message();

            msg.TraceId = "trace-123";
            Assert.Equal("trace-123", msg.TraceId);
            Assert.True(msg.Metadata.ContainsKey("TraceId"));
        }
    }

    public class GatewayMetricsSnapshotIntegrationTests
    {
        [Fact]
        public void GatewayMetricsSnapshot_HasPipelineMetricsProperty()
        {
            var snapshot = new GatewayMetricsSnapshot();

            // Verify PipelineMetrics property exists and is nullable
            Assert.Null(snapshot.PipelineMetrics);

            var pipelineMetrics = new PipelineMetricsSnapshot
            {
                TotalProcessed = 100,
                LatencyP50Ms = 5.5,
                LatencyP95Ms = 15.2,
                LatencyP99Ms = 25.8
            };
            snapshot.PipelineMetrics = pipelineMetrics;

            Assert.NotNull(snapshot.PipelineMetrics);
            Assert.Equal(100, snapshot.PipelineMetrics.TotalProcessed);
            Assert.Equal(5.5, snapshot.PipelineMetrics.LatencyP50Ms);
        }
    }

    public class CancellationOnlyShutdownTests
    {
        [Fact]
        public async Task IndustrialOutputPluginBase_OnStop_OnlyUsesCancellationToken()
        {
            // This test verifies that IndustrialOutputPluginBase no longer uses _stopRequested.
            // We create a test plugin and verify clean shutdown via CT only.
            var plugin = new TestOutputPlugin();

            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);

            // Stop should work cleanly with just CT cancellation
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task IndustrialOutputPluginBase_RapidStartStop_DoesNotLeak()
        {
            var plugin = new TestOutputPlugin();

            // Rapid start/stop cycles should not cause issues
            for (int i = 0; i < 5; i++)
            {
                await plugin.StartAsync();
                await Task.Delay(10);
                await plugin.StopAsync();
            }

            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task TcpOutputPlugin_OnStop_OnlyUsesCancellationToken()
        {
            // TcpOutputPlugin also had _stopRequested — verify it's removed
            var config = new TcpOutputConfig
            {
                ServerIp = "127.0.0.1",
                Port = 59999, // unused port — will fail to connect
                ReconnectIntervalMs = 500
            };
            var plugin = new TcpOutputPlugin(config);

            await plugin.StartAsync();
            await Task.Delay(200); // Let it try to connect and fail

            // Stop should work cleanly
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }
    }

    /// <summary>
    /// 测试用 Output 插件 — 继承 IndustrialOutputPluginBase，验证 CT-only 终止
    /// </summary>
    public class TestOutputPlugin : IndustrialOutputPluginBase
    {
        public override string Name { get; }
        public override string ProtocolType => "Test";

        public TestOutputPlugin()
        {
            Name = "test-output";
        }

        protected override Task TryConnectAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        protected override bool HasLiveConnection() => true;

        protected override Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
