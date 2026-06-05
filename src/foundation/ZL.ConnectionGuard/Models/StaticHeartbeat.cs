using System;

namespace ZL.ConnectionGuard.Models
{
    /// <summary>
    /// 固定值心跳策略。
    /// </summary>
    public sealed class StaticHeartbeat : IHeartbeatStrategy
    {
        private readonly byte[] _data;

        public StaticHeartbeat(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            _data = (byte[])data.Clone();
        }

        public byte[]? CreateHeartbeat()
        {
            return _data;
        }
    }
}
