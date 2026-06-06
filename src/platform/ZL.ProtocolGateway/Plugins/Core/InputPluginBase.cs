using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins;

/// <summary>
/// 输入插件公共基类 — 消除重复的状态管理样板代码
/// <para>所有 Input 插件继承此类，只需实现 OnStartAsync/OnStopAsync</para>
/// <para>子类在 OnStartAsync 中启动监听，收到消息后调用 InvokeMessageHandler</para>
/// </summary>
public abstract class InputPluginBase : IInputPlugin, IAsyncDisposable
{
    private Func<Message, Task>? _messageHandler;
    private CancellationTokenSource? _cts;

    public abstract string Name { get; }
    public abstract string ProtocolType { get; }

    /// <summary>
    /// 插件版本号，默认 "1.0.0"。子类可覆盖。
    /// </summary>
    public virtual string Version => "1.0.0";

    // P0-1 修复：volatile 保证跨线程可见性 — ConnectionLoop 线程写，SendAsync/查询线程读
    private volatile PluginStatus _status = PluginStatus.Stopped;
    public PluginStatus Status { get => _status; protected set => _status = value; }

    public event Action<string, bool>? ConnectionChanged;

    /// <summary>
    /// 详细状态变更事件 — 与 OutputPluginBase.DetailedStatusChanged 对等。
    /// </summary>
    public event Action<InputPluginStatusArgs>? DetailedStatusChanged;

    private Exception? _lastException;
    /// <summary>最近一次异常（供 GatewayManager 读取）</summary>
    public Exception? PublicLastException => _lastException;

    public virtual async Task StartAsync(Func<Message, Task> messageHandler, CancellationToken cancellationToken = default)
    {
        // 允许从 Error 状态重新 Start（前次启动失败后应能恢复）
        if (Status is PluginStatus.Running or PluginStatus.Starting) return;

        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        Status = PluginStatus.Starting;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await OnStartAsync(_cts.Token);
            Status = PluginStatus.Running;
            ConnectionChanged?.Invoke(Name, true);
        }
        catch
        {
            Status = PluginStatus.Error;
            _cts?.Dispose();
            _cts = null;
            throw;
        }
    }

    public virtual async Task StopAsync()
    {
        if (Status == PluginStatus.Stopped) return;
        Status = PluginStatus.Stopping;

        try
        {
            await OnStopAsync();
        }
        finally
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            Status = PluginStatus.Stopped;
            ConnectionChanged?.Invoke(Name, false);
        }
    }

    private int _disposed;

    public virtual void Dispose()
    {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        // P0-3 修复：同步 Dispose 不等待 StopAsync 完成，避免 ThreadPool 耗尽时死锁。
        // 仅释放 CTS 资源；调用方应优先使用 DisposeAsync。
        if (Status != PluginStatus.Stopped)
        {
            try { _cts?.Cancel(); } catch { }
        }
        _cts?.Dispose();
        _cts = null;

        GC.SuppressFinalize(this);
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
        _cts?.Dispose();
        _cts = null;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 子类实现：启动逻辑（打开监听端口、连接 Broker 等）
    /// </summary>
    protected abstract Task OnStartAsync(CancellationToken ct);

    /// <summary>
    /// 子类实现：停止逻辑（关闭连接、释放资源）
    /// </summary>
    protected abstract Task OnStopAsync();

    /// <summary>
    /// 调用消息处理回调 — 子类在收到数据后调用此方法转发消息
    /// </summary>
    protected Task InvokeMessageHandler(Message message)
    {
        if (_messageHandler == null)
            throw new InvalidOperationException("Message handler not set. Call InvokeMessageHandler after StartAsync.");
        return _messageHandler(message);
    }

    /// <summary>
    /// 当前取消令牌 — 子类在异步循环中使用
    /// </summary>
    protected CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>
    /// 获取当前消息处理器（用于延迟绑定场景）
    /// </summary>
    protected Func<Message, Task>? MessageHandler => _messageHandler;

    /// <summary>
    /// 设置最近一次异常。
    /// </summary>
    protected void SetLastException(Exception ex) => _lastException = ex;

    /// <summary>
    /// 触发 DetailedStatusChanged 事件。
    /// </summary>
    protected void RaiseDetailedStatusChanged(InputPluginHealthLevel level, string message, Exception? exception = null)
    {
        if (exception != null) SetLastException(exception);

        OnDetailedStatusChanged(new InputPluginStatusArgs
        {
            PluginName = Name,
            Status = Status,
            Message = message ?? (level == InputPluginHealthLevel.Healthy ? "Connected" : "Disconnected"),
            ErrorCode = level switch
            {
                InputPluginHealthLevel.Healthy => GatewayErrorCodes.None,
                InputPluginHealthLevel.Degraded => GatewayErrorCodes.ConnectionTimeout,
                InputPluginHealthLevel.Warning => GatewayErrorCodes.ConnectionLost,
                InputPluginHealthLevel.Error => GatewayErrorCodes.ConnectionRefused,
                InputPluginHealthLevel.Fatal => GatewayErrorCodes.ConfigurationInvalid,
                _ => GatewayErrorCodes.InternalException
            },
            UserMessage = message ?? "",
            Advice = level switch
            {
                InputPluginHealthLevel.Healthy => "",
                InputPluginHealthLevel.Degraded => "Monitor performance, may self-recover",
                InputPluginHealthLevel.Warning => "Waiting for automatic reconnection",
                InputPluginHealthLevel.Error => "Check network and target service status",
                InputPluginHealthLevel.Fatal => "Check input plugin configuration",
                _ => "Check logs for details"
            },
            HealthLevel = level,
            Timestamp = DateTime.UtcNow,
            LastException = exception
        });
    }

    /// <summary>
    /// 虚拟方法，便于子类或测试自定义事件触发行为。
    /// </summary>
    protected virtual void OnDetailedStatusChanged(InputPluginStatusArgs args)
    {
        DetailedStatusChanged?.Invoke(args);
    }
}
