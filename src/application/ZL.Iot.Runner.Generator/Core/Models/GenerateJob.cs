// ============================================================
//  ZL.Iot.Runner.Generator - GenerateJob
//  -------------------------------------------------------------
//  生成任务模型：状态机 + 结果承载 + SSE 事件推送
// ============================================================

using System.Threading.Channels;

namespace ZL.Iot.Runner.Generator.Core.Models;

/// <summary>
/// 任务状态
/// </summary>
public enum JobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    TimedOut = 5
}

/// <summary>
/// SSE 状态事件：每次状态变更时推送到前端
/// </summary>
public sealed class JobStatusEvent
{
    public DateTime Timestamp { get; set; }
    public JobStatus Status { get; set; }
    public string Message { get; set; } = "";
    public int? ProgressPercent { get; set; }
    public string? Phase { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultFileName { get; set; }
    public double? ElapsedSeconds { get; set; }
}

/// <summary>
/// 生成任务：从入队到完成的全生命周期模型
/// </summary>
public class GenerateJob
{
    // ================================================================
    //  线程安全状态字段（供 JobScheduler 原子操作）
    // ================================================================

    /// <summary>
    /// 状态整数值，使用 Interlocked.CompareExchange 实现原子状态转换
    /// </summary>
    internal int _statusInt;

    /// <summary>
    /// 完成时间（后台字段，供 JobScheduler 直接赋值）
    /// </summary>
    internal DateTime? _completedAt;

    /// <summary>
    /// 错误信息（后台字段，供 JobScheduler 直接赋值）
    /// </summary>
    internal string? _errorMessage;

    /// <summary>
    /// 当前阶段（后台字段，供 JobScheduler 直接赋值）
    /// </summary>
    internal string _phase = "queued";

    /// <summary>
    /// 任务唯一标识
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 用户标识（来自认证系统，可为 null 表示未登录）
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 生成请求
    /// </summary>
    public GenerateRequest Request { get; }

    /// <summary>
    /// 当前状态（基于 _statusInt 后台字段，支持 Interlocked 原子操作）
    /// </summary>
    public JobStatus Status
    {
        get => (JobStatus)System.Threading.Volatile.Read(ref _statusInt);
        private set => Interlocked.Exchange(ref _statusInt, (int)value);
    }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// 开始执行时间
    /// </summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>
    /// 完成时间（成功/失败/取消/超时）
    /// </summary>
    public DateTime? CompletedAt
    {
        get => _completedAt;
        private set => _completedAt = value;
    }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => _errorMessage = value;
    }

    /// <summary>
    /// 结果文件名
    /// </summary>
    public string? ResultFileName { get; private set; }

    /// <summary>
    /// 结果字节（ZIP 内容）
    /// </summary>
    public byte[]? ResultBytes { get; private set; }

    /// <summary>
    /// 安全下载令牌：SSE 和下载端点的访问凭证（128 位随机 GUID）
    /// 用于替代 JWT，因为 EventSource 和 window.open 无法携带自定义 Header
    /// </summary>
    public string DownloadToken { get; }

    /// <summary>
    /// 在队列中的近似位置（1 = 下一个执行）
    /// </summary>
    public int QueuePosition { get; set; }

    /// <summary>
    /// 生成结果对象
    /// </summary>
    public GenerateResult? Result { get; private set; }

    /// <summary>
    /// 耗时（仅在完成后有值）
    /// </summary>
    public TimeSpan? Elapsed => CompletedAt.HasValue ? CompletedAt.Value - CreatedAt : null;

    /// <summary>
    /// 进度百分比 0-100（供 REST API 序列化）
    /// </summary>
    public int Progress { get; private set; }

    /// <summary>
    /// 当前阶段描述（供 REST API 序列化）
    /// </summary>
    public string Phase
    {
        get => _phase;
        private set => _phase = value;
    }

    /// <summary>
    /// 状态显示文本
    /// </summary>
    public string StatusText => Status switch
    {
        JobStatus.Queued => $"排队中 (位置: {QueuePosition})",
        JobStatus.Running => "生成中...",
        JobStatus.Succeeded => "生成完成",
        JobStatus.Failed => "生成失败",
        JobStatus.Cancelled => "已取消",
        JobStatus.TimedOut => "超时",
        _ => Status.ToString()
    };

    // ================================================================
    //  SSE 事件推送
    // ================================================================

    /// <summary>
    /// 状态变更事件 Channel。每个 job 独立一个 Channel，
    /// SSE 端点订阅此 Channel 实现实时推送。
    /// 容量 16 足够（状态变更只有 3-5 次），避免内存堆积。
    /// </summary>
    private readonly Channel<JobStatusEvent> _eventChannel;
    private readonly object _eventLock = new();

    /// <summary>
    /// 执行取消令牌源（由 JobScheduler.ProcessJobAsync 设置）
    /// 用户取消时调用 CancelExecution() 可立即中断 dotnet publish
    /// </summary>
    private CancellationTokenSource? _executionCts;

    /// <summary>
    /// 设置执行 CTS（由 JobScheduler 调用）
    /// </summary>
    public void SetExecutionCts(CancellationTokenSource cts)
    {
        _executionCts = cts;
    }

