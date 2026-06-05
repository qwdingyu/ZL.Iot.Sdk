using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// Modbus TCP 适配器：封装 MBAP 头部与基础拆包逻辑。
    /// 默认 SendAsync 传入 PDU（功能码+数据），接收回调返回 UnitId+PDU。
    /// </summary>
    public sealed class ModbusTcpAdapter : IChannelAdapter
    {
        private readonly string _ip;
        private readonly int _port;
        private readonly bool _sendRawFrame;
        private readonly bool _emitRawFrame;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _readCts;
        private Task? _readTask;
        private int _transactionId;
        private volatile bool _isDisposed;
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(MaxFrameSize);
        private int _bufferCount;
        private readonly object _sync = new object();
        private const int MaxFrameSize = 512;

        public ModbusTcpAdapter(string ip, int port, byte unitId = 1, bool sendRawFrame = false, bool emitRawFrame = false)
        {
            if (string.IsNullOrWhiteSpace(ip)) throw new ArgumentNullException(nameof(ip));
            if (port <= 0) throw new ArgumentOutOfRangeException(nameof(port));
            _ip = ip;
            _port = port;
            UnitId = unitId;
            _sendRawFrame = sendRawFrame;
            _emitRawFrame = emitRawFrame;
        }

        public byte UnitId { get; set; }
        public string ChannelId => $"ModbusTCP:{_ip}:{_port}";
        public bool IsConnected => _client != null && _client.Connected && !_isDisposed;

        public event Action<byte[]>? OnDataReceived;

        public async Task OpenAsync(CancellationToken token)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ModbusTcpAdapter));
            CloseInternal();
            _client = new TcpClient();
            await _client.ConnectAsync(_ip, _port);
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
            if (_stream == null) throw new InvalidOperationException("Modbus TCP stream not open.");
            byte[] payload = _sendRawFrame ? data : BuildRequest(data, UnitId, NextTransactionId());
            await _stream.WriteAsync(payload, 0, payload.Length, token);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            CloseInternal();
            lock (_sync)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = ArrayPool<byte>.Shared.Rent(1);
                _bufferCount = 0;
            }
        }

        public static byte[] BuildRequest(byte[] pdu, byte unitId, ushort transactionId)
        {
            if (pdu == null) throw new ArgumentNullException(nameof(pdu));
            int length = pdu.Length + 1;
            var frame = new byte[7 + pdu.Length];
            frame[0] = (byte)(transactionId >> 8);
            frame[1] = (byte)(transactionId & 0xFF);
            frame[2] = 0x00;
            frame[3] = 0x00;
            frame[4] = (byte)(length >> 8);
            frame[5] = (byte)(length & 0xFF);
            frame[6] = unitId;
            Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);
            return frame;
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
                    AppendAndDispatch(buffer, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void AppendAndDispatch(byte[] data, int count)
        {
            lock (_sync)
            {
                if (_isDisposed) return;
                EnsureCapacity(_bufferCount + count);
                Buffer.BlockCopy(data, 0, _buffer, _bufferCount, count);
                _bufferCount += count;

                while (_bufferCount >= 7)
                {
                    int length = (_buffer[4] << 8) | _buffer[5];
                    if (length <= 0 || length > MaxFrameSize)
                    {
                        _bufferCount = 0;
                        return;
                    }
                    int frameLen = 6 + length;
                    if (_bufferCount < frameLen) break;

                    byte[] output;
                    if (_emitRawFrame)
                    {
                        output = new byte[frameLen];
                        Buffer.BlockCopy(_buffer, 0, output, 0, frameLen);
                    }
                    else
                    {
                        output = new byte[length];
                        Buffer.BlockCopy(_buffer, 6, output, 0, length);
                    }
                    int remaining = _bufferCount - frameLen;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(_buffer, frameLen, _buffer, 0, remaining);
                    }
                    _bufferCount = remaining;

                    OnDataReceived?.Invoke(output);
                }
            }
        }

        private ushort NextTransactionId()
        {
            int next = Interlocked.Increment(ref _transactionId);
            if ((next & 0xFFFF) == 0) next = Interlocked.Increment(ref _transactionId);
            return (ushort)(next & 0xFFFF);
        }

        private void CloseInternal()
        {
            try { _readCts?.Cancel(); } catch { }
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
            _readCts?.Dispose();
            _readCts = null;
            _readTask = null;
            lock (_sync)
            {
                _bufferCount = 0;
            }
        }

        private void EnsureCapacity(int required)
        {
            if (_buffer.Length >= required) return;
            if (required > MaxFrameSize * 2)
            {
                _bufferCount = 0;
                return;
            }
            int newSize = Math.Max(required, _buffer.Length * 2);
            byte[] bigger = ArrayPool<byte>.Shared.Rent(newSize);
            Buffer.BlockCopy(_buffer, 0, bigger, 0, _bufferCount);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = bigger;
        }
    }
}
