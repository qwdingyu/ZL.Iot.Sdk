namespace ZL.ConnectionGuard.Models
{
    /// <summary>
    /// 心跳生成策略接口。
    /// </summary>
    public interface IHeartbeatStrategy
    {
        /// <summary>
        /// 生成心跳包数据。返回 null 或空数组表示本次不发送。
        /// </summary>
        byte[]? CreateHeartbeat();
    }
}
