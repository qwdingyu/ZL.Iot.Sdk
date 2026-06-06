using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ZL.Collections.Collections;

namespace ZL.ProtocolGateway
{
    // RouteRule 和 MessageRouter 已移至 MessageRouter.cs

    /// <summary>
    /// 消息追踪事件 — 记录消息在 Pipeline 中各阶段的流转信息。
    /// </summary>
    public record MessageTraceEvent(
        string TraceId,
        string Stage,        // "Enqueued", "Filtered", "Transformed", "Routed", "Sent", "DeadLetter"
        string? OutputName,
        string? Result,      // "Success", "Failed", "Dropped", "CircuitOpen"
        DateTimeOffset Timestamp
    );

    /// <summary>
    /// 流水线接口
    /// </summary>
    public interface IPipeline : IDisposable
    {
        void AddTransformer(Func<Message, Task<Message>> transformer);
        void AddFilter(Func<Message, Task<bool>> filter);
        void AddRouter(RouteRule rule);
        void RegisterOutput(IOutputPlugin output);
        void RegisterOutput(string name, IOutputPlugin output);
        bool UnregisterOutput(string name);
        Task ProcessAsync(Message message);
        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync();
    }

    /// <summary>
    /// 增强的消息流水线 - 添加背压、超时、重试和死信队列。
    /// 
    /// 职责：Channel 消费循环 + 编排 Filter/Transformer/Router/Output 各组件。
    /// 断路器、指标采集、死信管理、发送策略已拆分为独立类。
    /// </summary>
    public class ResilientMessagePipeline : IPipeline, IAsyncDisposable
    {
        private readonly List<Func<Message, Task<Message>>> _transformers = new();
        private readonly List<Func<Message, Task<bool>>> _filters = new();
        private readonly MessageRouter _router = new();
        private readonly ConcurrentDictionary<string, IOutputPlugin> _outputs = new();
        private volatile IReadOnlyList<GatewaySendResult> _lastSendResults = Array.Empty<GatewaySendResult>();
        private volatile int _isRunning;
        private CancellationTokenSource _cts;

        /// <summary>有界消息队列 - 实现背压控制</summary>
        private Channel<Message> _messageQueue;

        // ---- 提取的组件 ----

        /// <summary>死信管理器 — 内存队列 + 重试计数 + 持久化触发</summary>
        private readonly PipelineDeadLetterManager _deadLetterManager = new();

        /// <summary>输出插件断路器</summary>
        private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers = new();

        /// <summary>流水线指标收集器</summary>
        private readonly PipelineMetricsCollector _metrics = new();

        /// <summary>发送策略（重试+超时+断路器）</summary>
        private PipelineSendStrategy _sendStrategy;

        /// <summary>并发消息处理限制</summary>
        private readonly SemaphoreSlim _concurrencyLimiter;
        private const int DefaultMaxConcurrentMessages = 16;

        /// <summary>后台处理循环任务引用</summary>
        private Task? _processLoopTask;

        /// <summary>正在处理中的消息数（Interlocked 访问）</summary>
        private int _inFlightCount;

        /// <summary>已从队列取出但尚未处理完成的消息数（等待 SemaphoreSlim 或正在处理）</summary>
        private int _pendingMessageCount;