    /// <summary>
    /// 立即中断运行中的 dotnet publish（由 CancelJob 调用）
    /// </summary>
    public void CancelExecution()
    {
        _executionCts?.Cancel();
    }

    /// <summary>
    /// 订阅状态变更事件流（SSE 端点调用）
    /// </summary>
    public IAsyncEnumerable<JobStatusEvent> WatchStatusAsync()
        => _eventChannel.Reader.ReadAllAsync();

    /// <summary>
    /// 暴露 Channel Reader 供 SSE 端点直接访问
    /// </summary>
    public ChannelReader<JobStatusEvent> StatusEventsReader => _eventChannel.Reader;

    /// <summary>
    /// 发送状态事件（内部调用）
    /// </summary>
    internal void EmitEvent(JobStatus status, string message, int? progress = null, string? phase = null)
    {
        lock (_eventLock)
        {
            _eventChannel.Writer.TryWrite(new JobStatusEvent
            {
                Timestamp = DateTime.UtcNow,
                Status = status,
                Message = message,
                ProgressPercent = progress,
                Phase = phase,
                ErrorMessage = ErrorMessage,
                ResultFileName = ResultFileName,
                ElapsedSeconds = Elapsed?.TotalSeconds
            });
        }
    }

    /// <summary>
    /// 发送进度事件（由 JobScheduler 的进度回调触发）
    /// </summary>
    public void EmitProgressEvent(string phase, int percent)
    {
        lock (_eventLock)
        {
            _eventChannel.Writer.TryWrite(new JobStatusEvent
            {
                Timestamp = DateTime.UtcNow,
                Status = Status,
                Message = GetPhaseMessage(phase),
                ProgressPercent = percent,
                Phase = phase,
                ElapsedSeconds = Elapsed?.TotalSeconds ?? (StartedAt.HasValue ? (DateTime.UtcNow - StartedAt.Value).TotalSeconds : null)
            });
        }
    }

    private static string GetPhaseMessage(string phase) => phase switch
    {
        "validating" => "校验配置中...",
        "rendering" => "渲染模板中...",
        "building" => "编译构建中...",
        "packing" => "打包中...",
        "complete" => "完成",
        _ => $"处理中: {phase}"
    };

    /// <summary>
    /// 关闭事件通道（任务结束后调用，通知 SSE 消费者结束）
    /// </summary>
    internal void CompleteEvents()
    {
        lock (_eventLock)
        {
            _eventChannel.Writer.Complete();
        }
    }

    public GenerateJob(GenerateRequest request, string? userId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        DownloadToken = Guid.NewGuid().ToString("N");
        Request = request;
        CreatedAt = DateTime.UtcNow;
        Status = JobStatus.Queued;
        QueuePosition = 1;
        _eventChannel = Channel.CreateBounded<JobStatusEvent>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true
        });
        // 立即发送初始状态
        EmitEvent(JobStatus.Queued, "任务已提交，等待执行", progress: 0, phase: "queued");
    }

    public static GenerateJob Create(GenerateRequest request, string? userId)
        => new(request, userId);

    /// <summary>
    /// 标记为执行中
    /// </summary>
    public void SetRunning()
    {
        Status = JobStatus.Running;
        Progress = 10;
        Phase = "rendering";
        StartedAt = DateTime.UtcNow;
        EmitEvent(Status, "开始生成项目", progress: 10, phase: "rendering");
    }

    /// <summary>
    /// 标记为成功
    /// </summary>
    public void SetSucceeded(byte[] bytes, string fileName, GenerateResult result)
    {
        Status = JobStatus.Succeeded;
        Progress = 100;
        Phase = "complete";
        CompletedAt = DateTime.UtcNow;
        ResultBytes = bytes;
        ResultFileName = fileName;
        Result = result;
        EmitEvent(Status, $"生成完成: {fileName}", progress: 100, phase: "complete");
        CompleteEvents();
    }

    /// <summary>
    /// 标记为失败
    /// </summary>
    public void SetFailed(string error)
    {
        Status = JobStatus.Failed;
        // 保留当前 Progress，不重置为 0，让前端能看到失败时的进度快照
        Phase = "error";
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = error;
        EmitEvent(Status, $"生成失败: {error}", progress: Progress, phase: "error");
        CompleteEvents();
    }

    /// <summary>
    /// 标记为取消
    /// </summary>
    public void SetCancelled()
    {
        Status = JobStatus.Cancelled;
        Phase = "cancelled";
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = "用户取消";
        EmitEvent(Status, "任务已取消", phase: "cancelled");
        CompleteEvents();
    }

    /// <summary>
    /// 标记为超时
    /// </summary>
    public void SetTimedOut()
    {
        Status = JobStatus.TimedOut;
        Phase = "timeout";
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = "生成超时";
        EmitEvent(Status, "生成超时", phase: "timeout");
        CompleteEvents();
    }

    /// <summary>
    /// 清除结果字节（定时清理用，节省内存）
    /// </summary>
    public void ClearResultBytes()
    {
        ResultBytes = null;
    }
}
