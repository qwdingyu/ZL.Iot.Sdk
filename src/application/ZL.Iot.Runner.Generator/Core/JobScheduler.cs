// ============================================================
//  ZL.Iot.Runner.Generator - JobScheduler
//  -------------------------------------------------------------
//  核心调度引擎：信号量限流 + FIFO 队列 + 超时控制 + 用户限流
// ============================================================

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Core;

/// <summary>
/// 生成任务调度器
/// 
/// 架构：
///   用户请求 → Enqueue → Channel<FIFO> → Worker 循环
///                                           ↓
///                                     SemaphoreSlim (限流)
///                                           ↓
///                                     ProjectGenerator (执行)
/// 
/// 关键参数（默认值）：
///   - 最大并发: 2（dotnet publish 单实例占 ~2 核 CPU）
///   - 队列容量: 50（防止 DoS）
///   - 单任务超时: 120 秒
///   - 用户限流: 10 秒/次（防止刷请求）
/// </summary>
public class JobScheduler : IDisposable
{
    private readonly JobStore _store;
    private readonly SemaphoreSlim _semaphore;
    private readonly Channel<GenerateJob> _channel;
    private readonly ConcurrentDictionary<string, DateTime> _userLastRequest;
    private readonly Task _workerTask;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger<JobScheduler> _logger;
    private readonly int _maxConcurrency;
    private readonly int _maxQueueLength;
    private readonly TimeSpan _jobTimeout;
    private readonly TimeSpan _userRateLimitInterval;
    private volatile int _runningCount;
    private volatile int _queuedCount;
    private readonly Func<GenerateRequest, CancellationToken, Func<string, int, Task>?, Task<GenerateResult>>? _generatorFactory;

    /// <summary>
    /// 最大并发数
    /// </summary>
    public int MaxConcurrency => _maxConcurrency;

    /// <summary>
    /// 队列中等待的任务数（手动计数，因为 UnboundedChannel.Reader.Count 不可用）
    /// </summary>
    public int QueuedCount => _queuedCount;

    /// <summary>
    /// 正在执行的任务数
    /// </summary>
    public int RunningCount => _runningCount;

    /// <summary>
    /// 创建调度器
    /// </summary>
    /// <param name="store">共享任务存储（由 DI 注入，确保与端点层使用同一实例）</param>
    /// <param name="maxConcurrency">最大并发构建数，默认 2</param>
    /// <param name="maxQueueLength">队列最大长度，默认 50</param>
    /// <param name="jobTimeout">单任务超时，默认 120 秒</param>
    /// <param name="userRateLimitInterval">同一用户最小请求间隔，默认 5 秒</param>
    /// <param name="logger">日志记录器</param>
    public JobScheduler(
        JobStore store,
        ILogger<JobScheduler> logger,
        int maxConcurrency = 2,
        int maxQueueLength = 50,
        TimeSpan? jobTimeout = null,
        TimeSpan? userRateLimitInterval = null)
        : this(store, logger, maxConcurrency, maxQueueLength, jobTimeout, userRateLimitInterval, null)
    {
    }

    /// <summary>
    /// 测试用构造函数：允许注入自定义的生成器回调
    /// </summary>
    internal JobScheduler(
        JobStore store,
        ILogger<JobScheduler> logger,
        int maxConcurrency,
        int maxQueueLength,
        TimeSpan? jobTimeout,
        TimeSpan? userRateLimitInterval,
        Func<GenerateRequest, CancellationToken, Func<string, int, Task>?, Task<GenerateResult>>? generatorFactory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxConcurrency = maxConcurrency;
        _maxQueueLength = maxQueueLength;
        _jobTimeout = jobTimeout ?? TimeSpan.FromSeconds(120);
        _userRateLimitInterval = userRateLimitInterval ?? TimeSpan.FromSeconds(5);
        _generatorFactory = generatorFactory;

        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _channel = Channel.CreateUnbounded<GenerateJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _userLastRequest = new ConcurrentDictionary<string, DateTime>();
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(WorkerLoop);

        _logger.LogInformation("启动: 并发={MaxConcurrency}, 队列={MaxQueueLength}, 超时={JobTimeout}s, 限流={RateLimit}s",
            maxConcurrency, maxQueueLength, jobTimeout?.TotalSeconds ?? 120, userRateLimitInterval?.TotalSeconds ?? 5);
    }

