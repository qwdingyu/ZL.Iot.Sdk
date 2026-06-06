// ============================================================
// 文件：InMemoryPipelineTap.cs
// 描述：Pipeline Tap 内存实现 — 记录消息在各阶段的流转统计，用于协议分析
// 来源：配合 ResilientMessagePipeline.GetTapSnapshot() 使用
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 内存 Pipeline Tap — 无锁计数器实现，用于协议分析。
    /// 通过 Interlocked 操作记录消息在各阶段的流转统计。
    /// </summary>
    internal class InMemoryPipelineTap
    {
        private long _totalEnqueued;
        private long _totalFilteredPassed;
        private long _totalFilteredDropped;
        private long _totalTransformed;
        private long _totalRouted;
        private long _totalSentSuccess;
        private long _totalSentFailed;
        private long _totalDeadLettered;
        private long _totalLatencySumMs;
        private long _latencySampleCount;

        private readonly ConcurrentDictionary<string, long> _outputMessageCounts = new();
        private readonly ConcurrentDictionary<string, long> _topicCounts = new();

        private readonly Dictionary<string, long> _enqueueTimestamps = new();
        private readonly object _enqueueLock = new();

        /// <summary>
        /// 记录一个 Tap 事件。
        /// </summary>
        public void Record(string traceId, string stage, string? outputName, string? result)
        {
            switch (stage)
            {
                case "Enqueued":
                    Interlocked.Increment(ref _totalEnqueued);
                    lock (_enqueueLock)
                    {
                        _enqueueTimestamps[traceId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    }
                    break;

                case "Filtered":
                    if (result == "Passed")
                        Interlocked.Increment(ref _totalFilteredPassed);
                    else if (result == "Dropped")
                        Interlocked.Increment(ref _totalFilteredDropped);
                    break;

                case "Transformed":
                    Interlocked.Increment(ref _totalTransformed);
                    break;

                case "Routed":
                    Interlocked.Increment(ref _totalRouted);
                    break;

                case "Sent":
                    if (result == "Success")
                    {
                        Interlocked.Increment(ref _totalSentSuccess);
                        if (!string.IsNullOrEmpty(outputName))
                            _outputMessageCounts.AddOrUpdate(outputName, 1, (_, v) => v + 1);

                        // 计算延迟
                        lock (_enqueueLock)
                        {
                            if (_enqueueTimestamps.TryGetValue(traceId, out var ts))
                            {
                                var latency = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts;
                                Interlocked.Add(ref _totalLatencySumMs, latency);
                                Interlocked.Increment(ref _latencySampleCount);
                                _enqueueTimestamps.Remove(traceId);
                            }
                        }
                    }
                    else if (result == "Failed")
                    {
                        Interlocked.Increment(ref _totalSentFailed);
                    }
                    break;

                case "DeadLetter":
                    Interlocked.Increment(ref _totalDeadLettered);
                    break;
            }
        }

        /// <summary>
        /// 记录消息主题（从 ProcessMessageCore 调用）。
        /// </summary>
        public void RecordTopic(string topic)
        {
            if (!string.IsNullOrEmpty(topic))
                _topicCounts.AddOrUpdate(topic, 1, (_, v) => v + 1);
        }

        /// <summary>
        /// 获取快照。
        /// </summary>
        public ResilientMessagePipeline.PipelineTapSnapshot GetSnapshot()
        {
            var avgLatency = 0.0;
            var latencySum = Interlocked.Read(ref _totalLatencySumMs);
            var latencyCount = Interlocked.Read(ref _latencySampleCount);
            if (latencyCount > 0)
                avgLatency = (double)latencySum / latencyCount;

            return new ResilientMessagePipeline.PipelineTapSnapshot
            {
                TotalEnqueued = (int)Interlocked.Read(ref _totalEnqueued),
                TotalFilteredPassed = (int)Interlocked.Read(ref _totalFilteredPassed),
                TotalFilteredDropped = (int)Interlocked.Read(ref _totalFilteredDropped),
                TotalTransformed = (int)Interlocked.Read(ref _totalTransformed),
                TotalRouted = (int)Interlocked.Read(ref _totalRouted),
                TotalSentSuccess = (int)Interlocked.Read(ref _totalSentSuccess),
                TotalSentFailed = (int)Interlocked.Read(ref _totalSentFailed),
                TotalDeadLettered = (int)Interlocked.Read(ref _totalDeadLettered),
                OutputMessageCounts = CloneDict(_outputMessageCounts),
                TopicCounts = CloneDict(_topicCounts),
                AvgLatencyMs = avgLatency,
                TotalLatencySumMs = latencySum,
                LatencySampleCount = (int)latencyCount,
                CapturedAt = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// 重置所有计数器。
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _totalEnqueued, 0);
            Interlocked.Exchange(ref _totalFilteredPassed, 0);
            Interlocked.Exchange(ref _totalFilteredDropped, 0);
            Interlocked.Exchange(ref _totalTransformed, 0);
            Interlocked.Exchange(ref _totalRouted, 0);
            Interlocked.Exchange(ref _totalSentSuccess, 0);
            Interlocked.Exchange(ref _totalSentFailed, 0);
            Interlocked.Exchange(ref _totalDeadLettered, 0);
            Interlocked.Exchange(ref _totalLatencySumMs, 0);
            Interlocked.Exchange(ref _latencySampleCount, 0);

            _outputMessageCounts.Clear();
            _topicCounts.Clear();
            lock (_enqueueLock)
            {
                _enqueueTimestamps.Clear();
            }
        }

        private static Dictionary<string, int> CloneDict(ConcurrentDictionary<string, long> source)
        {
            var result = new Dictionary<string, int>();
            foreach (var kv in source)
            {
                result[kv.Key] = (int)kv.Value;
            }
            return result;
        }
    }
}
