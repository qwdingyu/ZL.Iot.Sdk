// ============================================================
//  ZL.Iot.Runner.Generator - JobStore
//  -------------------------------------------------------------
//  线程安全的内存任务存储 + 自动清理
// ============================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Core;

/// <summary>
/// 内存任务存储：存储所有生成任务的状态，定期清理过期数据。
/// - 结果字节（ResultBytes）保留 2 分钟，节省内存
/// - 任务元数据保留 30 分钟，供状态查询
/// </summary>
public class JobStore : IDisposable
{
    private readonly ConcurrentDictionary<Guid, GenerateJob> _jobs;
    private readonly PeriodicTimer _cleanupTimer;
    private readonly CancellationTokenSource _cts;
    private readonly Task _cleanupTask;
    private readonly TimeSpan _resultTtl;
    private readonly TimeSpan _jobTtl;
    private readonly ILogger<JobStore> _logger;

    /// <summary>
    /// 正在运行的任务数
    /// </summary>
    public int RunningCount => _jobs.Values.Count(j => j.Status == JobStatus.Running);

    /// <summary>
    /// 排队中的任务数
    /// </summary>
    public int QueuedCount => _jobs.Values.Count(j => j.Status == JobStatus.Queued);

    /// <summary>
    /// 活跃任务数（排队 + 运行中）
    /// </summary>
    public int ActiveCount => QueuedCount + RunningCount;

    public JobStore(
        ILogger<JobStore>? logger = null,
        TimeSpan? resultTtl = null,
        TimeSpan? jobTtl = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jobs = new ConcurrentDictionary<Guid, GenerateJob>();
        _resultTtl = resultTtl ?? TimeSpan.FromMinutes(2);
        _jobTtl = jobTtl ?? TimeSpan.FromMinutes(30);
        _cts = new CancellationTokenSource();
        _cleanupTimer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        _cleanupTask = Task.Run(CleanupLoop);
    }

    /// <summary>
    /// 添加任务
    /// </summary>
    public void Add(GenerateJob job)
    {
        _jobs[job.Id] = job;
    }

    /// <summary>
    /// 获取任务
    /// </summary>
    public GenerateJob? Get(Guid id)
    {
        _jobs.TryGetValue(id, out var job);
        return job;
    }

    /// <summary>
    /// 移除任务
    /// </summary>
    public bool Remove(Guid id)
    {
        return _jobs.TryRemove(id, out _);
    }

    /// <summary>
    /// 按用户查询最近的任务列表
    /// </summary>
    public GenerateJob[] ListByUserId(string userId, int max = 20)
    {
        return _jobs.Values
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(max)
            .ToArray();
    }

    /// <summary>
    /// 获取所有任务（供端点层查询最近列表使用）
    /// </summary>
    public GenerateJob[] ListAll()
    {
        return _jobs.Values.ToArray();
    }

    /// <summary>
    /// 定时清理：清除结果字节 → 清除过期任务
    /// </summary>
    private async Task CleanupLoop()
    {
        while (await _cleanupTimer.WaitForNextTickAsync(_cts.Token))
        {
            try
            {
                await CleanupAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 清理失败不影响服务运行
            }
        }
    }

    private async Task CleanupAsync()
    {
        var now = DateTime.UtcNow;
        int clearedBytes = 0;
        int removedJobs = 0;

        foreach (var job in _jobs.Values)
        {
            // 1) 清除过期的结果字节（节省内存）
            if (job.CompletedAt.HasValue
                && now - job.CompletedAt.Value > _resultTtl
                && job.ResultBytes != null)
            {
                job.ClearResultBytes();
                clearedBytes++;
            }

            // 2) 移除完全过期的任务
            if (job.CompletedAt.HasValue
                && now - job.CompletedAt.Value > _jobTtl)
            {
                _jobs.TryRemove(job.Id, out _);
                removedJobs++;
            }
        }

        // 也清理一直排队未完成的僵尸任务（超过 jobTtl）
        foreach (var job in _jobs.Values.Where(j => !j.CompletedAt.HasValue && now - j.CreatedAt > _jobTtl))
        {
            job.SetFailed("任务过期自动清理");
            _jobs.TryRemove(job.Id, out _);
            removedJobs++;
        }

        if (clearedBytes > 0 || removedJobs > 0)
        {
            _logger.LogDebug("清理: 释放结果 {ClearedResults} 个, 移除任务 {RemovedJobs} 个, 剩余 {Remaining} 个",
                clearedBytes, removedJobs, _jobs.Count);
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* idempotent */ }
        try { _cleanupTimer.Dispose(); }
        catch (ObjectDisposedException) { /* idempotent */ }
        // WaitAsync 不会在取消时抛异常（返回 false），但 Wait 会抛 TaskCanceledException
        // 使用 Wait + 忽略异常来安全等待
        try { _cleanupTask.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException) { }
        catch (TaskCanceledException) { }
    }
}
