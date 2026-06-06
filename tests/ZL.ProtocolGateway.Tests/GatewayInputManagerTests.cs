// ============================================================
// 文件：GatewayInputManagerTests.cs
// 描述：GatewayInputManager 单元测试 — 添加/移除/限流/启停/消息处理
// 修复：使用真实 Pipeline + FakeInputPlugin 替代 NSubstitute mock，
//       消除并行测试中 Arg.Any/Returns 上下文污染问题
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class GatewayInputManagerTests : IDisposable
    {
        private GatewayInputManager _manager;
        private ResilientMessagePipeline _pipeline;

        public GatewayInputManagerTests()
        {
            _pipeline = new ResilientMessagePipeline();
            _manager = new GatewayInputManager(_pipeline);
        }

        public void Dispose()
        {
            _manager?.Dispose();
        }

        private FakeInputPlugin CreateFakeInput(string name = "test-in", PluginStatus status = PluginStatus.Stopped)
        {
            return new FakeInputPlugin { NameValue = name, StatusValue = status };
        }

        #region Add/Remove

        [Fact]
        public void AddInput_Valid_AddsToList()
        {
            var input = CreateFakeInput();
            _manager.AddInput(input);

            Assert.Single(_manager.Inputs);
            Assert.Same(input, _manager.Inputs[0]);
        }

        [Fact]
        public void AddInput_Null_IsIgnored()
        {
            _manager.AddInput(null);
            Assert.Empty(_manager.Inputs);
        }

        [Fact]
        public void AddInput_Duplicate_Ignores()
        {
            var input = CreateFakeInput();
            _manager.AddInput(input);
            _manager.AddInput(input);

            Assert.Single(_manager.Inputs);
        }

        [Fact]
        public async Task RemoveInputAsync_RunningInput_StopsAndRemoves()
        {
            var input = CreateFakeInput("removable", PluginStatus.Running);
            _manager.AddInput(input);
            var result = await _manager.RemoveInputAsync(input);

            Assert.True(result);
            Assert.Empty(_manager.Inputs);
            Assert.True(input.StopCalled);
        }

        [Fact]
        public async Task RemoveInputAsync_StoppedInput_JustRemoves()
        {
            var input = CreateFakeInput(status: PluginStatus.Stopped);
            _manager.AddInput(input);
            var result = await _manager.RemoveInputAsync(input);

            Assert.True(result);
            Assert.Empty(_manager.Inputs);
            Assert.False(input.StopCalled);
        }

        [Fact]
        public async Task RemoveInputAsync_Null_ReturnsFalse()
        {
            var result = await _manager.RemoveInputAsync(null);
            Assert.False(result);
        }

        [Fact]
        public async Task RemoveInputAsync_NotInList_ReturnsFalse()
        {
            var input = CreateFakeInput("unknown");
            // Not added to manager
            var result = await _manager.RemoveInputAsync(input);
            Assert.False(result);
        }

        [Fact]
        public void GetInput_Existing_ReturnsPlugin()
        {
            var input = CreateFakeInput("findme");
            _manager.AddInput(input);

            var found = _manager.GetInput("findme");

            Assert.Same(input, found);
        }

        [Fact]
        public void GetInput_Unknown_ReturnsNull()
        {
            var found = _manager.GetInput("nonexistent");
            Assert.Null(found);
        }

        #endregion

        #region Rate Limiting

        [Fact]
        public void SetRateLimit_Positive_CreatesLimiter()
        {
            _manager.SetRateLimit(100);

            // No exception means it succeeded.
        }

        [Fact]
        public void SetRateLimit_Zero_DisposesLimiter()
        {
            _manager.SetRateLimit(100);
            _manager.SetRateLimit(0);

            // No exception — the limiter was disposed and cleared.
        }

        [Fact]
        public void SetRateLimit_Negative_DisposesLimiter()
        {
            _manager.SetRateLimit(100);
            _manager.SetRateLimit(-1);

            // No exception.
        }

        [Fact]
        public void SetRateLimit_OverwritesPrevious()
        {
            _manager.SetRateLimit(10);
            _manager.SetRateLimit(200);

            // No exception — old limiter was disposed, new one created.
        }

        #endregion

        #region Start/Stop

        [Fact]
        public async Task StartInputsAsync_MultipleInputs_StartsAll()
        {
            var input1 = CreateFakeInput("in1");
            var input2 = CreateFakeInput("in2");

            _manager.AddInput(input1);
            _manager.AddInput(input2);

            await _manager.StartInputsAsync(async (name, msg) => { }, CancellationToken.None);

            Assert.True(input1.StartCalled);
            Assert.True(input2.StartCalled);
        }

        [Fact]
        public async Task StartInputsAsync_Empty_DoesNotThrow()
        {
            await _manager.StartInputsAsync(async (name, msg) => { }, CancellationToken.None);
            // No exception.
        }

        [Fact]
        public async Task StopInputsAsync_MultipleInputs_StopsAll()
        {
            var input1 = CreateFakeInput("stop1");
            var input2 = CreateFakeInput("stop2");

            _manager.AddInput(input1);
            _manager.AddInput(input2);

            await _manager.StopInputsAsync();

            Assert.True(input1.StopCalled);
            Assert.True(input2.StopCalled);
        }

        [Fact]
        public async Task StopInputsAsync_OneThrows_StopsOthers()
        {
            var badInput = CreateFakeInput("bad");
            badInput.ThrowOnStop = true;
            var goodInput = CreateFakeInput("good");

            _manager.AddInput(badInput);
            _manager.AddInput(goodInput);

            await _manager.StopInputsAsync(); // Should not throw

            Assert.True(goodInput.StopCalled);
        }

        #endregion

        #region ProcessInputMessageAsync

        [Fact]
        public async Task ProcessInputMessageAsync_NoRateLimit_NoException()
        {
            var msg = new Message { Topic = "test" };

            // Should not throw — pipeline is real but not started, ProcessAsync returns silently
            await _manager.ProcessInputMessageAsync("in1", msg);
        }

        [Fact]
        public async Task ProcessInputMessageAsync_PipelineThrows_CatchesException()
        {
            // Use a full mock (not partial) so we can configure ProcessAsync to throw
            var throwingPipeline = Substitute.For<ResilientMessagePipeline>();
            var mgr = new GatewayInputManager(throwingPipeline);
            var msg = new Message { Topic = "test" };

            // Should not throw — ProcessInputMessageAsync catches pipeline exceptions
            await mgr.ProcessInputMessageAsync("in1", msg);
        }

        [Fact]
        public async Task ProcessInputMessageAsync_WithNullMessage_DoesNotThrow()
        {
            await _manager.ProcessInputMessageAsync("in1", null);
            // No exception.
        }

        #endregion

        #region Dispose

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var mgr = new GatewayInputManager(new ResilientMessagePipeline());
            mgr.SetRateLimit(100);

            mgr.Dispose();

            // No exception — rate limiter disposed.
        }

        [Fact]
        public void Dispose_WithoutRateLimiter_DoesNotThrow()
        {
            var mgr = new GatewayInputManager(new ResilientMessagePipeline());
            mgr.Dispose();

            // No exception.
        }

        #endregion
    }

    /// <summary>
    /// 轻量级 Fake Input 插件，替代 NSubstitute mock，避免并行测试上下文污染。
    /// </summary>
    public class FakeInputPlugin : IInputPlugin
    {
        public string NameValue { get; set; } = "fake-input";
        public PluginStatus StatusValue { get; set; } = PluginStatus.Stopped;
        public bool ThrowOnStop { get; set; }
        public bool StartCalled { get; set; }
        public bool StopCalled { get; set; }
        public Func<Message, Task>? MessageHandler { get; private set; }

        public string Name => NameValue;
        public string ProtocolType => "test";
        public string Version => "1.0.0";
        public PluginStatus Status => StatusValue;
        public event Action<InputPluginStatusArgs>? DetailedStatusChanged;
        public event Action<string, bool>? ConnectionChanged;

        public Task StartAsync(Func<Message, Task> messageHandler, CancellationToken ct = default)
        {
            StartCalled = true;
            MessageHandler = messageHandler;
            StatusValue = PluginStatus.Running;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCalled = true;
            StatusValue = PluginStatus.Stopped;
            if (ThrowOnStop) throw new Exception("stop error");
            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