    /// <summary>
    /// 提交生成任务（立即返回，异步执行）
    /// </summary>
    /// <returns>(是否成功, 错误信息, 任务对象)</returns>
    public (bool Success, string? Error, GenerateJob? Job) Enqueue(GenerateRequest request, string? userId)
    {
        // 1) 用户限流检查
        if (!string.IsNullOrEmpty(userId))
        {
            if (_userLastRequest.TryGetValue(userId, out var lastTime))
            {
                var sinceLast = DateTime.UtcNow - lastTime;
                if (sinceLast < _userRateLimitInterval)
                {
                    var waitSec = (_userRateLimitInterval - sinceLast).TotalSeconds;
                    return (false, $"请求过于频繁，请 {waitSec:F0} 秒后再试", null);
                }
            }
            _userLastRequest[userId] = DateTime.UtcNow;
        }

        // 2) 队列容量检查 — 使用 Interlocked.Increment + 超限回退，避免 TOCTOU 竞争
        var newPosition = Interlocked.Increment(ref _queuedCount);
        if (newPosition > _maxQueueLength)
        {
            Interlocked.Decrement(ref _queuedCount);
            return (false, $"队列已满（当前 {_maxQueueLength} 个任务等待中），请稍后重试", null);
        }

        // 3) 创建任务并入队
        var job = GenerateJob.Create(request, userId);
        job.QueuePosition = newPosition;
        _store.Add(job);

        _channel.Writer.TryWrite(job);

        _logger.LogInformation("任务入队: {JobId} (用户={UserId}, 队列位置={QueuePosition}, 平台={Platform}, SKU={Sku})",
            job.Id, userId ?? "匿名", job.QueuePosition, request.Platform, request.Sku);

        return (true, null, job);
    }

    /// <summary>
    /// 提交生成任务（同步包装，供 ASP.NET Core 端点调用）
    /// </summary>
    /// <returns>任务 ID</returns>
    /// <exception cref="InvalidOperationException">队列满或请求过于频繁时抛出</exception>
    public Task<Guid> EnqueueAsync(GenerateRequest request, string? userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var (success, error, job) = this.Enqueue(request, userId);
        if (!success || job == null)
            throw new InvalidOperationException(error);

        return Task.FromResult(job.Id);
    }

    /// <summary>
    /// 取消任务
    /// </summary>
    public bool CancelJob(Guid jobId, string? userId = null)
    {
        var job = _store.Get(jobId);
        if (job == null) return false;
        if (!string.IsNullOrEmpty(userId) && job.UserId != userId) return false;

        // 原子状态转换：仅允许 Queued → Cancelled / Running → Cancelled
        var expected = job.Status;
        if (expected != JobStatus.Queued && expected != JobStatus.Running) return false;

        // 立即中断运行中的 dotnet publish（如果正在执行）
        job.CancelExecution();

        // 使用 CompareExchange 避免多线程取消的竞争
        if (Interlocked.CompareExchange(ref job._statusInt, (int)JobStatus.Cancelled, (int)expected) != (int)expected)
            return false;

        // 手动推进状态变更（不走 SetCancelled，避免重复调用 CompleteEvents）
        job._phase = "cancelled";
        job._completedAt = DateTime.UtcNow;
        job._errorMessage = "用户取消";
        job.EmitEvent(JobStatus.Cancelled, "任务已取消", phase: "cancelled");
        job.CompleteEvents();

        // 如果还在队列计数中，递减
        if (expected == JobStatus.Queued)
            Interlocked.Decrement(ref _queuedCount);

        _logger.LogInformation("任务取消: {JobId} (原状态={PrevStatus})", jobId, expected);
        return true;
    }

    /// <summary>
    /// 获取任务状态
    /// </summary>
    public GenerateJob? GetJob(Guid jobId) => _store.Get(jobId);

    /// <summary>
    /// 按用户查询最近的任务列表
    /// </summary>
    public GenerateJob[] ListJobsByUser(string userId, int max = 20)
    {
        return _store.ListByUserId(userId, max);
    }

    /// <summary>
    /// Worker 主循环：从 Channel 读取 → 等信号量 → 执行 → 释放
    /// </summary>
    private async Task WorkerLoop()
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            // 从队列中取出，减少排队计数
            Interlocked.Decrement(ref _queuedCount);

