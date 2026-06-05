using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ZL.ConnectionGuard.Logging;
using ZL.ConnectionGuard.Models;

namespace ZL.ConnectionGuard
{
    /// <summary>
    /// 链路卫士：负责连接生命周期、自动重连、心跳保活与发送串行化。
    /// 设计目标：确定性停机、线程安全、异常隔离、可观测性。
    /// </summary>
    public sealed class ConnectionGuard : IDisposable
    {
        private readonly IChannelAdapter _channel;
        private readonly ConnectionGuardOptions _options;
        private readonly IGuardLogger _logger;
        // 生命周期取消令牌：统一控制维护线程退出。
        private readonly CancellationTokenSource _lifecycleCts = new CancellationTokenSource();
        // 维护循环退出信号：Stop/Dispose 等待该信号实现优雅停机。
        private readonly TaskCompletionSource<bool> _maintenanceLoopStopped =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 发送串行化锁：避免同一连接并发写导致帧交织。
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        // 状态事件队列：保证状态事件触发的顺序与状态变更顺序一致。
        private readonly Channel<(GuardState state, string message)> _stateChannel =
            Channel.CreateUnbounded<(GuardState, string)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        private Task? _stateLoopTask;

        // 连接状态（原子变量）：避免并发读写导致状态错乱。
        private volatile int _connectionState = (int)GuardState.Disconnected;
        // 防重入启动标记：确保 Start() 只会启动一个维护循环。
        private int _isRunning;

        // 统计时间戳：用于心跳与看门狗判断。
        private DateTime _lastReceiveTime = DateTime.MinValue;
        private DateTime _lastSendTime = DateTime.MinValue;
        private DateTime _lastHeartbeatErrorLogTime = DateTime.MinValue;
        private DateTime _connectedAt = DateTime.MinValue;
        private bool _hasSentSinceConnect;

        public ConnectionGuard(IChannelAdapter channel, ConnectionGuardOptions? options = null, IGuardLogger? logger = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _options = options ?? new ConnectionGuardOptions();
            _logger = logger ?? NullLogger.Instance;
            _channel.OnDataReceived += HandleRawData;
        }

        public GuardState CurrentState => (GuardState)_connectionState;

        public event Action<GuardState, string>? OnStateChanged;
        public event Action<byte[]>? OnDataReceived;

        public void Start()
        {
            // 确保 Start 只执行一次。
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            {
                _logger.Warn("Guard is already running.");
                return;
            }

            // LongRunning 避免占用线程池工作线程。
            _stateLoopTask ??= Task.Run(StateEventLoop, _lifecycleCts.Token);
            Task.Factory.StartNew(MaintenanceLoop, _lifecycleCts.Token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default)
                .Unwrap()
                .ContinueWith(t =>
                {
                    // 标记维护循环彻底退出。
                    _maintenanceLoopStopped.TrySetResult(true);
                    if (t.IsFaulted)
                    {
                        _logger.Error("CRITICAL: MaintenanceLoop crashed!", t.Exception);
                        SetState(GuardState.Faulted, $"Internal Error: {t.Exception?.InnerException?.Message}");
                    }
                }, TaskScheduler.Default);
        }

        /// <summary>
        /// 优雅停机：触发取消并等待维护循环退出。
        /// </summary>
        public Task StopAsync()
        {
            if (_lifecycleCts.IsCancellationRequested) return Task.CompletedTask;

            _logger.Info("Stopping guard...");
            _lifecycleCts.Cancel();
            return _maintenanceLoopStopped.Task;
        }

