// ============================================================
// 文件：GatewayOutputManagerTests.cs
// 描述：GatewayOutputManager 单元测试 — 注册/移除/启停/状态/事件
// ============================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class GatewayOutputManagerTests
    {
        private ResilientMessagePipeline CreateMockPipeline()
        {
            var pipeline = Substitute.ForPartsOf<ResilientMessagePipeline>();
            pipeline.SendTimeoutMs = 30000;
            pipeline.MaxRetryAttempts = 3;
            pipeline.RetryBaseDelayMs = 100;
            pipeline.CircuitBreakerFailureThreshold = 5;
            pipeline.CircuitBreakerRecoveryTimeMs = 60000;
            return pipeline;
        }

        private GatewayOutputManager CreateManager()
        {
            return new GatewayOutputManager(CreateMockPipeline());
        }

        private IOutputPlugin CreateMockOutput(string name = "test-out", string protocol = "test")
        {
            var output = Substitute.For<IOutputPlugin>();
            output.Name.Returns(name);
            output.ProtocolType.Returns(protocol);
            output.Version.Returns("1.0.0");
            output.Status.Returns(PluginStatus.Stopped);
            return output;
        }

        #region Register/Unregister

        [Fact]
        public void RegisterOutput_ValidArgs_ReturnsTrue()
        {
            var manager = CreateManager();
            var output = new TestOutputPlugin("test-out");

            var result = manager.RegisterOutput("test-out", output);

            Assert.True(result);
            Assert.Contains("test-out", manager.RegisteredOutputNames);
        }

        [Fact]
        public void RegisterOutput_NullName_ReturnsFalse()
        {
            var manager = CreateManager();
            var output = CreateMockOutput();

            var result = manager.RegisterOutput(null, output);

            Assert.False(result);
            Assert.Empty(manager.RegisteredOutputNames);
        }

        [Fact]
        public void RegisterOutput_EmptyName_ReturnsFalse()
        {
            var manager = CreateManager();
            var output = CreateMockOutput();

            var result = manager.RegisterOutput("", output);

            Assert.False(result);
        }

        [Fact]
        public void RegisterOutput_NullPlugin_ReturnsFalse()
        {
            var manager = CreateManager();

            var result = manager.RegisterOutput("test-out", null);

            Assert.False(result);
        }

        [Fact]
        public async Task RegisterOutput_SameName_ReplacesExisting()
        {
            var manager = CreateManager();
            var oldOutput = CreateMockOutput();
            var newOutput = CreateMockOutput("new-out", "new-protocol");

            manager.RegisterOutput("test-out", oldOutput);
            var result = manager.RegisterOutput("test-out", newOutput);

            Assert.True(result);
            Assert.Single(manager.RegisteredOutputNames);
            Assert.Equal("test-out", manager.RegisteredOutputNames[0]);

            // 旧插件已被 Dispose
            oldOutput.Received(1).Dispose();
        }

        [Fact]
        public void UnregisterOutput_Existing_ReturnsTrue()
        {
            var manager = CreateManager();
            var output = CreateMockOutput();
            manager.RegisterOutput("test-out", output);

            var result = manager.UnregisterOutput("test-out");

            Assert.True(result);
            Assert.DoesNotContain("test-out", manager.RegisteredOutputNames);
        }

        [Fact]
        public void UnregisterOutput_Unknown_ReturnsFalse()
        {
            var manager = CreateManager();

            var result = manager.UnregisterOutput("nonexistent");

            Assert.False(result);
        }

        [Fact]
        public async Task UnregisterOutput_CallsDispose()
        {
            var manager = CreateManager();
            var output = new TestOutputPlugin("test-out");
            manager.RegisterOutput("test-out", output);

            manager.UnregisterOutput("test-out");

            Assert.True(output.DisposeAsyncCalled || output.Disposed);
        }

        #endregion

        #region Start/Stop

        [Fact]
        public async Task StartOutputAsync_Registered_StartsSuccessfully()
        {
            var pipeline = new ResilientMessagePipeline();
            var manager = new GatewayOutputManager(pipeline);

            var output = new TestOutputPlugin("start-test");
            manager.RegisterOutput("start-test", output);

            var result = await manager.StartOutputAsync("start-test");

            Assert.True(result);
            Assert.Equal(PluginStatus.Running, output.Status);
        }

        [Fact]
        public async Task StartOutputAsync_NotRegistered_ReturnsFalse()
        {
            var manager = CreateManager();

            var result = await manager.StartOutputAsync("nonexistent");

            Assert.False(result);
        }

        [Fact]
        public async Task StartOutputAsync_AlreadyRunning_ReturnsTrue()
        {
            var pipeline = CreateMockPipeline();
            var manager = new GatewayOutputManager(pipeline);
            var output = Substitute.For<IOutputPlugin>();
            output.Name.Returns("running-out");
            output.ProtocolType.Returns("test");
            output.Version.Returns("1.0.0");
            output.Status.Returns(PluginStatus.Running);

            manager.RegisterOutput("running-out", output);

            var result = await manager.StartOutputAsync("running-out");

            Assert.True(result);
            // 不应调用 pipeline.RegisterOutput 或 output.StartAsync
            pipeline.DidNotReceive().RegisterOutput(Arg.Any<IOutputPlugin>());
        }

        [Fact]
        public async Task StartOutputAsync_StartThrows_ReturnsFalse()
        {
            var pipeline = Substitute.ForPartsOf<ResilientMessagePipeline>();
            pipeline.SendTimeoutMs = 30000;
            pipeline.MaxRetryAttempts = 3;
            pipeline.RetryBaseDelayMs = 100;
            pipeline.CircuitBreakerFailureThreshold = 5;
            pipeline.CircuitBreakerRecoveryTimeMs = 60000;
            var manager = new GatewayOutputManager(pipeline);

            var output = Substitute.For<IOutputPlugin>();
            output.Name.Returns("failing-out");
            output.ProtocolType.Returns("test");
            output.Version.Returns("1.0.0");
            output.Status.Returns(PluginStatus.Stopped);
            output.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new Exception("startup failure")));

            manager.RegisterOutput("failing-out", output);

            var result = await manager.StartOutputAsync("failing-out");

            Assert.False(result);
        }

        [Fact]
        public async Task StopOutputAsync_Registered_StopsSuccessfully()
        {
            var manager = CreateManager();
            var output = new TestOutputPlugin("stop-test");
            output.Status = PluginStatus.Running;

            manager.RegisterOutput("stop-test", output);
            var result = await manager.StopOutputAsync("stop-test");

            Assert.True(result);
            Assert.DoesNotContain("stop-test", manager.RegisteredOutputNames);
        }

        [Fact]
        public async Task StopOutputAsync_Unknown_ReturnsFalse()
        {
            var manager = CreateManager();

            var result = await manager.StopOutputAsync("nonexistent");

            Assert.False(result);
        }

        [Fact]
        public async Task StopOutputAsync_StopThrows_ReturnsFalse()
        {
            var manager = CreateManager();
            var output = Substitute.For<IOutputPlugin>();
            output.Name.Returns("bad-stop");
            output.ProtocolType.Returns("test");
            output.Version.Returns("1.0.0");
            output.Status.Returns(PluginStatus.Running);
            output.StopAsync().Returns(Task.FromException(new Exception("stop failure")));

            manager.RegisterOutput("bad-stop", output);

            var result = await manager.StopOutputAsync("bad-stop");

            Assert.False(result);
            // 即使 stop 失败，插件已从注册表移除
            Assert.DoesNotContain("bad-stop", manager.RegisteredOutputNames);
        }

        [Fact]
        public async Task ClearOutputsAsync_StopsAllRegistered()
        {
            var manager = CreateManager();
            var output1 = new TestOutputPlugin("out1");
            var output2 = new TestOutputPlugin("out2");
            manager.RegisterOutput("out1", output1);
            manager.RegisterOutput("out2", output2);

            await manager.ClearOutputsAsync();

            Assert.Empty(manager.RegisteredOutputNames);
        }

        #endregion

        #region Status

        [Fact]
        public void GetOutputPluginStatuses_ReturnsAllRegistered()
        {
            var manager = CreateManager();
            var output1 = CreateMockOutput("status1");
            var output2 = CreateMockOutput("status2");
            manager.RegisterOutput("status1", output1);
            manager.RegisterOutput("status2", output2);

            var statuses = manager.GetOutputPluginStatuses();

            Assert.Equal(2, statuses.Count);
            Assert.Contains(statuses, s => s.Name == "status1");
            Assert.Contains(statuses, s => s.Name == "status2");
        }

        [Fact]
        public void GetOutputPluginStatus_Existing_ReturnsStatus()
        {
            var manager = CreateManager();
            var output = new TestOutputPlugin("query-out");
            manager.RegisterOutput("query-out", output);

            var status = manager.GetOutputPluginStatus("query-out");

            Assert.NotNull(status);
            Assert.Equal("query-out", status.Name);
        }

        [Fact]
        public void GetOutputPluginStatus_Unknown_ReturnsNull()
        {
            var manager = CreateManager();

            var status = manager.GetOutputPluginStatus("nonexistent");

            Assert.Null(status);
        }

        [Fact]
        public void GetOutputStatus_Existing_ReturnsPluginStatus()
        {
            var manager = CreateManager();
            var output = CreateMockOutput("status-out");
            output.Status.Returns(PluginStatus.Running);
            manager.RegisterOutput("status-out", output);

            var status = manager.GetOutputStatus("status-out");

            Assert.NotNull(status);
            Assert.Equal(PluginStatus.Running, status);
        }

        [Fact]
        public void GetOutputStatus_Unknown_ReturnsNull()
        {
            var manager = CreateManager();

            var status = manager.GetOutputStatus("nonexistent");

            Assert.Null(status);
        }

        [Fact]
        public void TryGetOutput_Existing_ReturnsTrue()
        {
            var manager = CreateManager();
            var output = CreateMockOutput("try-out");
            manager.RegisterOutput("try-out", output);

            var result = manager.TryGetOutput("try-out", out var found);

            Assert.True(result);
            Assert.Same(output, found);
        }

        [Fact]
        public void TryGetOutput_Unknown_ReturnsFalse()
        {
            var manager = CreateManager();

            var result = manager.TryGetOutput("nonexistent", out var found);

            Assert.False(result);
            Assert.Null(found);
        }

        #endregion

        #region Events

        [Fact]
        public void OutputHealthChanged_Fires_OnDetailedStatusChanged()
        {
            var pipeline = Substitute.ForPartsOf<ResilientMessagePipeline>();
            var manager = new GatewayOutputManager(pipeline);

            var output = new TestOutputPlugin("event-out");

            OutputPluginStatusArgs? captured = null;
            manager.OutputHealthChanged += args => { captured = args; };

            manager.RegisterOutput("event-out", output);

            // Fire the plugin's DetailedStatusChanged event via the test helper
            output.FireDetailedStatus(new OutputPluginStatusArgs
            {
                PluginName = "event-out",
                Status = PluginStatus.Running,
                Message = "test",
                HealthLevel = OutputPluginHealthLevel.Healthy
            });

            Assert.NotNull(captured);
            Assert.Equal("event-out", captured.PluginName);
        }

        /// <summary>Minimal IOutputPlugin implementation for event-firing tests.</summary>
        private sealed class TestOutputPlugin : IOutputPlugin
        {
            public TestOutputPlugin(string name) => Name = name;
            public string Name { get; }
            public string ProtocolType => "test";
            public string Version => "1.0.0";
            public PluginStatus Status { get; set; }
            public bool Disposed { get; private set; }
            public bool DisposeAsyncCalled { get; private set; }
            public event Action<string, bool>? ConnectionChanged;
            public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;
            public Task StartAsync(CancellationToken ct = default) { Status = PluginStatus.Running; return Task.CompletedTask; }
            public Task SendAsync(Message msg, CancellationToken ct = default) => Task.CompletedTask;
            public Task StopAsync() => Task.CompletedTask;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { DisposeAsyncCalled = true; return default; }
            public void FireDetailedStatus(OutputPluginStatusArgs args) => DetailedStatusChanged?.Invoke(args);
        }

        #endregion
    }
}
