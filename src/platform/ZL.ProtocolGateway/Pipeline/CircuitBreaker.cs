using System;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 断路器状态
    /// </summary>
    public enum CircuitBreakerState
    {
        Closed,     // 正常状态
        Open,       // 断路状态
        HalfOpen    // 半开状态（尝试恢复）
    }

    /// <summary>
    /// 断路器状态变更事件参数
    /// </summary>
    public class CircuitBreakerStateChangedArgs : EventArgs
    {
        /// <summary>输出插件名称</summary>
        public string OutputName { get; init; } = string.Empty;

        /// <summary>旧状态</summary>
        public CircuitBreakerState OldState { get; init; }

        /// <summary>新状态</summary>
        public CircuitBreakerState NewState { get; init; }

        /// <summary>当前连续失败次数</summary>
        public int FailureCount { get; init; }

        /// <summary>时间戳</summary>
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 断路器实现
    /// <para>Half-Open 状态只允许 1 个探测请求通过（_halfOpenProbeAllowed），防止并发探测导致误判</para>
    /// </summary>
    public class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly int _recoveryTimeMs;
        private int _failureCount;
        private DateTimeOffset _lastFailureTime;
        // volatile 保证 _state 跨线程可见性 — IsOpen 在 lock 内读写，
        // 但 GetState() 和诊断查询可能在锁外读取，需要 volatile 防止缓存不一致
        // 注意：enum 不能用 Volatile.Read<T>，直接读 volatile 字段即可
        private volatile CircuitBreakerState _state = CircuitBreakerState.Closed;
        /// <summary>Half-Open 探测令牌：仅第一个请求可消费，其余请求被拒绝</summary>
        private bool _halfOpenProbeAllowed;
        private readonly object _lock = new();

        /// <summary>断路器名称（用于事件通知）</summary>
        private readonly string _name;

        /// <summary>断路器状态变更事件</summary>
        public event Action<CircuitBreakerStateChangedArgs>? StateChanged;

        public CircuitBreaker(int failureThreshold, int recoveryTimeMs, string name = "")
        {
            _failureThreshold = failureThreshold;
            _recoveryTimeMs = recoveryTimeMs;
            _name = name;
        }

        private void OnStateChanged(CircuitBreakerState oldState, CircuitBreakerState newState)
        {
            if (oldState == newState) return;
            var handler = StateChanged;
            handler?.Invoke(new CircuitBreakerStateChangedArgs
            {
                OutputName = _name,
                OldState = oldState,
                NewState = newState,
                FailureCount = _failureCount
            });
        }

        /// <summary>
        /// 检查请求是否被断路器阻止。返回 true 表示请求被拒绝（Open），false 表示允许通过。
        /// </summary>
        public bool IsRequestBlocked()
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Open)
                {
                    // 检查是否可以尝试恢复
                    if ((DateTimeOffset.UtcNow - _lastFailureTime).TotalMilliseconds >= _recoveryTimeMs)
                    {
                        var oldState = _state;
                        _state = CircuitBreakerState.HalfOpen;
                        _halfOpenProbeAllowed = true;
                        OnStateChanged(oldState, _state);
                        return false; // 允许探测请求通过
                    }
                    return true; // 仍为 Open，拒绝
                }

                if (_state == CircuitBreakerState.HalfOpen)
                {
                    // 只允许一个探测请求通过
                    if (_halfOpenProbeAllowed)
                    {
                        _halfOpenProbeAllowed = false;
                        return false; // 允许通过
                    }
                    return true; // 其他请求在探测完成前被拒绝
                }

                return false; // Closed，允许通过
            }
        }

        [Obsolete("TryAcceptRequest 命名语义反直觉（返回 true 表示被阻止而非接受）。请使用 IsRequestBlocked()，其返回 true 表示断路器打开/请求被拒绝。", error: true)]
        public bool TryAcceptRequest() => IsRequestBlocked();

        /// <summary>
        /// 断路器是否处于打开状态（诊断用，不触发状态转换）。
        /// <para>注意：此属性可能触发 Open→HalfOpen 转换，请使用 IsRequestBlocked() 代替。</para>
        /// </summary>
        [Obsolete("IsOpen 属性有副作用（触发 Half-Open 状态转换），请使用 GetState() 或者 IsRequestBlocked() 代替。", error: true)]
        public bool IsOpen => IsRequestBlocked();

        /// <summary>
        /// 获取当前状态（诊断用，不触发状态转换）
        /// </summary>
        public CircuitBreakerState GetState() => _state;

        /// <summary>
        /// 获取当前连续失败次数
        /// </summary>
        public int FailureCount
        {
            get
            {
                lock (_lock)
                {
                    return _failureCount;
                }
            }
        }

        /// <summary>
        /// 记录一次成功的操作
        /// </summary>
        public void RecordSuccess()
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.HalfOpen)
                {
                    // 探测成功，关闭断路器
                    var oldState = _state;
                    _state = CircuitBreakerState.Closed;
                    _failureCount = 0;
                    _halfOpenProbeAllowed = false;
                    OnStateChanged(oldState, _state);
                }
                else if (_state == CircuitBreakerState.Closed)
                {
                    // 重置失败计数
                    _failureCount = 0;
                }
                // Open 状态下不应调用 RecordSuccess（IsOpen 会阻止请求）
            }
        }

        /// <summary>
        /// 记录一次失败的操作
        /// </summary>
        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTimeOffset.UtcNow;

                if (_state == CircuitBreakerState.HalfOpen)
                {
                    // 探测失败，重新打开断路器
                    var oldState = _state;
                    _state = CircuitBreakerState.Open;
                    _halfOpenProbeAllowed = false;
                    OnStateChanged(oldState, _state);
                }
                else if (_state == CircuitBreakerState.Closed && _failureCount >= _failureThreshold)
                {
                    // 达到失败阈值，打开断路器
                    var oldState = _state;
                    _state = CircuitBreakerState.Open;
                    OnStateChanged(oldState, _state);
                }
            }
        }

        /// <summary>
        /// 重置断路器到关闭状态
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                if (_state != CircuitBreakerState.Closed)
                {
                    var oldState = _state;
                    _state = CircuitBreakerState.Closed;
                    _halfOpenProbeAllowed = false;
                    OnStateChanged(oldState, _state);
                }
                // 始终清零失败计数，即使当前已是 Closed 状态
                _failureCount = 0;
            }
        }
    }
}
