using System;

namespace ZL.ConnectionGuard.Models
{
    /// <summary>
    /// 委托型心跳策略，允许每次动态生成心跳数据。
    /// </summary>
    public sealed class DelegateHeartbeat : IHeartbeatStrategy
    {
        private readonly Func<byte[]> _generator;

        public DelegateHeartbeat(Func<byte[]> generator)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        public byte[]? CreateHeartbeat()
        {
            try
            {
                return _generator();
            }
            catch
            {
                return null;
            }
        }
    }
}
