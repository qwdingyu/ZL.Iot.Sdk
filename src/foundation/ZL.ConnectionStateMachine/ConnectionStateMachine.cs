namespace ZL.ConnectionStateMachine
{
    /// <summary>
    /// 连接状态机
    /// <para>管理连接生命周期的状态转换，确保状态转换的合法性和可预测性。</para>
    /// <para>状态转换图：</para>
    /// <code>
    /// Disconnected ──[Connect]──> Connecting ──[Success]──> Connected
    ///     ▲                           │                          │
    ///     │                           │[Timeout/Error]           │[Disconnect]
    ///     │                           ▼                          │
    ///     └─────────────────────  Error  <──────────────────────┘
    ///                                │
    ///                                │[AutoReconnect]
    ///                                ▼
    ///                           Reconnecting ──[Success]──> Connected
    ///                                │
    ///                                │[MaxRetries]
    ///                                ▼
    ///                             Error
    /// </code>
    /// </summary>
    public class ConnectionStateMachine
    {
        private readonly object _lock = new();
        private readonly Dictionary<(ConnectionState From, ConnectionState To), Func<bool>> _transitions;

        /// <summary>
        /// 当前状态
        /// </summary>
        public ConnectionState CurrentState { get; private set; }

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// 创建状态机
        /// </summary>
        /// <param name="initialState">初始状态，默认 Disconnected</param>
        public ConnectionStateMachine(ConnectionState initialState = ConnectionState.Disconnected)
        {
            CurrentState = initialState;

            // 定义合法的状态转换和守卫条件
            _transitions = new Dictionary<(ConnectionState, ConnectionState), Func<bool>>
            {
                // 从 Disconnected 出发
                [(ConnectionState.Disconnected, ConnectionState.Connecting)] = () => true,
                [(ConnectionState.Disconnected, ConnectionState.Connected)] = () => false, // 不能直接跳到 Connected
                [(ConnectionState.Disconnected, ConnectionState.Reconnecting)] = () => false, // 必须先 Connecting
                [(ConnectionState.Disconnected, ConnectionState.Error)] = () => true,

                // 从 Connecting 出发
                [(ConnectionState.Connecting, ConnectionState.Connected)] = () => true,
                [(ConnectionState.Connecting, ConnectionState.Error)] = () => true,
                [(ConnectionState.Connecting, ConnectionState.Disconnected)] = () => true, // 取消连接
                [(ConnectionState.Connecting, ConnectionState.Reconnecting)] = () => false, // 不能直接重连

                // 从 Connected 出发
                [(ConnectionState.Connected, ConnectionState.Disconnected)] = () => true,
                [(ConnectionState.Connected, ConnectionState.Reconnecting)] = () => true, // 连接断开，自动重连
                [(ConnectionState.Connected, ConnectionState.Error)] = () => true,
                [(ConnectionState.Connected, ConnectionState.Connecting)] = () => false, // 已连接不能再次连接

                // 从 Reconnecting 出发
                [(ConnectionState.Reconnecting, ConnectionState.Connected)] = () => true,
                [(ConnectionState.Reconnecting, ConnectionState.Error)] = () => true, // 重连失败
                [(ConnectionState.Reconnecting, ConnectionState.Disconnected)] = () => true, // 手动停止
                [(ConnectionState.Reconnecting, ConnectionState.Connecting)] = () => false, // 不能重新连接

                // 从 Error 出发
                [(ConnectionState.Error, ConnectionState.Connecting)] = () => true, // 手动重试
                [(ConnectionState.Error, ConnectionState.Disconnected)] = () => true, // 重置
                [(ConnectionState.Error, ConnectionState.Connected)] = () => false, // 不能直接连接
                [(ConnectionState.Error, ConnectionState.Reconnecting)] = () => false, // 必须先 Connecting
            };
        }

        /// <summary>
        /// 尝试转换到目标状态
        /// </summary>
        /// <param name="targetState">目标状态</param>
        /// <param name="errorMessage">错误信息（仅在 Error 状态时使用）</param>
        /// <param name="exception">异常对象（仅在 Error 状态时使用）</param>
        /// <returns>true 表示转换成功</returns>
        public bool TryTransition(ConnectionState targetState, string? errorMessage = null, Exception? exception = null)
        {
            lock (_lock)
            {
                var fromState = CurrentState;

                // 检查是否是合法转换
                if (!_transitions.TryGetValue((fromState, targetState), out var guard))
                {
                    return false;
                }

                // 检查守卫条件
                if (!guard())
                {
                    return false;
                }

                // 执行转换
                CurrentState = targetState;

                // 触发事件
                var args = new ConnectionStateChangedEventArgs(fromState, targetState, errorMessage, exception);
                StateChanged?.Invoke(this, args);

                return true;
            }
        }

        /// <summary>
        /// 强制转换到目标状态（跳过守卫检查）
        /// <para>仅在特殊情况下使用，如紧急停止</para>
        /// </summary>
        /// <param name="targetState">目标状态</param>
        /// <param name="errorMessage">错误信息</param>
        /// <param name="exception">异常对象</param>
        public void ForceTransition(ConnectionState targetState, string? errorMessage = null, Exception? exception = null)
        {
            lock (_lock)
            {
                var fromState = CurrentState;
                CurrentState = targetState;

                var args = new ConnectionStateChangedEventArgs(fromState, targetState, errorMessage, exception);
                StateChanged?.Invoke(this, args);
            }
        }

        /// <summary>
        /// 检查是否可以转换到目标状态
        /// </summary>
        /// <param name="targetState">目标状态</param>
        /// <returns>true 表示可以转换</returns>
        public bool CanTransition(ConnectionState targetState)
        {
            lock (_lock)
            {
                if (!_transitions.TryGetValue((CurrentState, targetState), out var guard))
                {
                    return false;
                }

                return guard();
            }
        }

        /// <summary>
        /// 获取从当前状态可以转换到的所有状态
        /// </summary>
        /// <returns>目标状态集合</returns>
        public IReadOnlyCollection<ConnectionState> GetAvailableTransitions()
        {
            lock (_lock)
            {
                var result = new List<ConnectionState>();

                foreach (var key in _transitions.Keys)
                {
                    if (key.From == CurrentState && _transitions[key]())
                    {
                        result.Add(key.To);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 重置状态机到初始状态
        /// </summary>
        public void Reset()
        {
            ForceTransition(ConnectionState.Disconnected);
        }

        public override string ToString()
        {
            return $"StateMachine(Current={CurrentState}, Available=[{string.Join(", ", GetAvailableTransitions())}])";
        }
    }
}
