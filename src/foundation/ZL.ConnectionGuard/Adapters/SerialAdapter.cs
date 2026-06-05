using System;
using System.Buffers;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// 串口适配器：仅负责打开/关闭/收发，不包含重连逻辑。
    /// </summary>
    public sealed class SerialAdapter : IChannelAdapter
    {
        private SerialPort? _port;
        private CancellationTokenSource? _readCts;
        private Task? _readTask;

        public SerialAdapter(string portName, int baudRate, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentNullException(nameof(portName));
            if (baudRate <= 0) throw new ArgumentOutOfRangeException(nameof(baudRate));

            PortName = portName;
            BaudRate = baudRate;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
        }

        public string PortName { get; }
        public int BaudRate { get; }
        public Parity Parity { get; }
        public int DataBits { get; }
        public StopBits StopBits { get; }

        public string ChannelId => $"Serial:{PortName}/{BaudRate}";
        public bool IsConnected => _port != null && _port.IsOpen;

        public event Action<byte[]>? OnDataReceived;

        public Task OpenAsync(CancellationToken token)
        {
            return Task.Run(() =>
            {
                CloseInternal();

                _port = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                _port.Open();
                _readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);
            }, token);
        }

        public Task CloseAsync()
        {
            return Task.Run(CloseInternal);
        }

        public Task SendAsync(byte[] data, CancellationToken token)
        {
            if (_port == null || !_port.IsOpen) throw new InvalidOperationException("Serial port not open.");
            return Task.Run(() => _port.Write(data, 0, data.Length), token);
        }

        public void Dispose()
        {
            CloseInternal();
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            if (_port == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(2048);

            try
            {
                while (!token.IsCancellationRequested && _port.IsOpen)
                {
                    try
                    {
                        int read = await _port.BaseStream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (read <= 0) continue;
                        byte[] data = new byte[read];
                        Buffer.BlockCopy(buffer, 0, data, 0, read);
                        OnDataReceived?.Invoke(data);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // IO 错误交由 ConnectionGuard 的看门狗处理。
                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void CloseInternal()
        {
            try
            {
                _readCts?.Cancel();
            }
            catch
            {
                // Ignore cancellation errors.
            }

            if (_port != null)
            {
                // Give the pending read loop a moment to observe cancellation and exit.
                Thread.Sleep(30);
                if (_port.IsOpen)
                {
                    try { _port.Close(); } catch { }
                }
                _port.Dispose();
                _port = null;
            }

            try
            {
                _readCts?.Dispose();
            }
            catch
            {
                // Ignore dispose errors.
            }
            _readCts = null;
            _readTask = null;
        }
    }
}
