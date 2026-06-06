using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// ResilientMessagePipeline 单元测试
    /// 测试死信队列、熔断器状态、重试与超时、背压控制
    /// </summary>
    public class ResilientPipelineTests
    {
        /// <summary>
        /// 轮询等待死信队列非空，避免固定延迟的时序问题。
        /// </summary>
        private static async Task WaitForDeadLetter(ResilientMessagePipeline pipeline, int timeoutMs = 2000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (pipeline.GetDeadLetterMessages().Count > 0) return;
                await Task.Delay(50);
            }
            Assert.Fail($"Dead letter queue not populated within {timeoutMs}ms");
        }

        /// <summary>
        /// 轮询等待断路器达到指定状态。
        /// </summary>
        private static async Task WaitForCircuitBreakerState(ResilientMessagePipeline pipeline, string outputName, CircuitBreakerState expected, int timeoutMs = 3000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (pipeline.GetCircuitBreakerState(outputName) == expected) return;
                await Task.Delay(50);
            }
            var actual = pipeline.GetCircuitBreakerState(outputName);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task ProcessAsync_NullMessage_IsNoOp()
        {
            var pipeline = new ResilientMessagePipeline();
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(null);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task ProcessAsync_SuccessfulSend_NoDeadLetter()
        {
            var output = new ReliableMockOutput();
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "test", Payload = new byte[] { 1 } });
            await Task.Delay(100); // 等待后台处理

            Assert.Empty(pipeline.GetDeadLetterMessages());
            Assert.Equal(1, output.SendCount);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task ProcessAsync_OutputFails_MessageGoesToDeadLetterQueue()
        {
            var output = new FailingMockOutput();
            var pipeline = new ResilientMessagePipeline
            {
                MaxRetryAttempts = 0,
                RetryBaseDelayMs = 1
            };
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "fail", Payload = new byte[] { 1 } });
            await WaitForDeadLetter(pipeline, timeoutMs: 2000);

            var deadLetters = pipeline.GetDeadLetterMessages();
            Assert.NotEmpty(deadLetters);
            Assert.Equal("fail", deadLetters[0].Message.Topic);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task ClearDeadLetterQueue_RemovesAllEntries()
        {
            var output = new FailingMockOutput();
            var pipeline = new ResilientMessagePipeline
            {
                MaxRetryAttempts = 0,
                RetryBaseDelayMs = 1
            };
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "fail" });
            await WaitForDeadLetter(pipeline, timeoutMs: 2000);

            Assert.NotEmpty(pipeline.GetDeadLetterMessages());
            pipeline.ClearDeadLetterQueue();
            Assert.Empty(pipeline.GetDeadLetterMessages());

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task CircuitBreaker_OpensAfterRepeatedFailures()
        {
            var output = new FailingMockOutput();
            var pipeline = new ResilientMessagePipeline
            {
                MaxRetryAttempts = 0,
                RetryBaseDelayMs = 1
            };
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            // 发送多条消息触发熔断（默认阈值 5 次）
            for (int i = 0; i < 10; i++)
            {
                await pipeline.ProcessAsync(new Message { Topic = $"fail-{i}" });
            }
            await WaitForCircuitBreakerState(pipeline, output.Name, CircuitBreakerState.Open, timeoutMs: 3000);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task CircuitBreaker_Reset_ClearsState()
        {
            var output = new FailingMockOutput();
            var pipeline = new ResilientMessagePipeline
            {
                MaxRetryAttempts = 0,
                RetryBaseDelayMs = 1
            };
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            for (int i = 0; i < 10; i++)
                await pipeline.ProcessAsync(new Message { Topic = $"fail-{i}" });
            await WaitForCircuitBreakerState(pipeline, output.Name, CircuitBreakerState.Open, timeoutMs: 3000);

            pipeline.ResetCircuitBreaker(output.Name);
            Assert.Equal(CircuitBreakerState.Closed, pipeline.GetCircuitBreakerState(output.Name));

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task GetCircuitBreakerState_UnknownOutput_ReturnsClosed()
        {
            var pipeline = new ResilientMessagePipeline();
            await pipeline.StartAsync();

            var state = pipeline.GetCircuitBreakerState("nonexistent");
            Assert.Equal(CircuitBreakerState.Closed, state);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task Filter_RemovesFilteredMessages()
        {
            var output = new ReliableMockOutput();
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddFilter(msg => Task.FromResult(msg.Topic != "blocked"));
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "allowed", Payload = new byte[] { 1 } });
            await pipeline.ProcessAsync(new Message { Topic = "blocked", Payload = new byte[] { 2 } });
            await Task.Delay(100);

            Assert.Equal(1, output.SendCount);
            Assert.Equal("allowed", output.LastMessage?.Topic);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task Transformer_ModifiesMessageBeforeRouting()
        {
            var output = new ReliableMockOutput();
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddTransformer(async msg =>
            {
                msg.Topic = "transformed-" + msg.Topic;
                return msg;
            });
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "original" });
            await Task.Delay(100);

            Assert.Equal(1, output.SendCount);
            Assert.Equal("transformed-original", output.LastMessage?.Topic);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task Transformer_ReturningNull_DropsMessage()
        {
            var output = new ReliableMockOutput();
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddTransformer(async msg => null); // 丢弃所有消息
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "test" });
            await Task.Delay(100);

            Assert.Equal(0, output.SendCount);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task StartAsync_StopsAsync_LifecycleCompletes()
        {
            var pipeline = new ResilientMessagePipeline();
            var output = new ReliableMockOutput();
            pipeline.RegisterOutput(output);

            await pipeline.StartAsync();
            Assert.Empty(pipeline.GetDeadLetterMessages());

            await pipeline.StopAsync();
            // Stop 不应抛异常
        }

        [Fact]
        public async Task MultipleOutputs_OneFails_OtherSucceeds()
        {
            var goodOutput = new ReliableMockOutput();
            var badOutput = new FailingMockOutput();
            var pipeline = new ResilientMessagePipeline
            {
                MaxRetryAttempts = 0,
                RetryBaseDelayMs = 1
            };
            pipeline.RegisterOutput(goodOutput);
            pipeline.RegisterOutput(badOutput);
            pipeline.AddRouter(new RouteRule
            {
                Condition = _ => true,
                OutputNames = { goodOutput.Name, badOutput.Name }
            });
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "multi", Payload = new byte[] { 1 } });
            await WaitForDeadLetter(pipeline, timeoutMs: 2000);

            Assert.Equal(1, goodOutput.SendCount);
            Assert.NotEmpty(pipeline.GetDeadLetterMessages()); // badOutput 失败进入死信

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task NoMatchingRoute_MessageNotSent()
        {
            var output = new ReliableMockOutput();
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = msg => msg.Topic == "never", OutputNames = { output.Name } });
            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "test" });
            await Task.Delay(100);

            Assert.Equal(0, output.SendCount);
            await pipeline.StopAsync();
        }

        #region Mock Outputs

        private class ReliableMockOutput : IOutputPlugin
        {
            public string Name { get; set; } = "reliable-mock";
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            public event Action<string, bool> ConnectionChanged { add { } remove { } }
            public event Action<OutputPluginStatusArgs> DetailedStatusChanged { add { } remove { } }

            public int SendCount { get; private set; }
            public Message LastMessage { get; private set; }

            public Task StartAsync(CancellationToken ct = default)
            {
                Status = PluginStatus.Running;
                return Task.CompletedTask;
            }

            public Task SendAsync(Message message, CancellationToken cancellationToken = default)
            {
                SendCount++;
                LastMessage = message;
                return Task.CompletedTask;
            }

            public Task StopAsync()
            {
                Status = PluginStatus.Stopped;
                return Task.CompletedTask;
            }

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private class FailingMockOutput : IOutputPlugin
        {
            public string Name { get; set; } = "failing-mock";
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            public event Action<string, bool> ConnectionChanged { add { } remove { } }
            public event Action<OutputPluginStatusArgs> DetailedStatusChanged { add { } remove { } }

            public Task StartAsync(CancellationToken ct = default)
            {
                Status = PluginStatus.Running;
                return Task.CompletedTask;
            }

            public Task SendAsync(Message message, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Simulated failure");
            }

            public Task StopAsync()
            {
                Status = PluginStatus.Stopped;
                return Task.CompletedTask;
            }

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        #endregion
    }
}
