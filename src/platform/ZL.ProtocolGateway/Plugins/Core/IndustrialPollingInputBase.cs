using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins;

/// <summary>
/// 工业协议轮询输入插件公共基类。
/// <para>提供轮询调度器、值缓存、变化检测、连接管理等通用能力。</para>
/// <para>子类只需实现 TryConnectAsync/HasLiveConnection/CleanupConnection/OnPollAsync。</para>
/// </summary>
public abstract class IndustrialPollingInputBase : InputPluginBase
{
    /// <summary>上次各 Tag 的值（用于变化检测）</summary>
    protected readonly ConcurrentDictionary<string, PollValueEntry> _lastValues = new();

    /// <summary>基础重连间隔（毫秒）</summary>
    protected virtual int BaseReconnectIntervalMs => 3000;

    /// <summary>最大重连间隔（毫秒）</summary>
    protected virtual int MaxReconnectIntervalMs => 60000;

    /// <summary>
    /// 子类实现：尝试建立连接并等待直到断开或取消。
    /// 由基类轮询循环调用，返回后自动调用 CleanupConnection。
    /// </summary>
    protected abstract Task TryConnectAsync(CancellationToken ct);

    /// <summary>检查连接是否仍然有效</summary>
    protected virtual bool HasLiveConnection() => false;

    /// <summary>同步清理连接资源</summary>
    protected virtual void CleanupConnection() { }

    /// <summary>
    /// 子类实现：执行一次轮询读取，返回读取到的标签值。
    /// 仅在连接存活时被调用。
    /// </summary>
    protected abstract Task<PollResult[]> OnPollAsync(CancellationToken ct);

    /// <summary>
    /// 子类实现：停止时的额外清理逻辑。
    /// </summary>
    protected virtual Task OnStopCoreAsync() => Task.CompletedTask;

    #region 轮询调度

    /// <summary>
    /// 轮询条目 — 用于优先级队列调度。
    /// </summary>
    private sealed class PollEntry : IComparable<PollEntry>
    {
        public string TagAddress { get; }
        public long NextPollTicks { get; }

        public PollEntry(string tagAddress, long nextPollTicks)
        {
            TagAddress = tagAddress;
            NextPollTicks = nextPollTicks;
        }

        public int CompareTo(PollEntry other) =>
            other is null ? 1 : NextPollTicks.CompareTo(other.NextPollTicks);
    }

    /// <summary>
    /// 注册一个轮询标签。在 OnStartAsync 之前调用。
    /// </summary>
    protected void RegisterPollTag(string address, int pollIntervalMs)
    {
        _pollTags[address] = pollIntervalMs;
    }

    /// <summary>
    /// 清除所有轮询标签。
    /// </summary>
    protected void ClearPollTags()
    {
        _pollTags.Clear();
    }

    private readonly ConcurrentDictionary<string, int> _pollTags = new();

    #endregion

    #region 值比较

    /// <summary>
    /// 轮询结果
    /// </summary>
    protected sealed class PollResult
    {
        public string Address { get; set; }
        public object Value { get; set; }
        public string DataType { get; set; } = "BYTE";

        public PollResult(string address, object value, string dataType = "BYTE")
        {
            Address = address;
            Value = value;
            DataType = dataType;
        }
    }

    /// <summary>
    /// 上次读取的值条目
    /// </summary>
    protected sealed class PollValueEntry
    {
        public object Value { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 检查值是否发生变化。返回 true 表示值已变化（应转发）。
    /// </summary>
    protected bool HasValueChanged(string address, object value)
    {
        if (_lastValues.TryGetValue(address, out var last))
        {
            if (Equals(last.Value, value))
                return false;

            // 值变化了，更新缓存
            _lastValues[address] = new PollValueEntry { Value = value, Timestamp = DateTime.UtcNow };
            return true;
        }

        // 首次看到该地址，记录并转发
        _lastValues.TryAdd(address, new PollValueEntry { Value = value, Timestamp = DateTime.UtcNow });
        return true;
    }

    /// <summary>
    /// 清除值缓存（重置变化检测状态）。
    /// </summary>
    public void ResetValueCache()
    {
        _lastValues.Clear();
    }

    #endregion

    #region 连接管理（由 OnStartAsync 调用）

