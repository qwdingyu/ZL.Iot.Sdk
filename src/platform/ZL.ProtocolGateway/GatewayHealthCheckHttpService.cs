using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// HTTP 健康检查与管理 API 服务 — 基于 HttpListener 的轻量级 HTTP 端点，无需 ASP.NET。
    /// <para>健康检查端点：</para>
    /// <para>GET /health → 完整健康检查 JSON（包含各插件详情）</para>
    /// <para>GET /health/live → 进程存活检查（K8s Liveness Probe）</para>
    /// <para>GET /health/ready → 就绪检查（K8s Readiness Probe）</para>
    /// <para>GET /metrics → Pipeline 指标快照</para>
    /// <para>管理 API 端点（/api/ 前缀）：</para>
    /// <para>GET /api/plugins → 所有插件状态</para>
    /// <para>POST /api/plugins/{name}/start → 启动输出插件</para>
    /// <para>POST /api/plugins/{name}/stop → 停止输出插件</para>
    /// <para>POST /api/plugins/{name}/restart → 重启输出插件</para>
    /// <para>GET /api/dead-letters → 死信队列</para>
    /// <para>POST /api/dead-letters/retry → 重试死信</para>
    /// <para>POST /api/dead-letters/clear → 清空死信</para>
    /// <para>POST /api/circuit-breaker/{name}/reset → 重置断路器</para>
    /// <para>POST /api/config/reload → 重载配置</para>
    /// <para>GET /api/traces → 最近链路追踪</para>
    /// <para>POST /api/test-send/{name} → 测试发送</para>
    /// <para>GET /api/config → 当前配置</para>
    /// <para>GET /api/tap → 协议分析快照</para>
    /// </summary>
    public class GatewayHealthCheckHttpService : IDisposable, IAsyncDisposable
    {
        private readonly GatewayManager _manager;
        private readonly GatewayManagementApi _managementApi;
        private HttpListener? _listener;
        private Task? _listenTask;
        private long _isRunning; // 0 = false, 1 = true

        /// <summary>
        /// 监听地址，默认 http://+:5000/
        /// </summary>
        public string ListenUrl { get; }

        /// <summary>
        /// 服务是否正在运行。
        /// </summary>
        public bool IsRunning => Interlocked.Read(ref _isRunning) == 1;

        public GatewayHealthCheckHttpService(GatewayManager manager, string? listenUrl = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            ListenUrl = listenUrl ?? "http://+:5000/";
            _managementApi = new GatewayManagementApi(manager);
        }

        /// <summary>
        /// 启动 HTTP 健康检查服务。
        /// <para>注意：在 Windows 上需要管理员权限才能监听任意主机（+），
        /// 在 Linux/macOS 上需要 root 权限或配置 urlacl。</para>
        /// </summary>
        public async Task StartAsync()
        {
            if (Interlocked.Read(ref _isRunning) == 1) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add(ListenUrl);
            _listener.Start();
            Interlocked.Exchange(ref _isRunning, 1);

            _listenTask = Task.Run(async () =>
            {
                while (Interlocked.Read(ref _isRunning) == 1 && _listener.IsListening)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        // fire-and-forget，但观察异常避免静默丢失
                        _ = Task.Run(() => HandleRequestAsync(context)).ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                GatewayLog.Error("HealthCheckHttp", $"Request handler failed: {t.Exception?.GetBaseException().Message}");
                        }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        GatewayLog.Warn("HealthCheckHttp", $"Request handling error: {ex.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// 停止 HTTP 健康检查服务。
        /// </summary>
        public async Task StopAsync()
        {
            Interlocked.Exchange(ref _isRunning, 0);
            _listener?.Stop();
            _listener?.Close();

            if (_listenTask != null)
            {
                try { await Task.WhenAny(_listenTask, Task.Delay(5000)); } catch { }
            }

            _listener = null;
            _listenTask = null;
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath ?? "/";
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = 200;

                string body;

                if (path == "/health/live" || path == "/health/live/")
                {
                    body = HandleLiveness();
                }
                else if (path == "/health/ready" || path == "/health/ready/")
                {
                    var (status, json) = HandleReadiness();
                    context.Response.StatusCode = status;
                    body = json;
                }
                else if (path == "/health" || path == "/health/")
                {
                    body = HandleFullHealth();
                }
                else if (path == "/metrics" || path == "/metrics/")
                {
                    body = HandleMetrics();
                }
                else if (path.StartsWith("/api/"))
                {
                    var (status, contentType, json) = await _managementApi.HandleAsync(context);
                    context.Response.StatusCode = status;
                    context.Response.ContentType = contentType;
                    body = json;
                }
                else
                {
                    context.Response.StatusCode = 404;
                    body = "{\"error\":\"Not found\"}";
                }

                using var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8);
                await writer.WriteAsync(body);
            }
            catch (Exception ex)
            {
                try
                {
                    context.Response.StatusCode = 500;
                    using var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8);
                    await writer.WriteAsync($"{{\"error\":\"Internal server error: {ex.Message}\"}}");
                }
                catch { }
            }
            finally
            {
                context.Response.Close();
            }
        }

        private string HandleLiveness()
        {
            var body = new
            {
                status = "live",
                timestamp = DateTime.UtcNow.ToString("O")
            };
            return JsonSerializer.Serialize(body);
        }

        private (int StatusCode, string Body) HandleReadiness()
        {
            var result = _manager.HealthCheck.Check();
            var isReady = result.IsReady;

            var body = new
            {
                status = isReady ? "ready" : "not_ready",
                totalPlugins = result.TotalPlugins,
                healthy = result.HealthyCount,
                degraded = result.DegradedCount,
                unhealthy = result.UnhealthyCount,
                timestamp = DateTime.UtcNow.ToString("O")
            };

            return (isReady ? 200 : 503, JsonSerializer.Serialize(body));
        }

        private string HandleFullHealth()
        {
            var result = _manager.HealthCheck.Check();

            var body = new
            {
                status = result.Status.ToString().ToLower(),
                description = result.Description,
                totalPlugins = result.TotalPlugins,
                healthy = result.HealthyCount,
                degraded = result.DegradedCount,
                unhealthy = result.UnhealthyCount,
                isReady = result.IsReady,
                isLive = result.IsLive,
                plugins = result.PluginResults.Select(p => new
                {
                    name = p.PluginName,
                    protocolType = p.ProtocolType,
                    status = p.Status.ToString().ToLower(),
                    healthLevel = p.HealthLevel.ToString().ToLower(),
                    message = p.Message,
                    errorCode = p.ErrorCode,
                    consecutiveFailures = p.ConsecutiveFailures
                }),
                timestamp = DateTime.UtcNow.ToString("O")
            };

            return JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });
        }

        private string HandleMetrics()
        {
            var snapshot = _manager.GetMetricsSnapshot();
            var pm = snapshot.PipelineMetrics;

            var body = new
            {
                snapshot.IsRunning,
                queuedMessages = snapshot.QueuedMessageCount,
                deadLetterCount = snapshot.DeadLetterCount,
                circuitBreakerStates = snapshot.CircuitBreakerStates,
                outputPlugins = snapshot.OutputPlugins.Select(p => new
                {
                    p.Name,
                    p.ProtocolType,
                    p.Status,
                    p.IsRunning,
                    p.CircuitBreakerState,
                    healthLevel = p.HealthLevel.ToString().ToLower(),
                    p.Message,
                    p.ErrorCode,
                    p.Advice,
                    p.ConsecutiveFailures
                }),
                pipeline = pm != null ? new
                {
                    pm.TotalProcessed,
                    pm.TotalFiltered,
                    pm.TotalFailed,
                    pm.TotalDeadLetters,
                    pm.LatencyCount,
                    latencyAvgMs = Math.Round(pm.LatencyAvgMs, 2),
                    latencyP50Ms = Math.Round(pm.LatencyP50Ms, 2),
                    latencyP95Ms = Math.Round(pm.LatencyP95Ms, 2),
                    latencyP99Ms = Math.Round(pm.LatencyP99Ms, 2)
                } : null,
                timestamp = DateTime.UtcNow.ToString("O")
            };

            return JsonSerializer.Serialize(body);
        }

        public void Dispose()
        {
            // P1 修复：避免 StopAsync().Wait() 同步死锁。
            // 同步 Dispose 仅停止监听器，不等待后台任务完成。
            Interlocked.Exchange(ref _isRunning, 0);
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
        }

        /// <summary>
        /// 异步释放资源 — 优雅停止 HTTP 监听器。
        /// 优先使用此方法而非同步 Dispose。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _listener?.Close();
            _listener = null;
        }
    }
}
