// ============================================================
// 文件：GatewayManagementApi.cs
// 描述：网关管理 API 处理器 — 处理 /api/ 前缀的 HTTP 管理请求
// 来源：从 GatewayHealthCheckHttpService 拆分，避免 HTTP 服务类膨胀
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 网关管理 API 处理器。
    /// 路由 /api/ 前缀的请求到对应的管理操作。
    /// </summary>
    internal class GatewayManagementApi
    {
        private readonly GatewayManager _manager;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public GatewayManagementApi(GatewayManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>
        /// 处理管理 API 请求。
        /// 返回 (HttpStatusCode, ContentType, Body)。
        /// </summary>
        public async Task<(int StatusCode, string ContentType, string Body)> HandleAsync(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath ?? "/";
                var method = context.Request.HttpMethod;

                // --- 插件管理 ---
                if (path == "/api/plugins" || path == "/api/plugins/")
                {
                    if (method != "GET") return MethodNotAllowed();
                    return OkJson(SerializePluginsList());
                }

                if (path.StartsWith("/api/plugins/") && path.EndsWith("/start"))
                {
                    if (method != "POST") return MethodNotAllowed();
                    var name = ExtractPluginName(path, "/api/plugins/", "/start");
                    if (name == null) return BadRequest("Invalid plugin name");
                    var success = await _manager.StartOutputAsync(name);
                    return success ? OkJson(new { success = true, message = $"Plugin '{name}' started" })
                                   : NotFoundJson($"Plugin '{name}' not found");
                }

                if (path.StartsWith("/api/plugins/") && path.EndsWith("/stop"))
                {
                    if (method != "POST") return MethodNotAllowed();
                    var name = ExtractPluginName(path, "/api/plugins/", "/stop");
                    if (name == null) return BadRequest("Invalid plugin name");
                    var success = await _manager.StopOutputAsync(name);
                    return success ? OkJson(new { success = true, message = $"Plugin '{name}' stopped" })
                                   : NotFoundJson($"Plugin '{name}' not found");
                }

                if (path.StartsWith("/api/plugins/") && path.EndsWith("/restart"))
                {
                    if (method != "POST") return MethodNotAllowed();
                    var name = ExtractPluginName(path, "/api/plugins/", "/restart");
                    if (name == null) return BadRequest("Invalid plugin name");
                    // Restart = stop + start
                    await _manager.StopOutputAsync(name);
                    var success = await _manager.StartOutputAsync(name);
                    return success ? OkJson(new { success = true, message = $"Plugin '{name}' restarted" })
                                   : NotFoundJson($"Plugin '{name}' not found for restart");
                }

                // --- 死信管理 ---
                if (path == "/api/dead-letters" || path == "/api/dead-letters/")
                {
                    if (method != "GET") return MethodNotAllowed();
                    return OkJson(new
                    {
                        count = _manager.GetDeadLetters().Count,
                        messages = _manager.GetDeadLetters()
                    });
                }

                if (path == "/api/dead-letters/retry" || path == "/api/dead-letters/retry/")
                {
                    if (method != "POST") return MethodNotAllowed();
                    var retried = await _manager.RetryDeadLettersAsync();
                    return OkJson(new { success = true, retried });
                }

                if (path == "/api/dead-letters/clear" || path == "/api/dead-letters/clear/")
                {
                    if (method != "POST") return MethodNotAllowed();
                    _manager.ClearDeadLetterQueue();
                    return OkJson(new { success = true, message = "Dead letter queue cleared" });
                }

                // --- 断路器管理 ---
                if (path.StartsWith("/api/circuit-breaker/") && path.EndsWith("/reset"))
                {
                    if (method != "POST") return MethodNotAllowed();
                    var name = ExtractPluginName(path, "/api/circuit-breaker/", "/reset");
                    if (name == null) return BadRequest("Invalid plugin name");
                    var success = _manager.ResetCircuitBreaker(name);
                    return success ? OkJson(new { success = true, message = $"Circuit breaker reset for '{name}'" })
                                   : NotFoundJson($"Output '{name}' not found");
                }

                // --- 配置管理 ---
                if (path == "/api/config" || path == "/api/config/")
                {
                    if (method != "GET") return MethodNotAllowed();
                    return OkJson(GetCurrentConfig());
                }

                if (path == "/api/config/reload" || path == "/api/config/reload/")
                {
                    if (method != "POST") return MethodNotAllowed();
                    var body = await ReadRequestBody(context);
                    if (string.IsNullOrEmpty(body)) return BadRequest("Request body required: provide new config JSON");

                    try
                    {
                        var newOptions = JsonSerializer.Deserialize<GatewayManagerOptions>(body, JsonOptions);
                        if (newOptions == null) return BadRequest("Invalid config JSON");
                        await _manager.ReloadPipelineConfigAsync(newOptions);
                        return OkJson(new { success = true, message = "Pipeline configuration reloaded" });
                    }
                    catch (JsonException ex)
                    {
                        return BadRequest($"Invalid JSON: {ex.Message}");
                    }
                }

                // --- 链路追踪 ---
                if (path == "/api/traces" || path == "/api/traces/")
                {
                    if (method != "GET") return MethodNotAllowed();
                    var traces = _manager.Pipeline.GetRecentTraces(50);
                    return OkJson(new
                    {
                        count = traces.Count,
                        traces = traces.Select(t => new
                        {
                            t.TraceId,
                            t.Stage,
                            t.OutputName,
                            t.Result,
                            t.Timestamp
                        })
                    });
                }

                // --- 测试发送 ---
                if (path.StartsWith("/api/test-send/"))
                {
                    if (method != "POST") return MethodNotAllowed();
                    var name = ExtractPluginName(path, "/api/test-send/");
                    if (name == null) return BadRequest("Invalid plugin name");

                    var payload = await ReadRequestBody(context);
                    var result = await _manager.TestSendAsync(name, payload);
                    return OkJson(result);
                }

                // --- 协议分析 Tap ---
                if (path == "/api/tap" || path == "/api/tap/")
                {
                    if (method != "GET") return MethodNotAllowed();
                    return OkJson(_manager.Pipeline.GetTapSnapshot());
                }

                return (404, "application/json", JsonSerializer.Serialize(new { error = "API endpoint not found" }, JsonOptions));
            }
            catch (Exception ex)
            {
                GatewayLog.Error("ManagementApi", $"Request handling error: {ex.Message}", ex);
                return (500, "application/json", JsonSerializer.Serialize(new { error = "Internal server error", detail = ex.Message }, JsonOptions));
            }
        }

        #region Helpers

        private string SerializePluginsList()
        {
            var statuses = _manager.GetOutputPluginStatuses();
            return JsonSerializer.Serialize(new
            {
                count = statuses.Count,
                plugins = statuses.Select(s => new
                {
                    s.Name,
                    s.ProtocolType,
                    s.Status,
                    s.IsRunning,
                    s.CircuitBreakerState,
                    healthLevel = s.HealthLevel.ToString().ToLower(),
                    s.Message,
                    s.ErrorCode,
                    s.Advice,
                    s.ConsecutiveFailures,
                    s.Timestamp
                })
            }, JsonOptions);
        }

        private object GetCurrentConfig()
        {
            var pipeline = _manager.Pipeline;
            return new
            {
                sendTimeoutMs = pipeline.SendTimeoutMs,
                maxRetryAttempts = pipeline.MaxRetryAttempts,
                retryBaseDelayMs = pipeline.RetryBaseDelayMs,
                queueCapacity = pipeline.QueueCapacity,
                circuitBreakerFailureThreshold = pipeline.CircuitBreakerFailureThreshold,
                circuitBreakerRecoveryTimeMs = pipeline.CircuitBreakerRecoveryTimeMs,
                registeredOutputs = _manager.RegisteredOutputNames,
                isRunning = _manager.IsRunning
            };
        }

        private static string? ExtractPluginName(string path, string prefix, string suffix = "")
        {
            var start = path.IndexOf(prefix) + prefix.Length;
            var end = suffix.Length > 0 ? path.IndexOf(suffix) : path.Length;
            if (start < 0 || end < 0 || end <= start) return null;
            return Uri.UnescapeDataString(path.Substring(start, end - start));
        }

        private static async Task<string> ReadRequestBody(HttpListenerContext context)
        {
            if (context.Request.ContentLength64 <= 0) return string.Empty;
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private static (int, string, string) OkJson(object data)
        {
            return (200, "application/json", JsonSerializer.Serialize(data, JsonOptions));
        }

        private static (int, string, string) BadRequest(string message)
        {
            return (400, "application/json", JsonSerializer.Serialize(new { error = "Bad request", message }, JsonOptions));
        }

        private static (int, string, string) MethodNotAllowed()
        {
            return (405, "application/json", JsonSerializer.Serialize(new { error = "Method not allowed" }, JsonOptions));
        }

        private static (int, string, string) NotFoundJson(string message)
        {
            return (404, "application/json", JsonSerializer.Serialize(new { error = "Not found", message }, JsonOptions));
        }

        #endregion
    }
}
