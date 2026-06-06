// ============================================================
// 文件：GatewayDiagnosticsManager.cs
// 描述：网关诊断管理器 — 负责指标、死信、断路器、测试发送
// 来源：从 GatewayManager God Class 拆分
// ============================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 网关诊断管理器 — 提供指标查询、死信管理、断路器重置、测试发送等运维能力。
    /// </summary>
    internal class GatewayDiagnosticsManager
    {
        private readonly ResilientMessagePipeline _pipeline;
        private readonly Func<bool> _isRunningGetter;
        private readonly Func<string, bool> _hasOutputGetter;
        private readonly Func<IReadOnlyList<OutputPluginStatus>> _outputStatusesGetter;

        public GatewayDiagnosticsManager(
            ResilientMessagePipeline pipeline,
            Func<bool> isRunningGetter,
            Func<string, bool> hasOutputGetter,
            Func<IReadOnlyList<OutputPluginStatus>> outputStatusesGetter)
        {
            _pipeline = pipeline;
            _isRunningGetter = isRunningGetter;
            _hasOutputGetter = hasOutputGetter;
            _outputStatusesGetter = outputStatusesGetter;
        }

        #region 指标

        /// <summary>
        /// 获取网关转发指标快照。
        /// </summary>
        public GatewayMetricsSnapshot GetMetricsSnapshot(IReadOnlyCollection<string> outputNames)
        {
            var cbStates = new Dictionary<string, string>();
            foreach (var name in outputNames)
            {
                cbStates[name] = _pipeline.GetCircuitBreakerState(name).ToString();
            }

            IReadOnlyList<GatewaySendResult> lastResults = _pipeline.LastSendResults?.ToList() ?? new List<GatewaySendResult>();

            return new GatewayMetricsSnapshot
            {
                Timestamp = DateTime.UtcNow,
                IsRunning = _isRunningGetter(),
                QueuedMessageCount = _pipeline.QueuedMessageCount,
                QueueCapacity = _pipeline.QueueCapacity,
                DeadLetterCount = _pipeline.DeadLetterCount,
                OutputPlugins = _outputStatusesGetter(),
                CircuitBreakerStates = cbStates,
                LastSendResults = lastResults,
                PipelineMetrics = _pipeline.Metrics.GetSnapshot()
            };
        }

        #endregion

        #region 死信管理

        /// <summary>
        /// 获取死信队列消息。
        /// </summary>
        public IReadOnlyList<DeadLetterInfo> GetDeadLetters(int limit = 100)
        {
            var messages = _pipeline.GetDeadLetterMessages();
            return messages
                .Skip(Math.Max(0, messages.Count - limit))
                .Select(dl => new DeadLetterInfo
                {
                    TraceId = dl.Message.TraceId,
                    Topic = dl.Message.Topic,
                    OutputName = dl.Message.Metadata.TryGetValue("OutputName", out var on) ? on : "",
                    ErrorMessage = dl.Exception.Message,
                    FailedAt = dl.FailedAt
                })
                .ToList();
        }

        /// <summary>
        /// 清空死信队列。
        /// </summary>
        public void ClearDeadLetterQueue()
        {
            _pipeline.ClearDeadLetterQueue();
            GatewayLog.Info("GatewayDiagnosticsManager", "Dead letter queue cleared");
        }

        /// <summary>
        /// 将死信消息重新注入 Pipeline 进行重试。
        /// </summary>
        public async Task<int> RetryDeadLettersAsync(Func<Message, CancellationToken, Task> publishAsync, CancellationToken ct = default)
        {
            var messages = _pipeline.GetDeadLetterMessages();
            if (messages.Count == 0) return 0;

            int retried = 0;
            foreach (var dl in messages)
            {
                // 跳过已耗尽重试次数的消息，避免死信膨胀
                if (dl.RetryCount >= PipelineDeadLetterManager.MaxDeadLetterRetries)
                {
                    GatewayLog.Warn("GatewayDiagnosticsManager",
                        $"Skipping dead letter retry for topic '{dl.Message.Topic}': retry count ({dl.RetryCount}) exceeded limit");
                    continue;
                }

                try
                {
                    var retriedMessage = dl.Message.Clone();
                    retriedMessage.Metadata[GatewayMetadataKeys.DeadLetterRetryCount] = dl.RetryCount.ToString();
                    await publishAsync(retriedMessage, ct);
                    retried++;
                }
                catch
                {
                    // 单个消息重试失败，继续下一个
                }
            }

            GatewayLog.Info("GatewayDiagnosticsManager", $"Dead letter retry completed: {retried}/{messages.Count}");
            return retried;
        }

        #endregion

        #region 断路器

        /// <summary>
        /// 重置指定输出插件的断路器。
        /// </summary>
        public bool ResetCircuitBreaker(string outputName)
        {
            if (!_hasOutputGetter(outputName)) return false;
            _pipeline.ResetCircuitBreaker(outputName);
            GatewayLog.Info("GatewayDiagnosticsManager", $"Circuit breaker reset: {outputName}");
            return true;
        }

        #endregion

        #region 测试发送

        /// <summary>
        /// 测试发送消息到指定输出插件（直接调用插件的 SendAsync，不经过 Pipeline）。
        /// </summary>
        public async Task<GatewayTestResult> TestSendAsync(IOutputPlugin output, string? testPayload, CancellationToken ct = default)
        {
            var result = new GatewayTestResult { OutputName = output.Name };

            if (output.Status != PluginStatus.Running)
            {
                result.Success = false;
                result.ErrorMessage = $"输出插件 '{output.Name}' 未运行 (状态: {output.Status})";
                return result;
            }

            var testMessage = new Message
            {
                Topic = "__gateway_test__",
                Metadata =
                {
                    ["Source"] = "GatewayTest",
                    ["OutputId"] = output.Name
                }
            };

            if (!string.IsNullOrEmpty(testPayload))
            {
                testMessage.Payload = System.Text.Encoding.UTF8.GetBytes(testPayload);
                testMessage.ContentType = "json";
            }

            try
            {
                var sw = Stopwatch.StartNew();
                await output.SendAsync(testMessage, ct);
                sw.Stop();
                result.Success = true;
                result.DurationMs = sw.ElapsedMilliseconds;
                result.ErrorMessage = "测试发送成功";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        #endregion
    }
}
