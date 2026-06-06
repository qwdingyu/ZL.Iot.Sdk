using System;
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
    /// InfluxDB 输出插件配置
    /// <para>支持 InfluxDB v2 版本：通过 HTTP API 写入 Line Protocol 格式数据</para>
    /// </summary>
    public class InfluxDbOutputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// InfluxDB URL（如 http://localhost:8086）
        /// </summary>
        public string Url { get; set; } = "http://localhost:8086";

        /// <summary>
        /// InfluxDB v2 Token（认证用）
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// InfluxDB v2 Organization
        /// </summary>
        public string Organization { get; set; } = "plc-simulator";

        /// <summary>
        /// InfluxDB v2 Bucket
        /// </summary>
        public string Bucket { get; set; } = "plc-data";

        /// <summary>
        /// 写入超时（毫秒），默认 5000
        /// </summary>
        public int TimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 默认测量名称（相当于关系型数据库的表）
        /// </summary>
        public string DefaultMeasurement { get; set; } = "plc_memory";

        /// <summary>
        /// 标签变更写入的测量名称
        /// </summary>
        public string TagMeasurement { get; set; } = "plc_tag";

        /// <summary>
        /// 是否启用批量写入（将多条消息合并为一次 HTTP 请求），默认 true
        /// </summary>
        public bool EnableBatchWrite { get; set; } = true;

        /// <summary>
        /// 最大行数（用于批量写入时的拆分），默认 5000
        /// </summary>
        public int MaxBatchLines { get; set; } = 5000;

        /// <summary>
        /// 是否写入 MemoryChange 事件，默认 true
        /// </summary>
        public bool WriteMemoryChanges { get; set; } = true;

        /// <summary>
        /// 是否写入 TagChange 事件，默认 true
        /// </summary>
        public bool WriteTagChanges { get; set; } = true;

        /// <summary>
        /// 是否使用 TLS/HTTPS 连接 InfluxDB（默认 false）
        /// </summary>
        public bool UseTls { get; set; }

        /// <summary>
        /// 是否跳过服务器证书验证（仅用于测试环境，默认 false）
        /// </summary>
        public bool InsecureSkipVerify { get; set; }

        /// <summary>
        /// CA 证书路径（可选，用于自定义 CA 的 TLS 连接）
        /// </summary>
        public string? CaCertificatePath { get; set; }

        /// <summary>
        /// 验证配置合法性
        /// </summary>
        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(Url))
                errors.Add(new ConfigValidationError(nameof(Url), "InfluxDB URL 不能为空"));
            else if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                errors.Add(new ConfigValidationError(nameof(Url), $"无效的 InfluxDB URL: {Url}（需要 http/https）"));
            else if (UseTls && Url.StartsWith("http://"))
                errors.Add(new ConfigValidationError(nameof(Url), "UseTls=true 但 URL 使用 http://，请改用 https://"));
            if (string.IsNullOrWhiteSpace(Token))
                errors.Add(new ConfigValidationError(nameof(Token), "InfluxDB v2 Token 不能为空"));
            if (string.IsNullOrWhiteSpace(Organization))
                errors.Add(new ConfigValidationError(nameof(Organization), "Organization 不能为空"));
            if (string.IsNullOrWhiteSpace(Bucket))
                errors.Add(new ConfigValidationError(nameof(Bucket), "Bucket 不能为空"));
            if (!ConfigValidation.IsValidTimeout(TimeoutMs, 1000))
                errors.Add(new ConfigValidationError(nameof(TimeoutMs), $"写入超时最小 1000ms，当前: {TimeoutMs}"));
            if (MaxBatchLines < 1)
                errors.Add(new ConfigValidationError(nameof(MaxBatchLines), $"最大批行数最小 1，当前: {MaxBatchLines}"));
            return errors;
        }
    }

    /// <summary>
    /// InfluxDB 输出插件 — 将 PLC 数据写入 InfluxDB v2 时序数据库
    /// <para>数据格式：Line Protocol (measurement,tag=val field=val timestamp)</para>
    /// <para>行业标准：配合 Grafana 可构建完整的实时监控仪表板</para>
    /// </summary>
    public class InfluxDbOutputPlugin : OutputPluginBase
    {
        private readonly InfluxDbOutputConfig _config;
        private CancellationTokenSource _cts;
        private HttpClient _httpClient;

        /// <summary>根据配置创建 HttpClient，支持 TLS/HTTPS 连接</summary>
        private HttpClient CreateHttpClient()
        {
            if (!_config.UseTls)
            {
#if NETCOREAPP2_1_OR_GREATER
                var simpleHandler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
                return new HttpClient(simpleHandler);
#else
                return new HttpClient();
#endif
            }

            // TLS 模式：配置 SslOptions
#if NETCOREAPP2_1_OR_GREATER
            var sslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
            };

            if (_config.InsecureSkipVerify)
            {
                sslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }

            if (!string.IsNullOrEmpty(_config.CaCertificatePath) && System.IO.File.Exists(_config.CaCertificatePath))
            {
                var caCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(_config.CaCertificatePath);
                // 将自定义 CA 加入证书验证链
                sslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None) return true;
                    var chainBuilder = new System.Security.Cryptography.X509Certificates.X509Chain
                    {
                        ChainPolicy = { ExtraStore = { caCert }, VerificationFlags = System.Security.Cryptography.X509Certificates.X509VerificationFlags.AllowUnknownCertificateAuthority }
                    };
                    return chainBuilder.Build(new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate));
                };
            }

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                SslOptions = sslOptions
            };
            return new HttpClient(handler);