            try
            {
                // 检查是否已被取消
                if (job.Status == JobStatus.Cancelled)
                {
                    continue;
                }

                // 等待可用槽位
                if (job.Status != JobStatus.Cancelled)
                {
                    await _semaphore.WaitAsync(_cts.Token);
                }

                try
                {
                    // 再次检查取消（可能在等信号量期间被取消）
                    if (job.Status == JobStatus.Cancelled)
                        continue;

                    Interlocked.Increment(ref _runningCount);
                    job.SetRunning();
                    _logger.LogInformation("开始执行: {JobId} (并发={RunningCount}/{MaxConcurrency})", job.Id, _runningCount, _maxConcurrency);

                    await ProcessJobAsync(job);

                    _logger.LogInformation("执行完成: {JobId} (状态={Status})", job.Id, job.Status);
                }
                finally
                {
                    Interlocked.Decrement(ref _runningCount);
                    _semaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker 异常");
                job.SetFailed($"系统内部错误: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 执行单个生成任务（带超时控制）
    /// </summary>
    private async Task ProcessJobAsync(GenerateJob job)
    {
        using var jobCts = new CancellationTokenSource();

        // 将执行 CTS 绑定到 job，使 CancelJob 可立即中断 dotnet publish
        job.SetExecutionCts(jobCts);

        var timeoutTask = TimeoutAfterAsync(_jobTimeout, jobCts);

        // 进度回调：将 ProjectGenerator 的阶段进度转发到 job 的事件 Channel
        async Task OnProgress(string phase, int percent)
        {
            job.EmitProgressEvent(phase, percent);
        }

        Task<GenerateResult> generateTask = _generatorFactory is not null
            ? _generatorFactory(job.Request, jobCts.Token, OnProgress)
            : new ProjectGenerator().GenerateAsync(job.Request, jobCts.Token, OnProgress);

        var completedTask = await Task.WhenAny(generateTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            // 原子转换：仅当状态仍为 Running 时才标记超时（防止 CancelJob 已经设为 Cancelled）
            if (Interlocked.CompareExchange(ref job._statusInt, (int)JobStatus.TimedOut, (int)JobStatus.Running) == (int)JobStatus.Running)
            {
                jobCts.Cancel();
                job._phase = "timeout";
                job._completedAt = DateTime.UtcNow;
                job._errorMessage = $"生成超时（超过 {_jobTimeout.TotalSeconds:F0} 秒）";
                job.EmitEvent(JobStatus.TimedOut, "生成超时", phase: "timeout");
                job.CompleteEvents();
            }
            // 必须等待 generateTask 完成，确保 dotnet publish 子进程被正确清理，避免孤儿进程
            try
            {
                await generateTask;
            }
            catch
            {
                // 取消/超时后 generateTask 可能抛异常，忽略
            }
            return;
        }

        try
        {
            var result = await generateTask;

            if (job.Status == JobStatus.Cancelled)
                return;

            if (result.Success && result.ZipBytes != null)
            {
                job.SetSucceeded(result.ZipBytes, result.ZipFileName ?? "output.zip", result);
            }
            else
            {
                job.SetFailed(result.ErrorMessage ?? "生成失败：未知错误");
            }
        }
        catch (OperationCanceledException)
        {
            // 原子转换：仅当状态仍为 Running 时才标记取消（防止超时已标记）
            if (Interlocked.CompareExchange(ref job._statusInt, (int)JobStatus.Cancelled, (int)JobStatus.Running) == (int)JobStatus.Running)
            {
                job._phase = "cancelled";
                job._completedAt = DateTime.UtcNow;
                job._errorMessage = "用户取消";
                job.EmitEvent(JobStatus.Cancelled, "任务已取消", phase: "cancelled");
                job.CompleteEvents();
            }
        }
        catch (Exception ex)
        {
            job.SetFailed($"生成异常: {ex.Message}");
        }
    }

    private static async Task TimeoutAfterAsync(TimeSpan timeout, CancellationTokenSource cts)
    {
        using var timer = new PeriodicTimer(timeout);
        await timer.WaitForNextTickAsync();
        cts.Cancel();
    }

    public void Dispose()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* idempotent */ }
        try { _workerTask.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException) { }
        catch (TaskCanceledException) { }
        _semaphore.Dispose();
        try { _cts.Dispose(); }
        catch (ObjectDisposedException) { /* idempotent */ }
        // 注意：_store 由 DI 容器管理生命周期，此处不 Dispose
    }
}
