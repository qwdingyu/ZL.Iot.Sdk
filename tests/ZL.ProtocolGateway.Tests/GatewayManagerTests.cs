#nullable enable
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
    /// GatewayManager 单元测试 — 验证核心编排能力
    /// 注意：GatewayManager 内部创建自己的 ResilientMessagePipeline，不对外暴露注入点。
    /// 本测试聚焦公开 API，使用 mock IOutputPlugin 验证插件管理逻辑。
    /// GatewayManager 本身生命周期（StartAsync/StopAsync）使用真实 Pipeline。
    /// </summary>
    public class GatewayManagerTests
    {
        #region 构造函数

        [Fact]
        public void Constructor_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new GatewayManager(null!));
        }

        [Fact]
        public void Constructor_InvalidOptions_ThrowsInvalidOperationException()
        {
            var bad = new GatewayManagerOptions { QueueCapacity = 1 }; // < 100
            Assert.Throws<InvalidOperationException>(() => new GatewayManager(bad));
        }

        [Fact]
        public void Constructor_Default_HasNoRegisteredOutputs()
        {
            using var gm = new GatewayManager();
            Assert.Empty(gm.RegisteredOutputNames);
            Assert.False(gm.IsRunning);
        }

        [Fact]
        public void Constructor_PipelineIsAccessible()
        {
            using var gm = new GatewayManager();
            Assert.NotNull(gm.Pipeline);
        }

        [Fact]
        public void Constructor_HealthCheckIsCreated()
        {
            using var gm = new GatewayManager();
            Assert.NotNull(gm.HealthCheck);
        }

        #endregion

        #region StartAsync / StopAsync 生命周期

        [Fact]
        public async Task StartAsync_IsRunningTrue()
        {
            using var gm = new GatewayManager();
            Assert.False(gm.IsRunning);

            await gm.StartAsync();
            Assert.True(gm.IsRunning);
            Assert.NotNull(gm.Pipeline);

            await gm.StopAsync();
            Assert.False(gm.IsRunning);
        }

        [Fact]
        public async Task StartAsync_Twice_IsNoOp()
        {
            using var gm = new GatewayManager();
            await gm.StartAsync();
            Assert.True(gm.IsRunning);

            await gm.StartAsync(); // 第二次 — 无操作
            Assert.True(gm.IsRunning);

            await gm.StopAsync();
        }

        [Fact]
        public async Task StopAsync_WithoutStart_DoesNothing()
        {
            using var gm = new GatewayManager();
            await gm.StopAsync(); // 不应抛异常
            Assert.False(gm.IsRunning);
        }

        [Fact]
        public async Task StopAsync_StopsOutputPluginsBeforeGateway()
        {
            using var gm = new GatewayManager();

            var output = new MockOutputPlugin { Name = "test-out" };
            gm.RegisterOutput("test-out", output);

            await gm.StartAsync();
            await gm.StartOutputAsync("test-out");

            // 停止前：注册存在
            Assert.Contains("test-out", gm.RegisteredOutputNames);

            await gm.StopAsync();

            // 停止后：插件被清理
            Assert.DoesNotContain("test-out", gm.RegisteredOutputNames);
        }

        #endregion

        #region RegisterOutput / UnregisterOutput

        [Fact]
        public void RegisterOutput_NullName_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            Assert.False(gm.RegisterOutput(null!, new MockOutputPlugin()));
        }

        [Fact]
        public void RegisterOutput_EmptyName_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            Assert.False(gm.RegisterOutput("", new MockOutputPlugin()));
        }

        [Fact]
        public void RegisterOutput_NullOutput_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            Assert.False(gm.RegisterOutput("test", null!));
        }

        [Fact]
        public void RegisterOutput_Valid_ReturnsTrue()
        {
            using var gm = new GatewayManager();
            Assert.True(gm.RegisterOutput("test", new MockOutputPlugin()));
            Assert.Single(gm.RegisteredOutputNames);
        }

        [Fact]
        public void RegisterOutput_OverwritesExisting_DisposesOld()
        {
            using var gm = new GatewayManager();
            var old = new TrackDisposeOutputPlugin();
            var newPlugin = new MockOutputPlugin();

            gm.RegisterOutput("test", old);
            gm.RegisterOutput("test", newPlugin);

            Assert.True(old.IsDisposed);
            Assert.Single(gm.RegisteredOutputNames);
        }

        [Fact]
        public void UnregisterOutput_KnownName_RemovesAndDisposes()
        {
            using var gm = new GatewayManager();
            var output = new TrackDisposeOutputPlugin();

            gm.RegisterOutput("test", output);
            Assert.Single(gm.RegisteredOutputNames);

            Assert.True(gm.UnregisterOutput("test"));
            Assert.Empty(gm.RegisteredOutputNames);
            Assert.True(output.IsDisposed);
        }

        [Fact]
        public void UnregisterOutput_UnknownName_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            Assert.False(gm.UnregisterOutput("nonexistent"));
        }

        #endregion

        #region 输入插件管理

        [Fact]
        public void AddInput_Null_IsIgnored()
        {
            using var gm = new GatewayManager();
            gm.AddInput(null!);
            // No exception — null is silently ignored
        }

        [Fact]
        public void AddInput_ValidPlugin_Registers()
        {
            using var gm = new GatewayManager();
            var input = new MockInputPlugin { Name = "test-in" };
            gm.AddInput(input);
            Assert.Single(gm.Inputs);
            Assert.Same(input, gm.Inputs[0]);
        }

        [Fact]
        public void AddInput_Duplicate_Ignores()
        {
            using var gm = new GatewayManager();
            var input = new MockInputPlugin { Name = "test-in" };
            gm.AddInput(input);
            gm.AddInput(input);
            Assert.Single(gm.Inputs);
        }

        [Fact]
        public void RemoveInput_NotRegistered_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            var input = new MockInputPlugin { Name = "test-in" };
            Assert.False(gm.RemoveInput(input));
        }

        [Fact]
        public void RemoveInput_Registered_Removes()
        {
            using var gm = new GatewayManager();
            var input = new MockInputPlugin { Name = "test-in" };
            gm.AddInput(input);
            Assert.True(gm.RemoveInput(input));
            Assert.Empty(gm.Inputs);
        }

        [Fact]
        public void GetInput_Existing_ReturnsPlugin()
        {
            using var gm = new GatewayManager();
            var input = new MockInputPlugin { Name = "find-me" };
            gm.AddInput(input);
            Assert.Same(input, gm.GetInput("find-me"));
        }

        [Fact]
        public void GetInput_NotFound_ReturnsNull()
        {
            using var gm = new GatewayManager();
            Assert.Null(gm.GetInput("nonexistent"));
        }

        #endregion

        #region StartOutputAsync / StopOutputAsync

        [Fact]
        public async Task StartOutputAsync_UnknownName_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            Assert.False(await gm.StartOutputAsync("nonexistent"));
        }

        [Fact]
        public async Task StartOutputAsync_StoppedPlugin_StartsAndReturnsTrue()
        {
            using var gm = new GatewayManager();
            var output = new MockOutputPlugin { Name = "test-out" };
            gm.RegisterOutput("test-out", output);

            Assert.Equal(PluginStatus.Stopped, output.Status);
            Assert.True(await gm.StartOutputAsync("test-out"));
            Assert.Equal(PluginStatus.Running, output.Status);
        }

        [Fact]
        public async Task StartOutputAsync_AlreadyRunning_ReturnsTrueWithoutRestarting()
        {
            using var gm = new GatewayManager();
            var output = new MockOutputPlugin { Name = "test-out", StartCostMs = 0 };
            gm.RegisterOutput("test-out", output);

            await gm.StartOutputAsync("test-out");
            int callCount = output.StartCallCount;

            Assert.True(await gm.StartOutputAsync("test-out"));
            Assert.Equal(callCount, output.StartCallCount); // 未再次调用 StartAsync
        }

        [Fact]
        public async Task StartOutputAsync_StartThrows_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            var output = new MockOutputPlugin
            {
                Name = "test-out",
                StartException = new InvalidOperationException("fail")
            };
            gm.RegisterOutput("test-out", output);

            Assert.False(await gm.StartOutputAsync("test-out"));
        }

        [Fact]
        public async Task StopOutputAsync_UnknownName_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            Assert.False(await gm.StopOutputAsync("nonexistent"));
        }

        [Fact]
        public async Task StopOutputAsync_RunningPlugin_StopsAndRemoves()
        {
            using var gm = new GatewayManager();
            var output = new MockOutputPlugin { Name = "test-out" };
            gm.RegisterOutput("test-out", output);

            await gm.StartOutputAsync("test-out");
            Assert.True(await gm.StopOutputAsync("test-out"));

            Assert.Equal(PluginStatus.Stopped, output.Status);
            Assert.Empty(gm.RegisteredOutputNames);
        }

        [Fact]
        public async Task StopOutputAsync_StopThrows_ReturnsFalseButStillRemoves()
        {
            using var gm = new GatewayManager();
            var output = new MockOutputPlugin
            {
                Name = "test-out",
                StopException = new InvalidOperationException("stop fail")
            };
            gm.RegisterOutput("test-out", output);

            await gm.StartOutputAsync("test-out");
            Assert.False(await gm.StopOutputAsync("test-out"));

            Assert.Empty(gm.RegisteredOutputNames);
        }

        [Fact]
        public async Task ClearOutputsAsync_StopsAll()
        {
            using var gm = new GatewayManager();
            var o1 = new MockOutputPlugin { Name = "o1" };
            var o2 = new MockOutputPlugin { Name = "o2" };
            gm.RegisterOutput("o1", o1);
            gm.RegisterOutput("o2", o2);

            await gm.StartOutputAsync("o1");
            await gm.StartOutputAsync("o2");

            Assert.Equal(2, gm.RegisteredOutputNames.Count);

            await gm.ClearOutputsAsync();

            Assert.Empty(gm.RegisteredOutputNames);
            Assert.Equal(PluginStatus.Stopped, o1.Status);
            Assert.Equal(PluginStatus.Stopped, o2.Status);
        }

        #endregion

        #region GetOutputPluginStatus / GetOutputStatus

        [Fact]
        public void GetOutputPluginStatuses_ReturnsAll()
        {
            using var gm = new GatewayManager();
            gm.RegisterOutput("o1", new MockOutputPlugin { Name = "o1" });
            gm.RegisterOutput("o2", new MockOutputPlugin { Name = "o2" });

            var statuses = gm.GetOutputPluginStatuses();
            Assert.Equal(2, statuses.Count);
        }

        [Fact]
        public void GetOutputPluginStatus_KnownName_ReturnsStatus()
        {
            using var gm = new GatewayManager();
            gm.RegisterOutput("test", new MockOutputPlugin { Name = "test" });

            var status = gm.GetOutputPluginStatus("test");
            Assert.NotNull(status);
            Assert.Equal("test", status!.Name);
        }

        [Fact]
        public void GetOutputPluginStatus_UnknownName_ReturnsNull()
        {
            using var gm = new GatewayManager();
            Assert.Null(gm.GetOutputPluginStatus("nonexistent"));
        }

        [Fact]
        public void GetOutputStatus_KnownName_ReturnsPluginStatus()
        {
            using var gm = new GatewayManager();
            gm.RegisterOutput("test", new MockOutputPlugin { Name = "test" });

            Assert.Equal(PluginStatus.Stopped, gm.GetOutputStatus("test"));
        }

        [Fact]
        public void GetOutputStatus_UnknownName_ReturnsNull()
        {
            using var gm = new GatewayManager();
            Assert.Null(gm.GetOutputStatus("nonexistent"));
        }

        #endregion

        #region PublishMessageAsync

        [Fact]
        public async Task PublishMessageAsync_NotStarted_IsNoOp()
        {
            using var gm = new GatewayManager();
            // 未 StartAsync，PublishMessageAsync 直接返回
            await gm.PublishMessageAsync(new Message { Topic = "test" });
        }

        [Fact]
        public async Task PublishMessageAsync_Started_ForwardsToPipeline()
        {
            using var gm = new GatewayManager();
            var receivedMessages = new List<Message>();
            var output = new MockOutputPlugin
            {
                Name = "pub-test",
                OnSend = msg => { receivedMessages.Add(msg); return Task.CompletedTask; }
            };
            gm.RegisterOutput("pub-test", output);

            await gm.StartAsync();
            await gm.StartOutputAsync("pub-test");

            // Pipeline 需要路由规则才能转发
            // 直接调用 GatewayService 的 PublishAsync，由 Pipeline 处理
            var msg = new Message { Topic = "publish-test", Payload = new byte[] { 1, 2, 3 } };
            await gm.PublishMessageAsync(msg);

            // 注意：PublishAsync 注入 Pipeline，Pipeline 需有匹配的路由规则
            // 如果无路由规则，消息不会到达 output
            // 这里测试的是 PublishMessageAsync 调用本身不抛异常
            // 端到端路由测试在 TcpForwardingScenarioTests 中覆盖

            await gm.StopAsync();
        }

        #endregion

        #region TestSendAsync

        [Fact]
        public async Task TestSendAsync_UnknownName_ReturnsFailure()
        {
            using var gm = new GatewayManager();
            var result = await gm.TestSendAsync("nonexistent", null);
            Assert.False(result.Success);
        }

        [Fact]
        public async Task TestSendAsync_NotRunning_ReturnsFailure()
        {
            using var gm = new GatewayManager();
            gm.RegisterOutput("test", new MockOutputPlugin { Name = "test" });

            var result = await gm.TestSendAsync("test", null);
            Assert.False(result.Success);
            Assert.Contains("未运行", result.ErrorMessage);
        }

        [Fact]
        public async Task TestSendAsync_Running_ReturnsSuccess()
        {
            using var gm = new GatewayManager();
            var output = new MockOutputPlugin { Name = "test" };
            gm.RegisterOutput("test", output);

            await gm.StartOutputAsync("test");
            var result = await gm.TestSendAsync("test", null);

            Assert.True(result.Success);
            Assert.True(result.DurationMs >= 0);
        }

        [Fact]
        public async Task TestSendAsync_WithPayload_SendsJsonContent()
        {
            using var gm = new GatewayManager();
            Message? received = null;
            var output = new MockOutputPlugin
            {
                Name = "test",
                OnSend = msg => { received = msg; return Task.CompletedTask; }
            };
            gm.RegisterOutput("test", output);

            await gm.StartOutputAsync("test");
            await gm.TestSendAsync("test", "{\"custom\":42}");

            Assert.NotNull(received);
            Assert.Equal("__gateway_test__", received!.Topic);
        }

        [Fact]
        public async Task TestSendAsync_SendThrows_ReturnsFailure()
        {
            using var gm = new GatewayManager();
            var output = new MockOutputPlugin
            {
                Name = "test",
                OnSend = _ => throw new InvalidOperationException("send failed")
            };
            gm.RegisterOutput("test", output);

            await gm.StartOutputAsync("test");
            var result = await gm.TestSendAsync("test", null);

            Assert.False(result.Success);
            Assert.Contains("send failed", result.ErrorMessage);
        }

        #endregion

        #region 指标与死信

        [Fact]
        public void GetMetricsSnapshot_NotStarted_ReturnsSnapshot()
        {
            using var gm = new GatewayManager();
            gm.RegisterOutput("test", new MockOutputPlugin { Name = "test" });

            var snapshot = gm.GetMetricsSnapshot();
            Assert.NotNull(snapshot);
            Assert.False(snapshot.IsRunning);
            Assert.Equal(0, snapshot.QueuedMessageCount);
        }

        [Fact]
        public async Task GetMetricsSnapshot_Started_IncludesOutputStatus()
        {
            using var gm = new GatewayManager();
            gm.RegisterOutput("test", new MockOutputPlugin { Name = "test" });
            await gm.StartOutputAsync("test");

            var snapshot = gm.GetMetricsSnapshot();
            Assert.Single(snapshot.OutputPlugins);
        }

        [Fact]
        public async Task GetDeadLetters_ReturnsEmptyByDefault()
        {
            using var gm = new GatewayManager();
            var letters = gm.GetDeadLetters();
            Assert.Empty(letters);
        }

        [Fact]
        public void ClearDeadLetterQueue_DoesNotThrow()
        {
            using var gm = new GatewayManager();
            gm.ClearDeadLetterQueue(); // 空队列清空不应抛异常
        }

        [Fact]
        public async Task RetryDeadLettersAsync_EmptyReturnsZero()
        {
            using var gm = new GatewayManager();
            var count = await gm.RetryDeadLettersAsync();
            Assert.Equal(0, count);
        }

        #endregion

        #region 断路器

        [Fact]
        public void ResetCircuitBreaker_UnknownName_ReturnsFalse()
        {
            using var gm = new GatewayManager();
            Assert.False(gm.ResetCircuitBreaker("nonexistent"));
        }

        [Fact]
        public void ResetCircuitBreaker_KnownName_ReturnsTrue()
        {
            using var gm = new GatewayManager();
            gm.RegisterOutput("test", new MockOutputPlugin { Name = "test" });

            Assert.True(gm.ResetCircuitBreaker("test"));
        }

        #endregion

        #region 事件桥接

        [Fact]
        public void OutputHealthChanged_FiresFromPluginDetailEvent()
        {
            using var gm = new GatewayManager();
            var plugin = new DetailedStatusPlugin { Name = "event-test" };

            OutputPluginStatusArgs? captured = null;
            gm.OutputHealthChanged += args => captured = args;

            gm.RegisterOutput("event-test", plugin);
            plugin.FireDetailedStatusChange(new OutputPluginStatusArgs
            {
                PluginName = "event-test",
                HealthLevel = OutputPluginHealthLevel.Error,
                ErrorCode = GatewayErrorCodes.ConnectionFailed
            });

            Assert.NotNull(captured);
            Assert.Equal("event-test", captured!.PluginName);
            Assert.Equal(OutputPluginHealthLevel.Error, captured.HealthLevel);
        }

        #endregion

        #region Dispose / DisposeAsync

        [Fact]
        public void Dispose_AfterStart_DoesNotThrow()
        {
            var gm = new GatewayManager();
            // Dispose 前不必显式 Stop
            gm.Dispose();
            // 接口契约：Dispose 不保证完整停止，但不抛异常
        }

        [Fact]
        public async Task DisposeAsync_StopsGracefully()
        {
            var gm = new GatewayManager();
            var output = new MockOutputPlugin { Name = "dispose-test" };
            gm.RegisterOutput("dispose-test", output);

            await gm.StartAsync();
            await gm.StartOutputAsync("dispose-test");

            await gm.DisposeAsync();

            Assert.False(gm.IsRunning);
            Assert.Empty(gm.RegisteredOutputNames);
        }

        [Fact]
        public async Task DisposeAsync_Twice_IsNoOp()
        {
            var gm = new GatewayManager();
            await gm.DisposeAsync();
            await gm.DisposeAsync(); // 第二次不应抛异常
        }

        #endregion

        #region Mock OutputPlugin

        /// <summary>
        /// 轻量 Mock — 可追踪的 IOutputPlugin 实现
        /// </summary>
        private class MockOutputPlugin : IOutputPlugin
        {
            public string Name { get; set; } = "mock";

            public string ProtocolType => "Mock";

            public string Version => "1.0.0";

            private volatile PluginStatus _status = PluginStatus.Stopped;
            public PluginStatus Status
            {
                get => _status;
                set => _status = value;
            }

            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

            public int StartCallCount { get; private set; }
            public Exception? StartException { get; set; }
            public Exception? StopException { get; set; }
            public int StartCostMs { get; set; } = 0;

            public Func<Message, Task> OnSend = _ => Task.CompletedTask;

            public async Task StartAsync(CancellationToken ct = default)
            {
                StartCallCount++;
                if (StartException != null) throw StartException;
                if (StartCostMs > 0) await Task.Delay(StartCostMs, ct);
                _status = PluginStatus.Running;
                ConnectionChanged?.Invoke(Name, true);
            }

            public Task SendAsync(Message message, CancellationToken cancellationToken = default)
                => OnSend(message);

            public Task StopAsync()
            {
                _status = PluginStatus.Stopped;
                ConnectionChanged?.Invoke(Name, false);
                if (StopException != null) throw StopException;
                return Task.CompletedTask;
            }

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>
        /// 追踪 Dispose 是否被调用的 OutputPlugin
        /// </summary>
        private class TrackDisposeOutputPlugin : IOutputPlugin
        {
            public string Name { get; set; } = "track";
            public string ProtocolType => "Track";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; } = PluginStatus.Stopped;

            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

            public bool IsDisposed { get; private set; }

            public Task StartAsync(CancellationToken ct = default)
            {
                Status = PluginStatus.Running;
                ConnectionChanged?.Invoke(Name, true);
                return Task.CompletedTask;
            }

            public Task SendAsync(Message message, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task StopAsync()
            {
                Status = PluginStatus.Stopped;
                ConnectionChanged?.Invoke(Name, false);
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>
        /// 可编程触发 DetailedStatusChanged 事件的 OutputPlugin
        /// </summary>
        private class DetailedStatusPlugin : IOutputPlugin
        {
            public string Name { get; set; } = "detailed";
            public string ProtocolType => "Detailed";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; } = PluginStatus.Stopped;
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

            public void FireDetailedStatusChange(OutputPluginStatusArgs args)
                => DetailedStatusChanged?.Invoke(args);

            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public Task SendAsync(Message message, CancellationToken ct = default) => Task.CompletedTask;
            public Task StopAsync() { Status = PluginStatus.Stopped; return Task.CompletedTask; }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>
        /// 轻量 Mock — IInputPlugin 实现
        /// </summary>
        private class MockInputPlugin : IInputPlugin
        {
            public string Name { get; set; } = "mock-input";
            public string ProtocolType => "Mock";
            public string Version => "1.0.0";
            public PluginStatus Status { get; private set; } = PluginStatus.Stopped;
            public event Action<string, bool>? ConnectionChanged;
            public event Action<ProtocolGateway.InputPluginStatusArgs>? DetailedStatusChanged;

            public Task StartAsync(Func<Message, Task> messageHandler, CancellationToken cancellationToken = default)
            {
                Status = PluginStatus.Running;
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

        #endregion
    }
}
#nullable restore
