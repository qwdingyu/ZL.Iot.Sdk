// ============================================================
// 文件：PipelineMetricsCollectorTests.cs
// 描述：PipelineMetricsCollector 完整测试 — 计数器、滑动窗口、百分位
// 修改日期：2026-06-05
// ============================================================

using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    public class PipelineMetricsCollectorTests
    {
        [Fact]
        public void Constructor_InitializesEmptySnapshot()
        {
            var collector = new PipelineMetricsCollector();
            var snapshot = collector.GetSnapshot();

            Assert.Equal(0L, snapshot.TotalProcessed);
            Assert.Equal(0L, snapshot.TotalFiltered);
            Assert.Equal(0L, snapshot.TotalFailed);
            Assert.Equal(0L, snapshot.TotalDeadLetters);
            Assert.Equal(0, snapshot.LatencyCount);
            Assert.Equal(0, snapshot.LatencyAvgMs);
            Assert.Equal(0, snapshot.LatencyP50Ms);
            Assert.Equal(0, snapshot.LatencyP95Ms);
            Assert.Equal(0, snapshot.LatencyP99Ms);
            Assert.Equal(0, snapshot.LatencyMaxMs);
        }

        [Fact]
        public void RecordSuccess_IncrementsProcessedAndRecordsLatency()
        {
            var collector = new PipelineMetricsCollector();
            collector.RecordSuccess(10.5);

            var snapshot = collector.GetSnapshot();
            Assert.Equal(1L, snapshot.TotalProcessed);
            Assert.Equal(1, snapshot.LatencyCount);
            Assert.Equal(10.5, snapshot.LatencyAvgMs);
            Assert.Equal(10.5, snapshot.LatencyP50Ms);
            Assert.Equal(10.5, snapshot.LatencyMaxMs);
        }

        [Fact]
        public void RecordFailure_IncrementsFailed()
        {
            var collector = new PipelineMetricsCollector();
            collector.RecordFailure();
            collector.RecordFailure();

            var snapshot = collector.GetSnapshot();
            Assert.Equal(2L, snapshot.TotalFailed);
        }

        [Fact]
        public void RecordFiltered_IncrementsFiltered()
        {
            var collector = new PipelineMetricsCollector();
            collector.RecordFiltered();

            var snapshot = collector.GetSnapshot();
            Assert.Equal(1L, snapshot.TotalFiltered);
        }

        [Fact]
        public void RecordDeadLetter_IncrementsDeadLetters()
        {
            var collector = new PipelineMetricsCollector();
            collector.RecordDeadLetter();

            var snapshot = collector.GetSnapshot();
            Assert.Equal(1L, snapshot.TotalDeadLetters);
        }

        [Fact]
        public void Percentiles_WithMultipleValues_ComputesCorrectly()
        {
            var collector = new PipelineMetricsCollector();

            // 记录 10 个已知值
            for (int i = 1; i <= 10; i++)
            {
                collector.RecordSuccess(i * 10.0); // 10, 20, 30, ..., 100
            }

            var snapshot = collector.GetSnapshot();
            Assert.Equal(10, snapshot.LatencyCount);
            Assert.Equal(55.0, snapshot.LatencyAvgMs); // (10+20+...+100)/10
            Assert.Equal(100.0, snapshot.LatencyMaxMs);
            // P50: with 10 values (10,20,...,100), index 5 = 60
            Assert.Equal(60.0, snapshot.LatencyP50Ms);
        }

        [Fact]
        public void SlidingWindow_After100Records_OldestDropped()
        {
            var collector = new PipelineMetricsCollector();

            // 记录 110 个值 — 窗口大小 100，最早的 10 个被覆盖
            for (int i = 1; i <= 110; i++)
            {
                collector.RecordSuccess(i);
            }

            var snapshot = collector.GetSnapshot();
            Assert.Equal(110L, snapshot.TotalProcessed); // 计数器不滑动
            Assert.Equal(100, snapshot.LatencyCount);    // 窗口只有 100 个有效值
            Assert.Equal(110.0, snapshot.LatencyMaxMs);  // 环形缓冲区 100 格，最终包含 11-110
        }

        [Fact]
        public void Reset_ClearsAllCountersAndLatency()
        {
            var collector = new PipelineMetricsCollector();
            collector.RecordSuccess(42.0);
            collector.RecordFailure();
            collector.RecordFiltered();
            collector.RecordDeadLetter();

            collector.Reset();

            var snapshot = collector.GetSnapshot();
            Assert.Equal(0L, snapshot.TotalProcessed);
            Assert.Equal(0L, snapshot.TotalFiltered);
            Assert.Equal(0L, snapshot.TotalFailed);
            Assert.Equal(0L, snapshot.TotalDeadLetters);
            Assert.Equal(0, snapshot.LatencyCount);
        }

        [Fact]
        public async Task ConcurrentRecordSuccess_ThreadSafe()
        {
            var collector = new PipelineMetricsCollector();
            var tasks = new Task[100];

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => collector.RecordSuccess(1.0));
            }

            await Task.WhenAll(tasks);

            var snapshot = collector.GetSnapshot();
            Assert.Equal(100L, snapshot.TotalProcessed);
            Assert.Equal(100, snapshot.LatencyCount);
        }

        [Fact]
        public async Task ConcurrentMixedOperations_ThreadSafe()
        {
            var collector = new PipelineMetricsCollector();
            var tasks = new Task[400];

            for (int i = 0; i < 100; i++) tasks[i] = Task.Run(() => collector.RecordSuccess(1.0));
            for (int i = 100; i < 200; i++) tasks[i] = Task.Run(() => collector.RecordFailure());
            for (int i = 200; i < 300; i++) tasks[i] = Task.Run(() => collector.RecordFiltered());
            for (int i = 300; i < 400; i++) tasks[i] = Task.Run(() => collector.RecordDeadLetter());

            await Task.WhenAll(tasks);

            var snapshot = collector.GetSnapshot();
            Assert.Equal(100L, snapshot.TotalProcessed);
            Assert.Equal(100L, snapshot.TotalFailed);
            Assert.Equal(100L, snapshot.TotalFiltered);
            Assert.Equal(100L, snapshot.TotalDeadLetters);
        }

        [Fact]
        public void GetSnapshot_IsThreadSafe_DoesNotMutateState()
        {
            var collector = new PipelineMetricsCollector();
            collector.RecordSuccess(5.0);
            collector.RecordSuccess(10.0);

            var s1 = collector.GetSnapshot();
            var s2 = collector.GetSnapshot();

            Assert.NotSame(s1, s2);
            Assert.Equal(s1.LatencyAvgMs, s2.LatencyAvgMs);
            Assert.Equal(2, s1.LatencyCount);
        }
    }
}
