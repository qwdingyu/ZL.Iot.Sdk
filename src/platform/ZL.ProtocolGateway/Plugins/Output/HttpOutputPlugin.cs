using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// HTTP 输出插件配置
    /// </summary>
    public class HttpOutputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// HTTP 请求地址
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// HTTP 方法
        /// </summary>
        public string Method { get; set; } = "POST";

        /// <summary>
        /// 请求头
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> Headers { get; set; }
            = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>
        /// 内容类型
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        /// <summary>
        /// 请求超时（毫秒）
        /// </summary>
        public int Timeout { get; set; } = 30000;

        /// <summary>
        /// 认证 Token（Bearer）
        /// </summary>
        public string AuthToken { get; set; }

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(Url))
                errors.Add(new ConfigValidationError(nameof(Url), "Url 不能为空"));
            else if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                errors.Add(new ConfigValidationError(nameof(Url), $"Url '{Url}' 必须是有效的 http 或 https URI"));
            if (!ConfigValidation.IsValidTimeout(Timeout, 100))
                errors.Add(new ConfigValidationError(nameof(Timeout), $"Timeout {Timeout} 必须 >= 100"));
            return errors;
        }
    }

    /// <summary>
    /// HTTP 输出插件 - 将 Message 发送到 HTTP/WebAPI 端点
    /// 继承 OutputPluginBase，消除状态管理样板代码
    /// </summary>
    public class HttpOutputPlugin : OutputPluginBase
    {
        /// <summary>
        /// 共享 HttpClient 实例 — 避免每个插件创建新客户端导致 Socket TIME_WAIT 耗尽。
        /// </summary>
        private static readonly HttpClient SharedHttpClient = SharedHttpClientFactory.Default;

        private readonly HttpOutputConfig _config;
        private CancellationTokenSource _cts;
        private bool _started;

        public HttpOutputPlugin(HttpOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        public override string Name => _config.Name ?? $"HTTP-{(Uri.TryCreate(_config.Url, UriKind.Absolute, out var uri) ? uri.Host : _config.Url)}";

        public override string ProtocolType => "HTTP";

        /// <summary>
        /// 启动：不再创建 HttpClient，仅初始化取消令牌和记录状态。
        /// 使用共享 SharedHttpClient 避免 Socket TIME_WAIT 耗尽。
        /// </summary>
        protected override async Task OnStartAsync(CancellationToken ct)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _started = true;

            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"HTTP output ready: {_config.Url}");
            await Task.CompletedTask;
        }

        /// <summary>
        /// 发送：将 Message 转为 HTTP 请求并发送。
        /// 使用共享 HttpClient + per-request CancellationTokenSource 实现超时控制。
        /// </summary>
        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (!_started)
                throw new InvalidOperationException("HTTP output plugin is not running");

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            HttpContent content;
            byte[] payloadToSend;
            if (message.Writes?.Count > 0)
            {
                payloadToSend = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(message.Writes));
            }
            else
            {
                payloadToSend = message.Payload ?? Array.Empty<byte>();
            }

            if (message.ContentType == "json")
            {
                content = new StringContent(Encoding.UTF8.GetString(payloadToSend), Encoding.UTF8, _config.ContentType);
            }
            else if (message.ContentType == "text")
            {
                content = new StringContent(Encoding.UTF8.GetString(payloadToSend), Encoding.UTF8, "text/plain");
            }
            else
            {
                content = new ByteArrayContent(payloadToSend);
            }

            // 添加自定义 Header（从 Message.Metadata）
            if (message.Metadata.TryGetValue("X-Device-Id", out var deviceId))
            {
                content.Headers.TryAddWithoutValidation("X-Device-Id", deviceId);
            }

            using var request = new HttpRequestMessage
            {
                Method = new HttpMethod(_config.Method),
                RequestUri = new Uri(_config.Url),
                Content = content
            };

            // 设置认证和自定义请求头（per-request，不污染共享 HttpClient）
            if (!string.IsNullOrEmpty(_config.AuthToken))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.AuthToken);
            }

            foreach (var header in _config.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // 使用 per-request 超时 CancellationTokenSource，避免与 _cts 耦合
            using var timeoutCts = new CancellationTokenSource(_config.Timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

            var response = await SharedHttpClient.SendAsync(request, linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"HTTP request failed: {(int)response.StatusCode} {response.ReasonPhrase} - {responseBody}");
            }
        }

        /// <summary>
        /// 停止：取消令牌，不再需要释放 HttpClient（使用共享实例）。
        /// </summary>
        protected override Task OnStopAsync()
        {
            _started = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Dispose();
                _cts = null;
            }
            base.Dispose(disposing);
        }
    }
}
