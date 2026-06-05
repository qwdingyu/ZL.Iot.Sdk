using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// UDP 适配器：支持指定远端地址与本地端口的无连接收发。
    /// </summary>
    public sealed class UdpClientAdapter : IChannelAdapter
    {
        private readonly IPEndPoint _remote;
        private readonly int _localPort;
        private UdpClient? _client;
        private CancellationTokenSource? _readCts;
        private Task? _readTask;

        public UdpClientAdapter(string remoteIp, int remotePort, int localPort = 0)
        {
            if (string.IsNullOrWhiteSpace(remoteIp)) throw new ArgumentNullException(nameof(remoteIp));
            if (remotePort <= 0) throw new ArgumentOutOfRangeException(nameof(remotePort));
            _remote = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
            _localPort = localPort;
        }

        public string ChannelId => $"UDP:{_remote.Address}:{_remote.Port}";
        public bool IsConnected => _client != null;

        public event Action<byte[]>? OnDataReceived;

        public Task OpenAsync(CancellationToken token)
        {
            return Task.Run(() =>
            {
                CloseInternal();
                _client = _localPort > 0 ? new UdpClient(_localPort) : new UdpClient();
                _client.Connect(_remote);

                _readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _readTask = Task.Run(() => ReceiveLoopAsync(_readCts.Token), _readCts.Token);
            }, token);
        }

        public Task CloseAsync()
        {
            return Task.Run(CloseInternal);
        }

        public Task SendAsync(byte[] data, CancellationToken token)
        {
            if (_client == null) throw new InvalidOperationException("UDP client not open.");
            return _client.SendAsync(data, data.Length);
        }

        public void Dispose()
        {
            CloseInternal();
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            if (_client == null) return;

            while (!token.IsCancellationRequested)
            {
                Task<UdpReceiveResult> receiveTask = _client.ReceiveAsync();
                Task completed = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, token));
                if (completed != receiveTask) break;

                try
                {
                    var result = receiveTask.Result;
                    if (result.Buffer.Length > 0)
                    {
                        byte[] data = ArrayPool<byte>.Shared.Rent(result.Buffer.Length);
                        try
                        {
                            Buffer.BlockCopy(result.Buffer, 0, data, 0, result.Buffer.Length);
                            byte[] packet = new byte[result.Buffer.Length];
                            Buffer.BlockCopy(data, 0, packet, 0, result.Buffer.Length);
                            OnDataReceived?.Invoke(packet);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(data);
                        }
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        private void CloseInternal()
        {
            try { _readCts?.Cancel(); } catch { }
            try { _client?.Close(); } catch { }
            _client = null;
            _readCts?.Dispose();
            _readCts = null;
            _readTask = null;
        }
    }
}
