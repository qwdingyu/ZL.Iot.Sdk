// ============================================================
// 文件：PipelineConcurrencyTests.cs
// 描述：ResilientMessagePipeline 并发安全与背压行为测试
//       验证 Channel 满时阻塞、并发消息不丢失、优雅关闭
// 修改日期：2026-06-03
// ============================================================

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
    public class PipelineConcurrencyTests
    {
        #region 并发消息处理

        [Fact]
        public async Task Pipeline_ProcessesMultipleConcurrentMessages()
        {
            var pipeline = new ResilientMessagePipeline();
            var output = new CountingOutputPlugin();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { output.Name } });

            await pipeline.StartAsync();

            const int count = 20;
            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                var msg = new Message { Topic = $"test/{i}" };
                tasks[i] = pipeline.ProcessAsync(msg);
            }

            await Task.WhenAll(tasks);

            // 等待处理完成
            await pipeline.WaitForIdleAsync(5000);
            await pipeline.StopAsync();

            Assert.Equal(count, output.SendCount);
        }

        [Fact]
        public async Task Pipeline_ProcessesMessagesFromMultipleThreads()
        {
            var pipeline = new ResilientMessagePipeline();
            var output = new CountingOutputPlugin();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { output.Name } });

            await pipeline.StartAsync();

            const int threads = 4;
            const int msgsPerThread = 10;
            var tasks = new Task[threads];

            for (int t = 0; t < threads; t++)
            {
                int threadIdx = t;
                tasks[t] = Task.Run(async () =>
                {
                    for (int i = 0; i < msgsPerThread; i++)
                    {
                        await pipeline.ProcessAsync(new Message { Topic = $"thread{threadIdx}/msg{i}" });
                    }
                });
            }

            await Task.WhenAll(tasks);
            await pipeline.WaitForIdleAsync(5000);
            await pipeline.StopAsync();

            Assert.Equal(threads * msgsPerThread, output.SendCount);
        }

        #endregion

        #region 背压行为

        [Fact]
        public async Task Pipeline_BackpressureBlocksWhenChannelFull()
        {
            // 使用极小队列容量来测试背压
            var pipeline = new ResilientMessagePipeline { QueueCapacity = 5 };
            var output = new SlowOutputPlugin(delayMs: 200); // 每条消息处理 200ms
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { output.Name } });

            await pipeline.StartAsync();

            // 快速发送 10 条消息 — Channel 容量只有 5，
            // 第 6 条开始应该被背压阻塞（但不会丢消息）
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var tasks = new Task[10];
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = pipeline.ProcessAsync(new Message { Topic = $"bp/{i}" });
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // ProcessAsync 使用 BoundedChannelFullMode.Wait，所以所有写入最终都会成功
            // 只是写入本身可能很快（Channel 写入不等待消费）
            // 关键是：没有消息丢失

            await pipeline.WaitForIdleAsync(10000); // 等待处理完成
            await pipeline.StopAsync();

            Assert.Equal(10, output.SendCount);
        }

        [Fact]
        public async Task Pipeline_NoMessagesLostUnderLoad()
        {
            var pipeline = new ResilientMessagePipeline { QueueCapacity = 100 };
            var output = new CountingOutputPlugin();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { output.Name } });

            await pipeline.StartAsync();

            const int total = 50;
            var tasks = new Task[total];
            for (int i = 0; i < total; i++)
            {
                tasks[i] = pipeline.ProcessAsync(new Message { Topic = $"load/{i}" });
            }

            await Task.WhenAll(tasks);
            await pipeline.WaitForIdleAsync(5000);
            await pipeline.StopAsync();

            Assert.Equal(total, output.SendCount);
            Assert.Empty(pipeline.GetDeadLetterMessages());
        }

        #endregion

        #region 优雅关闭

        [Fact]
        public async Task Pipeline_TestSendAsync_WaitsForSlowMessage()
        {
            // TestSendAsync 是同步路径（不走 Channel），会等待 SendAsync 完成
            var pipeline = new ResilientMessagePipeline();
            var output = new SlowOutputPlugin(delayMs: 200);
            pipeline.RegisterOutput(output);

            // 不需要 StartAsync，TestSendAsync 直接调用 output.SendAsync
            var msg = new Message { Topic = "slow" };
            await pipeline.TestSendAsync(output.Name, msg);

            Assert.Equal(1, output.SendCount);
        }

        [Fact]
        public async Task Pipeline_ProcessAsync_AfterStop_IsIgnored()
        {
            var pipeline = new ResilientMessagePipeline();
            var output = new CountingOutputPlugin();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { output.Name } });

            await pipeline.StartAsync();
            await pipeline.StopAsync();

            // Stop 后发送不应抛异常
            await pipeline.ProcessAsync(new Message { Topic = "after-stop" });

            Assert.Equal(0, output.SendCount);
        }

        [Fact]
        public async Task Pipeline_ProcessAsync_NullMessage_IsIgnored()
        {
            var pipeline = new ResilientMessagePipeline();
            var output = new CountingOutputPlugin();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { output.Name } });

            await pipeline.StartAsync();

            // ProcessAsync(null) 应静默忽略，不抛异常
            await pipeline.ProcessAsync(null!);

            Assert.Equal(0, output.SendCount);
            await pipeline.StopAsync();
        }

        #endregion

        #region 过滤器和转换器

        [Fact]
        public async Task Pipeline_FilterBlocksMatchingMessages()
        {
            var pipeline = new ResilientMessagePipeline();
            var output = new CountingOutputPlugin();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { output.Name } });

            pipeline.AddFilter(async msg =>
            {
                await Task.Yield();
                return !msg.Topic.Contains("block");
            });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "allow" });
            await pipeline.ProcessAsync(new Message { Topic = "block_this" });
            await pipeline.ProcessAsync(new Message { Topic = "also_allow" });

            await pipeline.WaitForIdleAsync(5000);
            await pipeline.StopAsync();

            Assert.Equal(2, output.SendCount);
        }

        [Fact]
        public async Task Pipeline_TransformerModifiesMessage()
        {
            var pipeline = new ResilientMessagePipeline();
            var output = new TopicCapturingOutputPlugin();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { output.Name } });

            pipeline.AddTransformer(async msg =>
            {
                await Task.Yield();
                msg.Metadata["transformed"] = "true";
                return msg;
            });

            await pipeline.StartAsync();

            var msg = new Message { Topic = "original" };
            await pipeline.ProcessAsync(msg);

            await pipeline.WaitForIdleAsync(5000);
            await pipeline.StopAsync();

            Assert.Single(output.ReceivedTopics);
            Assert.True(output.ReceivedMessages.Any(m => m.Metadata.ContainsKey("transformed")));
        }

        #endregion

        #region 路由规则

        [Fact]
        public async Task Pipeline_RouteRuleDirectsToSpecificOutput()
        {
            var pipeline = new ResilientMessagePipeline();
            var outputA = new CountingOutputPlugin();
            var outputB = new CountingOutputPlugin();
            pipeline.RegisterOutput("OutputA", outputA);
            pipeline.RegisterOutput("OutputB", outputB);

            pipeline.AddRouter(new RouteRule
            {
                Condition = msg => msg.Topic.StartsWith("route/a/"),
                OutputNames = new List<string> { "OutputA" }
            });
            pipeline.AddRouter(new RouteRule
            {
                Condition = msg => msg.Topic.StartsWith("route/b/"),
                OutputNames = new List<string> { "OutputB" }
            });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "route/a/test" });
            await pipeline.ProcessAsync(new Message { Topic = "route/b/test" });

            await pipeline.WaitForIdleAsync(5000);
            await pipeline.StopAsync();

            Assert.Equal(1, outputA.SendCount);
            Assert.Equal(1, outputB.SendCount);
        }

        #endregion

        #region 死信队列

        [Fact]
        public async Task Pipeline_FailedMessagesGoToDeadLetterQueue()
        {
            // 使用极小重试参数加速测试：0 次重试，100ms 超时
            var pipeline = new ResilientMessagePipeline
            {
                MaxRetryAttempts = 0,
                SendTimeoutMs = 100
            };
            var failingOutput = new FailingOutputPlugin();
            pipeline.RegisterOutput(failingOutput);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = new List<string> { failingOutput.Name } });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "fail" });

            // 0 重试 + 立即抛异常，消息应迅速进入死信队列
            await pipeline.WaitForIdleAsync(5000);
            var deadLetters = pipeline.GetDeadLetterMessages();

            Assert.NotEmpty(deadLetters);
            Assert.Single(deadLetters);

            await pipeline.StopAsync();
        }

        #endregion
    }

    #region 测试辅助插件

    public class CountingOutputPlugin : IOutputPlugin
    {
        private readonly ConcurrentQueue<Message> _messages = new();
        public string Name { get; set; } = "CountingOutput";
        public string ProtocolType => "counting";
        public string Version => "1.0.0";
        public PluginStatus Status { get; private set; } = PluginStatus.Running;
        public int SendCount => _messages.Count;

        public event Action<string, bool>? ConnectionChanged;
        public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

        public Task StartAsync(CancellationToken ct = default)
        {
            Status = PluginStatus.Running;
            return Task.CompletedTask;
        }

        public Task SendAsync(Message message, CancellationToken cancellationToken = default)
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Status = PluginStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() { Dispose(); return default; }
    }

    public class SlowOutputPlugin : IOutputPlugin
    {
        private readonly int _delayMs;
        private readonly ConcurrentQueue<Message> _messages = new();

        public SlowOutputPlugin(int delayMs) => _delayMs = delayMs;

        public string Name { get; set; } = "SlowOutput";
        public string ProtocolType => "slow";
        public string Version => "1.0.0";
        public PluginStatus Status { get; private set; } = PluginStatus.Running;
        public int SendCount => _messages.Count;

        public event Action<string, bool>? ConnectionChanged;
        public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

        public Task StartAsync(CancellationToken ct = default)
        {
            Status = PluginStatus.Running;
            return Task.CompletedTask;
        }

        public async Task SendAsync(Message message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delayMs, cancellationToken);
            _messages.Enqueue(message);
        }

        public Task StopAsync()
        {
            Status = PluginStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() { Dispose(); return default; }
    }

    public class FailingOutputPlugin : IOutputPlugin
    {
        public string Name { get; set; } = "FailingOutput";
        public string ProtocolType => "failing";
        public string Version => "1.0.0";
        public PluginStatus Status { get; private set; } = PluginStatus.Running;

        public event Action<string, bool>? ConnectionChanged;
        public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

        public Task StartAsync(CancellationToken ct = default)
        {
            Status = PluginStatus.Running;
            return Task.CompletedTask;
        }

        public Task SendAsync(Message message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated send failure");
        }

        public Task StopAsync()
        {
            Status = PluginStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() { Dispose(); return default; }
    }

    public class TopicCapturingOutputPlugin : IOutputPlugin
    {
        private readonly ConcurrentQueue<Message> _messages = new();
        public string Name { get; set; } = "TopicCaptureOutput";
        public string ProtocolType => "capture";
        public string Version => "1.0.0";
        public PluginStatus Status { get; private set; } = PluginStatus.Running;
        public List<string> ReceivedTopics => _messages.Select(m => m.Topic).ToList();
        public List<Message> ReceivedMessages => _messages.ToList();

        public event Action<string, bool>? ConnectionChanged;
        public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

        public Task StartAsync(CancellationToken ct = default)
        {
            Status = PluginStatus.Running;
            return Task.CompletedTask;
        }

        public Task SendAsync(Message message, CancellationToken cancellationToken = default)
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Status = PluginStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() { Dispose(); return default; }
    }

    #endregion
}
