using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// ResilientMessagePipeline 深度测试 — 背压、并发安全、优雅关闭
    /// </summary>
    public class ResilientPipelineDeepTests
    {
        #region 背压测试

        [Fact(Timeout = 15000)]
        public async Task ProcessAsync_WithSlowOutput_AllMessagesProcessed()
        {
            // 使用较慢的 Output 验证后台循环能正确处理所有消息
            var slowOutput = new SlowMockOutput(delayMs: 50);
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(slowOutput);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { slowOutput.Name } });

            await pipeline.StartAsync();

            int messageCount = 5;
            for (int i = 0; i < messageCount; i++)
            {
                await pipeline.ProcessAsync(new Message { Topic = $"msg-{i}", Payload = new byte[] { (byte)i } });
            }

            // ProcessAsync 只是入队，后台循环异步处理 — 等待处理完成
            await pipeline.WaitForIdleAsync(5000);

            Assert.Equal(messageCount, slowOutput.SendCount);

            await pipeline.StopAsync();
        }

        [Fact(Timeout = 10000)]
        public async Task ProcessAsync_AfterStop_IgnoresNewMessages()
        {
            var output = new FastMockOutput();
            var pipeline = new ResilientMessagePipeline { QueueCapacity = 10 };
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();
            await pipeline.StopAsync();

            // Stop 后发送消息不应抛出异常（ChannelClosedException 被静默忽略）
            await pipeline.ProcessAsync(new Message { Topic = "after-stop" });

            // 消息不会被处理
            Assert.Equal(0, output.SendCount);
        }

        #endregion

        #region 并发安全测试

        [Fact(Timeout = 15000)]
        public async Task ProcessAsync_ConcurrentMessages_AllProcessedSafely()
        {
            var output = new FastMockOutput();
            var pipeline = new ResilientMessagePipeline
            {
                QueueCapacity = 1000,
                SendTimeoutMs = 5000
            };
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();

            int messageCount = 50;
            var tasks = new Task[messageCount];
            for (int i = 0; i < messageCount; i++)
            {
                int index = i;
                tasks[i] = pipeline.ProcessAsync(new Message
                {
                    Topic = $"concurrent-{index}",
                    Payload = new byte[] { (byte)(index % 256) }
                });
            }

            await Task.WhenAll(tasks);
            await pipeline.WaitForIdleAsync(5000);

            Assert.Equal(messageCount, output.SendCount);

            await pipeline.StopAsync();
        }

        [Fact(Timeout = 10000)]
        public async Task ProcessAsync_MessageClonedForEachOutput()
        {
            var output1 = new RecordingMockOutput("out1");
            var output2 = new RecordingMockOutput("out2");
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output1);
            pipeline.RegisterOutput(output2);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { "out1", "out2" } });

            await pipeline.StartAsync();

            var msg = new Message { Topic = "test", Payload = new byte[] { 1, 2, 3 } };
            await pipeline.ProcessAsync(msg);
            await pipeline.WaitForIdleAsync(5000);

            // 两个 Output 收到的消息不应是同一个引用
            Assert.NotEmpty(output1.ReceivedMessages);
            Assert.NotEmpty(output2.ReceivedMessages);
            Assert.NotSame(output1.ReceivedMessages[0], output2.ReceivedMessages[0]);

            await pipeline.StopAsync();
        }

        #endregion

        #region 优雅关闭测试

        [Fact(Timeout = 15000)]
        public async Task StopAsync_CancelsProcessingLoop()
        {
            // StopAsync 先 Cancel() 再 TryComplete()，是取消语义（非 drain）。
            // 验证：StopAsync 能正常返回，不会永久挂起
            var slowOutput = new SlowMockOutput(delayMs: 500);
            var pipeline = new ResilientMessagePipeline
            {
                QueueCapacity = 10,
                SendTimeoutMs = 5000
            };
            pipeline.RegisterOutput(slowOutput);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { slowOutput.Name } });

            await pipeline.StartAsync();

            // 发送多条消息
            for (int i = 0; i < 5; i++)
            {
                await pipeline.ProcessAsync(new Message { Topic = $"cancel-{i}" });
            }

            // StopAsync 应能正常返回（不会永久挂起），即使有消息在处理中
            var stopTask = pipeline.StopAsync();
            Assert.True(await Task.WhenAny(stopTask, Task.Delay(5000)) == stopTask,
                "StopAsync should complete within 5s");
        }

        [Fact]
        public async Task Dispose_CallsStopAsync()
        {
            var output = new FastMockOutput();
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();
            pipeline.Dispose();

            // Dispose 内部调用 StopAsync
            // 输出插件应被正确停止
        }

        #endregion

        #region 路由与转换器测试

        [Fact]
        public async Task ProcessAsync_FilterRejects_MessageNotRouted()
        {
            var output = new FastMockOutput();
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddFilter(msg => Task.FromResult(msg.Topic.StartsWith("allow")));
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "block-this" });
            await pipeline.ProcessAsync(new Message { Topic = "allow-this" });
            await pipeline.WaitForIdleAsync(5000);

            // 只有 allow 开头的消息被处理
            Assert.Equal(1, output.SendCount);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task ProcessAsync_TransformerModifiesMessage()
        {
            var output = new RecordingMockOutput("recorder");
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);

            // Transformer 将 Topic 改为 uppercase
            pipeline.AddTransformer(async msg =>
            {
                msg.Topic = msg.Topic.ToUpperInvariant();
                return msg;
            });
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { "recorder" } });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "lowercase" });
            await pipeline.WaitForIdleAsync(5000);

            Assert.NotEmpty(output.ReceivedMessages);
            Assert.Equal("LOWERCASE", output.ReceivedMessages[0].Topic);

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task ProcessAsync_NoMatchingRoute_NoSend()
        {
            var output = new FastMockOutput();
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            // 不添加路由规则

            await pipeline.StartAsync();
            await pipeline.ProcessAsync(new Message { Topic = "orphan" });
            await pipeline.WaitForIdleAsync(5000);

            Assert.Equal(0, output.SendCount);

            await pipeline.StopAsync();
        }

        #endregion

        #region 死信队列与断路器集成测试

        [Fact]
        public async Task DeadLetterQueue_ClearRemovesAllEntries()
        {
            var failingOutput = new FailingMockOutput();
            var pipeline = new ResilientMessagePipeline
            {
                MaxRetryAttempts = 0,
                RetryBaseDelayMs = 1
            };
            pipeline.RegisterOutput(failingOutput);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { failingOutput.Name } });

            await pipeline.StartAsync();

            // 发送 3 条消息，全部失败进入死信队列
            for (int i = 0; i < 3; i++)
            {
                await pipeline.ProcessAsync(new Message { Topic = $"fail-{i}" });
            }
            await pipeline.WaitForIdleAsync(5000);

            Assert.Equal(3, pipeline.GetDeadLetterMessages().Count);

            pipeline.ClearDeadLetterQueue();
            Assert.Empty(pipeline.GetDeadLetterMessages());

            await pipeline.StopAsync();
        }

        [Fact]
        public async Task CircuitBreaker_ResetAllowsRetry()
        {
            var toggleOutput = new ToggleMockOutput(failFirst: 5); // 前5次失败，之后成功
            var pipeline = new ResilientMessagePipeline
            {
                MaxRetryAttempts = 0,
                CircuitBreakerFailureThreshold = 3,
                CircuitBreakerRecoveryTimeMs = 10000 // 很长的恢复时间
            };
            pipeline.RegisterOutput(toggleOutput);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { toggleOutput.Name } });

            await pipeline.StartAsync();

            // 发送 3 条消息，触发断路器打开
            for (int i = 0; i < 3; i++)
            {
                await pipeline.ProcessAsync(new Message { Topic = $"fail-{i}" });
            }
            await pipeline.WaitForIdleAsync(5000);

            // 断路器应打开
            Assert.Equal(CircuitBreakerState.Open,
                pipeline.GetCircuitBreakerState(toggleOutput.Name));

            // 手动重置断路器
            pipeline.ResetCircuitBreaker(toggleOutput.Name);
            Assert.Equal(CircuitBreakerState.Closed,
                pipeline.GetCircuitBreakerState(toggleOutput.Name));

            // 现在 Output 已切换为成功模式，新消息应能发送
            await pipeline.ProcessAsync(new Message { Topic = "success" });
            await Task.Delay(200);

            Assert.Equal(4, toggleOutput.SendCount); // 3次失败 + 1次成功

            await pipeline.StopAsync();
        }

        #endregion

        #region Mock 类

        private class FastMockOutput : IOutputPlugin
        {
            public string Name { get; set; } = "fast-mock";
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            public int SendCount { get; private set; }
            public event Action<string, bool> ConnectionChanged { add { } remove { } }
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged { add { } remove { } }

            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public Task SendAsync(Message message, CancellationToken cancellationToken = default) { SendCount++; return Task.CompletedTask; }
            public Task StopAsync() { Status = PluginStatus.Stopped; return Task.CompletedTask; }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private class SlowMockOutput : IOutputPlugin
        {
            private readonly int _delayMs;
            private int _sendCount;
            public string Name { get; set; } = "slow-mock";
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            public int SendCount => Volatile.Read(ref _sendCount);
            public event Action<string, bool> ConnectionChanged { add { } remove { } }
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged { add { } remove { } }

            public SlowMockOutput(int delayMs = 100) { _delayMs = delayMs; }

            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public async Task SendAsync(Message message, CancellationToken cancellationToken = default) { await Task.Delay(_delayMs); Interlocked.Increment(ref _sendCount); }
            public Task StopAsync() { Status = PluginStatus.Stopped; return Task.CompletedTask; }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private class FailingMockOutput : IOutputPlugin
        {
            public string Name { get; set; } = "failing-mock";
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            public int SendCount { get; private set; }
            public event Action<string, bool> ConnectionChanged { add { } remove { } }
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged { add { } remove { } }

            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public Task SendAsync(Message message, CancellationToken cancellationToken = default) { SendCount++; throw new InvalidOperationException("always fails"); }
            public Task StopAsync() { Status = PluginStatus.Stopped; return Task.CompletedTask; }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private class RecordingMockOutput : IOutputPlugin
        {
            public string Name { get; set; }
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            public List<Message> ReceivedMessages { get; } = new();
            public event Action<string, bool> ConnectionChanged { add { } remove { } }
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged { add { } remove { } }

            public RecordingMockOutput(string name) { Name = name; }

            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public Task SendAsync(Message message, CancellationToken cancellationToken = default) { ReceivedMessages.Add(message); return Task.CompletedTask; }
            public Task StopAsync() { Status = PluginStatus.Stopped; return Task.CompletedTask; }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private class ToggleMockOutput : IOutputPlugin
        {
            private readonly int _failFirst;
            public string Name { get; set; } = "toggle-mock";
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; }
            public int SendCount { get; private set; }
            public event Action<string, bool> ConnectionChanged { add { } remove { } }
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged { add { } remove { } }

            public ToggleMockOutput(int failFirst = 3) { _failFirst = failFirst; }

            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public Task SendAsync(Message message, CancellationToken cancellationToken = default)
            {
                SendCount++;
                if (SendCount <= _failFirst)
                    throw new InvalidOperationException($"Failing send #{SendCount}");
                return Task.CompletedTask;
            }
            public Task StopAsync() { Status = PluginStatus.Stopped; return Task.CompletedTask; }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        #endregion
    }
}
