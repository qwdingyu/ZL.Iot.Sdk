using System;

namespace ZL.Watchdog
{
    /// <summary>
    /// Watchdog 事件基类。
    /// </summary>
    public class WatchdogEventArgs : EventArgs
    {
        /// <summary>组件名称</summary>
        public string Name { get; }

        /// <summary>事件发生时间（UTC）</summary>
        public DateTime Timestamp { get; }

        public WatchdogEventArgs(string name)
        {
            Name = name;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 健康检查失败事件。
    /// </summary>
    public class HealthCheckFailedEventArgs : WatchdogEventArgs
    {
        /// <summary>是否触发了重启</summary>
        public bool RestartTriggered { get; }

        /// <summary>未重启原因（RestartTriggered=false 时有值）</summary>
        public string SkipReason { get; }

        public HealthCheckFailedEventArgs(string name, bool restartTriggered, string skipReason = null)
            : base(name)
        {
            RestartTriggered = restartTriggered;
            SkipReason = skipReason;
        }
    }

    /// <summary>
    /// 重启完成事件。
    /// </summary>
    public class RestartedEventArgs : WatchdogEventArgs
    {
        /// <summary>重启是否成功</summary>
        public bool Success { get; }

        /// <summary>窗口内累计重启次数</summary>
        public int RestartCount { get; }

        public RestartedEventArgs(string name, bool success, int restartCount)
            : base(name)
        {
            Success = success;
            RestartCount = restartCount;
        }
    }
}
