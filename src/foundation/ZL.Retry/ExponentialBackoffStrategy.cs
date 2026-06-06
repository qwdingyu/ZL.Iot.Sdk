namespace ZL.Retry
{
    /// <summary>
    /// 指数退避重连策略
    /// <para>算法（参考 Eclipse Paho MQTT）：</para>
    /// <code>
    /// delay = min(baseDelay * 2^attempt + jitter, maxDelay)
    /// </code>
    /// <para>参数说明：</para>
    /// <list type="bullet">
    ///   <item>baseDelay: 基础延迟（默认 1000ms）</item>
    ///   <item>maxDelay: 最大延迟（默认 60000ms）</item>
    ///   <item>jitter: 随机抖动（±20%，防雷群效应）</item>
    ///   <item>maxRetries: 最大重试次数（-1 表示无限重试）</item>
    /// </list>
    /// <para>重连序列示例（baseDelay=1000ms, maxDelay=60000ms）：</para>
    /// <list type="bullet">
    ///   <item>第 1 次: ~1000ms ± 20%</item>
    ///   <item>第 2 次: ~2000ms ± 20%</item>
    ///   <item>第 3 次: ~4000ms ± 20%</item>
    ///   <item>第 4 次: ~8000ms ± 20%</item>
    ///   <item>第 5 次: ~16000ms ± 20%</item>
    ///   <item>第 6 次: ~32000ms ± 20%</item>
    ///   <item>第 7 次+: ~60000ms ± 20%（达到上限）</item>
    /// </list>
    /// </summary>
    public class ExponentialBackoffStrategy
    {
        private readonly Random _random = new();
        private readonly object _lock = new();

        /// <summary>
        /// 基础延迟（毫秒）
        /// </summary>
        public int BaseDelayMs { get; }

        /// <summary>
        /// 最大延迟（毫秒）
        /// </summary>
        public int MaxDelayMs { get; }

        /// <summary>
        /// 最大重试次数（-1 表示无限）
        /// </summary>
        public int MaxRetries { get; }

        /// <summary>
        /// 抖动因子（0.0-1.0，默认 0.2 即 ±20%）
        /// </summary>
        public double JitterFactor { get; }

        /// <summary>
        /// 当前重试次数
        /// </summary>
        public int CurrentAttempt { get; private set; }

        /// <summary>
        /// 创建指数退避策略
        /// </summary>
        /// <param name="baseDelayMs">基础延迟（毫秒），默认 1000</param>
        /// <param name="maxDelayMs">最大延迟（毫秒），默认 60000</param>
        /// <param name="maxRetries">最大重试次数，默认 -1（无限）</param>
        /// <param name="jitterFactor">抖动因子，默认 0.2（±20%）</param>
        public ExponentialBackoffStrategy(
            int baseDelayMs = 1000,
            int maxDelayMs = 60000,
            int maxRetries = -1,
            double jitterFactor = 0.2)
        {
            if (baseDelayMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseDelayMs), "基础延迟必须大于 0");
            }

            if (maxDelayMs < baseDelayMs)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDelayMs), "最大延迟不能小于基础延迟");
            }

            if (jitterFactor < 0.0 || jitterFactor > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(jitterFactor), "抖动因子必须在 0.0-1.0 范围内");
            }

            BaseDelayMs = baseDelayMs;
            MaxDelayMs = maxDelayMs;
            MaxRetries = maxRetries;
            JitterFactor = jitterFactor;
            CurrentAttempt = 0;
        }

        /// <summary>
        /// 获取下一次重连的延迟时间
        /// </summary>
        /// <returns>延迟时间（毫秒）</returns>
        public int GetNextDelayMs()
        {
            lock (_lock)
            {
                // 检查是否超过最大重试次数
                if (MaxRetries >= 0 && CurrentAttempt >= MaxRetries)
                {
                    return -1; // 表示不再重试
                }

                // 计算指数延迟：baseDelay * 2^attempt
                var exponentialDelay = BaseDelayMs * Math.Pow(2, CurrentAttempt);

                // 应用最大延迟限制
                var delay = Math.Min(exponentialDelay, MaxDelayMs);

                // 应用随机抖动（防雷群效应）
                var jitterRange = delay * JitterFactor;
                double jitter;
                lock (_random)
                {
                    jitter = (_random.NextDouble() * 2 - 1) * jitterRange; // [-jitterRange, +jitterRange]
                }

                var finalDelay = (int)Math.Round(delay + jitter);

                // 确保延迟不为负数且不小于基础延迟
                finalDelay = Math.Max(finalDelay, BaseDelayMs);

                CurrentAttempt++;

                return finalDelay;
            }
        }

        /// <summary>
        /// 获取下一次重连的延迟时间（TimeSpan）
        /// </summary>
        /// <returns>延迟时间，如果不再重试返回 null</returns>
        public TimeSpan? GetNextDelay()
        {
            var delayMs = GetNextDelayMs();
            return delayMs < 0 ? null : TimeSpan.FromMilliseconds(delayMs);
        }

        /// <summary>
        /// 重置重试计数器（连接成功后调用）
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                CurrentAttempt = 0;
            }
        }

        /// <summary>
        /// 检查是否还可以重试
        /// </summary>
        /// <returns>true 表示可以重试</returns>
        public bool CanRetry()
        {
            return MaxRetries < 0 || CurrentAttempt < MaxRetries;
        }

        /// <summary>
        /// 获取当前延迟（不增加计数器）
        /// </summary>
        /// <returns>当前延迟（毫秒）</returns>
        public int GetCurrentDelayMs()
        {
            lock (_lock)
            {
                var exponentialDelay = BaseDelayMs * Math.Pow(2, CurrentAttempt);
                var delay = Math.Min(exponentialDelay, MaxDelayMs);
                return (int)Math.Round(delay);
            }
        }

        public override string ToString()
        {
            return $"ExponentialBackoff(Base={BaseDelayMs}ms, Max={MaxDelayMs}ms, Attempt={CurrentAttempt}, MaxRetries={MaxRetries})";
        }
    }
}
