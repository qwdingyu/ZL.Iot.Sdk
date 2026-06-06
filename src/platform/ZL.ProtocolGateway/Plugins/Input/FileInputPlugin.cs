using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Framing;

namespace ZL.ProtocolGateway.Plugins
{
    public class FileInputConfig
    {
        public string Name { get; set; }
        public string FilePath { get; set; } = "gateway-input.log";
        public byte[] Delimiter { get; set; } = Encoding.UTF8.GetBytes("\n");
        public int PollIntervalMs { get; set; } = 200;
        public bool ReadFromEnd { get; set; }
        public string EncodingName { get; set; } = "UTF-8";

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(FilePath))
                errors.Add(new ConfigValidationError(nameof(FilePath), $"FilePath 不能为空"));
            if (PollIntervalMs < 50)
                errors.Add(new ConfigValidationError(nameof(PollIntervalMs), $"PollIntervalMs {PollIntervalMs} 必须 >= 50"));
            if (Delimiter == null || Delimiter.Length == 0)
                errors.Add(new ConfigValidationError(nameof(Delimiter), $"Delimiter 不能为空"));
            return errors;
        }
    }

    public class FileInputPlugin : InputPluginBase
    {
        private readonly FileInputConfig _config;
        private readonly IFrameSplitter _splitter;
        private Task _pollTask;
        private long _position;

        public override string Name { get; }

        public FileInputPlugin(FileInputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Name = string.IsNullOrWhiteSpace(config.Name) ? $"FileInput-{Path.GetFileName(config.FilePath)}" : config.Name;
            _splitter = new DelimiterSplitter(config.Delimiter);
        }
        public override string ProtocolType => "File";

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            EnsureFileExists();
            var fileInfo = new FileInfo(_config.FilePath);
            _position = _config.ReadFromEnd && fileInfo.Exists ? fileInfo.Length : 0;

            var pollTask = Task.Run(() => PollLoopAsync(ct), ct);
            _pollTask = pollTask;
            // 捕获轮询循环的未处理异常，避免 fire-and-forget 静默失败
            _ = pollTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    GatewayLog.Error("FileInput", $"Poll loop failed: {t.Exception}", t.Exception);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

            await Task.CompletedTask;
        }

        private async Task PollLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ReadNewContentAsync(cancellationToken);
                    await Task.Delay(_config.PollIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("FileInput", $"Poll error: {ex.Message}");
                    await Task.Delay(_config.PollIntervalMs, cancellationToken);
                }
            }
        }

        private async Task ReadNewContentAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_config.FilePath))
            {
                return;
            }

            using (var stream = new FileStream(_config.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length < _position)
                {
                    _position = 0;
                }

                if (stream.Length == _position)
                {
                    return;
                }

                stream.Seek(_position, SeekOrigin.Begin);
                var readBuffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken)) > 0)
                {
                    _splitter.Append(readBuffer, 0, bytesRead);
                    _position += bytesRead;

                    foreach (var frame in _splitter.ExtractFrames())
                    {
                        var payload = frame.ToArray();
                        if (payload.Length == 0)
                        {
                            continue;
                        }

                        var msg = new Message
                        {
                            Topic = _config.FilePath,
                            ContentType = "text",
                            Metadata = {
                                ["Protocol"] = "File",
                                ["Source"] = _config.FilePath
                            }
                        };
                        msg.SetPayload(payload);

                        GatewayTraceContext.EnsureTraceId(msg);
                        await InvokeMessageHandler(msg);
                    }
                }
            }
        }

        protected override async Task OnStopAsync()
        {
            if (_pollTask != null)
            {
                try { await _pollTask; } catch { }
                _pollTask = null;
            }
        }

        private void EnsureFileExists()
        {
            var directory = Path.GetDirectoryName(_config.FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_config.FilePath))
            {
                File.WriteAllText(_config.FilePath, string.Empty);
            }
        }
    }
}