        /// <summary>等待所有消息处理完成（测试用）</summary>
        public async Task WaitForIdleAsync(int timeoutMs = 5000)
        {
            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_pendingMessageCount == 0 && (_messageQueue?.Reader.Count ?? 0) == 0)
                    return;
                await Task.Delay(50);
            }
        }

        // ---- 可配置属性 ----

        /// <summary>每个输出的发送超时（毫秒）</summary>
        public int SendTimeoutMs { get; set; } = 30000;

        /// <summary>最大重试次数</summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>重试基础延迟（毫秒），使用指数退避</summary>
        public int RetryBaseDelayMs { get; set; } = 100;

        /// <summary>消息队列容量</summary>
        public int QueueCapacity { get; set; } = 10000;

        /// <summary>断路器失败阈值</summary>
        public int CircuitBreakerFailureThreshold { get; set; } = 5;

        /// <summary>断路器恢复时间（毫秒）</summary>
        public int CircuitBreakerRecoveryTimeMs { get; set; } = 60000;

        /// <summary>
        /// 最大并发消息处理数。
        /// 注意：SemaphoreSlim 不支持运行时调整容量，此属性仅在构造函数中读取。
        /// GatewayConfigManager.ReloadPipelineConfigAsync 修改此值不会生效，需重建 Pipeline 实例。
        /// </summary>
        public int MaxConcurrentMessages { get; set; } = 16;

        /// <summary>最后一次发送结果</summary>
        public IReadOnlyList<GatewaySendResult> LastSendResults => _lastSendResults;

        /// <summary>断路器状态变更事件</summary>
        public event Action<CircuitBreakerStateChangedArgs>? CircuitBreakerStateChanged;

        /// <summary>新死信消息入队时触发的事件（桥接自 PipelineDeadLetterManager）</summary>
        public event Action<DeadLetterMessage>? DeadLetterAdded
        {
            add => _deadLetterManager.DeadLetterAdded += value;
            remove => _deadLetterManager.DeadLetterAdded -= value;
        }

        /// <summary>
        /// 消息追踪事件 — 在消息流经 Pipeline 各阶段时触发。
        /// 用于端到端调试：入队 → Filter → Transform → Route → Send → 成功/失败。
        /// </summary>
        public event Action<MessageTraceEvent>? MessageTraced;

        /// <summary>最近 N 条追踪事件（固定容量环形缓冲区，零分配追加）</summary>
        private readonly FixedSizeRingBuffer<MessageTraceEvent> _traceBuffer = new(MaxTraceBufferSize);
        private const int MaxTraceBufferSize = 500;

        /// <summary>获取最近的追踪事件（最近 count 条，按时间正序）</summary>
        public IReadOnlyList<MessageTraceEvent> GetRecentTraces(int count = 50)
        {
            return _traceBuffer.GetNewest(count);
        }

        private void EmitTrace(string traceId, string stage, string? outputName, string? result)
        {
            MessageTraced?.Invoke(new MessageTraceEvent(
                traceId, stage, outputName, result, DateTimeOffset.UtcNow));
        }

        private void BufferTrace(MessageTraceEvent trace)
        {
            _traceBuffer.Append(trace);
            // Tap: record for protocol analysis
            _tap?.Record(trace.TraceId, trace.Stage, trace.OutputName, trace.Result);
        }

        #region Pipeline Tap (协议分析)

        /// <summary>
        /// Pipeline Tap 快照 — 记录消息在各阶段的流转统计，用于协议分析。
        /// </summary>
        public class PipelineTapSnapshot
        {
            public int TotalEnqueued { get; set; }
            public int TotalFilteredPassed { get; set; }
            public int TotalFilteredDropped { get; set; }
            public int TotalTransformed { get; set; }
            public int TotalRouted { get; set; }
            public int TotalSentSuccess { get; set; }
            public int TotalSentFailed { get; set; }
            public int TotalDeadLettered { get; set; }
            public Dictionary<string, int> OutputMessageCounts { get; set; } = new();
            public Dictionary<string, int> TopicCounts { get; set; } = new();
            public double AvgLatencyMs { get; set; }
            public long TotalLatencySumMs { get; set; }
            public int LatencySampleCount { get; set; }
            public DateTimeOffset CapturedAt { get; set; }
        }

        private readonly InMemoryPipelineTap _tap = new();

        /// <summary>
        /// 获取协议分析快照。
        /// </summary>
        public PipelineTapSnapshot GetTapSnapshot()
        {
            return _tap.GetSnapshot();
        }

        /// <summary>
        /// 清空 Tap 数据。
        /// </summary>
        public void ClearTapData()
        {
            _tap.Reset();
        }

        #endregion

        /// <summary>死信队列中的消息数量</summary>
        public int DeadLetterCount => _deadLetterManager.Count;

        /// <summary>当前队列中的消息数量</summary>
        public int QueuedMessageCount => _messageQueue?.Reader.Count ?? 0;

        /// <summary>获取流水线指标收集器</summary>
        public PipelineMetricsCollector Metrics => _metrics;

        /// <summary>
        /// 设置死信持久化存储。传入 null 禁用持久化。
        /// </summary>
        public DeadLetterStore? DeadLetterStore
        {
            get => _deadLetterManager.Store;
            set => _deadLetterManager.Store = value;
        }

        /// <summary>
        /// 死信自动重试回调 — 将到期消息重新注入 Pipeline。
        /// 由外部（GatewayManager）注入，避免循环依赖。
        /// </summary>
        public Func<Message, CancellationToken, Task>? RetryCallback
        {
            get => _deadLetterManager.RetryCallback;
            set => _deadLetterManager.RetryCallback = value;
        }

        /// <summary>
        /// 已注册的输出插件集合（只读视图）。
        /// </summary>
        public IReadOnlyCollection<IOutputPlugin> RegisteredOutputs => new List<IOutputPlugin>(_outputs.Values);

        public ResilientMessagePipeline()
        {
            InitializeQueue();
            _concurrencyLimiter = new SemaphoreSlim(MaxConcurrentMessages, MaxConcurrentMessages);
            _sendStrategy = CreateSendStrategy();
        }

        private void InitializeQueue()
        {
            var options = new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };
            _messageQueue = Channel.CreateBounded<Message>(options);
        }

        private PipelineSendStrategy CreateSendStrategy()
        {
            return new PipelineSendStrategy(
                () => SendTimeoutMs, () => MaxRetryAttempts, () => RetryBaseDelayMs);
        }

        // ---- IPipeline 接口实现 ----

        public void AddTransformer(Func<Message, Task<Message>> transformer)
        {
            _transformers.Add(transformer);
        }

        public void AddFilter(Func<Message, Task<bool>> filter)
        {
            _filters.Add(filter);
        }

        public void AddRouter(RouteRule rule)
        {
            _router.AddRule(rule);
        }

        public void RegisterOutput(IOutputPlugin output)
        {
            if (output != null && !string.IsNullOrEmpty(output.Name))
            {
                _outputs[output.Name] = output;
                var breaker = new CircuitBreaker(
                    CircuitBreakerFailureThreshold,
                    CircuitBreakerRecoveryTimeMs,
                    output.Name);
                breaker.StateChanged += args => CircuitBreakerStateChanged?.Invoke(args);
                _circuitBreakers[output.Name] = breaker;
            }
        }

        /// <summary>
        /// 注册输出插件（使用指定名称）。
        /// </summary>
        public void RegisterOutput(string name, IOutputPlugin output)
        {
            if (output != null && !string.IsNullOrEmpty(name))
            {
                _outputs[name] = output;
                var breaker = new CircuitBreaker(
                    CircuitBreakerFailureThreshold,
                    CircuitBreakerRecoveryTimeMs,
                    name);
                breaker.StateChanged += args => CircuitBreakerStateChanged?.Invoke(args);
                _circuitBreakers[name] = breaker;
            }
        }

        /// <summary>
        /// 移除输出插件（从 Pipeline 中注销，并清除对应的断路器）。
        /// </summary>
        public bool UnregisterOutput(string name)
        {
            if (_outputs.TryRemove(name, out _))
            {
                _circuitBreakers.TryRemove(name, out _);
                return true;
            }
            return false;
        }

        // ---- 生命周期 ----

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var startedOutputs = new List<IOutputPlugin>();
            try
            {
                foreach (var output in _outputs.Values)
                {
                    await output.StartAsync(_cts.Token);
                    startedOutputs.Add(output);
                }

                _processLoopTask = Task.Run(() => ProcessMessagesLoop(_cts.Token), _cts.Token);
            }
            catch
            {
                foreach (var output in startedOutputs)
                {
                    try { await output.StopAsync(); } catch { }
                }

                Interlocked.Exchange(ref _isRunning, 0);
                _cts.Dispose();
                _cts = null;

                throw;
            }
        }

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _isRunning, 0) == 0) return;

            _messageQueue.Writer.TryComplete();

            CancellationTokenSource? drainCts = null;
            CancellationTokenSource? ctsToCancel = null;
            try
            {
                drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                // Wait for the loop to finish processing in-flight messages
                while (_pendingMessageCount > 0 && !drainCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(50, drainCts.Token);
                }

                // Drain any remaining messages from the channel before cancelling.
                // The processing loop may be suspended on ReadAsync and not yet
                // have dequeued the last message, so we must consume them here.
                while (_messageQueue.Reader.TryRead(out _)) { }

                ctsToCancel = _cts;
                _cts = null;
                ctsToCancel?.Cancel();

                if (_processLoopTask != null)
                {
                    try
                    {
                        await _processLoopTask.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch (TimeoutException)
                    {
                        GatewayLog.Warn("ResilientMessagePipeline", "Process loop did not complete within 10s during shutdown.");
                    }
                    catch (OperationCanceledException)
                    {
                        // 取消是预期行为
                    }
                    catch (ObjectDisposedException)
                    {
                        // 取消令牌源已被 dispose
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 排空超时，继续关闭流程
            }
            finally
            {
                drainCts?.Dispose();
                ctsToCancel?.Dispose();
            }

            // 停止所有输出插件 — 每个输出带独立超时（10s）
            foreach (var output in _outputs.Values)
            {
                try
                {
                    var stopTask = output.StopAsync();
                    var completed = await Task.WhenAny(stopTask, Task.Delay(10_000));

                    if (completed != stopTask)
                    {
                        GatewayLog.Warn("ResilientMessagePipeline",
                            $"Output '{output.Name}' StopAsync did not complete within 10s; continuing shutdown.");
                    }
                    else
                    {
                        await stopTask;
                    }
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("ResilientMessagePipeline", $"Failed to stop output '{output.Name}': {ex.Message}", ex);
                }
            }
        }

        // ---- 消息入口 ----

        /// <summary>
        /// 处理消息 - 将消息放入队列，实现背压控制
        /// </summary>
        public async Task ProcessAsync(Message message)
        {
            if (message == null) return;
            if (Volatile.Read(ref _isRunning) == 0) return;

            try
            {
                // 背压检测：使用 WaitToWriteAsync 超时判断，避免每条消息创建 CTS（高吞吐 GC 优化）
                var waitTask = _messageQueue.Writer.WaitToWriteAsync().AsTask();
                if (!await waitTask.WaitAsync(TimeSpan.FromSeconds(5)))
                {
                    _metrics.RecordBackpressureWarning();
                    GatewayLog.Warn("Pipeline",
                        $"Backpressure detected: message enqueue blocked >5s (queue capacity: {QueueCapacity})");
                }

                await _messageQueue.Writer.WriteAsync(message);
            }
            catch (ChannelClosedException)
            {
                // Pipeline 已停止，忽略新消息
            }
        }

        /// <summary>
        /// 直接向指定输出插件发送测试消息（同步等待结果，不走 Channel）。
        /// </summary>
        public async Task<GatewaySendResult> TestSendAsync(string outputName, Message message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (!_outputs.TryGetValue(outputName, out var plugin))
            {
                return PipelineSendStrategy.CreateSkippedResult(
                    GatewayTraceContext.EnsureTraceId(message),
                    outputName, message, $"Output '{outputName}' not found");
            }

            var traceId = GatewayTraceContext.EnsureTraceId(message);
            if (!_circuitBreakers.TryGetValue(outputName, out var breaker))
            {
                breaker = new CircuitBreaker(CircuitBreakerFailureThreshold, CircuitBreakerRecoveryTimeMs, outputName);
                _circuitBreakers[outputName] = breaker;
            }

            if (breaker.IsRequestBlocked())
            {
                return PipelineSendStrategy.CreateCircuitOpenResult(traceId, outputName, message);
            }

            return await _sendStrategy.SendAsync(plugin, message, traceId, breaker, AddToDeadLetterQueue, BufferTrace, CancellationToken.None);
        }

        // ---- 内部处理循环 ----

        /// <summary>
        /// 消息处理主循环 — 单读者从 Channel 取消息，通过 SemaphoreSlim 实现有限并发处理
        /// </summary>
        private async Task ProcessMessagesLoop(CancellationToken ct)
        {
            Message? pendingMessage = null;

            try
            {
                while (true)
                {
                    if (pendingMessage == null)
                    {
                        try
                        {
                            pendingMessage = await _messageQueue.Reader.ReadAsync(ct);
                            Interlocked.Increment(ref _pendingMessageCount);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (ChannelClosedException)
                        {
                            break;
                        }
                    }

                    bool acquired = false;
                    try
                    {
                        await _concurrencyLimiter.WaitAsync(ct);
                        acquired = true;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (acquired)
                    {
                        var msg = pendingMessage;
                        pendingMessage = null;

                        // P0 修复（v3）：消除外层 Task.Run。
                        // 原设计用 Task.Run 实现 fire-and-forget 让循环继续取下一条消息，
                        // 但这导致每条消息多一次线程池调度。改为直接 await ProcessMessageCore，
                        // 由 SemaphoreSlim 自然控制并发度——当所有槽位被占用时 WaitAsync 会
                        // 挂起当前协程（不阻塞线程），ProcessMessageCore 内部的 Task.WhenAll
                        // 已提供足够的并行度。
                        Interlocked.Increment(ref _inFlightCount);
                        try
                        {
                            await ProcessMessageCore(msg, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            // 取消是预期行为
                        }
                        catch (Exception ex)
                        {
                            GatewayLog.Error("ResilientMessagePipeline", $"Failed to process message: {ex.Message}", ex);
                            AddToDeadLetterQueue(msg, ex);
                        }
                        finally
                        {
                            _concurrencyLimiter.Release();
                            Interlocked.Decrement(ref _inFlightCount);
                            Interlocked.Decrement(ref _pendingMessageCount);
                        }
                    }
                }
            }
            finally
            {
                if (pendingMessage != null)
                {
                    AddToDeadLetterQueue(pendingMessage, new OperationCanceledException("Pipeline shutting down; message was dequeued but not processed."));
                    Interlocked.Decrement(ref _pendingMessageCount);
                }
            }
        }

        /// <summary>
        /// 单条消息核心处理：Filter → Transform → Route → Send
        /// </summary>
        private async Task ProcessMessageCore(Message message, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var results = new List<GatewaySendResult>();
            var traceId = GatewayTraceContext.EnsureTraceId(message);

            BufferTrace(new MessageTraceEvent(traceId, "Enqueued", null, null, DateTimeOffset.UtcNow));

            // 快照集合，避免运行时修改导致 foreach 抛异常
            // 0/1 个元素时避免 ToArray 分配（常见场景）
            var filters = _filters.Count switch
            {
                0 => Array.Empty<Func<Message, Task<bool>>>(),
                1 => new[] { _filters[0] },
                _ => _filters.ToArray()
            };
            var transformers = _transformers.Count switch
            {
                0 => Array.Empty<Func<Message, Task<Message>>>(),
                1 => new[] { _transformers[0] },
                _ => _transformers.ToArray()
            };

            // 1. 执行过滤器
            foreach (var filter in filters)
            {
                var shouldContinue = await filter(message);
                if (!shouldContinue)
                {
                    _metrics.RecordFiltered();
                    BufferTrace(new MessageTraceEvent(traceId, "Filtered", null, "Dropped", DateTimeOffset.UtcNow));
                    return;
                }
            }
            BufferTrace(new MessageTraceEvent(traceId, "Filtered", null, "Passed", DateTimeOffset.UtcNow));

            // 2. 执行转换器
            var currentMessage = message;
            foreach (var transformer in transformers)
            {
                currentMessage = await transformer(currentMessage);
                if (currentMessage == null) return;
            }
            BufferTrace(new MessageTraceEvent(traceId, "Transformed", null, "Success", DateTimeOffset.UtcNow));

            GatewayTraceContext.EnsureTraceId(currentMessage);

            // 3. 匹配路由规则（委托给 MessageRouter）
            var matchedOutputs = _router.Route(currentMessage);
            BufferTrace(new MessageTraceEvent(traceId, "Routed", string.Join(",", matchedOutputs), matchedOutputs.Count.ToString(), DateTimeOffset.UtcNow));

            // 4. 并行发送到匹配的输出插件
            if (matchedOutputs.Count == 0)
            {
                _lastSendResults = results.AsReadOnly();
                return;
            }

            var sendTasks = new List<Task<GatewaySendResult>>();
            foreach (var outputName in matchedOutputs)
            {
                if (!_outputs.TryGetValue(outputName, out var output))
                {
                    results.Add(PipelineSendStrategy.CreateSkippedResult(traceId, outputName, currentMessage, "Output plugin not registered."));
                    GatewayLog.Warn("ResilientMessagePipeline", $"Skipped send because output '{outputName}' is not registered.");
                    continue;
                }

                // 检查断路器
                var breaker = _circuitBreakers.GetOrAdd(outputName,
                     _ => new CircuitBreaker(CircuitBreakerFailureThreshold, CircuitBreakerRecoveryTimeMs, outputName));

                if (breaker.IsRequestBlocked())
                {
                    results.Add(PipelineSendStrategy.CreateCircuitOpenResult(traceId, outputName, currentMessage));
                    GatewayLog.Warn("ResilientMessagePipeline", $"Circuit breaker is OPEN for output '{outputName}', message sent to dead letter queue.");
                    // Clone 防止死信持久化/重试修改共享消息
                    AddToDeadLetterQueue(currentMessage.Clone(), new Exception($"Circuit breaker OPEN for output '{outputName}'"));
                    continue;
                }

                // P0 修复（v4）：消除内层 Task.Run + 为每个输出 Clone Message。
                // 1) Task.Run 多余：ProcessMessageCore 已在线程池上运行，SendAsync 是异步 I/O，
                //    Task.WhenAll 自然并行，无需额外线程调度。
                // 2) Clone Message：currentMessage.Metadata 是 Dictionary（非线程安全），
                //    多个输出并发修改会数据竞争。
                var clone = currentMessage.Clone();
                sendTasks.Add(_sendStrategy.SendAsync(output, clone, traceId, breaker, AddToDeadLetterQueue, BufferTrace, ct));
            }

            if (sendTasks.Count > 0)
            {
                var sendResults = await Task.WhenAll(sendTasks);
                results.AddRange(sendResults);
            }

            _lastSendResults = results.AsReadOnly();
            sw.Stop();

            // 指标：只要有任何成功就算成功
            if (results.Any(r => r.FinalStatus == GatewaySendFinalStatus.Success))
            {
                _metrics.RecordSuccess(sw.Elapsed.TotalMilliseconds);
            }
            else if (results.Count > 0)
            {
                _metrics.RecordFailure();
            }
        }

        // ---- 死信管理（委托给 PipelineDeadLetterManager）----

        private void AddToDeadLetterQueue(Message message, Exception exception)
        {
            _deadLetterManager.Add(message, exception, _metrics.RecordDeadLetter);
        }

        public IReadOnlyList<DeadLetterMessage> GetDeadLetterMessages()
        {
            return _deadLetterManager.GetMessages();
        }

        public void ClearDeadLetterQueue()
        {
            _deadLetterManager.Clear();
        }

        // ---- 断路器诊断 ----

        public CircuitBreakerState GetCircuitBreakerState(string outputName)
        {
            if (_circuitBreakers.TryGetValue(outputName, out var breaker))
            {
                return breaker.GetState();
            }
            return CircuitBreakerState.Closed;
        }

        public void ResetCircuitBreaker(string outputName)
        {
            if (_circuitBreakers.TryGetValue(outputName, out var breaker))
            {
                breaker.Reset();
            }
        }

        // ---- Dispose ----

        public void Dispose()
        {
            // 同步 Dispose：强制停止运行状态，释放所有可同步释放的资源。
            // 不等待 StopAsync 完成，避免 async-over-sync 死锁。
            // 推荐使用 DisposeAsync() 进行优雅关闭。
            if (Interlocked.Exchange(ref _isRunning, 0) == 0) return;

            // 1) 完成 Channel 写入端，阻止新消息入队
            try { _messageQueue.Writer.TryComplete(); } catch { }

            // 2) 取消后台处理循环的 CT
            CancellationTokenSource? ctsToCancel = null;
            try
            {
                ctsToCancel = _cts;
                _cts = null;
                ctsToCancel?.Cancel();
            }
            catch { }

            // 3) 释放同步可释放资源
            try { _concurrencyLimiter.Dispose(); } catch { }
            try { _deadLetterManager.Store?.Dispose(); } catch { }
            _deadLetterManager.Store = null;

            // 4) 清理 CT
            try { ctsToCancel?.Dispose(); } catch { }

            GatewayLog.Warn("ResilientMessagePipeline",
                "Dispose() called without awaiting DisposeAsync(). " +
                "Pipeline stopped immediately without graceful drain. " +
                "Consider using DisposeAsync() for graceful shutdown.");
        }

        public async ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref _isRunning) != 0)
            {
                try { await StopAsync(); } catch { }
            }

            // 确保 Channel 写入端已 Complete（StopAsync 已做，但双重保护）
            try { _messageQueue.Writer.TryComplete(); } catch { }

            // 释放同步可释放资源
            try { _concurrencyLimiter.Dispose(); } catch { }
            try { _deadLetterManager.Store?.Dispose(); } catch { }
            _deadLetterManager.Store = null;
        }
    }

    // ---- 以下类型已移至独立文件 ----
    // DeadLetterMessage       → Pipeline/DeadLetterMessage.cs
    // CircuitBreakerState     → Pipeline/CircuitBreaker.cs
    // CircuitBreaker          → Pipeline/CircuitBreaker.cs
    // CircuitBreakerStateChangedArgs → Pipeline/CircuitBreaker.cs
    // PipelineMetricsCollector → Pipeline/PipelineMetricsCollector.cs
    // FixedSizeRingBuffer<T>  → ZL.Collections.Collections
    // PipelineMetricsSnapshot  → Pipeline/PipelineMetricsCollector.cs
    // RouteRule               → Pipeline/MessageRouter.cs
    // MessageRouter            → Pipeline/MessageRouter.cs
    // PipelineDeadLetterManager → Pipeline/PipelineDeadLetterManager.cs
    // PipelineSendStrategy     → Pipeline/PipelineSendStrategy.cs
}
