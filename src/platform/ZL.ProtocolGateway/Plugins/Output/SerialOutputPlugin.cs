using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    public class SerialOutputConfig
    {
        public string Name { get; set; }
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
        public Parity Parity { get; set; } = Parity.None;
        public string Suffix { get; set; } = string.Empty;
        public string EncodingName { get; set; } = "UTF-8";

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(PortName))
                errors.Add(new ConfigValidationError(nameof(PortName), "PortName 不能为空"));
            if (BaudRate <= 0)
                errors.Add(new ConfigValidationError(nameof(BaudRate), $"BaudRate {BaudRate} 必须 > 0"));
            return errors;
        }
    }

    /// <summary>
    /// 串口输出插件 - 通过串口转发消息到设备
    /// 继承 OutputPluginBase，消除状态管理样板代码
    /// </summary>
    public class SerialOutputPlugin : OutputPluginBase
    {
        private readonly SerialOutputConfig _config;
        private SerialPort _serialPort;

        public SerialOutputPlugin(SerialOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        public override string Name => string.IsNullOrWhiteSpace(_config.Name)
            ? $"Serial-{_config.PortName}"
            : _config.Name;

        public override string ProtocolType => "Serial";

        /// <summary>
        /// 启动：打开串口
        /// 异常由基类自动捕获并触发 Fatal 状态通知
        /// </summary>
        protected override Task OnStartAsync(CancellationToken ct)
        {
            _serialPort = new SerialPort
            {
                PortName = _config.PortName,
                BaudRate = _config.BaudRate,
                DataBits = _config.DataBits,
                StopBits = _config.StopBits,
                Parity = _config.Parity,
                Encoding = Encoding.GetEncoding(_config.EncodingName),
                WriteTimeout = 3000
            };

            _serialPort.Open();
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"Serial output ready: {_config.PortName}");
            // SetConnectionState 由基类 StartAsync 统一调用
            return Task.CompletedTask;
        }

        /// <summary>
        /// 发送：将消息写入串口
        /// </summary>
        protected override Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null || _serialPort == null || !_serialPort.IsOpen)
            {
                return Task.CompletedTask;
            }

            var payload = message.Payload ?? Array.Empty<byte>();
            _serialPort.Write(payload, 0, payload.Length);
            if (!string.IsNullOrEmpty(_config.Suffix))
            {
                var suffixBytes = _serialPort.Encoding.GetBytes(_config.Suffix);
                _serialPort.Write(suffixBytes, 0, suffixBytes.Length);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止：关闭串口
        /// </summary>
        protected override Task OnStopAsync()
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

            _serialPort = null;
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _serialPort?.Dispose();
                _serialPort = null;
            }

            base.Dispose(disposing);
        }
    }
}
