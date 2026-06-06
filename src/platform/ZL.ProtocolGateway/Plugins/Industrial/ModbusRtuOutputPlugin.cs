using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    public class ModbusRtuOutputConfig
    {
        public string Name { get; set; } = string.Empty;
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
        public Parity Parity { get; set; } = Parity.None;
        public byte UnitId { get; set; } = 1;
        public int TimeoutMs { get; set; } = 3000;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(PortName))
                errors.Add(new ConfigValidationError(nameof(PortName), $"PortName 不能为空"));
            if (BaudRate <= 0)
                errors.Add(new ConfigValidationError(nameof(BaudRate), $"BaudRate {BaudRate} 必须 > 0"));
            if (DataBits < 5 || DataBits > 8)
                errors.Add(new ConfigValidationError(nameof(DataBits), $"DataBits {DataBits} 必须在 5-8 范围内"));
            if (!ConfigValidation.IsValidTimeout(TimeoutMs))
                errors.Add(new ConfigValidationError(nameof(TimeoutMs), $"TimeoutMs {TimeoutMs} 必须 >= 100"));
            return errors;
        }
    }

    public interface IModbusRtuTransport : IDisposable
    {
        Task OpenAsync(ModbusRtuOutputConfig config, CancellationToken cancellationToken = default);
        Task<byte[]> SendAndReceiveAsync(byte[] request, int timeoutMs, CancellationToken cancellationToken = default);
        Task CloseAsync();
    }

    public class ModbusRtuOutputPlugin : IndustrialOutputPluginBase
    {
        private readonly ModbusRtuOutputConfig _config;
        private readonly IModbusRtuTransport _transport;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _cts;

        public ModbusRtuOutputPlugin(ModbusRtuOutputConfig config, IModbusRtuTransport? transport = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _transport = transport ?? new SerialModbusRtuTransport();
        }

        public override string Name => string.IsNullOrWhiteSpace(_config.Name) ? $"ModbusRtu-{_config.PortName}" : _config.Name;
        public override string ProtocolType => "ModbusRtu";

        /// <summary>Modbus RTU 协议特定的错误分类</summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            return level switch
            {
                OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, "Modbus RTU connected", "No action needed"),
                OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, "Modbus RTU communication warning", "Check serial port connection"),
                OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, "Modbus RTU communication failed", "Check serial port, baud rate, and cabling"),
                OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, "Modbus RTU configuration error", "Check output plugin configuration"),
                _ => (GatewayErrorCodes.InternalException, "Modbus RTU unknown error", "Check logs for details")
            };
        }

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await _transport.OpenAsync(_config, _cts.Token);
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"Modbus RTU output ready: {_config.PortName}");
            SetConnectionState(true, OutputPluginHealthLevel.Healthy);
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null)
            {
                return;
            }

            List<ModbusWriteOperation> writes;
            // 优先使用协议无关的 TagWrite 列表（解决 Bridge→ModbusRTU 消息格式不匹配的"暗契约"问题）
            if (message.Writes != null && message.Writes.Count > 0)
            {
                writes = new List<ModbusWriteOperation>();
                foreach (var tw in message.Writes)
                {
                    if (ModbusWriteSupport.TryParseAddress(tw.Address, tw.Value?.ToString() ?? "", _config.UnitId, out var op))
                    {
                        writes.Add(op);
                    }
                }
            }
            else
            {
                writes = ModbusWriteSupport.ParseWrites(message, _config.UnitId);
            }
            if (writes.Count == 0)
            {
                return;
            }

            await _sendLock.WaitAsync(_cts.Token);
            try
            {
                foreach (var write in writes)
                {
                    var request = ModbusWriteSupport.BuildRtuWriteRequest(write);
                    var response = await _transport.SendAndReceiveAsync(request, _config.TimeoutMs, _cts.Token);
                    ModbusWriteSupport.ValidateRtuWriteResponse(response, write);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        protected override async Task OnStopAsync()
        {
            _cts?.Cancel();
            await _transport.CloseAsync();
            _cts?.Dispose();
            _cts = null;
            _transport.Dispose();
            // _sendLock 不在此处 Dispose，避免 OnSendAsync 中正在 WaitAsync 的线程拿到 ObjectDisposedException
            // _sendLock 由基类 Dispose 路径清理
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sendLock?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class SerialModbusRtuTransport : IModbusRtuTransport
    {
        private SerialPort? _serialPort;

        public Task OpenAsync(ModbusRtuOutputConfig config, CancellationToken cancellationToken = default)
        {
            _serialPort = new SerialPort
            {
                PortName = config.PortName,
                BaudRate = config.BaudRate,
                DataBits = config.DataBits,
                StopBits = config.StopBits,
                Parity = config.Parity,
                ReadTimeout = config.TimeoutMs,
                WriteTimeout = config.TimeoutMs
            };
            _serialPort.Open();
            return Task.CompletedTask;
        }

        public Task<byte[]> SendAndReceiveAsync(byte[] request, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                throw new InvalidOperationException("Serial port is not open");
            }

            return Task.Run(() =>
            {
                _serialPort.DiscardInBuffer();
                _serialPort.Write(request, 0, request.Length);

                // P1-3 修复：根据 Modbus 功能码动态计算响应长度，替代硬编码 8 字节
                // 请求帧格式: [单位ID][功能码][起始地址Hi][起始地址Lo][数量Hi/字节数...][CRC16]
                // 最少 6 字节（写单个线圈/寄存器），读操作更多
                int expectedResponseLength = 8; // 默认最小响应（单位ID+功能码+回显2字节+CRC16）
                if (request.Length >= 6)
                {
                    byte functionCode = request[1];
                    switch (functionCode)
                    {
                        case 0x01: // 读线圈
                        case 0x02: // 读离散输入
                            if (request.Length >= 7)
                            {
                                int qty1 = (request[4] << 8) | request[5];
                                expectedResponseLength = 3 + ((qty1 + 7) / 8) + 2; // 单位ID+FC+字节数+数据+CRC
                            }
                            break;
                        case 0x03: // 读保持寄存器
                        case 0x04: // 读输入寄存器
                            if (request.Length >= 7)
                            {
                                int qty2 = (request[4] << 8) | request[5];
                                expectedResponseLength = 3 + qty2 * 2 + 2; // 单位ID+FC+字节数+数据(每寄存器2字节)+CRC
                            }
                            break;
                        case 0x05: // 写单个线圈
                        case 0x06: // 写单个寄存器
                            expectedResponseLength = 6; // 回显请求（单位ID+FC+地址Hi+地址Lo+值Hi+值Lo），无 CRC
                            break;
                        case 0x0F: // 写多个线圈
                        case 0x10: // 写多个寄存器
                            expectedResponseLength = 6; // 回显（单位ID+FC+起始地址Hi+Lo+数量Hi+Lo）
                            break;
                    }
                }

                // 至少 6 字节，最多 256 字节（Modbus RTU ADU 上限约 256）
                expectedResponseLength = Math.Max(6, Math.Min(expectedResponseLength, 256));
                var response = new byte[expectedResponseLength];
                int offset = 0;
                while (offset < response.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested(); // P1-2 修复：每次循环检查取消
                    int read = _serialPort.Read(response, offset, response.Length - offset);
                    if (read <= 0)
                    {
                        throw new InvalidOperationException("Modbus RTU response timeout");
                    }
                    offset += read;
                }

                return response;
            }, cancellationToken);
        }

        public Task CloseAsync()
        {
            try
            {
                _serialPort?.Close();
                _serialPort?.Dispose();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _serialPort = null;
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            // CloseAsync() 内部为同步操作（SerialPort.Close/Dispose），直接调用无需 Task.Run
            try { CloseAsync().GetAwaiter().GetResult(); } catch { }
        }
    }
}
