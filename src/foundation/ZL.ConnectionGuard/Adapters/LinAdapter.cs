using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using ZL.ConnectionGuard.Models;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// LIN 适配器：通过驱动接口接入任意 LIN 硬件 SDK。
    /// </summary>
    public sealed class LinAdapter : IChannelAdapter
    {
        private readonly ILinDriver _driver;

        public LinAdapter(ILinDriver driver)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _driver.FrameReceived += OnDriverFrame;
        }

        public string ChannelId => _driver.ChannelId;
        public bool IsConnected => _driver.IsOpen;

        public event Action<byte[]>? OnDataReceived;

        public Task OpenAsync(CancellationToken token)
        {
            return _driver.OpenAsync(token);
        }

        public Task CloseAsync()
        {
            return _driver.CloseAsync();
        }

        public Task SendAsync(byte[] data, CancellationToken token)
        {
            var frame = LinFrameCodec.Decode(data);
            return _driver.SendAsync(frame, token);
        }

        public void Dispose()
        {
            _driver.Dispose();
        }

        private void OnDriverFrame(LinFrame frame)
        {
            var bytes = LinFrameCodec.Encode(frame);
            OnDataReceived?.Invoke(bytes);
        }
    }

    /// <summary>
    /// LIN 驱动接口：由具体硬件 SDK 实现。
    /// </summary>
    public interface ILinDriver : IDisposable
    {
        string ChannelId { get; }
        bool IsOpen { get; }
        Task OpenAsync(CancellationToken token);
        Task CloseAsync();
        Task SendAsync(LinFrame frame, CancellationToken token);
        event Action<LinFrame> FrameReceived;
    }

    /// <summary>
    /// LIN 帧编码：用于将 LIN 帧统一封装为 byte[]。
    /// 格式：Id(1) + Len(1) + Data(N)
    /// </summary>
    public static class LinFrameCodec
    {
        public static byte[] Encode(LinFrame frame)
        {
            int len = frame.Data.Length;
            int total = 2 + len;
            byte[] rented = ArrayPool<byte>.Shared.Rent(total);
            try
            {
                rented[0] = frame.Id;
                rented[1] = (byte)len;
                Buffer.BlockCopy(frame.Data, 0, rented, 2, len);
                byte[] result = new byte[total];
                Buffer.BlockCopy(rented, 0, result, 0, total);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static LinFrame Decode(byte[] data)
        {
            if (data == null || data.Length < 2) throw new ArgumentException("Invalid LIN frame.");
            byte id = data[0];
            int len = data[1];
            if (data.Length < 2 + len) throw new ArgumentException("Invalid LIN length.");
            var payload = new byte[len];
            Buffer.BlockCopy(data, 2, payload, 0, len);
            return new LinFrame(id, payload);
        }
    }
}
