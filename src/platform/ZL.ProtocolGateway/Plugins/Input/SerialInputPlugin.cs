using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Framing;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// 串口输入插件配置
    /// </summary>
    public class SerialInputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 串口端口名（如：COM1, /dev/ttyUSB0）
        /// </summary>
        public string PortName { get; set; }

        /// <summary>
        /// 波特率
        /// </summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>
        /// 数据位
        /// </summary>
        public int DataBits { get; set; } = 8;

        /// <summary>
        /// 停止位
        /// </summary>
        public StopBits StopBits { get; set; } = StopBits.One;

        /// <summary>
        /// 校验位
        /// </summary>
        public Parity Parity { get; set; } = Parity.None;

        /// <summary>
        /// 读取超时（毫秒）
        /// </summary>
        public int ReadTimeout { get; set; } = 1000;

        /// <summary>
        /// 数据分隔符（用于判断一条完整消息的结束）
        /// </summary>
        public byte[] Delimiter { get; set; } = Encoding.ASCII.GetBytes("\n");

        /// <summary>
        /// 编码方式
        /// </summary>
        public string EncodingName { get; set; } = "UTF-8";

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(PortName))
                errors.Add(new ConfigValidationError(nameof(PortName), $"PortName 不能为空"));
            if (BaudRate <= 0)
                errors.Add(new ConfigValidationError(nameof(BaudRate), $"BaudRate {BaudRate} 必须 > 0"));
            if (DataBits < 5 || DataBits > 8)
                errors.Add(new ConfigValidationError(nameof(DataBits), $"DataBits {DataBits} 必须在 5-8 范围内"));
            if (ReadTimeout < 100)
                errors.Add(new ConfigValidationError(nameof(ReadTimeout), $"ReadTimeout {ReadTimeout} 必须 >= 100"));
            if (Delimiter == null || Delimiter.Length == 0)
                errors.Add(new ConfigValidationError(nameof(Delimiter), $"Delimiter 不能为空"));
            return errors;
        }
    }

    /// <summary>
    /// 串口输入插件 — 继承 InputPluginBase，复用 DelimiterSplitter 消除重复分帧逻辑
    /// </summary>
    public class SerialInputPlugin : InputPluginBase
    {
        private readonly SerialInputConfig _config;
        private SerialPort _serialPort;
        private Task _readTask;

        public SerialInputPlugin(SerialInputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Name = config.Name ?? $"Serial-{config.PortName}";
        }

        public override string Name { get; }
        public override string ProtocolType => "Serial";

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            _serialPort = new SerialPort
            {
                PortName = _config.PortName,
                BaudRate = _config.BaudRate,
                DataBits = _config.DataBits,
                Parity = _config.Parity,
                StopBits = _config.StopBits,
                ReadTimeout = _config.ReadTimeout,
                Encoding = Encoding.GetEncoding(_config.EncodingName)
            };

            _serialPort.Open();

            var readTask = ReadLoopAsync(ct);
            _readTask = readTask;
            _ = readTask.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception is AggregateException ae)
                {
                    GatewayLog.Error("SerialInput", $"Read loop failed: {ae.InnerExceptions[0]?.Message}", ae);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var splitter = new DelimiterSplitter(_config.Delimiter);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_serialPort?.IsOpen != true) break;

                    if (_serialPort.BytesToRead > 0)
                    {
                        var available = _serialPort.BytesToRead;
                        var readBuffer = new byte[available];
                        int read = _serialPort.Read(readBuffer, 0, available);
                        splitter.Append(readBuffer, 0, read);

                        var frames = splitter.ExtractFrames();
                        foreach (var frame in frames)
                        {
                            var msg = new Message
                            {
                                Topic = _config.PortName,
                                ContentType = "text",
                                Metadata =
                                {
                                    ["Protocol"] = "Serial",
                                    ["PortName"] = _config.PortName,
                                    ["BaudRate"] = _config.BaudRate.ToString(),
                                    ["Encoding"] = _config.EncodingName
                                }
                            };
                            msg.SetPayload(frame.ToArray());

                            GatewayTraceContext.EnsureTraceId(msg);
                            await InvokeMessageHandler(msg);
                        }
                    }
                    else
                    {
                        await Task.Delay(10, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    // 读取超时，继续循环
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("SerialInput", $"Serial read error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }
        }

        protected override async Task OnStopAsync()
        {
            if (_readTask != null)
            {
                try { await _readTask; } catch { }
                _readTask = null;
            }

            _serialPort?.Close();
            _serialPort?.Dispose();
            _serialPort = null;
        }
    }
}
