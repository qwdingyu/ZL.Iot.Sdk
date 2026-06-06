using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 死信管理器 — 管理 Pipeline 内部的内存死信队列。
    /// 负责：入队、重试计数、容量控制、持久化触发、指数退避自动重试调度。
    /// 持久化由 DeadLetterStore（外部设置）负责，本类仅触发。
    /// </summary>
    public class PipelineDeadLetterManager
    {
        // ---- 内存死信队列（快照/展示用） ----
        private readonly ConcurrentQueue<DeadLetterMessage> _queue = new();
        private readonly object _queueLock = new();
        private readonly int _maxSize;
        private DeadLetterStore? _store;

        // ---- 延迟重试队列（PriorityQueue: 包装项 → 到期时间 tick） ----
        // .NET 8 PriorityQueue is a min-heap by default (lowest priority dequeued first).
        private readonly PriorityQueue<RetryDelayEntry, long> _retryDelayQueue = new();
        private readonly object _retryQueueLock = new();

        /// <summary>
        /// 延迟重试包装项 — 将 DeadLetterMessage 与其到期时间绑定。
        /// </summary>
        private sealed class RetryDelayEntry
        {
            public DeadLetterMessage Message { get; }
            public DateTimeOffset RetryAt { get; }

            public RetryDelayEntry(DeadLetterMessage message, DateTimeOffset retryAt)
            {
                Message = message;
                RetryAt = retryAt;
            }
        }

        // 自动重试调度
        private Task? _retryLoopTask;
        private CancellationTokenSource? _retryCts;

        /// <summary>
        /// 死信消息最大重试次数 — 超过此值的消息将被永久丢弃，防止死信膨胀。
        /// </summary>
        public const int MaxDeadLetterRetries = 3;

        /// <summary>
        /// 指数退避间隔（秒）：RetryCount=1 → 30s, =2 → 2min, =3 → 10min。
        /// </summary>
        private static readonly TimeSpan[] BackoffIntervals =
        {
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10)
        };

        /// <summary>
        /// 新死信消息入队时触发的事件。
        /// </summary>
        public event Action<DeadLetterMessage>? DeadLetterAdded;


        /// <summary>
        /// 死信消息可重试时触发的事件（保留向后兼容）。
        /// </summary>
        public event Action<IReadOnlyList<DeadLetterMessage>>? RetryableMessagesAvailable;

        /// <summary>
        /// 重试回调 — 自动重试循环通过此委托将到期的死信消息重新注入 Pipeline。
        /// 由外部（GatewayManager）在构造函数中注入，避免循环依赖。
        /// </summary>
        public Func<Message, CancellationToken, Task>? RetryCallback { get; set; }

        public PipelineDeadLetterManager(int maxSize = 1000)
        {
            _maxSize = maxSize;
        }

        /// <summary>
        /// 设置死信持久化存储。传入 null 禁用持久化。
        /// </summary>
        public DeadLetterStore? Store
        {
            get => _store;
            set
            {
                _store?.Dispose();
                _store = value;
            }
        }

        /// <summary>
        /// 死信队列中的消息数量
        /// </summary>
        public int Count => _queue.Count;

        /// <summary>
        /// 将失败消息加入死信队列。
        /// 自动递增重试计数，超过上限则永久丢弃。
        /// <para>克隆原始消息后在克隆体上管理重试计数，原始消息零副作用。</para>
        /// </summary>
        public void Add(Message message, Exception exception, Action? recordDeadLetterMetric)
        {
            recordDeadLetterMetric?.Invoke();

            // 克隆消息，避免修改原始消息的 Metadata（P0-5 修复）
            var cloned = message.Clone();

            // 从 Metadata 恢复已有重试计数（RetryDeadLettersAsync 注入），避免重置为 1 导致无限循环
            int retryCount = 1;
            if (cloned.Metadata.TryGetValue(GatewayMetadataKeys.DeadLetterRetryCount, out var existingRetry)
                && int.TryParse(existingRetry, out var parsedRetry)
                && parsedRetry > 0)
            {
                retryCount = parsedRetry + 1;
            }
            cloned.Metadata[GatewayMetadataKeys.DeadLetterRetryCount] = retryCount.ToString();

            if (retryCount > MaxDeadLetterRetries)
            {
                GatewayLog.Warn("PipelineDeadLetterManager",
                    $"Dead letter exceeded max retries ({MaxDeadLetterRetries}), permanently dropping message for topic '{message.Topic}'");
                _ = PersistAsync(cloned, exception, retryCount, null).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        GatewayLog.Error("PipelineDeadLetterManager",
                            $"Dead letter persistence failed: {t.Exception?.GetBaseException().Message}");
                }, TaskContinuationOptions.OnlyOnFaulted);
                return;
            }

            var entry = new DeadLetterMessage
            {
                Message = cloned,
                Exception = exception,
                FailedAt = DateTimeOffset.UtcNow,
                RetryCount = retryCount
            };

            EnqueueWithCapacityControl(entry);

            // 加入延迟重试队列（可重试的消息）
            EnqueueForRetry(entry);

            // 通知外部监听者
            DeadLetterAdded?.Invoke(entry);

            // 持久化以 fire-and-forget 方式执行（不阻塞入队），失败时通过 ContinueWith 记录错误日志。
            _ = PersistAsync(cloned, exception, retryCount, null).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    GatewayLog.Error("PipelineDeadLetterManager",
                        $"Dead letter persistence failed: {t.Exception?.GetBaseException().Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// 将重试失败的消息重新加入死信队列（由自动重试循环调用）。
        /// </summary>
        internal void Reenqueue(DeadLetterMessage entry)
        {
            entry.RetryCount++;

            if (entry.RetryCount > MaxDeadLetterRetries)
            {
                GatewayLog.Warn("PipelineDeadLetterManager",
                    $"Dead letter retry {MaxDeadLetterRetries} failed, permanently dropping message for topic '{entry.Message.Topic}'");
                _ = PersistAsync(entry.Message, entry.Exception, entry.RetryCount, null).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        GatewayLog.Error("PipelineDeadLetterManager",
                            $"Dead letter persistence failed: {t.Exception?.GetBaseException().Message}");
                }, TaskContinuationOptions.OnlyOnFaulted);
                return;
            }

            entry.FailedAt = DateTimeOffset.UtcNow;
            EnqueueWithCapacityControl(entry);
            EnqueueForRetry(entry);
        }

        private void EnqueueWithCapacityControl(DeadLetterMessage entry)
        {
            lock (_queueLock)
            {
                while (_queue.Count >= _maxSize)
                {
                    GatewayLog.Warn("PipelineDeadLetterManager",
                        $"Dead letter queue full ({_maxSize} messages), dropping oldest entry to persistence store");
                    if (_queue.TryDequeue(out var oldest))
                    {
                        _ = PersistAsync(oldest.Message, oldest.Exception, oldest.RetryCount, null).ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                GatewayLog.Error("PipelineDeadLetterManager",
                                    $"Dead letter persistence failed: {t.Exception?.GetBaseException().Message}");
                        }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                }
                _queue.Enqueue(entry);
            }
        }

        private void EnqueueForRetry(DeadLetterMessage entry)
        {
            var backoffIndex = entry.RetryCount - 1;
            if (backoffIndex < 0 || backoffIndex >= BackoffIntervals.Length) return;

            var retryAt = entry.FailedAt + BackoffIntervals[backoffIndex];

            lock (_retryQueueLock)
            {
                _retryDelayQueue.Enqueue(new RetryDelayEntry(entry, retryAt), retryAt.UtcDateTime.Ticks);
            }
        }

        /// <summary>
        /// 获取所有死信消息（快照）。
        /// </summary>
        public IReadOnlyList<DeadLetterMessage> GetMessages()
        {
            return _queue.ToArray();
        }

        /// <summary>
        /// 清空死信队列。
        /// </summary>
        public void Clear()
        {
            lock (_queueLock)
            {
                _queue.Clear();
            }
            lock (_retryQueueLock)
            {
                _retryDelayQueue.Clear();
            }
        }

        private async Task PersistAsync(Message message, Exception exception, int retryCount, string? outputName)
        {
            if (_store == null) return;

            try
            {
                var entry = new DeadLetterStore.DeadLetterEntry
                {
                    Topic = message.Topic ?? string.Empty,
                    ContentType = message.ContentType,
                    PayloadText = message.GetTextContent(),
                    PayloadHex = message.GetHexContent(),
                    ExceptionMessage = exception.Message,
                    ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                    OutputName = outputName,
                    RetryCount = retryCount,
                    FailedAt = DateTimeOffset.UtcNow.ToString("O"),

                };
                await _store.AddAsync(entry);
            }
            catch (Exception ex)
            {
                GatewayLog.Warn("PipelineDeadLetterManager", $"Failed to persist dead letter: {ex.Message}");
            }
        }

        /// <summary>
        /// 启动死信自动重试调度 — 基于 PriorityQueue 延迟队列的指数退避策略。
        /// RetryCount=1: 30s 后重试, RetryCount=2: 2min 后重试, RetryCount=3: 10min 后重试。
        /// </summary>
        public void StartAutoRetry()
        {
            if (_retryLoopTask != null) return;

            _retryCts = new CancellationTokenSource();
            _retryLoopTask = Task.Run(() => AutoRetryLoop(_retryCts.Token));
        }

        /// <summary>
        /// 停止死信自动重试调度。
        /// </summary>
        public void StopAutoRetry()
        {
            _retryCts?.Cancel();
            _retryCts?.Dispose();
            _retryCts = null;
            _retryLoopTask = null;
        }

        private async Task AutoRetryLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 等待直到有到期消息或取消
                    var waitResult = await WaitForNextRetryAsync(ct);
                    if (!waitResult) break; // cancelled

                    var now = DateTimeOffset.UtcNow;

                    // 从延迟队列中取出所有到期消息
                    var readyToRetry = new List<DeadLetterMessage>();
                    lock (_retryQueueLock)
                    {
                        while (_retryDelayQueue.Count > 0)
                        {
                            var peek = _retryDelayQueue.Peek();
                            if (peek.RetryAt > now) break;
                            _retryDelayQueue.Dequeue();
                            readyToRetry.Add(peek.Message);
                        }
                    }

                    if (readyToRetry.Count == 0) continue;

                    // 通过 RetryCallback 逐条重试（GatewayManager 注入的回调）
                    RetryableMessagesAvailable?.Invoke(readyToRetry);

                    var failed = new List<DeadLetterMessage>();
                    if (RetryCallback != null)
                    {
                        foreach (var dl in readyToRetry)
                        {
                            try
                            {
                                await RetryCallback(dl.Message, ct);
                                // 重试成功 — 从内存死信队列中移除
                                RemoveFromQueue(dl);
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                break;
                            }
                            catch
                            {
                                failed.Add(dl);
                            }
                        }
                    }
                    else
                    {
                        // 无 RetryCallback：所有消息都视为失败，重新排队
                        failed.AddRange(readyToRetry);
                    }

                    // 将失败的消息重新加入延迟队列
                    foreach (var dl in failed)
                    {
                        Reenqueue(dl);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("PipelineDeadLetterManager", $"Auto-retry loop error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 从内存死信队列中移除指定消息（重试成功时调用）。
        /// </summary>
        private void RemoveFromQueue(DeadLetterMessage entry)
        {
            lock (_queueLock)
            {
                var temp = new ConcurrentQueue<DeadLetterMessage>();
                while (_queue.TryDequeue(out var item))
                {
                    if (item != entry)
                    {
                        temp.Enqueue(item);
                    }
                }
                foreach (var item in temp)
                {
                    _queue.Enqueue(item);
                }
            }
        }

        /// <summary>
        /// 等待直到延迟队列中有到期消息，或取消。
        /// 返回 false 表示已取消。
        /// </summary>
        private async Task<bool> WaitForNextRetryAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                DateTimeOffset? nextRetryAt = null;
                lock (_retryQueueLock)
                {
                    if (_retryDelayQueue.Count > 0)
                    {
                        nextRetryAt = _retryDelayQueue.Peek().RetryAt;
                    }
                }

                if (nextRetryAt == null)
                {
                    // 队列为空，等待 5 秒后重新检查
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                var waitTime = nextRetryAt.Value - DateTimeOffset.UtcNow;
                if (waitTime <= TimeSpan.Zero)
                {
                    return true; // 已有到期消息
                }

                // 精确等待到到期时间（加 100ms 余量避免边界问题）
                await Task.Delay(waitTime.Add(TimeSpan.FromMilliseconds(100)), ct);
                return true;
            }

            return false;
        }
    }
}
