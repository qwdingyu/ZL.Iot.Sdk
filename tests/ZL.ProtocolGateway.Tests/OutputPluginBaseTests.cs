using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// OutputPluginBase 基类行为测试
    /// 验证状态管理、事件通知、异常处理等公共逻辑
    /// </summary>
    public class OutputPluginBaseTests
    {
        [Fact]
        public async Task StartAsync_Success_TransitionsToRunning()
        {
            var plugin = new TestOutputPlugin("test");
            await plugin.StartAsync();

            Assert.Equal(PluginStatus.Running, plugin.Status);
            Assert.True(plugin.StartCalled);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsNoOp()
        {
            var plugin = new TestOutputPlugin("test");
            await plugin.StartAsync();
            await plugin.StartAsync(); // 第二次调用

            Assert.Equal(1, plugin.StartCallCount);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_AlreadyStarting_IsNoOp()
        {
            var plugin = new TestOutputPlugin("test", delayStart: true);
            var startTask = plugin.StartAsync();

            // 等待进入 Starting 状态
            while (plugin.Status != PluginStatus.Starting)
                await Task.Delay(10);

            await plugin.StartAsync(); // 重复调用

            plugin.CompleteStart.TrySetResult();
            await startTask;

            Assert.Equal(1, plugin.StartCallCount);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_OnStartThrows_TransitionsToError()
        {
            var plugin = new TestOutputPlugin("test", startException: new InvalidOperationException("boom"));
            await plugin.StartAsync();

            Assert.Equal(PluginStatus.Fatal, plugin.Status);
            Assert.NotNull(plugin.LastException);
            Assert.Equal("boom", plugin.LastException?.Message);
        }

        [Fact]
        public async Task StartAsync_Success_RaisesConnectionChangedTrue()
        {
            var plugin = new TestOutputPlugin("test");
            bool connectionFired = false;
            bool? connectionValue = null;
            plugin.ConnectionChanged += (name, connected) =>
            {
                connectionFired = true;
                connectionValue = connected;
            };

            await plugin.StartAsync();

            Assert.True(connectionFired);
            Assert.True(connectionValue);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_Success_RaisesDetailedStatusChanged()
        {
            var plugin = new TestOutputPlugin("test");
            var statusArgs = await CaptureNextDetailedStatusAsync(plugin, async () => await plugin.StartAsync());

            Assert.NotNull(statusArgs);
            Assert.Equal("test", statusArgs.PluginName);
            Assert.Equal(OutputPluginHealthLevel.Healthy, statusArgs.HealthLevel);
            Assert.Equal(GatewayErrorCodes.None, statusArgs.ErrorCode);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_Success_SetsHealthyState()
        {
            var plugin = new TestOutputPlugin("test");
            await plugin.StartAsync();

            await plugin.SendAsync(new Message { Topic = "test", Payload = new byte[] { 1, 2, 3 } });

            Assert.Equal(1, plugin.SendCallCount);
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_WhenNotRunning_ThrowsInvalidOperationException()
        {
            var plugin = new TestOutputPlugin("test");
            // 不调用 StartAsync，直接发送应抛异常

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Equal(0, plugin.SendCallCount);
        }

        [Fact]
        public async Task SendAsync_OnSendThrows_RaisesErrorAndThrows()
        {
            var plugin = new TestOutputPlugin("test", sendException: new InvalidOperationException("send failed"));
            await plugin.StartAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.SendAsync(new Message()));

            Assert.Equal("send failed", ex.Message);
            Assert.NotNull(plugin.LastException);
        }

        [Fact]
        public async Task SendAsync_OnSendThrows_RaisesDetailedStatusError()
        {
            var plugin = new TestOutputPlugin("test", sendException: new InvalidOperationException("send failed"));
            await plugin.StartAsync();

            OutputPluginStatusArgs capturedArgs = null;
            plugin.DetailedStatusChanged += args => capturedArgs = args;

            try { await plugin.SendAsync(new Message()); } catch { }

            Assert.NotNull(capturedArgs);
            Assert.Equal(OutputPluginHealthLevel.Error, capturedArgs.HealthLevel);
            Assert.Equal(GatewayErrorCodes.ConnectionFailed, capturedArgs.ErrorCode);
            Assert.Equal(1, capturedArgs.ConsecutiveFailures);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new TestOutputPlugin("test");
            await plugin.StartAsync();
            await plugin.StopAsync();

            Assert.Equal(PluginStatus.Stopped, plugin.Status);
            Assert.True(plugin.StopCalled);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoOp()
        {
            var plugin = new TestOutputPlugin("test");
            await plugin.StartAsync();
            await plugin.StopAsync();
            await plugin.StopAsync();

            Assert.Equal(1, plugin.StopCallCount);
        }

        [Fact]
        public async Task StopAsync_OnStopThrows_StillTransitionsToStopped()
        {
            var plugin = new TestOutputPlugin("test", stopException: new InvalidOperationException("stop failed"));
            await plugin.StartAsync();
            await plugin.StopAsync();

            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_RaisesConnectionChangedFalse()
        {
            var plugin = new TestOutputPlugin("test");
            bool connectionFired = false;
            bool? connectionValue = null;
            plugin.ConnectionChanged += (name, connected) =>
            {
                connectionFired = true;
                connectionValue = connected;
            };

            await plugin.StartAsync();
            // 清除 Start 的事件
            connectionFired = false;
            connectionValue = null;

            await plugin.StopAsync();

            Assert.True(connectionFired);
            Assert.False(connectionValue);
        }

        [Fact]
        public async Task StopAsync_RaisesDetailedStatusStopped()
        {
            var plugin = new TestOutputPlugin("test");
            await plugin.StartAsync();

            var statusArgs = await CaptureNextDetailedStatusAsync(plugin, async () => await plugin.StopAsync());

            Assert.NotNull(statusArgs);
            Assert.Equal(OutputPluginHealthLevel.Healthy, statusArgs.HealthLevel);
            Assert.Contains("stopped", statusArgs.Message);
        }

        [Fact]
        public async Task CalculateBackoffDelay_ZeroFailures_ReturnsBase()
        {
            var plugin = new TestOutputPlugin("test");
            var delay = plugin.GetBackoffDelay(0);
            Assert.Equal(3000, delay);
        }

        [Fact]
        public async Task CalculateBackoffDelay_ExponentialGrowth()
        {
            var plugin = new TestOutputPlugin("test");
            // Equal Jitter 使返回值在 [capped/2, capped] 范围内随机，保证最低退避时间
            Assert.InRange(plugin.GetBackoffDelay(1), 1500, 3000);   // cap=3000 * 2^0, range=[1500,3000]
            Assert.InRange(plugin.GetBackoffDelay(2), 3000, 6000);   // cap=3000 * 2^1, range=[3000,6000]
            Assert.InRange(plugin.GetBackoffDelay(3), 6000, 12000);  // cap=3000 * 2^2, range=[6000,12000]
            Assert.InRange(plugin.GetBackoffDelay(4), 12000, 24000); // cap=3000 * 2^3, range=[12000,24000]
        }

        [Fact]
        public async Task CalculateBackoffDelay_CapsAtMax()
        {
            var plugin = new TestOutputPlugin("test");
            var delay = plugin.GetBackoffDelay(20); // 远超上限，cap=60000
            // Full Jitter 使返回值在 [0, 60000] 范围内随机
            Assert.InRange(delay, 0, 60000);
        }

        [Fact]
        public async Task NameAndProtocolType_AreSetFromConfig()
        {
            var plugin = new TestOutputPlugin("my-plugin");
            Assert.Equal("my-plugin", plugin.Name);
            Assert.Equal("Test", plugin.ProtocolType);
        }

        [Fact]
        public async Task Dispose_DoesNotCallStop_AvoidsDeadlock()
        {
            // Dispose 不再同步调用 StopAsync().Wait()，避免 async-over-sync 死锁。
            // 调用方应使用 StopAsync() 显式停止。
            var plugin = new TestOutputPlugin("test");
            await plugin.StartAsync();
            plugin.Dispose();

            // Dispose 不保证停止，状态可能仍为 Running
            Assert.NotEqual(PluginStatus.Stopped, plugin.Status);

            // 正确做法：显式调用 StopAsync
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task Dispose_NoOpIfAlreadyStopped()
        {
            var plugin = new TestOutputPlugin("test");
            await plugin.StartAsync();
            await plugin.StopAsync();

            Assert.Equal(1, plugin.StopCallCount);
            plugin.Dispose();
            Assert.Equal(1, plugin.StopCallCount); // Dispose 不重复调用 Stop
        }

        #region Helper: 捕获事件

        private static async Task<OutputPluginStatusArgs> CaptureNextDetailedStatusAsync(TestOutputPlugin plugin, Func<Task> action)
        {
            OutputPluginStatusArgs captured = null;
            plugin.DetailedStatusChanged += args => captured = args;
            await action();
            return captured;
        }

        #endregion

        #region Test Plugin

        private class TestOutputPlugin : OutputPluginBase
        {
            private readonly string _name;
            private readonly Exception _startException;
            private readonly Exception _sendException;
            private readonly Exception _stopException;
            private readonly bool _delayStart;

            public TestOutputPlugin(string name, Exception startException = null, Exception sendException = null,
                Exception stopException = null, bool delayStart = false)
            {
                _name = name;
                _startException = startException;
                _sendException = sendException;
                _stopException = stopException;
                _delayStart = delayStart;
            }

            public override string Name => _name;
            public override string ProtocolType => "Test";

            public bool StartCalled { get; private set; }
            public int StartCallCount { get; private set; }
            public bool StopCalled { get; private set; }
            public int StopCallCount { get; private set; }
            public int SendCallCount { get; private set; }
            public Message LastSentMessage { get; private set; }
            public Exception LastException { get => base.LastException; private set => _lastException = value; }
            private Exception _lastException;

            // 用于测试 Starting 状态下的重复调用
            public TaskCompletionSource CompleteStart { get; } = new TaskCompletionSource();

            // 测试用：暴露 protected CalculateBackoffDelay
            public int GetBackoffDelay(int consecutiveFailures) => CalculateBackoffDelay(consecutiveFailures);

            protected override async Task OnStartAsync(CancellationToken ct)
            {
                StartCalled = true;
                StartCallCount++;

                if (_delayStart)
                    await CompleteStart.Task.WaitAsync(ct);

                if (_startException != null)
                    throw _startException;
            }

            protected override Task OnSendAsync(Message message, CancellationToken cancellationToken)
            {
                SendCallCount++;
                LastSentMessage = message;
                LastException = _sendException;

                if (_sendException != null)
                    throw _sendException;

                return Task.CompletedTask;
            }

            protected override Task OnStopAsync()
            {
                StopCalled = true;
                StopCallCount++;

                if (_stopException != null)
                    throw _stopException;

                return Task.CompletedTask;
            }
        }

        #endregion
    }
}
