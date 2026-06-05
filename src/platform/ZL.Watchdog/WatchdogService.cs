using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Watchdog
{
    /// <summary>
    /// 通用 Watchdog 服务。
    /// 周期性检查已注册组件的健康状态，不健康时自动执行重启操作。
    /// 支持滑动窗口限流，防止重启风暴。
    /// 线程安全，可安全地从多个线程调用 Register/Unregister/GetStatus。
    /// </summary>
    public sealed class WatchdogService : IDisposable
    {
        private readonly WatchdogOptions _options;
        private readonly ConcurrentDictionary<string, EntryState> _entries =
            new ConcurrentDictionary<string, EntryState>();
        private CancellationTokenSource _cts;
        private Task _loopTask;
        private long _totalChecks;
        private long _totalRestarts;
        private long _totalAlerts;
        private bool _disposed;

        /// <summary>健康检查失败时触发</summary>
        public event EventHandler<HealthCheckFailedEventArgs> HealthCheckFailed;

        /// <summary>重启完成时触发</summary>
        public event EventHandler<RestartedEventArgs> Restarted;

        /// <summary>总检查次数</summary>
        public long TotalChecks => Interlocked.Read(ref _totalChecks);

        /// <summary>总重启次数</summary>
        public long TotalRestarts => Interlocked.Read(ref _totalRestarts);

        /// <summary>总告警次数</summary>
        public long TotalAlerts => Interlocked.Read(ref _totalAlerts);

        /// <summary>
        /// 创建 Watchdog 服务实例。
        /// </summary>
        /// <param name="options">配置选项</param>
        public WatchdogService(WatchdogOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            var errors = options.Validate();
            if (errors.Count > 0)
                throw new ArgumentException("配置无效: " + string.Join("; ", errors));
        }

        /// <summary>
        /// 注册被监控组件。
        /// </summary>
        public void Register(WatchedEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            _entries[entry.Name] = new EntryState(entry);
        }

        /// <summary>
        /// 注销被监控组件。
        /// </summary>
        public bool Unregister(string name)
        {
            return _entries.TryRemove(name, out _);
        }

        /// <summary>
        /// 启动监控循环。
        /// </summary>
        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WatchdogService));
            if (_cts != null) return; // 已启动

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        /// <summary>
        /// 停止监控循环并等待退出。
        /// </summary>
        public void Stop()
        {
            if (_cts == null) return;
            _cts.Cancel();
            try { _loopTask?.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _cts.Dispose();
            _cts = null;
        }

        /// <summary>
        /// 获取所有被监控组件的当前状态。
        /// </summary>
        public WatchdogStatus GetStatus()
        {
            var entryStatuses = new List<EntryStatus>();
            foreach (var kvp in _entries)
            {
                var state = kvp.Value;
                lock (state.Lock)
                {
                    entryStatuses.Add(new EntryStatus
                    {
                        Name = state.Entry.Name,
                        Healthy = IsHealthy(state),
                        RestartCount = state.RestartTimestamps.Count,
                        RestartLimitReached = IsLimitReached(state),
                        LastRestartAt = state.RestartTimestamps.Count > 0
                            ? state.RestartTimestamps[state.RestartTimestamps.Count - 1]
                            : (DateTime?)null
                    });
                }
            }

            return new WatchdogStatus
            {
                Running = _cts != null && !_cts.IsCancellationRequested,
                TotalChecks = TotalChecks,
                TotalRestarts = TotalRestarts,
                TotalAlerts = TotalAlerts,
                Entries = entryStatuses.ToArray()
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.CheckIntervalSeconds * 1000, ct).ConfigureAwait(false);
                    Interlocked.Increment(ref _totalChecks);
                    CheckAll();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { /* 循环不因单次异常中断 */ }
            }
        }

        private void CheckAll()
        {
            foreach (var kvp in _entries)
            {
                var state = kvp.Value;
                bool healthy;
                try { healthy = state.Entry.HealthCheck(); }
                catch { healthy = false; }

                if (healthy) continue;

                HandleUnhealthy(state);
            }
        }

        private void HandleUnhealthy(EntryState state)
        {
            lock (state.Lock)
            {
                PruneTimestamps(state);

                if (!state.Entry.AutoRestart || state.Entry.Restart == null)
                {
                    Interlocked.Increment(ref _totalAlerts);
                    HealthCheckFailed?.Invoke(this,
                        new HealthCheckFailedEventArgs(state.Entry.Name, false, "AutoRestart 未启用或无 Restart 委托"));
                    return;
                }

                if (IsLimitReached(state))
                {
                    Interlocked.Increment(ref _totalAlerts);
                    HealthCheckFailed?.Invoke(this,
                        new HealthCheckFailedEventArgs(state.Entry.Name, false,
                            $"重启次数已达上限 ({_options.MaxRestartsPerWindow}/{_options.WindowSeconds}s)"));
                    return;
                }

                HealthCheckFailed?.Invoke(this,
                    new HealthCheckFailedEventArgs(state.Entry.Name, true));

                bool success;
                try { success = state.Entry.Restart(state.Entry.Name); }
                catch { success = false; }

                state.RestartTimestamps.Add(DateTime.UtcNow);
                Interlocked.Increment(ref _totalRestarts);

                Restarted?.Invoke(this,
                    new RestartedEventArgs(state.Entry.Name, success, state.RestartTimestamps.Count));
            }
        }

        private bool IsHealthy(EntryState state)
        {
            try { return state.Entry.HealthCheck(); }
            catch { return false; }
        }

        private bool IsLimitReached(EntryState state)
        {
            return state.RestartTimestamps.Count >= _options.MaxRestartsPerWindow;
        }

        private void PruneTimestamps(EntryState state)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-_options.WindowSeconds);
            state.RestartTimestamps.RemoveAll(t => t < cutoff);
        }

        /// <summary>内部状态：每个被监控组件的重启时间戳记录</summary>
        private class EntryState
        {
            public WatchedEntry Entry { get; }
            public List<DateTime> RestartTimestamps { get; } = new List<DateTime>();
            public object Lock { get; } = new object();

            public EntryState(WatchedEntry entry) { Entry = entry; }
        }
    }
}
