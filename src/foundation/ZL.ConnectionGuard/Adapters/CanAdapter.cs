using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using ZL.ConnectionGuard.Models;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// CAN 适配器：通过驱动接口接入任意 CAN 硬件 SDK。
    /// </summary>
    public sealed class CanAdapter : IChannelAdapter
    {
        private readonly ICanDriver _driver;

        public CanAdapter(ICanDriver driver)
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
            var frame = CanFrameCodec.Decode(data);
            return _driver.SendAsync(frame, token);
        }

        public void Dispose()
        {
            _driver.Dispose();
        }

        private void OnDriverFrame(CanFrame frame)
        {
            var bytes = CanFrameCodec.Encode(frame);
            OnDataReceived?.Invoke(bytes);
        }
    }

    /// <summary>
    /// CAN 驱动接口：由具体硬件 SDK 实现。
    /// </summary>
    public interface ICanDriver : IDisposable
    {
        string ChannelId { get; }
        bool IsOpen { get; }
        Task OpenAsync(CancellationToken token);
        Task CloseAsync();
        Task SendAsync(CanFrame frame, CancellationToken token);
        event Action<CanFrame> FrameReceived;
    }

    /// <summary>
    /// CAN 帧编码：用于将 CAN 帧统一封装为 byte[]。
    /// 格式：Id(4) + Flags(1) + Len(1) + Data(N)
    /// </summary>
    public static class CanFrameCodec
    {
        public static byte[] Encode(CanFrame frame)
        {
            int len = frame.Data.Length;
            int total = 6 + len;
            byte[] rented = ArrayPool<byte>.Shared.Rent(total);
            try
            {
                BitConverter.GetBytes(frame.Id).CopyTo(rented, 0);
                rented[4] = (byte)((frame.IsExtended ? 1 : 0) | (frame.IsRemote ? 2 : 0));
                rented[5] = (byte)len;
                Buffer.BlockCopy(frame.Data, 0, rented, 6, len);
                byte[] result = new byte[total];
                Buffer.BlockCopy(rented, 0, result, 0, total);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static CanFrame Decode(byte[] data)
        {
            if (data == null || data.Length < 6) throw new ArgumentException("Invalid CAN frame.");
            uint id = BitConverter.ToUInt32(data, 0);
            byte flags = data[4];
            int len = data[5];
            if (data.Length < 6 + len) throw new ArgumentException("Invalid CAN length.");
            var payload = new byte[len];
            Buffer.BlockCopy(data, 6, payload, 0, len);
            bool isExtended = (flags & 1) != 0;
            bool isRemote = (flags & 2) != 0;
            return new CanFrame(id, payload, isExtended, isRemote);
        }
    }
}
