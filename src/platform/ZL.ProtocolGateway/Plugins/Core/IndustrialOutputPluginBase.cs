using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins;

/// <summary>
/// 工业协议输出插件公共基类 — 扩展 OutputPluginBase，添加工业协议特有的连接管理模式。
/// <para>工业插件（S7, OPC-UA, AllenBradley, Mitsubishi, BACnet, IEC61850）统一继承此类，
/// 只需实现 TryConnectAsync/OnStopCoreAsync + 可选的 HasLiveConnection/CleanupConnection。</para>
/// <para>相比直接实现 IOutputPlugin，此类消除了 ~60 行/插件的重复代码：</para>
/// <list type="bullet">
///   <item>ConnectionLoopAsync — 通用连接-失败-退避循环（本类实现）</item>
///   <item>SetConnectionState — 连接状态通知（从 OutputPluginBase 继承）</item>
///   <item>RaiseDetailedStatusChanged — 状态变更通知（本类扩展，使用 ClassifyError）</item>
///   <item>ClassifyError — 协议特定的错误分类（子类覆盖）</item>
///   <item>_connectFailureStreak — 连续失败计数器</item>
///   <item>Dispose/DisposeAsync — 生命周期管理（从 OutputPluginBase 继承）</item>
///   <item>CalculateBackoffDelay — 指数退避（从 OutputPluginBase 继承）</item>
/// </list>
/// </summary>
public abstract class IndustrialOutputPluginBase : OutputPluginBase
{
    // ──────────────────────────────────────────────
    // 连接循环共享状态（由基类 ConnectionLoopAsync 管理）
    // ──────────────────────────────────────────────

    private CancellationTokenSource? _cts;
    private Task? _connectionLoop;
    private int _connectFailureStreak;
    private string _lastFailureKind = string.Empty;
    private DateTimeOffset? _firstConnectionRefusedAt;

    /// <summary>子类的 OnSendAsync 等热路径可使用的取消令牌</summary>
    protected CancellationToken? CtsToken => _cts?.Token;

    /// <summary>获取当前连续连接失败次数（线程安全）</summary>
    protected int ConnectFailureStreak => Volatile.Read(ref _connectFailureStreak);

    /// <summary>重置连续失败计数为 0（连接成功时调用）</summary>
    protected void ResetConnectFailureStreak() => Interlocked.Exchange(ref _connectFailureStreak, 0);

    /// <summary>递增连续失败计数并返回递增后的值（线程安全）</summary>
    protected int IncrementConnectFailureStreak() => Interlocked.Increment(ref _connectFailureStreak);

    /// <summary>
    /// 错误阈值 — 超过此连续失败次数后，健康级别从 Warning 升级为 Error。
    /// 子类可覆盖以从配置中读取。
    /// </summary>
    protected virtual int ErrorThreshold => 5;

    /// <summary>
    /// 基础重连间隔（毫秒）— 用于指数退避计算。
    /// 子类应覆盖以从配置中读取，默认 5000ms。
    /// </summary>
    protected override int BaseReconnectIntervalMs => 5000;

    // ──────────────────────────────────────────────
    // 子类必须/可选实现
    // ──────────────────────────────────────────────

    /// <summary>
    /// 子类覆盖：尝试建立连接。
    /// <para>包含协议特定的 TCP 连接、握手、会话注册等所有连接逻辑。</para>
    /// <para>如果连接失败，应抛出异常；基类会捕获并进入退避重连。</para>
    /// <para>默认实现抛出 NotImplementedException，使用基类 ConnectionLoop 的插件必须覆盖。</para>
    /// </summary>
    protected virtual Task TryConnectAsync(CancellationToken ct)
        => throw new NotImplementedException("Subclass must override TryConnectAsync to use base ConnectionLoop");

    /// <summary>
    /// 子类覆盖：停止时的清理逻辑。
    /// <para>在取消令牌触发、连接循环退出后调用。应关闭网络连接、释放会话等。</para>
    /// </summary>
    protected virtual Task OnStopCoreAsync() => Task.CompletedTask;

    /// <summary>
    /// 可选覆盖：检查连接是否仍然活跃。
    /// <para>默认返回 false（每次循环都尝试连接）。覆盖此方法可实现连接保活模式。</para>
    /// </summary>
    protected virtual bool HasLiveConnection() => false;

    /// <summary>
    /// 可选覆盖：清理当前连接。
    /// <para>在重新连接前调用。默认不做任何操作。</para>
    /// </summary>
    protected virtual void CleanupConnection() { }

    /// <summary>
    /// 可选覆盖：获取失败类型标识。
    /// <para>用于失败种类变化时重置失败计数。默认返回异常类型名。</para>
    /// </summary>
    protected virtual string GetFailureKind(Exception ex) => ex.GetType().Name;

    /// <summary>
    /// 可选覆盖：记录连接被拒绝的时间戳。
    /// <para>在连接循环中检测到 ConnectionRefused 时调用，用于日志节流决策。</para>
    /// </summary>
    protected virtual void RecordConnectionRefused()
    {
        if (_firstConnectionRefusedAt == null)
        {
            _firstConnectionRefusedAt = DateTimeOffset.UtcNow;
        }
    }

    // ──────────────────────────────────────────────
    // 标准连接循环（Template Method 模式）
    // ──────────────────────────────────────────────

