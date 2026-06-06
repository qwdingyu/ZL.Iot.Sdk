using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// Prometheus 输出插件配置
    /// <para>支持推送模式（PushGateway）：将 PLC 数据以 Prometheus 文本格式推送到 PushGateway</para>
    /// </summary>
    public class PrometheusOutputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Prometheus PushGateway URL（如 http://localhost:9091）
        /// </summary>
        public string PushGatewayUrl { get; set; } = "http://localhost:9091";

        /// <summary>
        /// 推送 Job 名称（Prometheus 标签）
        /// </summary>
        public string JobName { get; set; } = "plc_simulator";

        /// <summary>
        /// 推送 Instance 标签（默认主机名）
        /// </summary>
        public string Instance { get; set; } = "localhost";

        /// <summary>
        /// 推送间隔（秒），默认 15s
        /// <para>频繁推送会增加 PushGateway 负载，建议 >= 10s</para>
        /// </summary>
        public int PushIntervalSeconds { get; set; } = 15;

        /// <summary>
        /// 推送超时（毫秒），默认 5000
        /// </summary>
        public int TimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 是否启用内存变更指标（plc_memory_changed_bytes），默认 false
        /// <para>注意：内存变更频率极高，开启可能导致 PushGateway 负载过大</para>
        /// </summary>
        public bool EnableMemoryMetrics { get; set; } = false;

        /// <summary>
        /// 是否启用标签变更指标（plc_tag_value），默认 true
        /// </summary>
        public bool EnableTagMetrics { get; set; } = true;

        /// <summary>
        /// 额外固定标签（附加到所有指标）
        /// <para>例如：{"env": "production", "location": "factory1"}</para>
        /// </summary>
        public Dictionary<string, string> ExtraLabels { get; set; } = new();

        /// <summary>
        /// 验证配置有效性
        /// </summary>
        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();

            if (string.IsNullOrWhiteSpace(PushGatewayUrl))
            {
                errors.Add(new ConfigValidationError("PushGatewayUrl", "PushGatewayUrl 不能为空"));
            }
            else if (!Uri.TryCreate(PushGatewayUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add(new ConfigValidationError("PushGatewayUrl", "PushGatewayUrl 必须是有效的 http 或 https URI"));
            }

            if (string.IsNullOrWhiteSpace(JobName))
            {
                errors.Add(new ConfigValidationError("JobName", "JobName 不能为空"));
            }

            if (string.IsNullOrWhiteSpace(Instance))
            {
                errors.Add(new ConfigValidationError("Instance", "Instance 不能为空"));
            }

            if (PushIntervalSeconds < 1)
            {
                errors.Add(new ConfigValidationError("PushIntervalSeconds", "PushIntervalSeconds 必须 >= 1"));
            }

            if (TimeoutMs < 100)
            {
                errors.Add(new ConfigValidationError("TimeoutMs", "TimeoutMs 必须 >= 100"));
            }

            return errors;
        }
    }

    /// <summary>
    /// Prometheus 输出插件 — 将 PLC 数据推送到 Prometheus PushGateway
    /// <para>数据格式：Prometheus Exposition Text Format（符合 OpenMetrics 标准）</para>
    /// <para>配合 Grafana 可直接展示实时曲线，适合监控告警场景</para>
    /// </summary>
    public class PrometheusOutputPlugin : OutputPluginBase
    {
        private readonly PrometheusOutputConfig _config;
        private CancellationTokenSource _cts;

        /// <summary>静态共享 HttpClient 实例，避免每实例创建导致 Socket 耗尽</summary>
        private static readonly HttpClient SharedHttpClient = SharedHttpClientFactory.Default;

        /// <summary>最近一次推送的指标快照</summary>
        private string _lastMetricsText = "";

        /// <summary>标签值缓存（按 tagName 保留最新值用于 GAUGE）</summary>
        private readonly ConcurrentDictionary<string, TagMetricSnapshot> _tagCache = new();

        /// <summary>内存区数值缓存（按 area/db/address 保留最新值）</summary>
        private readonly ConcurrentDictionary<string, long> _memoryCache = new();

        public PrometheusOutputPlugin(PrometheusOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        public override string Name =>
            string.IsNullOrWhiteSpace(_config.Name) ? $"Prometheus-{_config.JobName}" : _config.Name;

        public override string ProtocolType => "Prometheus";

        protected override Task OnStartAsync(CancellationToken ct)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // 启动定时推送循环
            StartPushLoop(_cts.Token);

            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy,
                $"Prometheus output ready: {_config.PushGatewayUrl}, job={_config.JobName}, instance={_config.Instance}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// 后台定时推送循环
        /// </summary>
        private void StartPushLoop(CancellationToken ct)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, _config.PushIntervalSeconds));

            var loopTask = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(interval, ct);
                        await PushMetricsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常停止
                }
            }, ct);

            // 捕获推送循环的未处理异常，避免 fire-and-forget 静默失败
            _ = loopTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    GatewayLog.Error("PrometheusOutput", $"Push loop failed: {t.Exception}", t.Exception);
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, $"Push loop failed: {t.Exception?.GetBaseException().Message}");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null)
                return;

            try
            {
                UpdateMetricsCache(message);
                // 推送由定时器触发，OnSendAsync 只负责更新缓存
            }
            catch (Exception ex)
            {
                GatewayLog.Error("PrometheusOutput", $"Update metrics error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 从 Message 更新指标缓存
        /// </summary>
        private void UpdateMetricsCache(Message message)
        {
            // 优先使用结构化 Writes
            if (message.Writes?.Count > 0)
            {
                foreach (var write in message.Writes)
                {
                    var tagName = write.Alias ?? write.Address;
                    if (double.TryParse(write.Value?.ToString(), out var numericValue))
                    {
                        _tagCache[tagName] = new TagMetricSnapshot
                        {
                            Value = numericValue,
                            DataType = write.DataType ?? "unknown",
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                    }
                }
                return;
            }

            if (message.ContentType != "json" || message.Payload == null || message.Payload.Length == 0)
                return;

            try
            {
                var json = Encoding.UTF8.GetString(message.Payload);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var source = root.TryGetProperty("source", out var srcEl) ? srcEl.GetString() : "unknown";

                if (source == "TagRegistry" && _config.EnableTagMetrics)
                {
                    var tagName = root.TryGetProperty("tagName", out var tagEl) ? tagEl.GetString() : "unknown";
                    var dataType = root.TryGetProperty("dataType", out var dtEl) ? dtEl.GetString() : "unknown";
                    var value = root.TryGetProperty("value", out var valEl)
                        ? ExtractNumericValue(valEl)
                        : (double?)null;

                    if (value.HasValue && tagName != null)
                    {
                        _tagCache[tagName] = new TagMetricSnapshot
                        {
                            Value = value.Value,
                            DataType = dataType ?? "unknown",
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                    }
                }
                else if (source == "PlcMemory" && _config.EnableMemoryMetrics)
                {
                    var areaCode = root.TryGetProperty("areaCode", out var areaEl) ? areaEl.GetByte() : 0;
                    var dbNumber = root.TryGetProperty("dbNumber", out var dbEl) ? dbEl.GetInt32() : 0;
                    var address = root.TryGetProperty("address", out var addrEl) ? addrEl.GetInt32() : 0;
                    var intValue = root.TryGetProperty("intValue", out var intEl)
                        ? (intEl.ValueKind == JsonValueKind.Null ? (long?)null : long.TryParse(intEl.ToString(), out var l) ? l : null)
                        : null;

                    if (intValue.HasValue)
                    {
                        var key = $"{areaCode:X2}/{dbNumber}/{address}";
                        _memoryCache[key] = intValue.Value;
                    }
                }
            }
            catch (JsonException)
            {
                // 忽略无法解析的消息
            }
        }

        /// <summary>
        /// 从 JsonElement 提取数值（支持 int/long/double/string 数字）
        /// </summary>
        private static double? ExtractNumericValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetDouble(out var d) => d,
                JsonValueKind.Number => double.TryParse(element.GetRawText(), out var d) ? d : null,
                JsonValueKind.String when double.TryParse(element.GetString(), out var d) => d,
                JsonValueKind.True => 1.0,
                JsonValueKind.False => 0.0,
                _ => null
            };
        }

        /// <summary>
        /// 构建 Prometheus 文本格式指标并推送到 PushGateway
        /// </summary>
        private async Task PushMetricsAsync()
        {
            try
            {
                var metricsText = BuildMetricsText();
                if (string.IsNullOrWhiteSpace(metricsText))
                    return;

                _lastMetricsText = metricsText;

                // Prometheus PushGateway 使用 PUT 方法，路径为 /metrics/job/<job>/instance/<instance>
                var pushUrl = $"{_config.PushGatewayUrl.TrimEnd('/')}/metrics/job/{_config.JobName}" +
                    $"/instance/{_config.Instance}";

                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_config.TimeoutMs));
                using var content = new StringContent(metricsText, Encoding.UTF8, "text/plain");
                var response = await SharedHttpClient.PutAsync(pushUrl, content, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    GatewayLog.Info("PrometheusOutput",
                        $"Push failed: {(int)response.StatusCode} {response.ReasonPhrase} - {errorBody}");
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning,
                        $"Push to PushGateway failed: {(int)response.StatusCode}");
                }
                else
                {
                    SetConnectionState(true, OutputPluginHealthLevel.Healthy);
                }
            }
            catch (Exception ex)
            {
                GatewayLog.Error("PrometheusOutput", $"Push error: {ex.Message}");
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, $"Push failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 构建 Prometheus Exposition 文本格式指标
        /// <para>格式参考：https://prometheus.io/docs/instrumenting/exposition_formats/</para>
        /// </summary>
        private string BuildMetricsText()
        {
            var sb = new StringBuilder(4096);

            // 添加帮助信息
            sb.AppendLine("# HELP plc_tag_value Current tag value from PLC simulator");
            sb.AppendLine("# TYPE plc_tag_value gauge");

            // 遍历标签缓存，输出 GAUGE 指标
            foreach (var kvp in _tagCache)
            {
                var tagName = EscapeMetricLabelValue(kvp.Key);
                var dataType = EscapeMetricLabelValue(kvp.Value.DataType);

                // 标签名转 Prometheus 兼容格式（替换特殊字符）
                var safeTagName = SanitizeLabelValue(tagName);

                sb.Append("plc_tag_value{tag=\"");
                sb.Append(safeTagName);
                sb.Append("\",data_type=\"");
                sb.Append(dataType);
                sb.Append("\",job=\"");
                sb.Append(EscapeMetricLabelValue(_config.JobName));
                sb.Append("\",instance=\"");
                sb.Append(EscapeMetricLabelValue(_config.Instance));

                // 额外固定标签
                foreach (var extra in _config.ExtraLabels)
                {
                    sb.Append("\",");
                    sb.Append(EscapeMetricLabelValue(extra.Key));
                    sb.Append("=\"");
                    sb.Append(EscapeMetricLabelValue(extra.Value));
                }

                sb.Append("\"} ");
                sb.AppendLine(kvp.Value.Value.ToString("G"));
            }

            // 内存指标（仅在启用时）
            if (_config.EnableMemoryMetrics && _memoryCache.Count > 0)
            {
                sb.AppendLine("# HELP plc_memory_value Current PLC memory area value");
                sb.AppendLine("# TYPE plc_memory_value gauge");

                foreach (var kvp in _memoryCache)
                {
                    // key 格式: areaCode/dbNumber/address
                    var parts = kvp.Key.Split('/');
                    var areaCode = parts.Length > 0 ? parts[0] : "";
                    var dbNumber = parts.Length > 1 ? parts[1] : "";
                    var address = parts.Length > 2 ? parts[2] : "";

                    sb.Append("plc_memory_value{area=\"");
                    sb.Append(areaCode);
                    sb.Append("\",db=\"");
                    sb.Append(dbNumber);
                    sb.Append("\",address=\"");
                    sb.Append(address);
                    sb.Append("\",job=\"");
                    sb.Append(EscapeMetricLabelValue(_config.JobName));
                    sb.Append("\",instance=\"");
                    sb.Append(EscapeMetricLabelValue(_config.Instance));
                    sb.Append("\"} ");
                    sb.AppendLine(kvp.Value.ToString());
                }
            }

            // 添加遥测信息
            sb.AppendLine("# HELP plc_push_info Push metadata for PLC simulator metrics");
            sb.AppendLine("# TYPE plc_push_info gauge");
            sb.Append("plc_push_info{tag_count=\"");
            sb.Append(_tagCache.Count.ToString());
            sb.Append("\",memory_count=\"");
            sb.Append(_memoryCache.Count.ToString());
            sb.Append("\",job=\"");
            sb.Append(EscapeMetricLabelValue(_config.JobName));
            sb.Append("\",instance=\"");
            sb.Append(EscapeMetricLabelValue(_config.Instance));
            sb.Append("\"} 1");
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// 转义 Prometheus 标签值
        /// <para>需要转义: 反斜杠、双引号、换行符</para>
        /// </summary>
        private static string EscapeMetricLabelValue(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }

        /// <summary>
        /// 清理标签名中的 Prometheus 非法字符
        /// </summary>
        private static string SanitizeLabelValue(string value)
        {
            // Prometheus 标签值合法字符：[a-zA-Z0-9 _:.]，其余替换为 '_'
            // 标签值中反斜杠和双引号已被前面的 EscapeMetricLabelValue 处理
            if (string.IsNullOrEmpty(value))
                return value;
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '_' || c == ':' || c == '.')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 获取最新指标快照文本（用于调试/监视）
        /// </summary>
        public string GetLastMetricsText() => _lastMetricsText;

        protected override async Task OnStopAsync()
        {
            // 停止前推送最终数据（在取消 _cts 之前执行，确保推送循环不会中断）
            try
            {
                await PushMetricsAsync().ConfigureAwait(false);
            }
            catch
            {
                // 忽略关闭时的推送错误
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _tagCache.Clear();
            _memoryCache.Clear();
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

        /// <summary>
        /// 标签指标快照
        /// </summary>
        private sealed class TagMetricSnapshot
        {
            public double Value { get; set; }
            public string DataType { get; set; }
            public long Timestamp { get; set; }
        }
    }
}
