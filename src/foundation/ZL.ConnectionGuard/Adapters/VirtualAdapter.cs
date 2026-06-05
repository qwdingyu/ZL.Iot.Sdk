using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// 仿真适配器：用于离线开发与测试，无需真实硬件。
    /// </summary>
    public sealed class VirtualAdapter : IChannelAdapter
    {
        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;
        private bool _connected;

        public string ChannelId => "Virtual:Loopback";
        public bool IsConnected => _connected;

        public event Action<byte[]>? OnDataReceived;

        public Task OpenAsync(CancellationToken token)
        {
            _connected = true;
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _loopTask = Task.Run(() => LoopAsync(_loopCts.Token), _loopCts.Token);
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            _connected = false;
            try { _loopCts?.Cancel(); } catch { }
            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] data, CancellationToken token)
        {
            // 默认回环：发送即收到，便于模拟上位机交互。
            if (_connected && data != null)
            {
                byte[] rented = ArrayPool<byte>.Shared.Rent(data.Length);
                try
                {
                    Buffer.BlockCopy(data, 0, rented, 0, data.Length);
                    byte[] echo = new byte[data.Length];
                    Buffer.BlockCopy(rented, 0, echo, 0, data.Length);
                    OnDataReceived?.Invoke(echo);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            CloseAsync();
            _loopCts?.Dispose();
        }

        private async Task LoopAsync(CancellationToken token)
        {
            // 可选：模拟设备定期上报心跳或状态。
            while (!token.IsCancellationRequested && _connected)
            {
                await Task.Delay(1000, token);
            }
        }
    }
}
