using System;

namespace ZL.Framing
{
    /// <summary>
    /// 带会话 ID 的数据到达事件的传输接口。
    /// </summary>
    public interface ISessionByteTransport
    {
        /// <summary>
        /// 带会话 ID 的数据到达事件。
        /// </summary>
        event Action<byte[], string> DataReceivedSession;
    }
}