    /// <inheritdoc cref="OutputPluginBase.OnStartAsync"/>
    /// <remarks>
    /// 子类通常不需要覆盖此方法。如需自定义启动逻辑，可覆盖 <see cref="OnBeforeConnectionLoopAsync"/>。
    /// </remarks>
    protected override async Task OnStartAsync(CancellationToken ct)
    {
        
        ResetConnectFailureStreak();
        _lastFailureKind = string.Empty;
        _firstConnectionRefusedAt = null;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await OnBeforeConnectionLoopAsync();

        _connectionLoop = Task.Run(() => ConnectionLoopAsync(_cts.Token), _cts.Token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 连接循环启动前的钩子。子类可在此执行额外的初始化。
    /// </summary>
    protected virtual Task OnBeforeConnectionLoopAsync() => Task.CompletedTask;

    private async Task ConnectionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (HasLiveConnection())
                {
                    Status = PluginStatus.Running;
                    await Task.Delay(1000, ct);
                    continue;
                }

                Status = PluginStatus.Starting;
                CleanupConnection();

                await TryConnectAsync(ct);

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                Status = PluginStatus.Running;
                ResetConnectFailureStreak();
                _lastFailureKind = string.Empty;
                _firstConnectionRefusedAt = null;
                SetLastException(null);

                var connectedMessage = OnConnected();
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, connectedMessage);
                SetConnectionState(true, OutputPluginHealthLevel.Healthy);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                SetLastException(ex);
                CleanupConnection();

                string failureKind = GetFailureKind(ex);
                if (!string.Equals(_lastFailureKind, failureKind, StringComparison.Ordinal))
                {
                    ResetConnectFailureStreak();
                }
                int currentStreak = IncrementConnectFailureStreak();
                _lastFailureKind = failureKind;

                OnRecordFailure(ex);

                var level = currentStreak >= ErrorThreshold
                    ? OutputPluginHealthLevel.Error
                    : OutputPluginHealthLevel.Warning;

                // P1-7：连接断开但正在自动重连时，使用 Recovering 状态而非 Error，
                // 让 UI/监控能区分"需要人工介入的错误"和"自动恢复中的临时故障"
                Status = PluginStatus.Recovering;

                var message = OnFormatFailureMessage(ex, failureKind, currentStreak);

                if (ShouldLogConnectionFailure(currentStreak, failureKind))
                {
                    GatewayLog.Info(Name, message);
                }

                RaiseDetailedStatusChanged(level, message);
                SetConnectionState(false, level);

                try
                {
                    // Equal Jitter 指数退避重连（AWS 推荐），避免多实例同步重试惊群效应
                    int delayMs = OutputPluginBase.CalculateBackoffDelay(ConnectFailureStreak, BaseReconnectIntervalMs);
                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 子类覆盖：连接成功时返回连接消息。
    /// </summary>
    protected virtual string OnConnected() => $"Connected to {ProtocolType}";

    /// <summary>
    /// 子类覆盖：在失败处理中执行额外操作（如记录 ConnectionRefused 时间戳）。
    /// </summary>
    protected virtual void OnRecordFailure(Exception ex) { }

    /// <summary>
    /// 子类覆盖：格式化连接失败消息。
    /// </summary>
    protected virtual string OnFormatFailureMessage(Exception ex, string failureKind, int streak)
        => $"Connection failed ({streak}x, {failureKind}): {ex.Message}";

    /// <summary>
    /// 判断是否应该记录连接失败日志 — 新失败类型或达到阈值时记录。
    /// </summary>
    protected virtual bool ShouldLogConnectionFailure(int streak, string failureKind)
        => streak == 1 || streak % 10 == 0 || streak >= ErrorThreshold;

    /// <inheritdoc cref="OutputPluginBase.OnStopAsync"/>
    protected override async Task OnStopAsync()
    {
        _cts?.Cancel();

        if (_connectionLoop != null)
        {
            try
            {
                await _connectionLoop.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                GatewayLog.Warn(Name, "Connection loop did not exit within 10s");
            }
            catch (OperationCanceledException)
            {
                // Loop completed via cancellation — expected
            }
        }

        try
        {
            await OnStopCoreAsync();
        }
        catch (Exception ex)
        {
            GatewayLog.Warn(Name, $"Stop cleanup failed: {ex.Message}", ex);
        }

        CleanupConnection();
    }

    // ──────────────────────────────────────────────
    // 错误分类与状态通知
    // ──────────────────────────────────────────────

    /// <summary>
    /// 协议特定的错误分类 — 子类应覆盖以提供用户友好的错误消息和处置建议。
    /// </summary>
    protected virtual (string errorCode, string userMessage, string advice) ClassifyError(
        OutputPluginHealthLevel level, string message)
    {
        return level switch
        {
            OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, $"{ProtocolType} connected", "No action needed"),
            OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, $"{ProtocolType} connection retrying", "Check device network connection"),
            OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, $"{ProtocolType} connection failed", "Check device address and network settings"),
            OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, $"{ProtocolType} configuration error", "Check output plugin configuration"),
            _ => (GatewayErrorCodes.InternalException, $"{ProtocolType} unknown error", "Check logs for details")
        };
    }

    /// <summary>
    /// 触发 DetailedStatusChanged 事件 — 使用 ClassifyError 生成协议特定的错误分类。
    /// </summary>
    protected override void RaiseDetailedStatusChanged(OutputPluginHealthLevel level, string message, Exception? exception = null, int? customConsecutiveFailures = null)
    {
        if (exception != null) SetLastException(exception);

        var (errorCode, userMessage, advice) = ClassifyError(level, message);

        OnDetailedStatusChanged(new OutputPluginStatusArgs
        {
            PluginName = Name,
            Status = Status,
            Message = message ?? (level == OutputPluginHealthLevel.Healthy ? "Connected" : "Disconnected"),
            ErrorCode = errorCode,
            UserMessage = userMessage,
            Advice = advice,
            HealthLevel = level,
            ConsecutiveFailures = customConsecutiveFailures ?? Volatile.Read(ref _connectFailureStreak),
            Timestamp = DateTime.UtcNow,
            LastException = LastException
        });
    }
}
