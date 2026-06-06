using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 发送策略 — 封装带重试、超时、断路器的 Output 发送逻辑。
    /// 包含 SendWithRetryAndTimeout 及所有 GatewaySendResult 工厂方法。
    /// </summary>
    public class PipelineSendStrategy
    {
        private readonly Func<int> _sendTimeoutMs;
        private readonly Func<int> _maxRetryAttempts;
        private readonly Func<int> _retryBaseDelayMs;

        public PipelineSendStrategy(Func<int> sendTimeoutMs, Func<int> maxRetryAttempts, Func<int> retryBaseDelayMs)
        {
            _sendTimeoutMs = sendTimeoutMs;
            _maxRetryAttempts = maxRetryAttempts;
            _retryBaseDelayMs = retryBaseDelayMs;
        }

        /// <summary>
        /// 带重试和超时的发送。
        /// 完全复刻原 ResilientMessagePipeline.SendWithRetryAndTimeout 逻辑。
        /// </summary>
        public async Task<GatewaySendResult> SendAsync(
            IOutputPlugin output,
            Message message,
            string traceId,
            CircuitBreaker breaker,
            Action<Message, Exception> onDeadLetter,
            Action<MessageTraceEvent> onTrace,
            CancellationToken ct)
        {
            var startedAt = Stopwatch.StartNew();
            var attemptCount = 0;
            Exception lastException = null;

            while (attemptCount <= _maxRetryAttempts())
            {
                attemptCount++;

                try
                {
                    // 每次重试创建独立 CTS，超时 Cancel 后不影响后续重试。
                    using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var sendTask = output.SendAsync(message, sendCts.Token);
                    var completedTask = await Task.WhenAny(sendTask, Task.Delay(_sendTimeoutMs(), ct));

                    if (completedTask == sendTask)
                    {
                        await sendTask;
                        breaker.RecordSuccess();
                        onTrace?.Invoke(new MessageTraceEvent(traceId, "Sent", output.Name, "Success", DateTimeOffset.UtcNow));
                        return CreateSuccessResult(traceId, output, message, startedAt.Elapsed.TotalMilliseconds, attemptCount);
                    }
                    else
                    {
                        sendCts.Cancel();
                        try { await sendTask.WaitAsync(TimeSpan.FromMilliseconds(500)); }
                        catch (TaskCanceledException) { /* expected after cancel */ }
                        catch (Exception waitEx) { GatewayLog.Warn("PipelineSendStrategy", $"Wait after timeout for '{output.Name}' failed: {waitEx.Message}", waitEx); }
                        throw new TimeoutException($"Send to '{output.Name}' timed out after {_sendTimeoutMs()}ms");
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    GatewayLog.Warn("PipelineSendStrategy",
                        $"Failed to send to '{output.Name}' (attempt {attemptCount}/{_maxRetryAttempts() + 1}): {ex.Message}", ex);

                    if (attemptCount <= _maxRetryAttempts())
                    {
                        // 指数退避 + 随机抖动（0.5x~1.5x），使用位移代替 Math.Pow 避免浮点运算
                        var baseDelay = _retryBaseDelayMs() * (1 << (attemptCount - 1));
                        var jitteredDelay = (int)(baseDelay * (0.5 + Random.Shared.NextDouble()));
                        await Task.Delay(jitteredDelay, ct);
                    }
                }
            }

            breaker.RecordFailure();

            if (breaker.GetState() == CircuitBreakerState.Open)
            {
                GatewayLog.Warn("PipelineSendStrategy",
                    $"Circuit breaker OPENED for output '{output.Name}'.");
            }

            onTrace?.Invoke(new MessageTraceEvent(traceId, "DeadLetter", output.Name, lastException?.Message, DateTimeOffset.UtcNow));
            onDeadLetter(message, lastException);
            onTrace?.Invoke(new MessageTraceEvent(traceId, "Sent", output.Name, "Failed", DateTimeOffset.UtcNow));
            return CreateFailureResult(traceId, output, message, lastException, startedAt.Elapsed.TotalMilliseconds, attemptCount);
        }

        #region 结果工厂方法（与原 ResilientMessagePipeline 完全一致）

        public static GatewaySendResult CreateSuccessResult(string traceId, IOutputPlugin output, Message message, double durationMs, int attemptCount)
        {
            return new GatewaySendResult
            {
                TraceId = traceId,
                OutputId = output.Name ?? string.Empty,
                OutputName = output.Name ?? string.Empty,
                OutputType = output.ProtocolType ?? string.Empty,
                AttemptCount = attemptCount,
                FinalStatus = GatewaySendFinalStatus.Success,
                AckLevel = GatewayAckLevel.Accepted,
                DurationMs = durationMs,
                Source = ResolveSource(message)
            };
        }

        public static GatewaySendResult CreateFailureResult(string traceId, IOutputPlugin output, Message message, Exception ex, double durationMs, int attemptCount)
        {
            var diagnostic = GatewayErrorCatalog.FromException(ex, traceId, $"Failed to send to output '{output.Name}'.");
            return new GatewaySendResult
            {
                TraceId = traceId,
                OutputId = output.Name ?? string.Empty,
                OutputName = output.Name ?? string.Empty,
                OutputType = output.ProtocolType ?? string.Empty,
                AttemptCount = attemptCount,
                FinalStatus = GatewaySendFinalStatus.Failed,
                AckLevel = GatewayAckLevel.None,
                ErrorCode = diagnostic.ErrorCode,
                ErrorMessage = diagnostic.TechnicalMessage,
                UserMessage = diagnostic.UserMessage,
                Advice = diagnostic.Advice,
                DurationMs = durationMs,
                Source = ResolveSource(message)
            };
        }

        public static GatewaySendResult CreateSkippedResult(string traceId, string outputName, Message message, string reason)
        {
            return new GatewaySendResult
            {
                TraceId = traceId,
                OutputId = outputName ?? string.Empty,
                OutputName = outputName ?? string.Empty,
                OutputType = string.Empty,
                AttemptCount = 0,
                FinalStatus = GatewaySendFinalStatus.Skipped,
                AckLevel = GatewayAckLevel.None,
                ErrorCode = GatewayErrorCodes.ConfigurationMissing,
                ErrorMessage = reason,
                UserMessage = "目标输出未注册，消息已跳过。",
                Advice = "请检查路由规则与输出插件注册是否一致。",
                Source = ResolveSource(message)
            };
        }

        public static GatewaySendResult CreateCircuitOpenResult(string traceId, string outputName, Message message)
        {
            return new GatewaySendResult
            {
                TraceId = traceId,
                OutputId = outputName ?? string.Empty,
                OutputName = outputName ?? string.Empty,
                OutputType = string.Empty,
                AttemptCount = 0,
                FinalStatus = GatewaySendFinalStatus.DeadLettered,
                AckLevel = GatewayAckLevel.None,
                ErrorCode = GatewayErrorCodes.OutputCircuitOpen,
                ErrorMessage = $"Circuit breaker is OPEN for output '{outputName}'",
                UserMessage = "输出插件断路器已打开，消息已发送到死信队列。",
                Advice = "请检查输出插件状态，使用 ResetCircuitBreaker 重置断路器。",
                Source = ResolveSource(message)
            };
        }

        internal static string ResolveSource(Message message)
        {
            if (message?.Metadata != null
                && message.Metadata.TryGetValue(GatewayMetadataKeys.Source, out var source)
                && !string.IsNullOrWhiteSpace(source))
            {
                return source;
            }

            return message?.Topic ?? string.Empty;
        }

        #endregion
    }
}
