using System;
using ZL.Framing;

namespace ZL.Framing
{
    /// <summary>
    /// 字节传输层基础接口。
    /// </summary>
    public interface IByteTransport : IDisposable
    {
        /// <summary>资源名称（如设备/传输标识）</summary>
        string ResourceName { get; }

        /// <summary>是否已打开</summary>
        bool IsOpen { get; }

        /// <summary>打开传输</summary>
        void Open();

        /// <summary>关闭传输</summary>
        void Close();

        /// <summary>
        /// 发送字节数据。
        /// </summary>
        void Send(byte[] data);

        /// <summary>数据到达事件</summary>
        event Action<byte[]> DataReceived;

        /// <summary>帧状态变化事件</summary>
        event Action<FrameStatus> FrameStatusChanged;
    }
}
