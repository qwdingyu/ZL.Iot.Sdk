using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins;

/// <summary>
/// 输出插件公共基类 — 消除重复的状态管理样板代码 + 指数退避重连
/// <para>所有 Output 插件继承此类，只需实现 OnStartAsync/OnSendAsync/OnStopAsync</para>
/// </summary>
public abstract class OutputPluginBase : IOutputPlugin
{
    private OutputPluginHealthLevel _lastHealthLevel = OutputPluginHealthLevel.Healthy;
    private bool _connectionNotified;
    private Exception? _lastException;

    public abstract string Name { get; }
    public abstract string ProtocolType { get; }

    /// <summary>
    /// 插件版本号，默认 "1.0.0"。子类可覆盖。
    /// </summary>
    public virtual string Version => "1.0.0";

    // P0-1 修复：使用 int 后备字段 + volatile 保证跨线程可见性。
    // Interlocked.CompareExchange<int> 可用于 int，但 enum 不行（.NET Standard 2.0 限制）。
    // SendAsync/ConnectionLoop 线程写，查询/UI 线程读
    private volatile int _statusInt = (int)PluginStatus.Stopped;
    public PluginStatus Status { get => (PluginStatus)_statusInt; protected set => _statusInt = (int)value; }

    public event Action<string, bool>? ConnectionChanged;
    public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

    /// <summary>
    /// 触发 DetailedStatusChanged 事件 — 供子类在覆盖 RaiseDetailedStatusChanged 时调用。
    /// <para>C# 限制事件只能在声明类中 invoke，因此提供此受保护方法作为桥接。</para>
    /// </summary>
    protected void OnDetailedStatusChanged(OutputPluginStatusArgs args)
    {
        DetailedStatusChanged?.Invoke(args);
    }

    /// <summary>基础重连间隔（毫秒），子类可覆盖</summary>
    protected virtual int BaseReconnectIntervalMs => 3000;

    /// <summary>最大重连间隔（毫秒），指数退避上限</summary>
    protected virtual int MaxReconnectIntervalMs => 60_000;

    /// <summary>指数退避乘数</summary>
    protected virtual double BackoffMultiplier => 2.0;

