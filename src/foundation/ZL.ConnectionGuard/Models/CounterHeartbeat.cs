using System;

namespace ZL.ConnectionGuard.Models
{
    /// <summary>
    /// 计数器心跳策略，0-255 循环递增。
    /// </summary>
    public sealed class CounterHeartbeat : IHeartbeatStrategy
    {
        private readonly Func<byte, byte[]>? _frameBuilder;
        private byte _counter;

        public CounterHeartbeat(Func<byte, byte[]>? frameBuilder = null)
        {
            _frameBuilder = frameBuilder;
        }

        public byte[]? CreateHeartbeat()
        {
            unchecked { _counter++; }
            try
            {
                return _frameBuilder == null ? new[] { _counter } : _frameBuilder(_counter);
            }
            catch
            {
                return null;
            }
        }
    }
}