        /// <summary>
        /// 同步停机：带超时兜底，避免调用方永久阻塞。
        /// </summary>
        public void Stop()
        {
            StopAsync().Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// 发送数据（串行化）：若未连接则直接失败。
        /// </summary>
        public async Task<bool> SendAsync(byte[] data)
        {
            return await SendAsync(data, SendPriority.Normal);
        }

        public async Task<bool> SendAsync(byte[] data, SendPriority priority)
        {
            if (CurrentState != GuardState.Connected) return false;
            if (data == null || data.Length == 0) return false;
            // 当前实现不做中断式抢占，优先级只用于未来扩展。
            _ = priority;
            return await TrySendInternalAsync(data);
        }

        private async Task MaintenanceLoop()
        {
            _logger.Info("Maintenance loop started.");
            int currentDelay = _options.ReconnectMinDelayMs;

            while (!_lifecycleCts.IsCancellationRequested)
            {
                try
                {
                    // A. 建链阶段
                    if (!_channel.IsConnected)
                    {
                        if (CurrentState == GuardState.Connected)
                        {
                            SetState(GuardState.Reconnecting, "Connection lost.");
                        }
                        else if (CurrentState == GuardState.Disconnected)
                        {
                            SetState(GuardState.Connecting, "Connecting...");
                        }

                        try
                        {
                            await _channel.OpenAsync(_lifecycleCts.Token);
                            SetState(GuardState.Connected, "Connected.");
                            currentDelay = _options.ReconnectMinDelayMs;
                            _connectedAt = DateTime.Now;
                            _lastReceiveTime = DateTime.Now;
                            _lastSendTime = DateTime.Now;
                            _hasSentSinceConnect = false;
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn($"Connection failed: {ex.Message}. Retry in {currentDelay}ms");
                            // 重连等待可被取消，避免停机卡住。
                            double jitterFactor = Math.Max(0, _options.ReconnectJitterFactor);
                            int jitter = jitterFactor > 0
                                ? (int)Math.Round(currentDelay * (Random.Shared.NextDouble() * jitterFactor))
                                : 0;
                            await Task.Delay(currentDelay + jitter, _lifecycleCts.Token);
                            currentDelay = Math.Min(currentDelay * 2, _options.ReconnectMaxDelayMs);
                            continue;
                        }
                    }

                    // B. 保活阶段
                    if (_channel.IsConnected)
                    {
                        var now = DateTime.Now;

                        // 看门狗：长时间无接收触发重连。
                        if (_options.DeviceDeadTimeoutMs > 0
                            && ShouldCheckWatchdog(now)
                            && (now - _lastReceiveTime).TotalMilliseconds > _options.DeviceDeadTimeoutMs)
                        {
                            _logger.Warn($"Watchdog timeout ({(now - _lastReceiveTime).TotalSeconds:F1}s). Reconnecting...");
                            TriggerReconnect();
                            continue;
                        }

                        // 心跳：超过发送间隔触发。
                        if (ShouldSendHeartbeat(now))
                        {
                            await ExecuteHeartbeatAsync();
                        }
                    }

                    // 维护循环节奏，可被取消。
                    await Task.Delay(_options.MaintenanceLoopDelayMs, _lifecycleCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("Unexpected error in MaintenanceLoop.", ex);
                    await Task.Delay(1000, _lifecycleCts.Token);
                }
            }

            // 退出时归位状态并关闭连接。
            SetState(GuardState.Disconnected, "Maintenance loop stopped.");
            _ = SafeCloseChannelAsync();
        }

        private bool ShouldSendHeartbeat(DateTime now)
        {
            if (_options.HeartbeatStrategy == null && _options.HeartbeatData == null) return false;
            return (now - _lastSendTime).TotalMilliseconds > _options.HeartbeatIntervalMs;
        }

        private async Task ExecuteHeartbeatAsync()
        {
            try
            {
                byte[]? payload = null;
                if (_options.HeartbeatStrategy != null)
                {
                    payload = _options.HeartbeatStrategy.CreateHeartbeat();
                }
                else if (_options.HeartbeatData != null)
                {
#pragma warning disable CS0618
                    payload = _options.HeartbeatData;
#pragma warning restore CS0618
                }

                if (payload != null && payload.Length > 0)
                {
                    _logger.Debug("Sending heartbeat...");
                    // 心跳复用 SendAsync，确保串口/Socket 单通道安全。
                    await SendAsync(payload, SendPriority.High);
                }
            }
            catch (Exception ex)
            {
                // 限频日志，避免心跳异常刷屏。
                if ((DateTime.Now - _lastHeartbeatErrorLogTime).TotalSeconds > 10)
                {
                    _logger.Warn($"Heartbeat generation failed: {ex.Message}");
                    _lastHeartbeatErrorLogTime = DateTime.Now;
                }
            }
        }

        private void HandleRawData(byte[] data)
        {
            _lastReceiveTime = DateTime.Now;
            try
            {
                OnDataReceived?.Invoke(data);
            }
            catch (Exception ex)
            {
                // 用户回调异常隔离，避免影响维护线程。
                _logger.Error("Error in OnDataReceived user callback.", ex);
            }
        }

        private void TriggerReconnect()
        {
            // 异步关闭，避免阻塞维护线程。
            Task.Run(SafeCloseChannelAsync);
        }

        private async Task SafeCloseChannelAsync()
        {
            try
            {
                await _channel.CloseAsync();
            }
            catch
            {
                // Ignore close errors.
            }
        }

        private void SetState(GuardState state, string message)
        {
            int oldState = Interlocked.Exchange(ref _connectionState, (int)state);
            if (oldState == (int)state) return;
            _logger.Info(message);
            // 状态事件进入队列，确保顺序一致。
            _stateChannel.Writer.TryWrite((state, message));
        }

        private async Task StateEventLoop()
        {
            try
            {
                await foreach (var item in _stateChannel.Reader.ReadAllAsync(_lifecycleCts.Token))
                {
                    try
                    {
                        if (_options.StateCallbackMode == StateCallbackMode.Async)
                        {
                            await Task.Run(() => OnStateChanged?.Invoke(item.state, item.message), _lifecycleCts.Token);
                        }
                        else
                        {
                            OnStateChanged?.Invoke(item.state, item.message);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 用户回调异常隔离。
                        _logger.Error("Error in OnStateChanged user callback.", ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        public void Dispose()
        {
            // 先优雅停机，再释放资源。
            try
            {
                Stop();
            }
            catch
            {
                // Ignore shutdown errors.
            }

            try
            {
                _lifecycleCts.Cancel();
            }
            catch
            {
                // Ignore cancel errors.
            }

            try
            {
                _stateChannel.Writer.TryComplete();
            }
            catch
            {
                // Ignore channel completion errors.
            }
            try
            {
                _stateLoopTask?.Wait(2000);
            }
            catch
            {
                // Ignore shutdown wait errors.
            }
            try
            {
                _lifecycleCts.Dispose();
            }
            catch
            {
                // Ignore dispose errors.
            }
            try
            {
                _sendLock.Dispose();
            }
            catch
            {
                // Ignore dispose errors.
            }
            try
            {
                _channel.Dispose();
            }
            catch
            {
                // Ignore dispose errors.
            }
        }
        private async Task<bool> TrySendInternalAsync(byte[] data)
        {
            if (CurrentState != GuardState.Connected) return false;

            try
            {
                // 等待锁时带取消：Stop 时可立即退出。
                if (!await _sendLock.WaitAsync(_options.SendLockTimeoutMs, _lifecycleCts.Token))
                {
                    _logger.Warn("Send timeout: could not acquire send lock.");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            try
            {
                // 二次状态检查：避免锁等待期间发生断线。
                if (CurrentState != GuardState.Connected) return false;
                using var timeoutCts = new CancellationTokenSource(_options.SendTimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token, timeoutCts.Token);
                await _channel.SendAsync(data, linkedCts.Token);
                _lastSendTime = DateTime.Now;
                _hasSentSinceConnect = true;
                return true;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && !_lifecycleCts.IsCancellationRequested)
                {
                    _logger.Warn("Send timeout: forcing reconnect.");
                    TriggerReconnect();
                    return false;
                }
                _logger.Error("Send failed, triggering reconnect.", ex);
                TriggerReconnect();
                return false;
            }
            finally
            {
                try { _sendLock.Release(); } catch { }
            }
        }

        private bool ShouldCheckWatchdog(DateTime now)
        {
            if (_options.WatchdogWarmupMs > 0
                && (now - _connectedAt).TotalMilliseconds < _options.WatchdogWarmupMs)
            {
                return false;
            }

            if (_options.WatchdogRequiresSend && !_hasSentSinceConnect)
            {
                return false;
            }

            return true;
        }
    }
}