#else
            // .NET Framework: 使用 ServicePointManager 配置 TLS
            if (_config.InsecureSkipVerify)
            {
                System.Net.ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            return new HttpClient();
#endif
        }

        public InfluxDbOutputPlugin(InfluxDbOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
            _httpClient = CreateHttpClient();
        }
        private readonly object _batchLock = new();
        private readonly List<string> _batchLines = new();
        private Timer _batchTimer;
        private readonly TimeSpan _batchInterval = TimeSpan.FromMilliseconds(500); // 500ms 批量窗口

        public override string Name =>
            string.IsNullOrWhiteSpace(_config.Name)
                ? $"InfluxDB-{(Uri.TryCreate(_config.Url, UriKind.Absolute, out var uri) ? uri.Host : _config.Url)}"
                : _config.Name;

        public override string ProtocolType => "InfluxDB";

        protected override Task OnStartAsync(CancellationToken ct)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // 如果启用批量写入，启动定时器
            if (_config.EnableBatchWrite)
            {
                _batchTimer = new Timer(
                    callback: async _ => 
                    {
                        try { await FlushBatchAsync().ConfigureAwait(false); }
                        catch { /* Timer 回调中忽略错误，避免未观察异常 */ }
                    },
                    state: null,
                    dueTime: _batchInterval,
                    period: _batchInterval);
            }

            WriteApiUrl = $"{_config.Url.TrimEnd('/')}/api/v2/write" +
                $"?org={Uri.EscapeDataString(_config.Organization)}" +
                $"&bucket={Uri.EscapeDataString(_config.Bucket)}" +
                $"&precision=ms";

            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy,
                $"InfluxDB output ready: {_config.Url}, org={_config.Organization}, bucket={_config.Bucket}");

            return Task.CompletedTask;
        }

        /// <summary>InfluxDB v2 Write API URL（在 OnStartAsync 中初始化）</summary>
        private string WriteApiUrl { get; set; }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null)
                return;

            try
            {
                // 根据消息来源决定如何转换和写入
                var lines = ConvertMessageToLineProtocol(message);
                if (lines == null || lines.Count == 0)
                    return;

                if (_config.EnableBatchWrite)
                {
                    // 批量模式：写入缓冲区
                    lock (_batchLock)
                    {
                        _batchLines.AddRange(lines);
                    }
                }
                else
                {
                    // 立即模式：直接发送
                    await WriteLinesAsync(lines);
                }
            }
            catch (Exception ex)
            {
                GatewayLog.Error("InfluxDbOutput", $"Send error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 将 Message 转换为 InfluxDB Line Protocol 行
        /// <para>Line Protocol 格式: measurement,tag1=val1,tag2=val2 field1=val1,field2=val2 timestamp</para>
        /// </summary>
        private List<string>? ConvertMessageToLineProtocol(Message message)
        {
            // 优先使用结构化 Writes
            if (message.Writes?.Count > 0)
            {
                var lines = new List<string>();
                var ts = new DateTimeOffset(message.Timestamp.ToUniversalTime()).ToUnixTimeMilliseconds();
                foreach (var write in message.Writes)
                {
                    var metricName = write.Alias ?? write.Address;
                    var line = BuildLineProtocol(
                        _config.DefaultMeasurement,
                        tags: new Dictionary<string, string>
                        {
                            ["address"] = write.Address,
                            ["dataType"] = write.DataType
                        },
                        fields: new Dictionary<string, object>
                        {
                            ["value"] = write.Value
                        },
                        ts);
                    lines.Add(line);
                }
                return lines;
            }

            if (message.ContentType != "json" || message.Payload == null || message.Payload.Length == 0)
                return null;

            try
            {
                var json = Encoding.UTF8.GetString(message.Payload);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var source = root.TryGetProperty("source", out var srcEl) ? srcEl.GetString() : "unknown";
                var timestamp = root.TryGetProperty("timestamp", out var tsEl) && DateTime.TryParse(tsEl.GetString(), out var dt)
                    ? new DateTimeOffset(dt).ToUnixTimeMilliseconds()
                    : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (source == "PlcMemory" && _config.WriteMemoryChanges)
                {
                    // MemoryChanged 消息 → InfluxDB Line Protocol
                    // measurement: plc_memory
                    // tags: areaCode, dbNumber, address
                    // fields: hex, intValue
                    var areaCode = root.TryGetProperty("areaCode", out var areaEl) ? areaEl.GetByte() : 0;
                    var dbNumber = root.TryGetProperty("dbNumber", out var dbEl) ? dbEl.GetInt32() : 0;
                    var address = root.TryGetProperty("address", out var addrEl) ? addrEl.GetInt32() : 0;
                    var hex = root.TryGetProperty("hex", out var hexEl) ? hexEl.GetString() : "";
                    var intValue = root.TryGetProperty("intValue", out var intEl)
                        ? (intEl.ValueKind == JsonValueKind.Null ? null : intEl.ToString())
                        : null;

                    var line = BuildLineProtocol(
                        _config.DefaultMeasurement,
                        tags: new Dictionary<string, string>
                        {
                            ["areaCode"] = $"0x{areaCode:X2}",
                            ["dbNumber"] = dbNumber.ToString(),
                            ["address"] = address.ToString()
                        },
                        fields: new Dictionary<string, object>
                        {
                            ["hex"] = hex ?? "",
                            ["value"] = (object)intValue ?? 0
                        },
                        timestamp);

                    return new List<string> { line };
                }
                else if (source == "TagRegistry" && _config.WriteTagChanges)
                {
                    // TagChanged 消息 → InfluxDB Line Protocol
                    // measurement: plc_tag
                    // tags: tagName, dataType
                    // fields: value
                    var tagName = root.TryGetProperty("tagName", out var tagEl) ? tagEl.GetString() : "unknown";
                    var dataType = root.TryGetProperty("dataType", out var dtEl) ? dtEl.GetString() : "unknown";
                    var value = root.TryGetProperty("value", out var valEl) ? ExtractValue(valEl) : 0;

                    var line = BuildLineProtocol(
                        _config.TagMeasurement,
                        tags: new Dictionary<string, string>
                        {
                            ["tagName"] = EscapeTagValue(tagName ?? "unknown"),
                            ["dataType"] = dataType ?? "unknown"
                        },
                        fields: new Dictionary<string, object>
                        {
                            ["value"] = value
                        },
                        timestamp);

                    return new List<string> { line };
                }

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// 构建 InfluxDB Line Protocol 行
        /// </summary>
        private static string BuildLineProtocol(
            string measurement,
            Dictionary<string, string> tags,
            Dictionary<string, object> fields,
            long timestampMs)
        {
            // 1) measurement + tags（逗号分隔）
            var sb = new StringBuilder();
            sb.Append(EscapeMeasurement(measurement));

            foreach (var tag in tags)
            {
                sb.Append(',');
                sb.Append(EscapeKey(tag.Key));
                sb.Append('=');
                sb.Append(tag.Value); // tag value 已预先转义
            }

            // 2) 空格 + fields（逗号分隔）
            sb.Append(' ');
            bool first = true;
            foreach (var field in fields)
            {
                if (!first) sb.Append(',');
                sb.Append(EscapeKey(field.Key));
                sb.Append('=');
                sb.Append(FormatFieldValue(field.Value));
                first = false;
            }

            // 3) 空格 + timestamp（毫秒）
            sb.Append(' ');
            sb.Append(timestampMs);

            return sb.ToString();
        }

        /// <summary>
        /// 将 InfluxDB Line Protocol 行通过 HTTP API 写入
        /// </summary>
        private async Task WriteLinesAsync(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return;

            var body = string.Join("\n", lines);
            using var content = new StringContent(body, Encoding.UTF8, "text/plain");
            using var request = new HttpRequestMessage(HttpMethod.Post, WriteApiUrl)
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Token {_config.Token}");

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_config.TimeoutMs));
            var response = await _httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"InfluxDB write failed: {(int)response.StatusCode} {response.ReasonPhrase} - {errorBody}");
            }
        }

        /// <summary>
        /// 批量刷新：将缓冲区中的行写入 InfluxDB
        /// </summary>
        private async Task FlushBatchAsync()
        {
            List<string> linesToWrite;
            lock (_batchLock)
            {
                if (_batchLines.Count == 0)
                    return;

                linesToWrite = new List<string>(_batchLines);
                _batchLines.Clear();
            }

            try
            {
                // 如果行数超过最大限制，分批写入
                if (linesToWrite.Count <= _config.MaxBatchLines)
                {
                    await WriteLinesAsync(linesToWrite);
                }
                else
                {
                    for (int i = 0; i < linesToWrite.Count; i += _config.MaxBatchLines)
                    {
                        var batch = linesToWrite.GetRange(i, Math.Min(_config.MaxBatchLines, linesToWrite.Count - i));
                        await WriteLinesAsync(batch);
                    }
                }

                SetConnectionState(true, OutputPluginHealthLevel.Healthy);
            }
            catch (Exception ex)
            {
                GatewayLog.Error("InfluxDbOutput", $"Batch flush error: {ex.Message}");
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, $"Batch flush failed: {ex.Message}");
                // 不重抛，避免破坏 SendAsync 契约
            }
        }

        #region InfluxDB Line Protocol 辅助方法

        /// <summary>
        /// 转义 measurement 名称
        /// </summary>
        private static string EscapeMeasurement(string value)
        {
            return value.Replace(" ", "\\ ").Replace(",", "\\,");
        }

        /// <summary>
        /// 转义 tag key / field key
        /// </summary>
        private static string EscapeKey(string value)
        {
            return value.Replace(" ", "\\ ").Replace(",", "\\,").Replace("=", "\\=");
        }

        /// <summary>
        /// 转义 tag value（已由外部调用 EscapeTagValue 预转义，此处仅做安全检查）
        /// </summary>
        private static string EscapeTagValue(string value)
        {
            return value.Replace(" ", "\\ ").Replace(",", "\\,").Replace("=", "\\=");
        }

        /// <summary>
        /// 格式化 field 值
        /// <para>字符串用双引号，数值原样，布尔 true/false</para>
        /// </summary>
        private static string FormatFieldValue(object value)
        {
            return value switch
            {
                int i => i.ToString(),
                long l => l.ToString(),
                double d => d.ToString("G"),
                float f => f.ToString("G"),
                decimal m => m.ToString("G"),
                bool b => b ? "true" : "false",
                string s => $"\"{EscapeFieldString(s)}\"",
                null => "0",
                _ => $"\"{EscapeFieldString(value.ToString())}\""
            };
        }

        /// <summary>
        /// 转义字符串 field 值中的特殊字符
        /// </summary>
        private static string EscapeFieldString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// 从 JsonElement 提取数值（支持 int/long/double/string 数字）
        /// </summary>
        private static object ExtractValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt64(out var l) => l,
                JsonValueKind.Number when element.TryGetDouble(out var d) => d,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.String when double.TryParse(element.GetString(), out var d) => d,
                JsonValueKind.String => element.GetString() ?? "",
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => 0,
                _ => element.GetRawText()
            };
        }

        #endregion

        protected override async Task OnStopAsync()
        {
            _cts?.Cancel();

            // 停止定时器并刷新剩余缓冲区
            _batchTimer?.Dispose();
            _batchTimer = null;

            // 关闭前刷新剩余数据
            if (_config.EnableBatchWrite)
            {
                try
                {
                    await FlushBatchAsync().ConfigureAwait(false);
                }
                catch
                {
                    // 忽略关闭时的刷新错误
                }
            }

            _cts?.Dispose();
            _cts = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _batchTimer?.Dispose();
                _batchTimer = null;

                _cts?.Dispose();
                _cts = null;
            }

            base.Dispose(disposing);
        }
    }
}
