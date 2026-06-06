using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    public class FileOutputConfig
    {
        public string Name { get; set; }
        public string FilePath { get; set; } = "gateway-output.log";
        public bool Append { get; set; } = true;
        public string EncodingName { get; set; } = "UTF-8";

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(FilePath))
                errors.Add(new ConfigValidationError(nameof(FilePath), "FilePath 不能为空"));
            return errors;
        }
    }

    /// <summary>
    /// 文件输出插件 - 将消息写入本地文件
    /// 继承 OutputPluginBase，消除状态管理样板代码
    /// </summary>
    public class FileOutputPlugin : OutputPluginBase
    {
        private readonly FileOutputConfig _config;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private Encoding _encoding;
        private StreamWriter _writer; // 持久化写入器，避免每条消息都 open/close 文件

        public FileOutputPlugin(FileOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        public override string Name => string.IsNullOrWhiteSpace(_config.Name) ? $"File-{_config.FilePath}" : _config.Name;
        public override string ProtocolType => "File";

        /// <summary>
        /// 启动：初始化编码并确保目标文件就绪
        /// 异常由基类自动捕获并触发 Fatal 状态通知
        /// </summary>
        protected override Task OnStartAsync(CancellationToken ct)
        {
            string directory = Path.GetDirectoryName(_config.FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!_config.Append && File.Exists(_config.FilePath))
            {
                File.WriteAllText(_config.FilePath, string.Empty);
            }

            _encoding = Encoding.GetEncoding(_config.EncodingName);
            // 创建持久化 StreamWriter，避免每条消息都 open/close 文件
            var stream = new FileStream(_config.FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
            _writer = new StreamWriter(stream, _encoding);
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"File output ready: {_config.FilePath}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 发送：将消息内容写入文件
        /// </summary>
        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null) return;

            // 优先使用结构化 Writes，否则回退到 Payload
            string text;
            if (message.Writes?.Count > 0)
            {
                text = System.Text.Json.JsonSerializer.Serialize(message.Writes);
            }
            else
            {
                var payload = message.Payload ?? Array.Empty<byte>();
                text = message.ContentType == "binary"
                    ? BitConverter.ToString(payload).Replace("-", string.Empty)
                    : _encoding.GetString(payload);
            }

            await _writeLock.WaitAsync();
            try
            {
                if (_writer == null)
                {
                    throw new InvalidOperationException("File writer not initialized");
                }
                await _writer.WriteAsync(text);
                await _writer.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// 停止：释放写入锁
        /// </summary>
        protected override async Task OnStopAsync()
        {
            await _writeLock.WaitAsync();
            try
            {
                if (_writer != null)
                {
                    await _writer.FlushAsync();
                    await _writer.DisposeAsync();
                    _writer = null;
                }
            }
            finally
            {
                _writeLock.Release();
                // 不在此处 Dispose _writeLock，避免 OnSendAsync 中正在 WaitAsync 的线程拿到 ObjectDisposedException
                // _writeLock 由基类 Dispose 路径清理
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _writeLock?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
