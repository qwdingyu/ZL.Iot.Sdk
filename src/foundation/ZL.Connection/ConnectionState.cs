namespace ZL.Connection
{
    /// <summary>
    /// 连接状态枚举
    /// <para>状态转换规则：</para>
    /// <list type="bullet">
    ///   <item>Disconnected → Connecting (发起连接)</item>
    ///   <item>Connecting → Connected (连接成功)</item>
    ///   <item>Connecting → Error (连接失败/超时)</item>
    ///   <item>Connected → Disconnected (主动断开)</item>
    ///   <item>Connected → Reconnecting (连接断开，自动重连)</item>
    ///   <item>Connected → Error (协议错误)</item>
    ///   <item>Connected → Faulted (致命故障，不可自动恢复)</item>
    ///   <item>Reconnecting → Connected (重连成功)</item>
    ///   <item>Reconnecting → Error (重连失败，超过最大重试次数)</item>
    ///   <item>Error → Connecting (手动重试)</item>
    ///   <item>Error → Disconnected (重置)</item>
    ///   <item>Faulted → Disconnected (仅能重置)</item>
    /// </list>
    /// <para>Error 与 Faulted 的区别：</para>
    /// <list type="bullet">
    ///   <item>Error — 可恢复错误（超时、临时网络故障），可手动重试</item>
    ///   <item>Faulted — 致命故障（硬件故障、协议不兼容），只能重置后重建</item>
    /// </list>
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>
        /// 未连接 - 初始状态或已主动断开
        /// </summary>
        Disconnected = 0,

        /// <summary>
        /// 连接中 - 正在建立连接
        /// </summary>
        Connecting = 1,

        /// <summary>
        /// 已连接 - 连接正常，可以进行读写操作
        /// </summary>
        Connected = 2,

        /// <summary>
        /// 重连中 - 连接断开，正在自动重连
        /// </summary>
        Reconnecting = 3,

        /// <summary>
        /// 错误 - 可恢复错误（超时、临时网络故障），可手动重试
        /// </summary>
        Error = 4,

        /// <summary>
        /// 致命故障 - 不可自动恢复（硬件故障、协议不兼容），只能重置后重建
        /// 与 ConnectionGuard 的 GuardState.Faulted 对应
        /// </summary>
        Faulted = 5
    }
}
