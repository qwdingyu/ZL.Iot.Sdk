using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    public class RedisOutputConfig
    {
        public string Name { get; set; }
        public string ServerIp { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 6379;
        public string Channel { get; set; } = "plc:data";
        public string Password { get; set; }
        public int Database { get; set; }
        public int ReconnectIntervalMs { get; set; } = 3000;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(ServerIp))
                errors.Add(new ConfigValidationError(nameof(ServerIp), $"ServerIp '{ServerIp}' 不是有效的 IP 地址"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
                errors.Add(new ConfigValidationError(nameof(ReconnectIntervalMs), $"ReconnectIntervalMs {ReconnectIntervalMs} 必须 >= 500"));
            return errors;
        }
    }

    public class RedisOutputPlugin : OutputPluginBase
    {
        private readonly RedisOutputConfig _config;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private Task _connectionLoop;
        private int _connectFailureStreak;

        public RedisOutputPlugin(RedisOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        public override string Name => string.IsNullOrWhiteSpace(_config.Name) ? $"Redis-{_config.ServerIp}:{_config.Port}" : _config.Name;
        public override string ProtocolType => "Redis";

        public override Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (Status is PluginStatus.Running or PluginStatus.Starting) return Task.CompletedTask;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Status = PluginStatus.Starting;
            var loopTask = Task.Run(() => ConnectionLoopAsync(_cts.Token), _cts.Token);
            _connectionLoop = loopTask;
            // 捕获连接循环的未处理异常，避免 fire-and-forget 静默失败
            _ = loopTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    GatewayLog.Error("RedisOutput", $"Connection loop failed: {t.Exception}", t.Exception);
                    SetConnectionState(false, OutputPluginHealthLevel.Error);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
            return Task.CompletedTask;
        }

        // StartAsync 有自定义覆盖处理连接循环，无需 OnStartAsync 额外逻辑
        protected override Task OnStartAsync(CancellationToken ct) => Task.CompletedTask;

        private async Task ConnectionLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (IsConnected())
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    await ConnectAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Status = PluginStatus.Error;
                    _connectFailureStreak++;
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, $"Redis connection failed: {ex.Message}. Retrying in {_config.ReconnectIntervalMs}ms...", ex);
                    SetConnectionState(false, OutputPluginHealthLevel.Warning);
                    int delayMs = CalculateBackoffDelay(_connectFailureStreak);
                    GatewayLog.Warn("RedisOutput", $"Connection failed: {ex.Message}. Retrying in {delayMs}ms...");
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
        }

        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            DisposeConnection();

            var client = new TcpClient();
            await client.ConnectAsync(_config.ServerIp, _config.Port);
            var stream = client.GetStream();

            if (!string.IsNullOrWhiteSpace(_config.Password))
            {
                await SendCommandAndExpectOkAsync(stream, cancellationToken, "AUTH", _config.Password);
            }

            if (_config.Database > 0)
            {
                await SendCommandAndExpectOkAsync(stream, cancellationToken, "SELECT", _config.Database.ToString());
            }

            _client = client;
            _stream = stream;
            Status = PluginStatus.Running;
            _connectFailureStreak = 0;
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"Redis output ready: {_config.ServerIp}:{_config.Port}/{_config.Database}");
            SetConnectionState(true, OutputPluginHealthLevel.Healthy);
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null) return;

            if (!IsConnected())
            {
                throw new InvalidOperationException("Redis output is not connected");
            }

            await _sendLock.WaitAsync();
            try
            {
                if (!IsConnected())
                {
                    throw new InvalidOperationException("Redis output is not connected");
                }

                var channel = _config.Channel;
                if (message.Metadata.TryGetValue("RedisChannel", out var metaChannel) && !string.IsNullOrWhiteSpace(metaChannel))
                {
                    channel = metaChannel;
                }
                else if (string.IsNullOrWhiteSpace(channel) && !string.IsNullOrWhiteSpace(message.Topic))
                {
                    channel = message.Topic;
                }

                byte[] payloadToSend;
                if (message.Writes?.Count > 0)
                {
                    payloadToSend = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(message.Writes));
                }
                else
                {
                    payloadToSend = message.Payload ?? Array.Empty<byte>();
                }

                var command = BuildCommand(
                    Encoding.UTF8.GetBytes("PUBLISH"),
                    Encoding.UTF8.GetBytes(channel),
                    payloadToSend);

                await _stream.WriteAsync(command, 0, command.Length, _cts?.Token ?? CancellationToken.None);
                await _stream.FlushAsync(_cts?.Token ?? CancellationToken.None);

                var response = await ReadRespLineAsync(_stream, _cts?.Token ?? CancellationToken.None);
                if (string.IsNullOrWhiteSpace(response) || response[0] == '-')
                {
                    throw new InvalidOperationException($"Redis publish failed: {response}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DisposeConnection();
                throw new InvalidOperationException($"Redis publish failed: {ex.Message}", ex);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        protected override async Task OnStopAsync()
        {
            _cts?.Cancel();

            if (_connectionLoop != null)
            {
                try { await _connectionLoop; } catch { }
            }

            DisposeConnection();
            _cts?.Dispose();
            _cts = null;
            _connectionLoop = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sendLock?.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool IsConnected()
        {
            return _client != null && _client.Connected && _stream != null;
        }

        private async Task SendCommandAndExpectOkAsync(NetworkStream stream, CancellationToken cancellationToken, params string[] arguments)
        {
            var rawArguments = new byte[arguments.Length][];
            for (int i = 0; i < arguments.Length; i++)
            {
                rawArguments[i] = Encoding.UTF8.GetBytes(arguments[i]);
            }

            var command = BuildCommand(rawArguments);
            await stream.WriteAsync(command, 0, command.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var response = await ReadRespLineAsync(stream, cancellationToken);
            if (!response.StartsWith("+OK", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(response);
            }
        }

        private static async Task<string> ReadRespLineAsync(Stream stream, CancellationToken cancellationToken)
        {
            var ms = new MemoryStream();
            var buffer = new byte[1];

            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, 1, cancellationToken);
                if (read == 0)
                {
                    throw new IOException("Redis connection closed by remote host");
                }

                if (buffer[0] == '\n')
                {
                    break;
                }

                if (buffer[0] != '\r')
                {
                    ms.WriteByte(buffer[0]);
                }
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static byte[] BuildCommand(params byte[][] arguments)
        {
            using var ms = new MemoryStream();
            WriteAscii(ms, $"*{arguments.Length}\r\n");
            foreach (var argument in arguments)
            {
                var payload = argument ?? Array.Empty<byte>();
                WriteAscii(ms, $"${payload.Length}\r\n");
                ms.Write(payload, 0, payload.Length);
                WriteAscii(ms, "\r\n");
            }

            return ms.ToArray();
        }

        private static void WriteAscii(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        private void DisposeConnection()
        {
            try
            {
                _stream?.Dispose();
            }
            catch
            {
                // ignore
            }

            try
            {
                _client?.Close();
                _client?.Dispose();
            }
            catch
            {
                // ignore
            }

            _stream = null;
            _client = null;
        }
    }
}
