using System;

namespace ZL.Framing
{
    /// <summary>
    /// 会话生命周期事件接口。
    /// </summary>
    public interface ISessionLifecycleTransport
    {
        /// <summary>会话开始事件</summary>
        event Action<string> SessionStarted;

        /// <summary>会话结束事件</summary>
        event Action<string> SessionEnded;
    }
}
