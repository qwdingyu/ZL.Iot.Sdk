using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    public class HttpInputConfig
    {
        public string Name { get; set; }
        public string LocalIp { get; set; } = "127.0.0.1";
        public int Port { get; set; }
        public List<string> AcceptedMethods { get; set; } = new List<string> { "POST", "PUT" };
        public string PathPrefix { get; set; } = "/";
        public int MaxBodySizeBytes { get; set; } = 1024 * 1024;
        public string ResponseBody { get; set; } = "OK";
        public string ResponseContentType { get; set; } = "text/plain; charset=utf-8";

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(LocalIp))
                errors.Add(new ConfigValidationError(nameof(LocalIp), $"LocalIp '{LocalIp}' 不是有效的 IP 地址"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            if (MaxBodySizeBytes <= 0)
                errors.Add(new ConfigValidationError(nameof(MaxBodySizeBytes), $"MaxBodySizeBytes {MaxBodySizeBytes} 必须 > 0"));
            if (AcceptedMethods == null || AcceptedMethods.Count == 0)
                errors.Add(new ConfigValidationError(nameof(AcceptedMethods), $"AcceptedMethods 不能为空"));
            if (string.IsNullOrWhiteSpace(PathPrefix))
                errors.Add(new ConfigValidationError(nameof(PathPrefix), $"PathPrefix 不能为空"));
            return errors;
        }
    }

    public class HttpInputPlugin : InputPluginBase
    {
        private readonly HttpInputConfig _config;
        private HttpListener _httpListener;
        private HashSet<string>? _acceptedMethodsCache;

        public override string Name { get; }

        public HttpInputPlugin(HttpInputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Name = string.IsNullOrWhiteSpace(config.Name) ? $"HttpInput-{config.Port}" : config.Name;
        }

        public override string ProtocolType => "Http";

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://{_config.LocalIp}:{_config.Port}{_config.PathPrefix}");
                _httpListener.Start();
                _ = Task.Run(() => AcceptLoopAsync(ct), ct).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        GatewayLog.Error("HttpInput", $"Accept loop failed: {t.Exception}", t.Exception);
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to start HTTP input: {ex.Message}", ex);
            }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context, ct));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("HttpInput", $"Accept error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            var req = context.Request;
            var resp = context.Response;

            try
            {
                if (!IsAcceptedMethod(req.HttpMethod))
                {
                    await WriteResponseAsync(resp, 405, "method not allowed", ct);
                    return;
                }

                if (!MatchesPath(req.Url?.AbsolutePath))
                {
                    await WriteResponseAsync(resp, 404, "path not matched", ct);
                    return;
                }

                byte[] body;
                if (req.ContentLength64 > 0)
                {
                    var maxLength = (int)Math.Min(req.ContentLength64, _config.MaxBodySizeBytes);
                    body = new byte[maxLength];
                    int offset = 0;
                    using var bodyStream = req.InputStream;
                    while (offset < maxLength)
                    {
                        int read = await bodyStream.ReadAsync(body, offset, maxLength - offset, ct);
                        if (read == 0) break;
                        offset += read;
                    }
                    body = body[..offset];
                }
                else
                {
                    body = Array.Empty<byte>();
                }

                var contentType = req.ContentType;
                var msg = new Message
                {
                    Topic = req.Url?.AbsolutePath ?? "/",
                    ContentType = ResolveContentType(contentType),
                    Metadata =
                    {
                        ["Protocol"] = "Http",
                        ["Method"] = req.HttpMethod,
                        ["Path"] = req.Url?.AbsolutePath ?? "/",
                        ["Source"] = req.RemoteEndPoint?.ToString() ?? string.Empty
                    }
                };
                msg.SetPayload(body);

                if (!string.IsNullOrWhiteSpace(contentType))
                {
                    msg.Metadata["HttpContentType"] = contentType;
                }

                GatewayTraceContext.EnsureTraceId(msg);
                await InvokeMessageHandler(msg);
                await WriteResponseAsync(resp, 200, _config.ResponseBody, ct, _config.ResponseContentType);
            }
            catch (Exception ex)
            {
                GatewayLog.Error("HttpInput", $"Client error: {ex.Message}");
                try
                {
                    await WriteResponseAsync(resp, 500, "gateway error", ct);
                }
                catch
                {
                    // ignore
                }
            }
        }

        private bool IsAcceptedMethod(string method)
        {
            var cache = _acceptedMethodsCache ??= new HashSet<string>(
                _config.AcceptedMethods?.ToArray() ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            return cache.Contains(method);
        }

        private bool MatchesPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var prefix = string.IsNullOrWhiteSpace(_config.PathPrefix) ? "/" : _config.PathPrefix;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveContentType(string contentTypeHeader)
        {
            if (string.IsNullOrWhiteSpace(contentTypeHeader))
            {
                return "binary";
            }

            if (contentTypeHeader.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "json";
            }

            if (contentTypeHeader.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 ||
                contentTypeHeader.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "text";
            }

            return "binary";
        }

        private static async Task WriteResponseAsync(
            HttpListenerResponse response,
            int statusCode,
            string body,
            CancellationToken ct,
            string contentType = "text/plain; charset=utf-8")
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            var responseBody = Encoding.UTF8.GetBytes(body ?? string.Empty);
            response.ContentLength64 = responseBody.Length;
            if (responseBody.Length > 0)
            {
                await response.OutputStream.WriteAsync(responseBody, 0, responseBody.Length, ct);
            }
            await response.OutputStream.FlushAsync(ct);
            response.Close();
        }

        protected override async Task OnStopAsync()
        {
            _httpListener?.Stop();
        }
    }
}
