// ============================================================
//  ZL.Iot.Runner.GeneratorCli
//  -------------------------------------------------------------
//  HTTP 服务 + CLI 入口
//  模式 1: 启动 Kestrel（异步任务队列 + 状态查询）
//  模式 2: 单次 CLI 执行（开发期调试用）
// ============================================================

using System.Text.Json;
using ZL.Iot.Runner.Generator.Core;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.GeneratorCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("generate", StringComparison.OrdinalIgnoreCase))
        {
            return await RunOnceAsync(args[1..]);
        }

        // 默认：启动 HTTP 服务
        return await RunServerAsync(args);
    }

    /// <summary>
    /// 单次 CLI 执行模式（开发期调试）
    /// generate --config path --platform console --sku binary --runtime win-x64 --name MyPlc
    /// </summary>
    private static async Task<int> RunOnceAsync(string[] args)
    {
        var request = ParseCliArgs(args);
        var generator = new ProjectGenerator();
        var result = await generator.GenerateAsync(request);

        if (!result.Success)
        {
            Console.Error.WriteLine($"生成失败: {result.ErrorMessage}");
            return 1;
        }

        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), result.ZipFileName!);
        await File.WriteAllBytesAsync(outputPath, result.ZipBytes!);
        Console.WriteLine($"生成成功: {outputPath} ({result.ZipBytes!.Length} bytes, {result.Elapsed.TotalSeconds:F1}s)");
        return 0;
    }

    /// <summary>
    /// 启动 HTTP 服务模式（带任务队列调度）
    /// </summary>
    private static async Task<int> RunServerAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 注册调度器为单例
        var scheduler = new JobScheduler(
            maxConcurrency: 2,
            maxQueueLength: 50,
            jobTimeout: TimeSpan.FromSeconds(120),
            userRateLimitInterval: TimeSpan.FromSeconds(10));
        builder.Services.AddSingleton(scheduler);

        var app = builder.Build();

        // ============================================================
        //  API 路由
        // ============================================================

        // POST /api/generator/generate — 提交生成任务（异步，立即返回 jobId）
        app.MapPost("/api/generator/generate", async Task<IResult> (HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            try
            {
                using var jsonDoc = await JsonDocument.ParseAsync(httpRequest.Body);
                var root = jsonDoc.RootElement;

                var request = new GenerateRequest
                {
                    Platform = ParsePlatform(root.GetProperty("platform").GetString() ?? "console"),
                    Sku = ParseSku(root.GetProperty("sku").GetString() ?? "binary"),
                    ProjectName = root.GetProperty("projectName").GetString() ?? "MyPlc",
                    Version = root.GetProperty("version").GetString() ?? "1.0.0"
                };

                if (request.Sku == SkuMode.Binary)
                {
                    request.RuntimeIdentifier = root.GetProperty("runtimeIdentifier").GetString();
                }

                if (root.TryGetProperty("config", out var configEl))
                {
                    request.Config = JsonSerializer.Deserialize<ZL.Iot.Runner.Configuration.RunnerConfig>(
                        configEl.GetRawText()) ?? new ZL.Iot.Runner.Configuration.RunnerConfig();
                }

                // 获取用户标识（从 Header，实际项目应替换为认证中间件）
                var userId = httpRequest.Headers["X-User-Id"].ToString();

                // 入队（立即返回）
                var (success, error, job) = scheduler.Enqueue(request, userId);

                if (!success)
                {
                    return Results.BadRequest(new { error });
                }

                return Results.Accepted($"/api/generator/job/{job!.Id}", new
                {
                    jobId = job.Id,
                    status = job.Status.ToString(),
                    queuePosition = job.QueuePosition,
                    message = $"任务已提交，当前排队位置: {job.QueuePosition}"
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // GET /api/generator/job/{jobId} — 查询任务状态
        app.MapGet("/api/generator/job/{jobId:guid}", (Guid jobId, JobScheduler scheduler) =>
        {
            var job = scheduler.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new { error = $"任务 {jobId} 不存在或已过期" });

            return Results.Ok(new
            {
                jobId = job.Id,
                status = job.Status.ToString(),
                statusText = job.StatusText,
                createdAt = job.CreatedAt,
                startedAt = job.StartedAt,
                completedAt = job.CompletedAt,
                elapsed = job.Elapsed?.TotalSeconds,
                errorMessage = job.ErrorMessage,
                resultFileName = job.ResultFileName,
                hasResult = job.ResultBytes != null,
                queuePosition = job.Status == JobStatus.Queued ? job.QueuePosition : (int?)null
            });
        });

        // GET /api/generator/job/{jobId}/download — 下载生成结果
        app.MapGet("/api/generator/job/{jobId:guid}/download", (Guid jobId, JobScheduler scheduler) =>
        {
            var job = scheduler.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new { error = $"任务 {jobId} 不存在或已过期" });

            if (job.Status != JobStatus.Succeeded)
                return Results.BadRequest(new { error = $"任务未完成，当前状态: {job.Status}" });

            if (job.ResultBytes == null)
                return Results.NotFound(new { error = "结果文件已过期，请重新生成" });

            return Results.File(
                job.ResultBytes,
                "application/zip",
                job.ResultFileName ?? "output.zip");
        });

        // POST /api/generator/job/{jobId}/cancel — 取消任务
        app.MapPost("/api/generator/job/{jobId:guid}/cancel", (Guid jobId, HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            var userId = httpRequest.Headers["X-User-Id"].ToString();
            var cancelled = scheduler.CancelJob(jobId, userId);

            if (!cancelled)
                return Results.BadRequest(new { error = "任务无法取消（不存在、非本人、或已结束）" });

            return Results.Ok(new { message = "任务已取消", jobId });
        });

        // GET /api/generator/job/{jobId}/stream — SSE 实时状态推送
        app.MapGet("/api/generator/job/{jobId:guid}/stream", async Task<IResult> (Guid jobId, HttpContext httpContext, JobScheduler scheduler) =>
        {
            var response = httpContext.Response;
            var job = scheduler.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new { error = $"任务 {jobId} 不存在或已过期" });

            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";

            // 发送当前状态快照（无论任务是否完成，客户端都能收到最新状态）
            var snapshot = new JobStatusEvent
            {
                Timestamp = DateTime.UtcNow,
                Status = job.Status,
                Message = job.StatusText,
                ProgressPercent = job.Status switch
                {
                    JobStatus.Succeeded => 100,
                    JobStatus.Failed or JobStatus.Cancelled or JobStatus.TimedOut => 0,
                    JobStatus.Running => 50,
                    _ => job.QueuePosition > 0 ? 0 : 0
                },
                Phase = job.Status switch
                {
                    JobStatus.Succeeded => "complete",
                    JobStatus.Failed => "error",
                    JobStatus.Cancelled => "cancelled",
                    JobStatus.TimedOut => "timeout",
                    JobStatus.Running => "running",
                    _ => "queued"
                },
                ErrorMessage = job.ErrorMessage,
                ResultFileName = job.ResultFileName,
                ElapsedSeconds = job.Elapsed?.TotalSeconds
            };
            await WriteSseEvent(response, "status", snapshot);
            await response.Body.FlushAsync();

            // 如果任务已到达终态，直接关闭连接
            if (job.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.TimedOut)
            {
                // 发送 end 事件通知客户端
                await WriteSseEvent(response, "end", new JobStatusEvent
                {
                    Timestamp = DateTime.UtcNow,
                    Status = job.Status
                });
                await response.Body.FlushAsync();
                return Results.Ok();
            }

            try
            {
                // 流式订阅后续状态变更
                await foreach (var evt in job.WatchStatusAsync())
                {
                    await WriteSseEvent(response, "status", evt);
                    await response.Body.FlushAsync();

                    // 终态后关闭连接
                    if (evt.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.TimedOut)
                    {
                        await WriteSseEvent(response, "end", new JobStatusEvent { Timestamp = DateTime.UtcNow, Status = evt.Status });
                        await response.Body.FlushAsync();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开连接
            }
            catch
            {
                // 连接异常，静默关闭
            }

            return Results.Ok();
        });

        // GET /api/generator/jobs — 查询当前用户最近的任务列表
        app.MapGet("/api/generator/jobs", (HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            var userId = httpRequest.Headers["X-User-Id"].ToString();
            if (string.IsNullOrEmpty(userId))
                return Results.BadRequest(new { error = "需要用户标识（X-User-Id header）" });

            // 通过反射访问 JobStore（或通过 scheduler 暴露方法）
            // 简化方案：直接在 scheduler 上暴露
            var jobs = GetJobsByUser(scheduler, userId);

            return Results.Ok(new
            {
                jobs = jobs.Select(j => new
                {
                    jobId = j.Id,
                    status = j.Status.ToString(),
                    statusText = j.StatusText,
                    projectName = j.Request.ProjectName,
                    platform = j.Request.Platform.ToString(),
                    sku = j.Request.Sku.ToString(),
                    createdAt = j.CreatedAt,
                    elapsed = j.Elapsed?.TotalSeconds,
                    errorMessage = j.ErrorMessage,
                    hasResult = j.ResultBytes != null
                })
            });
        });

        // GET /api/generator/stats — 调度器统计信息
        app.MapGet("/api/generator/stats", (JobScheduler scheduler) =>
        {
            return Results.Ok(new
            {
                maxConcurrency = scheduler.MaxConcurrency,
                queuedCount = scheduler.QueuedCount,
                runningCount = scheduler.RunningCount,
                totalActive = scheduler.QueuedCount + scheduler.RunningCount
            });
        });

        // 健康检查
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        Console.WriteLine("ZL.Iot.Runner.GeneratorCli 启动（带任务调度 + SSE）");
        Console.WriteLine("API:");
        Console.WriteLine("  POST /api/generator/generate       — 提交生成任务");
        Console.WriteLine("  GET  /api/generator/job/{id}       — 查询任务状态");
        Console.WriteLine("  GET  /api/generator/job/{id}/stream — SSE 实时推送");
        Console.WriteLine("  GET  /api/generator/job/{id}/download — 下载结果");
        Console.WriteLine("  POST /api/generator/job/{id}/cancel — 取消任务");
        Console.WriteLine("  GET  /api/generator/jobs           — 用户任务列表");
        Console.WriteLine("  GET  /api/generator/stats          — 调度器统计");
        Console.WriteLine("  GET  /health                       — 健康检查");

        await app.RunAsync();
        return 0;
    }

    /// <summary>
    /// 通过 JobScheduler 内部 JobStore 获取用户任务列表
    /// 使用反射访问私有字段（避免公开内部实现细节）
    /// </summary>
    private static GenerateJob[] GetJobsByUser(JobScheduler scheduler, string userId)
    {
        var storeField = typeof(JobScheduler).GetField("_store",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var store = storeField?.GetValue(scheduler) as JobStore;
        return store?.ListByUserId(userId) ?? Array.Empty<GenerateJob>();
    }

    /// <summary>
    /// 写入 SSE 格式事件到响应流
    /// 格式: event: {eventType}\ndata: {json}\n\n
    /// 注意：必须全异步写入，Kestrel 禁止同步 IO（StreamWriter.Flush 会触发同步 Flush）
    /// </summary>
    private static async Task WriteSseEvent(HttpResponse response, string eventType, JobStatusEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var sseLine = $"event: {eventType}\r\ndata: {json}\r\n\r\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(sseLine);
        await response.Body.WriteAsync(bytes, 0, bytes.Length);
        await response.Body.FlushAsync();
    }

    /// <summary>
    /// 解析 CLI 参数
    /// </summary>
    private static GenerateRequest ParseCliArgs(string[] args)
    {
        var request = new GenerateRequest();
        string? configPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            arg = arg.TrimStart('-');

            switch (arg.ToLowerInvariant())
            {
                case "config":
                    if (i + 1 < args.Length) configPath = args[++i];
                    break;
                case "platform":
                    if (i + 1 < args.Length) request.Platform = ParsePlatform(args[++i]);
                    break;
                case "sku":
                    if (i + 1 < args.Length) request.Sku = ParseSku(args[++i]);
                    break;
                case "runtime":
                case "runtimeidentifier":
                    if (i + 1 < args.Length) request.RuntimeIdentifier = args[++i];
                    break;
                case "name":
                    if (i + 1 < args.Length) request.ProjectName = args[++i];
                    break;
                case "version":
                    if (i + 1 < args.Length) request.Version = args[++i];
                    break;
            }
        }

        if (!string.IsNullOrEmpty(configPath))
        {
            request.Config = ZL.Iot.Runner.Configuration.ConfigLoader.Load(configPath);
        }

        return request;
    }

    private static TargetPlatform ParsePlatform(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "console" => TargetPlatform.Console,
            "windows-service" or "windowservice" => TargetPlatform.WindowsService,
            "linux-systemd" or "linuxsystemd" => TargetPlatform.LinuxSystemd,
            "winform" => TargetPlatform.WinForm,
            "web" => TargetPlatform.Web,
            _ => throw new ArgumentException($"不支持的平台: {value}")
        };
    }

    private static SkuMode ParseSku(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "binary" => SkuMode.Binary,
            "source" => SkuMode.Source,
            _ => throw new ArgumentException($"不支持的 SKU 模式: {value}")
        };
    }
}
