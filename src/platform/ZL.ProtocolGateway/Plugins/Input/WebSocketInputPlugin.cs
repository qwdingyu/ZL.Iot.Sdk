using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    public class WebSocketInputConfig
    {
        public string Name { get; set; }
        public string Url { get; set; } = "ws://127.0.0.1:8080/ws";
        public string SubProtocol { get; set; }
        public int ReconnectIntervalMs { get; set; } = 3000;
        public int BufferSize { get; set; } = 4096;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(Url))
                errors.Add(new ConfigValidationError(nameof(Url), $"Url 不能为空"));
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                errors.Add(new ConfigValidationError(nameof(Url), $"Url '{Url}' 不是有效的 WebSocket URL（需要 ws:// 或 wss://）"));
            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
                errors.Add(new ConfigValidationError(nameof(ReconnectIntervalMs), $"ReconnectIntervalMs {ReconnectIntervalMs} 必须 >= 500"));
            if (BufferSize <= 0)
                errors.Add(new ConfigValidationError(nameof(BufferSize), $"BufferSize {BufferSize} 必须 > 0"));
            return errors;
        }
    }

    public class WebSocketInputPlugin : InputPluginBase
    {
        private readonly WebSocketInputConfig _config;
        private Task _receiveLoopTask;
        private ClientWebSocket _socket;

        public override string Name { get; }

        public WebSocketInputPlugin(WebSocketInputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Name = string.IsNullOrWhiteSpace(config.Name) ? $"WebSocketInput-{config.Url}" : config.Name;
        }

        public override string ProtocolType => "WebSocket";

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            var loopTask = Task.Run(() => ConnectAndReceiveLoopAsync(ct), ct);
            _receiveLoopTask = loopTask;
            _ = loopTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    GatewayLog.Error("WebSocketInput", $"Receive loop failed: {t.Exception}", t.Exception);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
            await Task.CompletedTask;
        }

        private async Task ConnectAndReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ConnectAsync(cancellationToken);
                    await ReceiveMessagesAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("WebSocketInput", $"Receive loop error: {ex.Message}");
                    await Task.Delay(_config.ReconnectIntervalMs, cancellationToken);
                }
                finally
                {
                    DisposeSocket();
                }
            }
        }

        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            DisposeSocket();

            var socket = new ClientWebSocket();
            if (!string.IsNullOrWhiteSpace(_config.SubProtocol))
            {
                socket.Options.AddSubProtocol(_config.SubProtocol);
            }

            await socket.ConnectAsync(new Uri(_config.Url), cancellationToken);
            _socket = socket;
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[_config.BufferSize];

            while (!cancellationToken.IsCancellationRequested && _socket != null && _socket.State == WebSocketState.Open)
            {
                using (var ms = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", cancellationToken);
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var payload = ms.ToArray();
                    var msg = new Message
                    {
                        Topic = _config.Url,
                        ContentType = result.MessageType == WebSocketMessageType.Text ? "text" : "binary",
                        Metadata =
                        {
                            ["Protocol"] = "WebSocket",
                            ["Source"] = _config.Url
                        }
                    };

                    msg.SetPayload(payload);
                    GatewayTraceContext.EnsureTraceId(msg);
                    await InvokeMessageHandler(msg);
                }
            }
        }

        protected override async Task OnStopAsync()
        {
            if (_receiveLoopTask != null)
            {
                try { await _receiveLoopTask; } catch { }
                _receiveLoopTask = null;
            }

            DisposeSocket();
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
