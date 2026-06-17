// ============================================================
//  ZL.Iot.Runner.Cli
//  -------------------------------------------------------------
//  统一入口：
//  1. run [config]                 加载配置并启动 DeviceRunner
//  2. generate --config ...         单次生成部署包
//  3. serve                         启动 Generator HTTP 服务
// ============================================================

using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Generator.Core;
using ZL.Iot.Runner.Generator.Core.Models;
using ZL.Iot.Runner.Runtime;

namespace ZL.Iot.Runner.Cli;

public static class Program
{
    private const string ApiKeyHeader = "X-API-Key";
    private static readonly string RequiredApiKey = GetApiKey();

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        if (args.Length == 0)
        {
            return RunRunner(args);
        }

        var command = args[0];
        if (command.Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            return RunRunner(args[1..]);
        }

        if (command.Equals("generate", StringComparison.OrdinalIgnoreCase))
        {
            return await RunOnceAsync(args[1..]);
        }

        if (IsServeCommand(command))
        {
            return await RunServerAsync(args[1..]);
        }

        // 兼容旧用法：ZL.Iot.Runner.Cli runner.config.json
        return RunRunner(args);
    }

    /// <summary>
    /// 加载 runner.config.json/xml 并启动 DeviceRunner。
    /// </summary>
    private static int RunRunner(string[] args)
    {
        var configPath = ResolveConfigPath(args);

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = false;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger("ZL.Iot.Runner.Cli");

        logger.LogInformation("========================================");
        logger.LogInformation("ZL.Iot.Runner.Cli 启动 Runner 模式");
        logger.LogInformation("配置文件: {ConfigPath}", configPath);
        logger.LogInformation("========================================");

        RunnerConfig config;
        try
        {
            config = ConfigLoader.Load(configPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "配置加载失败");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

        try
        {
            var runner = new DeviceRunner(config, loggerFactory);
            runner.Run(cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Runner 异常退出");
            return 2;
        }

        logger.LogInformation("ZL.Iot.Runner.Cli Runner 模式优雅退出");
        return 0;
    }

    /// <summary>
    /// 单次 CLI 生成模式。
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
    /// 启动 Generator HTTP 服务模式（带任务队列调度）。
    /// </summary>
    private static async Task<int> RunServerAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var store = new JobStore();

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var logger = loggerFactory.CreateLogger<JobScheduler>();
        var scheduler = new JobScheduler(
            store,
            logger,
            maxConcurrency: 2,
            maxQueueLength: 50,
            jobTimeout: TimeSpan.FromSeconds(120),
            userRateLimitInterval: TimeSpan.FromSeconds(10));
        builder.Services.AddSingleton(scheduler);

        var app = builder.Build();

        app.MapPost("/api/generator/generate", async Task<IResult> (HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            if (ValidateApiKey(httpRequest) is { } unauthorized)
            {
                return unauthorized;
            }

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
                    request.Config = JsonSerializer.Deserialize<RunnerConfig>(configEl.GetRawText()) ?? new RunnerConfig();
                }

                var userId = httpRequest.Headers["X-User-Id"].ToString();
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

        app.MapGet("/api/generator/job/{jobId:guid}", (Guid jobId, HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            if (ValidateApiKey(httpRequest) is { } unauthorized)
            {
                return unauthorized;
            }

            var job = scheduler.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound(new { error = $"任务 {jobId} 不存在或已过期" });
            }

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
                hasResult = job.ResultBytes is not null,
                queuePosition = job.Status == JobStatus.Queued ? job.QueuePosition : (int?)null
            });
        });

        app.MapGet("/api/generator/job/{jobId:guid}/download", (Guid jobId, HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            if (ValidateApiKey(httpRequest) is { } unauthorized)
            {
                return unauthorized;
            }

            var job = scheduler.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound(new { error = $"任务 {jobId} 不存在或已过期" });
            }

            if (job.Status != JobStatus.Succeeded)
            {
                return Results.BadRequest(new { error = $"任务未完成，当前状态: {job.Status}" });
            }

            if (job.ResultBytes is null)
            {
                return Results.NotFound(new { error = "结果文件已过期，请重新生成" });
            }

            return Results.File(
                job.ResultBytes,
                "application/zip",
                job.ResultFileName ?? "output.zip");
        });

        app.MapPost("/api/generator/job/{jobId:guid}/cancel", (Guid jobId, HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            if (ValidateApiKey(httpRequest) is { } unauthorized)
            {
                return unauthorized;
            }

            var userId = httpRequest.Headers["X-User-Id"].ToString();
            var cancelled = scheduler.CancelJob(jobId, userId);

            if (!cancelled)
            {
                return Results.BadRequest(new { error = "任务无法取消（不存在、非本人、或已结束）" });
            }

            return Results.Ok(new { message = "任务已取消", jobId });
        });

        app.MapGet("/api/generator/job/{jobId:guid}/stream", async Task<IResult> (Guid jobId, HttpContext httpContext, JobScheduler scheduler) =>
        {
            if (ValidateApiKey(httpContext.Request) is { } unauthorized)
            {
                return unauthorized;
            }

            var response = httpContext.Response;
            var job = scheduler.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound(new { error = $"任务 {jobId} 不存在或已过期" });
            }

            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";

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

            if (job.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.TimedOut)
            {
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
                await foreach (var evt in job.WatchStatusAsync())
                {
                    await WriteSseEvent(response, "status", evt);
                    await response.Body.FlushAsync();

                    if (evt.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.TimedOut)
                    {
                        await WriteSseEvent(response, "end", new JobStatusEvent
                        {
                            Timestamp = DateTime.UtcNow,
                            Status = evt.Status
                        });
                        await response.Body.FlushAsync();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开连接，SSE 直接结束。
            }
            catch
            {
                // 连接异常不影响后台任务，关闭当前 SSE 响应即可。
            }

            return Results.Ok();
        });

        app.MapGet("/api/generator/jobs", (HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            if (ValidateApiKey(httpRequest) is { } unauthorized)
            {
                return unauthorized;
            }

            var userId = httpRequest.Headers["X-User-Id"].ToString();
            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest(new { error = "需要用户标识（X-User-Id header）" });
            }

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
                    hasResult = j.ResultBytes is not null
                })
            });
        });

        app.MapGet("/api/generator/stats", (HttpRequest httpRequest, JobScheduler scheduler) =>
        {
            if (ValidateApiKey(httpRequest) is { } unauthorized)
            {
                return unauthorized;
            }

            return Results.Ok(new
            {
                maxConcurrency = scheduler.MaxConcurrency,
                queuedCount = scheduler.QueuedCount,
                runningCount = scheduler.RunningCount,
                totalActive = scheduler.QueuedCount + scheduler.RunningCount
            });
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        Console.WriteLine("ZL.Iot.Runner.Cli serve 启动（Generator 调度 + SSE）");
        Console.WriteLine("API:");
        Console.WriteLine("  POST /api/generator/generate          提交生成任务");
        Console.WriteLine("  GET  /api/generator/job/{id}          查询任务状态");
        Console.WriteLine("  GET  /api/generator/job/{id}/stream   SSE 实时推送");
        Console.WriteLine("  GET  /api/generator/job/{id}/download 下载结果");
        Console.WriteLine("  POST /api/generator/job/{id}/cancel   取消任务");
        Console.WriteLine("  GET  /api/generator/jobs              用户任务列表");
        Console.WriteLine("  GET  /api/generator/stats             调度器统计");
        Console.WriteLine("  GET  /health                          健康检查");

        await app.RunAsync();
        return 0;
    }

    private static GenerateJob[] GetJobsByUser(JobScheduler scheduler, string userId)
    {
        return scheduler.ListJobsByUser(userId);
    }

    /// <summary>
    /// 写入 SSE 格式事件到响应流。
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
        await response.Body.WriteAsync(bytes);
        await response.Body.FlushAsync();
    }

    /// <summary>
    /// 解析配置文件路径。
    /// 优先级：命令行参数 > ./runner.config.json > ./runner.config.xml
    /// </summary>
    private static string ResolveConfigPath(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return args[0];
        }

        foreach (var candidate in new[] { "./runner.config.json", "./runner.config.xml" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "./runner.config.json";
    }

    /// <summary>
    /// 解析 Generator 单次生成参数。
    /// </summary>
    private static GenerateRequest ParseCliArgs(string[] args)
    {
        var request = new GenerateRequest();
        string? configPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].TrimStart('-');

            switch (arg.ToLowerInvariant())
            {
                case "config":
                    if (i + 1 < args.Length)
                    {
                        configPath = args[++i];
                    }
                    break;
                case "platform":
                    if (i + 1 < args.Length)
                    {
                        request.Platform = ParsePlatform(args[++i]);
                    }
                    break;
                case "sku":
                    if (i + 1 < args.Length)
                    {
                        request.Sku = ParseSku(args[++i]);
                    }
                    break;
                case "runtime":
                case "runtimeidentifier":
                    if (i + 1 < args.Length)
                    {
                        request.RuntimeIdentifier = args[++i];
                    }
                    break;
                case "name":
                    if (i + 1 < args.Length)
                    {
                        request.ProjectName = args[++i];
                    }
                    break;
                case "version":
                    if (i + 1 < args.Length)
                    {
                        request.Version = args[++i];
                    }
                    break;
            }
        }

        if (!string.IsNullOrEmpty(configPath))
        {
            request.Config = ConfigLoader.Load(configPath);
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

    private static bool IsHelp(string value)
    {
        return value is "-h" or "--help" or "help";
    }

    private static bool IsServeCommand(string value)
    {
        return value.Equals("serve", StringComparison.OrdinalIgnoreCase)
            || value.Equals("server", StringComparison.OrdinalIgnoreCase)
            || value.Equals("generator-server", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetApiKey()
    {
        return Environment.GetEnvironmentVariable("ZL_GENERATOR_API_KEY")
            ?? "change-me-in-production";
    }

    private static IResult? ValidateApiKey(HttpRequest request)
    {
        var apiKey = request.Headers[ApiKeyHeader].ToString();
        if (string.IsNullOrEmpty(apiKey) || apiKey != RequiredApiKey)
        {
            return Results.Json(new { error = "缺少或无效的 API Key，请通过 X-API-Key Header 提供" }, statusCode: 401);
        }

        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ZL.Iot.Runner.Cli");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ZL.Iot.Runner.Cli [configPath]");
        Console.WriteLine("  ZL.Iot.Runner.Cli run [configPath]");
        Console.WriteLine("  ZL.Iot.Runner.Cli generate --config path --platform console --sku binary --runtime win-x64 --name MyPlc");
        Console.WriteLine("  ZL.Iot.Runner.Cli serve");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  run       加载 Runner 配置并启动采集运行时");
        Console.WriteLine("  generate 生成一次部署包并输出 zip");
        Console.WriteLine("  serve    启动 Generator HTTP 服务");
    }
}
