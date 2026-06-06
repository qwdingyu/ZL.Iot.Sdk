using System;
using System.Collections.Generic;
using System.Threading;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 流水线指标收集器 — 滑动窗口记录延迟和计数器
    /// </summary>
    public class PipelineMetricsCollector
    {
        private long _totalProcessed;
        private long _totalFiltered;
        private long _totalFailed;
        private long _totalDeadLetters;
        private long _totalDropped;
        private long _totalRateLimited;
        private long _totalBackpressureWarnings;
        private readonly double[] _latencyWindow;
        private int _latencyIndex;
        private readonly object _latencyLock = new();

        private const int LatencyWindowSize = 100;

        public PipelineMetricsCollector()
        {
            _latencyWindow = new double[LatencyWindowSize];
        }

        public void RecordSuccess(double latencyMs)
        {
            Interlocked.Increment(ref _totalProcessed);
            RecordLatency(latencyMs);
        }

        public void RecordFailure()
        {
            Interlocked.Increment(ref _totalFailed);
        }

        public void RecordFiltered()
        {
            Interlocked.Increment(ref _totalFiltered);
        }

        public void RecordDeadLetter()
        {
            Interlocked.Increment(ref _totalDeadLetters);
        }

        /// <summary>
        /// 记录因队列满而被 DropOldest 策略丢弃的消息。
        /// 工业场景下静默丢包是大忌，此计数器用于告警。
        /// </summary>
        public void RecordDropped()
        {
            Interlocked.Increment(ref _totalDropped);
        }

        /// <summary>
        /// 记录背压事件 — 消息入队等待超过阈值时间（队列满、消费慢）。
        /// 与 RecordDropped 不同，此方法记录的是"慢"而非"丢"。
        /// </summary>
        public void RecordBackpressureWarning()
        {
            Interlocked.Increment(ref _totalBackpressureWarnings);
        }

        /// <summary>
        /// 记录因限流而被丢弃的消息数。
        /// 与 RecordDropped（队列满丢弃）不同，此方法记录的是限流器拒绝的消息。
        /// </summary>
        public void RecordRateLimited()
        {
            Interlocked.Increment(ref _totalRateLimited);
        }

        private void RecordLatency(double latencyMs)
        {
            lock (_latencyLock)
            {
                _latencyWindow[_latencyIndex] = latencyMs;
                _latencyIndex = (_latencyIndex + 1) % LatencyWindowSize;
            }
        }

        public PipelineMetricsSnapshot GetSnapshot()
        {
            // 使用 stackalloc 避免每次调用分配 double[100] 堆对象
            Span<double> sortedBuffer = stackalloc double[LatencyWindowSize];

            int count;
            lock (_latencyLock)
            {
                count = 0;
                for (int i = 0; i < LatencyWindowSize; i++)
                {
                    double v = _latencyWindow[i];
                    if (v > 0)
                    {
                        sortedBuffer[count++] = v;
                    }
                }
            }

            if (count == 0)
            {
                return new PipelineMetricsSnapshot
                {
                    TotalProcessed = Volatile.Read(ref _totalProcessed),
                    TotalFiltered = Volatile.Read(ref _totalFiltered),
                    TotalFailed = Volatile.Read(ref _totalFailed),
                    TotalDeadLetters = Volatile.Read(ref _totalDeadLetters),
                    TotalDropped = Volatile.Read(ref _totalDropped),
                    TotalBackpressureWarnings = Volatile.Read(ref _totalBackpressureWarnings),
                    TotalRateLimited = Volatile.Read(ref _totalRateLimited)
                };
            }

            // 只排序有效数据
            sortedBuffer[..count].Sort();

            double sum = 0;
            for (int i = 0; i < count; i++) sum += sortedBuffer[i];
            double avg = sum / count;

            return new PipelineMetricsSnapshot
            {
                TotalProcessed = Volatile.Read(ref _totalProcessed),
                TotalFiltered = Volatile.Read(ref _totalFiltered),
                TotalFailed = Volatile.Read(ref _totalFailed),
                TotalDeadLetters = Volatile.Read(ref _totalDeadLetters),
                TotalDropped = Volatile.Read(ref _totalDropped),
                TotalBackpressureWarnings = Volatile.Read(ref _totalBackpressureWarnings),
                TotalRateLimited = Volatile.Read(ref _totalRateLimited),
                LatencyCount = count,
                LatencyAvgMs = avg,
                LatencyP50Ms = sortedBuffer[(int)(count * 0.5)],
                LatencyP95Ms = sortedBuffer[Math.Min((int)(count * 0.95), count - 1)],
                LatencyP99Ms = sortedBuffer[Math.Min((int)(count * 0.99), count - 1)],
                LatencyMaxMs = sortedBuffer[count - 1]
            };
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalProcessed, 0);
            Interlocked.Exchange(ref _totalFiltered, 0);
            Interlocked.Exchange(ref _totalFailed, 0);
            Interlocked.Exchange(ref _totalDeadLetters, 0);
            Interlocked.Exchange(ref _totalDropped, 0);
            Interlocked.Exchange(ref _totalBackpressureWarnings, 0);
            Interlocked.Exchange(ref _totalRateLimited, 0);
            lock (_latencyLock)
            {
                Array.Clear(_latencyWindow);
                _latencyIndex = 0;
            }
        }
    }

    /// <summary>
    /// 流水线指标快照
    /// </summary>
    public class PipelineMetricsSnapshot
    {
        public long TotalProcessed { get; set; }
        public long TotalFiltered { get; set; }
        public long TotalFailed { get; set; }
        public long TotalDeadLetters { get; set; }
        /// <summary>累计因队列满而被丢弃的消息数</summary>
        public long TotalDropped { get; set; }
        /// <summary>累计背压告警次数（入队等待超过阈值）</summary>
        public long TotalBackpressureWarnings { get; set; }
        /// <summary>累计因限流而被丢弃的消息数</summary>
        public long TotalRateLimited { get; set; }
        public int LatencyCount { get; set; }
        public double LatencyAvgMs { get; set; }
        public double LatencyP50Ms { get; set; }
        public double LatencyP95Ms { get; set; }
        public double LatencyP99Ms { get; set; }
        public double LatencyMaxMs { get; set; }
    }
}
