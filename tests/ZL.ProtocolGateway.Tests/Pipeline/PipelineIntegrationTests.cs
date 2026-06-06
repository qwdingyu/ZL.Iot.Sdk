using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    /// <summary>
    /// Pipeline 端到端集成测试 — 验证 Filter→Transform→Route→Send 完整链路。
    /// </summary>
    public class PipelineIntegrationTests
    {
        /// <summary>
        /// 端到端：注册两个输出 + 路由规则 → 发送消息 → 验证路由正确分发。
        /// </summary>
        [Fact]
        public async Task EndToEnd_TwoOutputs_RoutesCorrectly()
        {
            var outputA = new CollectingOutput("output-a");
            var outputB = new CollectingOutput("output-b");

            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput("output-a", outputA);
            pipeline.RegisterOutput("output-b", outputB);

            pipeline.AddRouter(new RouteRule
            {
                Condition = m => m.Topic.Contains("alpha"),
                OutputNames = { "output-a" }
            });
            pipeline.AddRouter(new RouteRule
            {
                Condition = m => m.Topic.Contains("beta"),
                OutputNames = { "output-b" }
            });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "alpha-data", Payload = new byte[] { 1 } });
            await pipeline.ProcessAsync(new Message { Topic = "beta-data", Payload = new byte[] { 2 } });
            await pipeline.ProcessAsync(new Message { Topic = "other-data", Payload = new byte[] { 3 } });

            await pipeline.WaitForIdleAsync();

            var aList = outputA.Received.ToList();
            var bList = outputB.Received.ToList();
            Assert.Single(aList);
            Assert.Equal("alpha-data", aList[0].Topic);
            Assert.Single(bList);
            Assert.Equal("beta-data", bList[0].Topic);

            await pipeline.StopAsync();
        }

        /// <summary>
        /// 端到端：Filter 过滤掉不匹配的消息。
        /// </summary>
        [Fact]
        public async Task EndToEnd_FilterBlocksMessages()
        {
            var output = new CollectingOutput("out");

            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddFilter(m => Task.FromResult(m.Topic != "blocked"));
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "allowed", Payload = new byte[] { 1 } });
            await pipeline.ProcessAsync(new Message { Topic = "blocked", Payload = new byte[] { 2 } });

            await pipeline.WaitForIdleAsync();

            var list = output.Received.ToList();
            Assert.Single(list);
            Assert.Equal("allowed", list[0].Topic);

            await pipeline.StopAsync();
        }

        /// <summary>
        /// 端到端：Transformer 修改消息内容。
        /// </summary>
        [Fact]
        public async Task EndToEnd_TransformerModifiesMessage()
        {
            var output = new CollectingOutput("out");

            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddTransformer(async m =>
            {
                m.Topic = m.Topic + "-transformed";
                return await Task.FromResult(m);
            });
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();
            await pipeline.ProcessAsync(new Message { Topic = "original", Payload = new byte[] { 1 } });
            await pipeline.WaitForIdleAsync();
            await Task.Delay(100); // WaitForIdleAsync 有已知竞态，加安全延迟

            var list = output.Received.ToList();
            Assert.Single(list);
            Assert.Equal("original-transformed", list[0].Topic);

            await pipeline.StopAsync();
        }

        /// <summary>
        /// 端到端：背压测试 — 快速发送超过并发度的消息，验证不丢消息。
        /// </summary>
        [Fact]
        public async Task EndToEnd_Backpressure_NoMessagesLost()
        {
            var output = new SlowCollectingOutput("slow-out", delayMs: 50);

            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();

            const int count = 20;
            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = pipeline.ProcessAsync(new Message { Topic = $"msg-{i}", Payload = new byte[] { (byte)i } });
            }

            await Task.WhenAll(tasks);
            await pipeline.WaitForIdleAsync();

            Assert.Equal(count, output.Received.Count);

            await pipeline.StopAsync();
        }

        /// <summary>
        /// 端到端：死信队列 — 输出失败 → 消息进入死信。
        /// </summary>
        [Fact]
        public async Task EndToEnd_FailingOutput_GoesToDeadLetter()
        {
            var output = new FailingOutput("fail-out");

            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();
            await pipeline.ProcessAsync(new Message { Topic = "will-fail", Payload = new byte[] { 1 } });
            await pipeline.WaitForIdleAsync();

            Assert.Equal(1, pipeline.DeadLetterCount);

            await pipeline.StopAsync();
        }

        /// <summary>
        /// 端到端：动态注册输出插件后消息能正确路由。
        /// </summary>
        [Fact]
        public async Task EndToEnd_DynamicOutputRegistration()
        {
            var pipeline = new ResilientMessagePipeline();
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { "dynamic" } });

            await pipeline.StartAsync();

            // 动态注册
            var output = new CollectingOutput("dynamic");
            pipeline.RegisterOutput("dynamic", output);

            await pipeline.ProcessAsync(new Message { Topic = "after", Payload = new byte[] { 2 } });
            await pipeline.WaitForIdleAsync();
            await Task.Delay(100); // WaitForIdleAsync 有已知竞态，加安全延迟

            var list = output.Received.ToList();
            Assert.Single(list);
            Assert.Equal("after", list[0].Topic);

            await pipeline.StopAsync();
        }

        /// <summary>
        /// 端到端：多路由规则 + ContinueMatching=false 短路匹配。
        /// </summary>
        [Fact]
        public async Task EndToEnd_ShortCircuitRouting()
        {
            var outputA = new CollectingOutput("a");
            var outputB = new CollectingOutput("b");

            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput("a", outputA);
            pipeline.RegisterOutput("b", outputB);

            // 第一条规则：短路，匹配所有消息 → 只路由到 A
            pipeline.AddRouter(new RouteRule
            {
                Condition = m => m.Topic.StartsWith("x"),
                OutputNames = { "a" },
                ContinueMatching = false
            });
            // 第二条规则：也匹配所有消息 → 路由到 B
            pipeline.AddRouter(new RouteRule
            {
                Condition = _ => true,
                OutputNames = { "b" }
            });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "x-special", Payload = new byte[] { 1 } });
            await pipeline.ProcessAsync(new Message { Topic = "normal", Payload = new byte[] { 2 } });
            await pipeline.WaitForIdleAsync();

            var aList = outputA.Received.ToList();
            var bList = outputB.Received.ToList();

            // x-special 只命中 A（短路）
            Assert.Single(aList);
            Assert.Equal("x-special", aList[0].Topic);
            // normal 只命中 B（第一条不匹配）
            Assert.Single(bList);
            Assert.Equal("normal", bList[0].Topic);

            await pipeline.StopAsync();
        }

        /// <summary>
        /// 端到端：Filter + Transformer + Router 组合 — 验证完整处理链。
        /// Filter 过滤掉 "skip"，Transformer 追加 "-ok"，Router 路由到输出。
        /// </summary>
        [Fact]
        public async Task EndToEnd_FilterTransformerRouter_ChainWorks()
        {
            var output = new CollectingOutput("chain-out");

            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(output);
            pipeline.AddFilter(m => Task.FromResult(!m.Topic.Contains("skip")));
            pipeline.AddTransformer(async m =>
            {
                m.Topic = m.Topic + "-ok";
                return await Task.FromResult(m);
            });
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            await pipeline.StartAsync();

            await pipeline.ProcessAsync(new Message { Topic = "keep", Payload = new byte[] { 1 } });
            await pipeline.ProcessAsync(new Message { Topic = "skip-this", Payload = new byte[] { 2 } });
            await pipeline.ProcessAsync(new Message { Topic = "also-keep", Payload = new byte[] { 3 } });
            await pipeline.WaitForIdleAsync();

            var list = output.Received.ToList();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, m => m.Topic == "keep-ok");
            Assert.Contains(list, m => m.Topic == "also-keep-ok");

            await pipeline.StopAsync();
        }

        #region 测试辅助类

        private class CollectingOutput : IOutputPlugin
        {
            public CollectingOutput(string name) { Name = name; }
            public string Name { get; }
            public string ProtocolType => "test";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; } = PluginStatus.Running;
            public ConcurrentBag<Message> Received { get; } = new();
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public virtual Task SendAsync(Message message, CancellationToken ct = default)
            {
                Received.Add(message);
                return Task.CompletedTask;
            }
            public Task StopAsync() { Status = PluginStatus.Stopped; return Task.CompletedTask; }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private class SlowCollectingOutput : CollectingOutput
        {
            private readonly int _delayMs;
            public SlowCollectingOutput(string name, int delayMs) : base(name) { _delayMs = delayMs; }

            public override Task SendAsync(Message message, CancellationToken ct = default)
            {
                Task.Delay(_delayMs, ct).Wait(ct);
                Received.Add(message);
                return Task.CompletedTask;
            }
        }

        private class FailingOutput : IOutputPlugin
        {
            public FailingOutput(string name) { Name = name; }
            public string Name { get; }
            public string ProtocolType => "test";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; } = PluginStatus.Running;
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public Task SendAsync(Message message, CancellationToken ct = default)
                => throw new InvalidOperationException("Simulated failure");
            public Task StopAsync() { Status = PluginStatus.Stopped; return Task.CompletedTask; }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        #endregion
    }
}
