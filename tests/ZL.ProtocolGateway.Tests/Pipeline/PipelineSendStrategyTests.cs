using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    /// <summary>
    /// PipelineSendStrategy 测试 — 验证重试、超时、断路器集成。
    /// </summary>
    public class PipelineSendStrategyTests
    {
        [Fact]
        public async Task SendAsync_Success_FirstAttempt()
        {
            var output = new SuccessOutputPlugin();
            var breaker = new CircuitBreaker(5, 1000);
            var strategy = new PipelineSendStrategy(() => 3000, () => 3, () => 100);
            var msg = new Message { Topic = "test" };
            int deadLetterCount = 0;

            var result = await strategy.SendAsync(
                output, msg, "trace-1", breaker,
                (m, e) => deadLetterCount++,
                _ => { },
                CancellationToken.None);

            Assert.Equal(GatewaySendFinalStatus.Success, result.FinalStatus);
            Assert.Equal(1, result.AttemptCount);
            Assert.Equal(0, deadLetterCount);
            Assert.Equal(CircuitBreakerState.Closed, breaker.GetState());
        }

        [Fact]
        public async Task SendAsync_Failure_RetriesThenDeadLetters()
        {
            var output = new FailingOutputPlugin();
            var breaker = new CircuitBreaker(5, 1000);
            var strategy = new PipelineSendStrategy(() => 3000, () => 2, () => 10);
            var msg = new Message { Topic = "fail" };
            int deadLetterCount = 0;

            var result = await strategy.SendAsync(
                output, msg, "trace-2", breaker,
                (m, e) => deadLetterCount++,
                _ => { },
                CancellationToken.None);

            Assert.Equal(GatewaySendFinalStatus.Failed, result.FinalStatus);
            Assert.Equal(3, result.AttemptCount); // 1 + 2 retries
            Assert.Equal(1, deadLetterCount);
            Assert.Equal(CircuitBreakerState.Closed, breaker.GetState());
        }

        [Fact]
        public async Task SendAsync_RepeatedFailures_OpensCircuitBreaker()
        {
            var output = new FailingOutputPlugin();
            var breaker = new CircuitBreaker(failureThreshold: 2, recoveryTimeMs: 1000);
            var strategy = new PipelineSendStrategy(() => 3000, () => 0, () => 100);
            var msg = new Message { Topic = "fail" };

            await strategy.SendAsync(output, msg, "t1", breaker, (_, __) => {}, _ => { }, CancellationToken.None);
            await strategy.SendAsync(output, msg, "t2", breaker, (_, __) => {}, _ => { }, CancellationToken.None);

            Assert.Equal(CircuitBreakerState.Open, breaker.GetState());
        }

        [Fact]
        public async Task SendAsync_Timeout_FailsWithTimeoutException()
        {
            // 使用永不完成的 TCS，避免 Task.Delay 竞态导致测试挂起
            var output = new NeverCompletingOutputPlugin();
            var breaker = new CircuitBreaker(5, 1000);
            var strategy = new PipelineSendStrategy(() => 100, () => 0, () => 100);
            var msg = new Message { Topic = "slow" };

            var result = await strategy.SendAsync(
                output, msg, "trace-3", breaker,
                (m, e) => {},
                _ => { },
                CancellationToken.None);

            Assert.Equal(GatewaySendFinalStatus.Failed, result.FinalStatus);
            Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CreateSuccessResult_PopulatesAllFields()
        {
            var output = new SuccessOutputPlugin();
            var msg = new Message { Topic = "test", Metadata = { [GatewayMetadataKeys.Source] = "test-src" } };

            var result = PipelineSendStrategy.CreateSuccessResult("trace-1", output, msg, 42.5, 1);

            Assert.Equal("trace-1", result.TraceId);
            Assert.Equal("success-out", result.OutputId);
            Assert.Equal("success-out", result.OutputName);
            Assert.Equal("Test", result.OutputType);
            Assert.Equal(1, result.AttemptCount);
            Assert.Equal(GatewaySendFinalStatus.Success, result.FinalStatus);
            Assert.Equal(GatewayAckLevel.Accepted, result.AckLevel);
            Assert.Equal(42.5, result.DurationMs);
            Assert.Equal("test-src", result.Source);
        }

        [Fact]
        public void CreateFailureResult_ContainsDiagnosticInfo()
        {
            var output = new FailingOutputPlugin();
            var ex = new IOException("connection refused");
            var msg = new Message { Topic = "fail" };

            var result = PipelineSendStrategy.CreateFailureResult("trace-2", output, msg, ex, 100.0, 3);

            Assert.Equal(GatewaySendFinalStatus.Failed, result.FinalStatus);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
            Assert.NotNull(result.UserMessage);
        }

        [Fact]
        public void CreateSkippedResult_HasCorrectStatus()
        {
            var msg = new Message { Topic = "skip" };

            var result = PipelineSendStrategy.CreateSkippedResult("trace-3", "missing-out", msg, "not registered");

            Assert.Equal(GatewaySendFinalStatus.Skipped, result.FinalStatus);
            Assert.Equal(GatewayErrorCodes.ConfigurationMissing, result.ErrorCode);
            Assert.Equal("missing-out", result.OutputName);
        }

        [Fact]
        public void CreateCircuitOpenResult_HasCorrectStatus()
        {
            var msg = new Message { Topic = "open" };

            var result = PipelineSendStrategy.CreateCircuitOpenResult("trace-4", "broken-out", msg);

            Assert.Equal(GatewaySendFinalStatus.DeadLettered, result.FinalStatus);
            Assert.Equal(GatewayErrorCodes.OutputCircuitOpen, result.ErrorCode);
            Assert.Equal("broken-out", result.OutputName);
        }

        [Fact]
        public void ResolveSource_UsesMetadataSource()
        {
            var msg = new Message { Topic = "topic", Metadata = { [GatewayMetadataKeys.Source] = "my-source" } };
            Assert.Equal("my-source", PipelineSendStrategy.ResolveSource(msg));
        }

        [Fact]
        public void ResolveSource_FallsBackToTopic()
        {
            var msg = new Message { Topic = "fallback-topic" };
            Assert.Equal("fallback-topic", PipelineSendStrategy.ResolveSource(msg));
        }

        [Fact]
        public void ResolveSource_NullMessage_ReturnsEmpty()
        {
            Assert.Equal("", PipelineSendStrategy.ResolveSource(null!));
        }

        #region Test Plugins

        private class SuccessOutputPlugin : IOutputPlugin
        {
            public string Name => "success-out";
            public string ProtocolType => "Test";
            public string Version => "1.0.0";
            public PluginStatus Status => PluginStatus.Running;
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;
            public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task SendAsync(Message message, CancellationToken ct = default) => Task.CompletedTask;
            public Task StopAsync() => Task.CompletedTask;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }

        private class FailingOutputPlugin : IOutputPlugin
        {
            public string Name => "failing-out";
            public string ProtocolType => "Test";
            public string Version => "1.0.0";
            public PluginStatus Status => PluginStatus.Running;
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;
            public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task SendAsync(Message message, CancellationToken ct = default)
                => Task.FromException(new IOException("always fails"));
            public Task StopAsync() => Task.CompletedTask;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }

        private class NeverCompletingOutputPlugin : IOutputPlugin
        {
            public string Name => "never-out";
            public string ProtocolType => "Test";
            public string Version => "1.0.0";
            public PluginStatus Status => PluginStatus.Running;
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;
            public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task SendAsync(Message message, CancellationToken ct = default)
            {
                // 使用远超超时窗口的延迟（10分钟），让 Task.WhenAny 的超时分支先完成。
                // 避免使用 Timeout.Infinite（某些 .NET 运行时上取消响应有延迟）。
                return Task.Delay(TimeSpan.FromMinutes(10), ct);
            }
            public Task StopAsync() => Task.CompletedTask;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }

        #endregion
    }
}
