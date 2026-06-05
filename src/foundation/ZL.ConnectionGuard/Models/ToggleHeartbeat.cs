using System;

namespace ZL.ConnectionGuard.Models
{
    /// <summary>
    /// 0/1 跳变心跳策略，通过委托动态构建帧。
    /// </summary>
    public sealed class ToggleHeartbeat : IHeartbeatStrategy
    {
        private readonly Func<bool, byte[]> _frameBuilder;
        private readonly object _lock = new object();
        private bool _state;

        public ToggleHeartbeat(Func<bool, byte[]> frameBuilder)
        {
            _frameBuilder = frameBuilder ?? throw new ArgumentNullException(nameof(frameBuilder));
        }

        public byte[]? CreateHeartbeat()
        {
            lock (_lock)
            {
                _state = !_state;
                try
                {
                    return _frameBuilder(_state);
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
