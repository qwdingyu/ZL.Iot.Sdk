using ZL.Framing;

namespace ZL.Framing
{
    /// <summary>
    /// 带会话 ID 的发送接口。
    /// </summary>
    public interface ISessionSendByteTransport
    {
        /// <summary>
        /// 向指定会话发送字节数据。
        /// </summary>
        void Send(byte[] data, string sessionId);
    }
}