    /// <summary>
    /// 标准轮询循环 — 在 OnStartAsync 中调用此方法。
    /// 管理连接生命周期 + 按优先级调度轮询。
    /// </summary>
    protected async Task PollingLoopAsync(CancellationToken ct)
    {
        var pollQueue = new PriorityQueue<PollEntry, long>();

        // 初始化轮询队列：所有 tag 立即到期
        var now = DateTime.UtcNow;
        foreach (var kvp in _pollTags)
        {
            pollQueue.Enqueue(
                new PollEntry(kvp.Key, now.Ticks),
                now.Ticks);
        }

        while (!ct.IsCancellationRequested)
        {
            // 连接管理：尝试连接 → 轮询 → 断开检测 → 重连
            int connectStreak = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await TryConnectAsync(ct);
                    connectStreak = 0; // 连接成功，重置
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    connectStreak++;
                    GatewayLog.Warn(Name, $"Connection failed (streak {connectStreak}): {ex.Message}");
                }

                if (!HasLiveConnection())
                {
                    CleanupConnection();

                    if (ct.IsCancellationRequested)
                        return;

                    // 指数退避 (Equal Jitter)
                    int delay = OutputPluginBase.CalculateBackoffDelay(connectStreak, 1000, 60_000);
                    GatewayLog.Info(Name, $"Reconnecting in {delay}ms...");
                    await Task.Delay(delay, ct);
                    continue;
                }

                // 连接成功，执行轮询循环
                await PollCycleAsync(pollQueue, ct);

                // PollCycleAsync 返回说明连接断开或取消
                if (ct.IsCancellationRequested)
                    return;

                CleanupConnection();
                break; // 回到外层连接管理循环
            }
        }
    }

    /// <summary>
    /// 在连接存活期间执行轮询周期。
    /// </summary>
    private async Task PollCycleAsync(PriorityQueue<PollEntry, long> pollQueue, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && HasLiveConnection())
        {
            // 等待最早到期的 tag
            if (pollQueue.Count == 0)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            var next = pollQueue.Peek();
            long nowTicks = DateTime.UtcNow.Ticks;

            if (next.NextPollTicks > nowTicks)
            {
                int waitMs = (int)((next.NextPollTicks - nowTicks) / TimeSpan.TicksPerMillisecond);
                if (waitMs > 5000) waitMs = 5000; // 上限 5s 检查取消
                await Task.Delay(Math.Max(waitMs, 1), ct);
                continue;
            }

            // 到期：出队并调度下次轮询
            pollQueue.Dequeue();

            try
            {
                var results = await OnPollAsync(ct);

                // 处理结果：仅转发值变化的 tag
                if (results != null)
                {
                    foreach (var result in results)
                    {
                        if (HasValueChanged(result.Address, result.Value))
                        {
                            var message = new Message
                            {
                                Topic = Name,
                                ContentType = "application/gateway+json",
                            };

                            message.Writes ??= new System.Collections.Generic.List<TagWrite>();
                            message.Writes.Add(new TagWrite(
                                Address: result.Address,
                                Value: result.Value,
                                DataType: result.DataType,
                                Alias: null,
                                Timestamp: DateTime.UtcNow));

                            message.Intent = MessageIntent.Forward;
                            message.Timestamp = DateTime.UtcNow;

                            try { await InvokeMessageHandler(message); }
                            catch (Exception ex)
                            {
                                GatewayLog.Warn(Name, $"Message handler error: {ex.Message}");
                            }
                        }
                    }
                }

                // 重新入队所有 tag（使用各自的轮询间隔）
                RequeueAll(pollQueue);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
            {
                // 连接可能已断开，退出轮询周期
                GatewayLog.Warn(Name, $"Poll error: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>
    /// 将所有注册的 tag 重新入队。
    /// </summary>
    private void RequeueAll(PriorityQueue<PollEntry, long> pollQueue)
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _pollTags)
        {
            long nextTicks = now.Ticks + (long)kvp.Value * TimeSpan.TicksPerMillisecond;
            pollQueue.Enqueue(new PollEntry(kvp.Key, nextTicks), nextTicks);
        }
    }

// Removed: unified with OutputPluginBase.CalculateBackoffDelay

    #endregion

    #region 生命周期

    /// <summary>
    /// 子类应覆盖 OnStartAsync 来初始化资源，然后调用 PollingLoopAsync。
    /// 默认实现直接启动轮询循环。
    /// </summary>
    protected override async Task OnStartAsync(CancellationToken ct)
    {
        await PollingLoopAsync(ct);
    }

    /// <summary>
    /// 停止逻辑：调用 OnStopCoreAsync + 清理连接。
    /// </summary>
    protected override async Task OnStopAsync()
    {
        try { await OnStopCoreAsync(); } catch { }
        CleanupConnection();
    }

    #endregion
}
