using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// TCP 服务端适配器：监听端口并接受单个客户端连接。
    /// </summary>
    public sealed class TcpServerAdapter : IChannelAdapter
    {
        private readonly int _port;
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _readCts;
        private Task? _readTask;

        public TcpServerAdapter(int port)
        {
            if (port <= 0) throw new ArgumentOutOfRangeException(nameof(port));
            _port = port;
        }

        public string ChannelId => $"TCP-Server:{_port}";
        public bool IsConnected => _client != null && _client.Connected;

        public event Action<byte[]>? OnDataReceived;

        public async Task OpenAsync(CancellationToken token)
        {
            CloseInternal();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            _client = await _listener.AcceptTcpClientAsync(token);
            _stream = _client.GetStream();

            _readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);
        }

        public Task CloseAsync()
        {
            return Task.Run(CloseInternal);
        }

        public async Task SendAsync(byte[] data, CancellationToken token)
        {
            if (_stream == null) throw new InvalidOperationException("TCP client not connected.");
            await _stream.WriteAsync(data, 0, data.Length, token);
        }

        public void Dispose()
        {
            CloseInternal();
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            if (_stream == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

            try
            {
                while (!token.IsCancellationRequested && _client != null && _client.Connected)
                {
                    int read;
                    try
                    {
                        read = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
                    }
                    catch
                    {
                        break;
                    }

                    if (read <= 0) break;
                    var data = new byte[read];
                    Buffer.BlockCopy(buffer, 0, data, 0, read);
                    OnDataReceived?.Invoke(data);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void CloseInternal()
        {
            try { _readCts?.Cancel(); } catch { }
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }

            _stream = null;
            _client = null;
            _listener = null;

            _readCts?.Dispose();
            _readCts = null;
            _readTask = null;
        }
    }
}
