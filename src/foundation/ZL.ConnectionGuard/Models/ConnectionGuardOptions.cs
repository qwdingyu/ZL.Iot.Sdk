using System;

namespace ZL.ConnectionGuard.Models
{
    public sealed class ConnectionGuardOptions
    {
        public int ReconnectMinDelayMs { get; set; } = 1000;
        public int ReconnectMaxDelayMs { get; set; } = 30000;
        // 重连抖动系数：0.2 表示最大抖动为当前退避时间的 20%。
        public double ReconnectJitterFactor { get; set; } = 0.2;
        public int HeartbeatIntervalMs { get; set; } = 3000;
        public int DeviceDeadTimeoutMs { get; set; } = 8000;
        [Obsolete("Use HeartbeatStrategy instead.")]
        public byte[]? HeartbeatData { get; set; }
        public IHeartbeatStrategy? HeartbeatStrategy { get; set; }
        public int SendLockTimeoutMs { get; set; } = 2000;
        // 发送超时：防止底层写阻塞导致锁长期占用。
        public int SendTimeoutMs { get; set; } = 3000;
        public int MaintenanceLoopDelayMs { get; set; } = 100;
        // 状态事件回调模式：同步或异步。
        public StateCallbackMode StateCallbackMode { get; set; } = StateCallbackMode.Async;
        // 看门狗启动条件：仅在发送过业务数据后才开始判定超时。
        public bool WatchdogRequiresSend { get; set; } = true;
        // 看门狗暖启动窗口：连接建立后的宽限期（毫秒）。
        public int WatchdogWarmupMs { get; set; } = 0;
    }
}
