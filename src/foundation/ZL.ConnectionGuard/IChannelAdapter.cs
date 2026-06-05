using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ConnectionGuard
{
    /// <summary>
    /// 通道适配器接口：屏蔽串口、TCP、HSL、CAN/LIN 等底层差异。
    /// 适配器只负责 IO，连接管理与重连由 ConnectionGuard 处理。
    /// </summary>
    public interface IChannelAdapter : IDisposable
    {
        /// <summary>
        /// 连接标识（例如 "COM3:9600" 或 "192.168.1.10:502"）。
        /// </summary>
        string ChannelId { get; }

        /// <summary>
        /// 当前物理连接是否打开。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 打开连接（失败需抛异常）。
        /// </summary>
        Task OpenAsync(CancellationToken token);

        /// <summary>
        /// 关闭连接。
        /// </summary>
        Task CloseAsync();

        /// <summary>
        /// 发送数据。
        /// </summary>
        Task SendAsync(byte[] data, CancellationToken token);

        /// <summary>
        /// 收到原始数据事件。
        /// </summary>
        event Action<byte[]> OnDataReceived;
    }
}
