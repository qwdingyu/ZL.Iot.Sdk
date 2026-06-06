// ============================================================
// 文件：InMemoryPipelineTapTests.cs
// 描述：InMemoryPipelineTap 单元测试 — 覆盖各阶段记录、快照、重置、并发安全
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    public class InMemoryPipelineTapTests
    {
        [Fact]
        public void Record_Enqueued_IncrementsTotalEnqueued()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("trace-1", "Enqueued", null, null);

            var snapshot = tap.GetSnapshot();
            Assert.Equal(1, snapshot.TotalEnqueued);
        }

        [Fact]
        public void Record_FilteredPassed_IncrementsFilteredPassed()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("trace-1", "Filtered", null, "Passed");

            var snapshot = tap.GetSnapshot();
            Assert.Equal(1, snapshot.TotalFilteredPassed);
            Assert.Equal(0, snapshot.TotalFilteredDropped);
        }

        [Fact]
        public void Record_FilteredDropped_IncrementsFilteredDropped()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("trace-1", "Filtered", null, "Dropped");

            var snapshot = tap.GetSnapshot();
            Assert.Equal(0, snapshot.TotalFilteredPassed);
            Assert.Equal(1, snapshot.TotalFilteredDropped);
        }

        [Fact]
        public void Record_Transformed_IncrementsTransformed()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("trace-1", "Transformed", null, null);

            var snapshot = tap.GetSnapshot();
            Assert.Equal(1, snapshot.TotalTransformed);
        }

        [Fact]
        public void Record_Routed_IncrementsRouted()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("trace-1", "Routed", null, null);

            var snapshot = tap.GetSnapshot();
            Assert.Equal(1, snapshot.TotalRouted);
        }

        [Fact]
        public void Record_SentSuccess_IncrementsSentSuccess()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("trace-1", "Sent", "outputA", "Success");

            var snapshot = tap.GetSnapshot();
            Assert.Equal(1, snapshot.TotalSentSuccess);
            Assert.Equal(0, snapshot.TotalSentFailed);
        }

        [Fact]
        public void Record_SentFailed_IncrementsSentFailed()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("trace-1", "Sent", "outputA", "Failed");

            var snapshot = tap.GetSnapshot();
            Assert.Equal(0, snapshot.TotalSentSuccess);
            Assert.Equal(1, snapshot.TotalSentFailed);
        }

        [Fact]
        public void Record_DeadLetter_IncrementsDeadLettered()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("trace-1", "DeadLetter", null, null);

            var snapshot = tap.GetSnapshot();
            Assert.Equal(1, snapshot.TotalDeadLettered);
        }

        [Fact]
        public void Record_SentSuccess_PopulatesOutputMessageCounts()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("t1", "Sent", "mqtt-out", "Success");
            tap.Record("t2", "Sent", "mqtt-out", "Success");
            tap.Record("t3", "Sent", "http-out", "Success");

            var snapshot = tap.GetSnapshot();
            Assert.Collection(snapshot.OutputMessageCounts.OrderBy(x => x.Key).ToList(),
                kv => { Assert.Equal("http-out", kv.Key); Assert.Equal(1, kv.Value); },
                kv => { Assert.Equal("mqtt-out", kv.Key); Assert.Equal(2, kv.Value); });
        }

        [Fact]
        public void Record_SentSuccess_WithNullOutputName_DoesNotThrow()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("t1", "Sent", null, "Success");

            var snapshot = tap.GetSnapshot();
            Assert.Equal(1, snapshot.TotalSentSuccess);
            Assert.Empty(snapshot.OutputMessageCounts);
        }

        [Fact]
        public void RecordTopic_IncrementsTopicCounts()
        {
            var tap = new InMemoryPipelineTap();
            tap.RecordTopic("sensor/temp");
            tap.RecordTopic("sensor/temp");
            tap.RecordTopic("sensor/humidity");

            var snapshot = tap.GetSnapshot();
            Assert.Equal(2, snapshot.TopicCounts["sensor/temp"]);
            Assert.Equal(1, snapshot.TopicCounts["sensor/humidity"]);
        }

        [Fact]
        public void RecordTopic_WithNullOrEmpty_DoesNothing()
        {
            var tap = new InMemoryPipelineTap();
            tap.RecordTopic(null);
            tap.RecordTopic("");

            var snapshot = tap.GetSnapshot();
            Assert.Empty(snapshot.TopicCounts);
        }

        [Fact]
        public void Reset_ZeroesAllCounters()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("t1", "Enqueued", null, null);
            tap.Record("t1", "Filtered", null, "Passed");
            tap.Record("t1", "Transformed", null, null);
            tap.Record("t1", "Routed", null, null);
            tap.Record("t1", "Sent", "out", "Success");
            tap.Record("t1", "DeadLetter", null, null);
            tap.RecordTopic("topic1");

            tap.Reset();

            var snapshot = tap.GetSnapshot();
            Assert.Equal(0, snapshot.TotalEnqueued);
            Assert.Equal(0, snapshot.TotalFilteredPassed);
            Assert.Equal(0, snapshot.TotalFilteredDropped);
            Assert.Equal(0, snapshot.TotalTransformed);
            Assert.Equal(0, snapshot.TotalRouted);
            Assert.Equal(0, snapshot.TotalSentSuccess);
            Assert.Equal(0, snapshot.TotalSentFailed);
            Assert.Equal(0, snapshot.TotalDeadLettered);
            Assert.Empty(snapshot.OutputMessageCounts);
            Assert.Empty(snapshot.TopicCounts);
        }

        [Fact]
        public void GetSnapshot_ReturnsClone_ModifyingReturnedDictDoesNotAffectInternal()
        {
            var tap = new InMemoryPipelineTap();
            tap.RecordTopic("topic1");

            var snapshot1 = tap.GetSnapshot();
            snapshot1.TopicCounts["topic1"] = 999;
            snapshot1.OutputMessageCounts["hack"] = 42;

            var snapshot2 = tap.GetSnapshot();
            Assert.Equal(1, snapshot2.TopicCounts["topic1"]);
            Assert.DoesNotContain("hack", snapshot2.OutputMessageCounts);
        }

        [Theory]
        [InlineData("UnknownStage")]
        [InlineData("enqueued")]
        [InlineData("SENT")]
        public void Record_UnknownStage_DoesNotIncrementAnyCounter(string stage)
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("t1", stage, null, null);

            var snapshot = tap.GetSnapshot();
            Assert.Equal(0, snapshot.TotalEnqueued);
            Assert.Equal(0, snapshot.TotalFilteredPassed);
            Assert.Equal(0, snapshot.TotalFilteredDropped);
            Assert.Equal(0, snapshot.TotalTransformed);
            Assert.Equal(0, snapshot.TotalRouted);
            Assert.Equal(0, snapshot.TotalSentSuccess);
            Assert.Equal(0, snapshot.TotalSentFailed);
            Assert.Equal(0, snapshot.TotalDeadLettered);
        }

        [Fact]
        public async Task Record_ConcurrentAccess_IsThreadSafe()
        {
            var tap = new InMemoryPipelineTap();
            int iterations = 1000;

            await Parallel.ForEachAsync(Enumerable.Range(0, iterations), async (i, ct) =>
            {
                tap.Record($"t-{i}", "Enqueued", null, null);
                tap.Record($"t-{i}", "Filtered", null, "Passed");
                tap.Record($"t-{i}", "Transformed", null, null);
                tap.Record($"t-{i}", "Routed", null, null);
                tap.Record($"t-{i}", "Sent", $"out-{i % 3}", "Success");
                tap.RecordTopic($"topic-{i % 5}");
            });

            var snapshot = tap.GetSnapshot();
            Assert.Equal(iterations, snapshot.TotalEnqueued);
            Assert.Equal(iterations, snapshot.TotalFilteredPassed);
            Assert.Equal(iterations, snapshot.TotalTransformed);
            Assert.Equal(iterations, snapshot.TotalRouted);
            Assert.Equal(iterations, snapshot.TotalSentSuccess);
            Assert.Equal(iterations, snapshot.OutputMessageCounts.Values.Sum());
            Assert.Equal(iterations, snapshot.TopicCounts.Values.Sum());
        }

        [Fact]
        public void Record_LatencyCalculation_AvgLatencyGreaterThanZero()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("t1", "Enqueued", null, null);
            Thread.Sleep(50); // 确保有可测延迟
            tap.Record("t1", "Sent", "out1", "Success");

            var snapshot = tap.GetSnapshot();
            Assert.True(snapshot.AvgLatencyMs >= 40, $"Expected latency >= 40ms but got {snapshot.AvgLatencyMs}ms");
            Assert.Equal(1, snapshot.LatencySampleCount);
            Assert.True(snapshot.TotalLatencySumMs >= 40);
        }

        [Fact]
        public void GetSnapshot_NoLatencySamples_ReturnsZeroAvg()
        {
            var tap = new InMemoryPipelineTap();
            var snapshot = tap.GetSnapshot();

            Assert.Equal(0.0, snapshot.AvgLatencyMs);
            Assert.Equal(0, snapshot.LatencySampleCount);
            Assert.Equal(0L, snapshot.TotalLatencySumMs);
        }

        [Fact]
        public void Record_FilteredWithUnknownResult_DoesNotIncrement()
        {
            var tap = new InMemoryPipelineTap();
            tap.Record("t1", "Filtered", null, "SomethingElse");

            var snapshot = tap.GetSnapshot();
            Assert.Equal(0, snapshot.TotalFilteredPassed);
            Assert.Equal(0, snapshot.TotalFilteredDropped);
        }
    }
}
