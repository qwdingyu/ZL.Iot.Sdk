namespace ZL.Connection
{
    /// <summary>
    /// 连接状态变更事件参数
    /// </summary>
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 之前的状态
        /// </summary>
        public ConnectionState PreviousState { get; }

        /// <summary>
        /// 当前状态
        /// </summary>
        public ConnectionState CurrentState { get; }

        /// <summary>
        /// 状态变更时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 错误信息（仅在 Error 状态时有值）
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// 异常对象（仅在 Error 状态时有值）
        /// </summary>
        public Exception? Exception { get; }

        /// <summary>
        /// 创建状态变更事件参数
        /// </summary>
        public ConnectionStateChangedEventArgs(
            ConnectionState previousState,
            ConnectionState currentState,
            string? errorMessage = null,
            Exception? exception = null)
        {
            PreviousState = previousState;
            CurrentState = currentState;
            Timestamp = DateTime.UtcNow;
            ErrorMessage = errorMessage;
            Exception = exception;
        }

        /// <summary>
        /// 返回状态变更的字符串表示
        /// </summary>
        public override string ToString()
        {
            var msg = $"{PreviousState} → {CurrentState} @ {Timestamp:HH:mm:ss.fff}";
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                msg += $" [{ErrorMessage}]";
            }
            return msg;
        }
    }
}
