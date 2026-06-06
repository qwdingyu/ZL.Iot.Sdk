using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    public class WebSocketOutputConfig
    {
        public string Name { get; set; }
        public string Url { get; set; } = "ws://127.0.0.1:8080/ws";
        public string SubProtocol { get; set; }
        public int ReconnectIntervalMs { get; set; } = 3000;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(Url))
                errors.Add(new ConfigValidationError(nameof(Url), "Url 不能为空"));
            else if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != "ws" && uri.Scheme != "wss"))
                errors.Add(new ConfigValidationError(nameof(Url), $"Url '{Url}' 必须是有效的 ws 或 wss URI"));
            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
                errors.Add(new ConfigValidationError(nameof(ReconnectIntervalMs), $"ReconnectIntervalMs {ReconnectIntervalMs} 必须 >= 500"));
            return errors;
        }
    }

    public class WebSocketOutputPlugin : OutputPluginBase
    {
        private readonly WebSocketOutputConfig _config;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _reconnectCts;
        private Task _connectionLoop;
        private int _connectFailureStreak;

        public WebSocketOutputPlugin(WebSocketOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        public override string Name => string.IsNullOrWhiteSpace(_config.Name) ? $"WebSocket-{_config.Url}" : _config.Name;
        public override string ProtocolType => "WebSocket";

        public override Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (Status is PluginStatus.Running or PluginStatus.Starting) return Task.CompletedTask;
            _reconnectCts = new CancellationTokenSource();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _reconnectCts.Token);
            Status = PluginStatus.Starting;
            var loopTask = Task.Run(() => ConnectionLoopAsync(_cts.Token), _cts.Token);
            _connectionLoop = loopTask;
            // 捕获连接循环的未处理异常，避免 fire-and-forget 静默失败
            _ = loopTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    GatewayLog.Error("WebSocketOutput", $"Connection loop failed: {t.Exception}", t.Exception);
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
                    if (IsSocketOpen())
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    await ConnectSocketAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _connectFailureStreak++;
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, $"WebSocket connection failed: {ex.Message}. Retrying in {_config.ReconnectIntervalMs}ms...", ex);
                    SetConnectionState(false, OutputPluginHealthLevel.Warning);
                    int delayMs = CalculateBackoffDelay(_connectFailureStreak);
                    GatewayLog.Warn("WebSocketOutput", $"Connection failed: {ex.Message}. Retrying in {delayMs}ms...");
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
        }

        private async Task ConnectSocketAsync(CancellationToken cancellationToken)
        {
            DisposeSocket();

            var socket = new ClientWebSocket();
            if (!string.IsNullOrWhiteSpace(_config.SubProtocol))
            {
                socket.Options.AddSubProtocol(_config.SubProtocol);
            }

            Status = PluginStatus.Starting;
            await socket.ConnectAsync(new Uri(_config.Url), cancellationToken);
            _socket = socket;
            Status = PluginStatus.Running;
            _connectFailureStreak = 0;
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"WebSocket connected: {_config.Url}");
            SetConnectionState(true, OutputPluginHealthLevel.Healthy);
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null) return;

            if (!IsSocketOpen())
            {
                throw new InvalidOperationException("WebSocket output is not connected");
            }

            await _sendLock.WaitAsync();
            try
            {
                if (!IsSocketOpen())
                {
                    throw new InvalidOperationException("WebSocket output is not connected");
                }

                var payload = message.Payload ?? Array.Empty<byte>();
                var messageType = message.ContentType is "json" or "text"
                    ? WebSocketMessageType.Text
                    : WebSocketMessageType.Binary;

                await _socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    messageType,
                    true,
                    _cts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DisposeSocket();
                throw new InvalidOperationException($"WebSocket send failed: {ex.Message}", ex);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        protected override async Task OnStopAsync()
        {
            _cts?.Cancel();
            _reconnectCts?.Cancel();

            if (_connectionLoop != null)
            {
                try { await _connectionLoop; } catch { }
            }

            if (_socket != null && (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived))
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "gateway-stop", CancellationToken.None);
                }
                catch
                {
                    // ignore
                }
            }

            DisposeSocket();
            _cts?.Dispose();
            _cts = null;
            _reconnectCts?.Dispose();
            _reconnectCts = null;
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

        private bool IsSocketOpen()
        {
            return _socket != null && _socket.State == WebSocketState.Open;
        }

        private void DisposeSocket()
        {
            try
            {
                _socket?.Dispose();
            }
            catch
            {
                // ignore
            }

            _socket = null;
        }
    }
}