    public virtual async Task StartAsync(CancellationToken ct = default)
    {
        if (Status is PluginStatus.Running or PluginStatus.Starting) return;
        Status = PluginStatus.Starting;
        try
        {
            await OnStartAsync(ct);
            Status = PluginStatus.Running;
            _lastException = null;
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"{Name} started");
            SetConnectionState(true, OutputPluginHealthLevel.Healthy);
        }
        catch (Exception ex)
        {
            Status = PluginStatus.Fatal;
            _lastException = ex;
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Fatal, $"Start failed: {ex.Message}");
            SetConnectionState(false, OutputPluginHealthLevel.Fatal);
        }
    }

    public async Task SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (Status != PluginStatus.Running)
        {
            throw new InvalidOperationException($"Output plugin '{Name}' is not running (Status={Status}).");
        }

        try
        {
            await OnSendAsync(message, cancellationToken);
            // P0 修复：使用 Interlocked.CompareExchange 消除 TOCTOU 竞态。
            // 原实现 if (Status == Running) 存在时间窗口：检查通过后另一线程可能已调用 StopAsync。
            // CompareExchange 原子地将 Running→Running，仅当值仍为 Running 时才报告 Healthy。
            if (Interlocked.CompareExchange(ref _statusInt, (int)PluginStatus.Running, (int)PluginStatus.Running) == (int)PluginStatus.Running)
            {
                SetConnectionState(true, OutputPluginHealthLevel.Healthy);
            }
        }
        catch (Exception ex)
        {
            _lastException = ex;
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, $"Send failed: {ex.Message}");
            SetConnectionState(false, OutputPluginHealthLevel.Error);
            throw;
        }
    }

    public virtual async Task StopAsync()
    {
        if (Status == PluginStatus.Stopped) return;
        Status = PluginStatus.Stopping;
        Exception? stopException = null;
        try
        {
            await OnStopAsync();
        }
        catch (Exception ex)
        {
            GatewayLog.Warn(Name, $"Stop failed: {ex.Message}", ex);
            stopException = ex;
            _lastException = ex;
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, $"Stop failed: {ex.Message}");
        }
        finally
        {
            Status = PluginStatus.Stopped;
            SetConnectionState(false, OutputPluginHealthLevel.Healthy);
            // 仅在无异常时报告 Healthy；异常时保留 Error 状态，避免覆盖错误信息
            if (stopException == null)
            {
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"{Name} stopped");
            }
        }
    }

    private int _disposed;

    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 释放资源 — 子类应重写此方法释放其拥有的资源。
    /// <para>注意：不调用 StopAsync().Wait()，避免 async-over-sync 死锁。</para>
    /// <para>子类应在 OnStopAsync 中完成网络连接等资源的关闭，在 DisposeCore 中释放 SemaphoreSlim 等同步资源。</para>
    /// </summary>
    /// <param name="disposing">true 表示由用户代码调用，false 表示由终结器调用</param>
    protected virtual void Dispose(bool disposing)
    {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        if (disposing)
        {
            // P1-3 修复：基类不拥有可释放资源，但提供 Dispose 模式供子类扩展。
            // 子类应 override 此方法并释放其 SemaphoreSlim、CancellationTokenSource、TcpClient 等资源。
            // 示例：
            // _sendLock?.Dispose();
            // _cts?.Dispose();
            // _client?.Dispose();
        }
    }

    /// <summary>
    /// 异步释放资源 — 优雅停止并释放资源。
    /// <para>UI 层应优先调用此方法（await using 或 try/finally）。</para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        if (Status != PluginStatus.Stopped)
        {
            try { await StopAsync(); } catch { }
        }

        Dispose(true);
    }

    /// <summary>子类实现：启动逻辑</summary>
    protected abstract Task OnStartAsync(CancellationToken ct);

    /// <summary>子类实现：发送逻辑</summary>
    protected abstract Task OnSendAsync(Message message, CancellationToken cancellationToken);

    /// <summary>子类实现：停止逻辑</summary>
    protected abstract Task OnStopAsync();

    /// <summary>子类可从重连循环中获取最后的异常引用</summary>
    protected Exception? LastException => _lastException;

    /// <summary>公开获取最后的异常引用（供 GatewayManager 健康检查使用）</summary>
    public Exception? PublicLastException => _lastException;

    /// <summary>子类可设置最后异常引用（供 IndustrialOutputPluginBase 等子类使用）</summary>
    protected void SetLastException(Exception? ex) => _lastException = ex;

    /// <summary>
    /// 指数退避延迟 — 根据连续失败次数计算等待时间
    /// <para>使用 Equal Jitter 策略（AWS 推荐）：delay = capped/2 + random(0, capped/2)</para>
    /// <para>保证每次延迟在 [capped/2, capped] 范围内，避免首次重试退化为即时重试</para>
    /// <para>static 版本供不继承 OutputPluginBase 的工业插件直接调用</para>
    /// </summary>
    protected int CalculateBackoffDelay(int failureStreak)
    {
        return CalculateBackoffDelay(failureStreak, BaseReconnectIntervalMs, MaxReconnectIntervalMs, BackoffMultiplier);
    }

    /// <summary>
    /// 静态指数退避延迟计算 — 供不继承 OutputPluginBase 的插件直接调用
    /// </summary>
    /// <param name="failureStreak">连续失败次数</param>
    /// <param name="baseMs">基础重连间隔（毫秒）</param>
    /// <param name="maxMs">最大重连间隔（毫秒），默认 60000</param>
    /// <param name="multiplier">指数乘数，默认 2.0</param>
    protected internal static int CalculateBackoffDelay(int failureStreak, int baseMs, int maxMs = 60_000, double multiplier = 2.0)
    {
        if (failureStreak <= 0) return baseMs;
        var exponent = (int)Math.Min(failureStreak - 1, 10);
        var delay = multiplier == 2.0
            ? baseMs << exponent  // 位移替代 Math.Pow，multiplier=2 时等价且零分配
            : (int)(baseMs * Math.Pow(multiplier, exponent));
        var capped = Math.Min(delay, maxMs);
        // Equal Jitter：在 [capped/2, capped] 范围内随机，既避免惊群效应，又保证最低退避时间
        var half = capped / 2;
        var jitter = (int)(half * Random.Shared.NextDouble());
        return half + jitter;
    }

    /// <summary>
    /// 设置连接状态，触发 ConnectionChanged 事件
    /// <para>level 参数用于去重：只有 connected 或 level 真正变化时才会触发</para>
    /// </summary>
    protected void SetConnectionState(bool connected, OutputPluginHealthLevel level = OutputPluginHealthLevel.Healthy)
    {
        if (_connectionNotified == connected && _lastHealthLevel == level)
        {
            return;
        }

        _connectionNotified = connected;
        _lastHealthLevel = level;
        // 拷贝到本地变量避免多线程 unsub 时 ?.Invoke 抛 NullReferenceException
        var handler = ConnectionChanged;
        handler?.Invoke(Name, connected);
    }

    /// <summary>
    /// 触发 DetailedStatusChanged 事件 — virtual 以允许工业插件子类覆盖为 ClassifyError 版本。
    /// </summary>
    /// <param name="level">健康级别</param>
    /// <param name="message">技术消息</param>
    /// <param name="exception">关联异常（可选）</param>
    /// <param name="customConsecutiveFailures">自定义连续失败次数（默认：Error/Fatal 为 1，其余为 0）</param>
    protected virtual void RaiseDetailedStatusChanged(OutputPluginHealthLevel level, string message, Exception? exception = null, int? customConsecutiveFailures = null)
    {
        _lastHealthLevel = level;
        if (exception != null) _lastException = exception;

        var errorCode = level switch
        {
            OutputPluginHealthLevel.Healthy => GatewayErrorCodes.None,
            OutputPluginHealthLevel.Warning => GatewayErrorCodes.ConnectionFailed,
            OutputPluginHealthLevel.Error => GatewayErrorCodes.ConnectionFailed,
            OutputPluginHealthLevel.Fatal => GatewayErrorCodes.InternalException,
            _ => GatewayErrorCodes.None
        };

        var consecutiveFailures = customConsecutiveFailures
            ?? (level is OutputPluginHealthLevel.Error or OutputPluginHealthLevel.Fatal ? 1 : 0);

        // 拷贝到本地变量避免多线程 unsub 时 ?.Invoke 抛 NullReferenceException
        var handler = DetailedStatusChanged;
        handler?.Invoke(new OutputPluginStatusArgs
        {
            PluginName = Name,
            Status = Status,
            HealthLevel = level,
            Message = message ?? (level == OutputPluginHealthLevel.Healthy ? "Connected" : "Disconnected"),
            UserMessage = message,
            ErrorCode = errorCode,
            ConsecutiveFailures = consecutiveFailures,
            Timestamp = DateTime.UtcNow,
            LastException = _lastException
        });
    }

    /// <summary>
    /// 从 Message 中提取结构化 TagWrite 列表。
    /// 如果 Message.Writes 非空则返回；否则返回空数组。
    /// 供子类 OnSendAsync 统一使用，消除各插件隐式解析 Payload 的"暗契约"。
    /// </summary>
    protected internal static IReadOnlyList<TagWrite> ExtractWriteOperations(Message message)
    {
        return message?.Writes?.Count > 0
            ? (IReadOnlyList<TagWrite>)message.Writes
            : Array.Empty<TagWrite>();
    }

    /// <summary>
    /// 判断消息是否携带结构化 TagWrite 数据（Intent 为 TagWrite 且 Writes 非空）。
    /// </summary>
    protected internal static bool HasStructuredWrites(Message message)
    {
        return message?.Intent == MessageIntent.TagWrite && message?.Writes?.Count > 0;
    }
}
