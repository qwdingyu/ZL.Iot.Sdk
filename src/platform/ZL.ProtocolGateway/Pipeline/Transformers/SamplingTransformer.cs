using System;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 降采样 Transformer — 每隔 N 条消息才放行一条。
    /// 工业现场刚需：降低高频信号的数据量，减少网络带宽和存储压力。
    /// 
    /// 使用场景：
    /// - 传感器 100Hz 采样 → 只需 1Hz 存储
    /// - 调试模式下全量数据，生产模式下降采样
    /// </summary>
    public class SamplingTransformer
    {
        private readonly int _interval; // 每隔 N 条放行 1 条（1=全部放行，5=每5条放行1条）
        private long _counter; // 使用 long 避免 int 回绕（10K msg/s 下约 2920 年）

        public SamplingTransformer(int interval)
        {
            if (interval < 1) throw new ArgumentException("Interval must be >= 1", nameof(interval));
            _interval = interval;
        }

        /// <summary>
        /// 返回一个 Func&lt;Message, Task&lt;Message&gt;&gt;，可直接传入 pipeline.AddTransformer()。
        /// </summary>
        public Func<Message, Task<Message>> Build()
        {
            if (_interval == 1)
            {
                // 间隔为 1，全部放行，无需计数
                return async (message) => await Task.FromResult(message);
            }

            return async (message) =>
            {
                // P0 修复：使用 Interlocked.Increment 原子递增，避免多线程并发时计数器丢失。
                // Pipeline 使用 SemaphoreSlim(4) 并发处理消息，多个 Transformer 实例可能同时执行。
                long current = Interlocked.Increment(ref _counter);
                if (current % _interval == 0)
                {
                    return await Task.FromResult(message);
                }

                // 不是采样点，返回 null 让 Pipeline 丢弃
                return await Task.FromResult<Message>(null!);
            };
        }

        /// <summary>
        /// 重置计数器（重新开始计数）。
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _counter, 0);
        }
    }

    /// <summary>
    /// 时间间隔采样 Transformer — 每隔指定时间间隔才放行一条消息。
    /// 与 SamplingTransformer（按条数）互补：此类型按时间。
    /// 
    /// 使用场景：
    /// - "每 1 秒最多发一条"（不管消息多密集）
    /// - 配合 DeadbandTransformer 实现"变化超过阈值 OR 超时"策略
    /// </summary>
    public class TimeBasedSamplingTransformer
    {
        private readonly TimeSpan _interval;
        // P0 修复：使用 long (Ticks) + Interlocked 保证线程安全。
        // 原实现 DateTime _lastPassed 在并发读取/写入时存在数据竞争。
        private long _lastPassedTicks;

        public TimeBasedSamplingTransformer(TimeSpan interval)
        {
            if (interval.TotalMilliseconds < 1)
                throw new ArgumentException("Interval must be >= 1ms", nameof(interval));
            _interval = interval;
            _lastPassedTicks = DateTime.MinValue.Ticks;
        }

        /// <summary>
        /// 返回一个 Func&lt;Message, Task&lt;Message&gt;&gt;，可直接传入 pipeline.AddTransformer()。
        /// </summary>
        public Func<Message, Task<Message>> Build()
        {
            return async (message) =>
            {
                var now = message?.Timestamp ?? DateTime.UtcNow;
                long nowTicks = now.Ticks;
                long lastTicks = Volatile.Read(ref _lastPassedTicks);

                if (new TimeSpan(nowTicks - lastTicks) >= _interval)
                {
                    // 尝试原子更新：仅当 _lastPassedTicks 仍为 lastTicks 时写入 nowTicks
                    if (Interlocked.CompareExchange(ref _lastPassedTicks, nowTicks, lastTicks) == lastTicks)
                    {
                        return await Task.FromResult(message);
                    }
                }

                // 未到采样时间或被另一线程抢先，丢弃
                return await Task.FromResult<Message>(null!);
            };
        }

        /// <summary>
        /// 重置时间戳（重新开始计时）。
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _lastPassedTicks, DateTime.MinValue.Ticks);
        }
    }
}
