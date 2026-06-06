#nullable enable
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins
{
    /// <summary>
    /// OutputPluginBase 基类深度测试 — 状态机、背压延迟、异常传播、事件通知
    /// </summary>
    public class OutputPluginBaseDeepTests
    {
        #region 状态机测试

        [Fact]
        public async Task StartAsync_Success_TransitionsToRunning()
        {
            var plugin = new TestOutputPlugin();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);

            await plugin.StartAsync();

            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsNoop()
        {
            var plugin = new TestOutputPlugin();
            await plugin.StartAsync();

            var before = plugin.StartCallCount;
            await plugin.StartAsync(); // 第二次调用

            Assert.Equal(before, plugin.StartCallCount); // 没有再次执行 OnStart
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_OnStartThrows_TransitionsToError()
        {
            var plugin = new TestOutputPlugin { StartException = new InvalidOperationException("start fail") };

            await plugin.StartAsync();

            Assert.Equal(PluginStatus.Fatal, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_Success_TransitionsToStopped()
        {
            var plugin = new TestOutputPlugin();
            await plugin.StartAsync();

            await plugin.StopAsync();

            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoop()
        {
            var plugin = new TestOutputPlugin();
            var before = plugin.StopCallCount;

            await plugin.StopAsync(); // 已经是 Stopped

            Assert.Equal(before, plugin.StopCallCount);
        }

        [Fact]
        public async Task StopAsync_OnStopThrows_StillTransitionsToStopped()
        {
            var plugin = new TestOutputPlugin { StopException = new InvalidOperationException("stop fail") };
            await plugin.StartAsync();

            await plugin.StopAsync();

            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        #endregion

        #region Send 异常传播

        [Fact]
        public async Task SendAsync_NotRunning_ThrowsInvalidOperationException()
        {
            var plugin = new TestOutputPlugin();
            // 不调用 StartAsync，直接 Send 应抛异常

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(new Message { Topic = "test" }));
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Equal(0, plugin.SendCallCount);
        }

        [Fact]
        public async Task SendAsync_OnSendThrows_PropagatesException()
        {
            var plugin = new TestOutputPlugin { SendException = new SocketException() };
            await plugin.StartAsync();

            await Assert.ThrowsAsync<SocketException>(() =>
                plugin.SendAsync(new Message { Topic = "test" }));

            Assert.Equal(1, plugin.SendCallCount);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_Success_IncrementsCallCount()
        {
            var plugin = new TestOutputPlugin();
            await plugin.StartAsync();

            await plugin.SendAsync(new Message { Topic = "test" });
            await plugin.SendAsync(new Message { Topic = "test2" });

            Assert.Equal(2, plugin.SendCallCount);
            await plugin.StopAsync();
        }

        #endregion

        #region 指数退避延迟计算

        [Fact]
        public void CalculateBackoffDelay_ZeroFailures_ReturnsBaseInterval()
        {
            var plugin = new TestOutputPlugin(); // BaseReconnectIntervalMs = 3000

            var delay = plugin.GetBackoffDelay(0);

            Assert.Equal(3000, delay);
        }

        [Fact]
        public void CalculateBackoffDelay_NegativeFailures_ReturnsBaseInterval()
        {
            var plugin = new TestOutputPlugin();

            var delay = plugin.GetBackoffDelay(-1);

            Assert.Equal(3000, delay);
        }

        [Fact]
        public void CalculateBackoffDelay_ExponentialGrowth_DoublesEachStep()
        {
            var plugin = new TestOutputPlugin(); // base=3000, multiplier=2.0
            // Equal Jitter 使返回值在 [capped/2, capped] 范围内随机，保证最低退避时间

            Assert.InRange(plugin.GetBackoffDelay(1), 1500, 3000);   // cap=3000 * 2^0, range=[1500,3000]
            Assert.InRange(plugin.GetBackoffDelay(2), 3000, 6000);   // cap=3000 * 2^1, range=[3000,6000]
            Assert.InRange(plugin.GetBackoffDelay(3), 6000, 12000);  // cap=3000 * 2^2, range=[6000,12000]
            Assert.InRange(plugin.GetBackoffDelay(4), 12000, 24000); // cap=3000 * 2^3, range=[12000,24000]
        }

        [Fact]
        public void CalculateBackoffDelay_CapsAtMaxInterval()
        {
            var plugin = new TestOutputPlugin(); // MaxReconnectIntervalMs = 60000

            var delay = plugin.GetBackoffDelay(20); // 3000 * 2^10 = 3072000, 但 cap 到 60000
            // Equal Jitter 使返回值在 [30000, 60000] 范围内随机

            Assert.InRange(delay, 30000, 60000);
        }

        [Fact]
        public void CalculateBackoffDelay_CustomIntervals_AppliesCorrectly()
        {
            // base=1000, max=10000, multiplier=3.0
            var plugin = new CustomBackoffPlugin();
            // Equal Jitter 使返回值在 [capped/2, capped] 范围内随机

            Assert.InRange(plugin.GetBackoffDelay(1), 500, 1000);  // cap=1000 * 3^0, range=[500,1000]
            Assert.InRange(plugin.GetBackoffDelay(2), 1500, 3000);  // cap=1000 * 3^1, range=[1500,3000]
            Assert.InRange(plugin.GetBackoffDelay(3), 4500, 9000);  // cap=1000 * 3^2, range=[4500,9000]
            Assert.InRange(plugin.GetBackoffDelay(4), 5000, 10000); // cap=1000 * 3^3 = 27000 → cap 到 10000, range=[5000,10000]
        }

        #endregion

        #region 事件通知

        [Fact]
        public async Task StartAsync_ConnectionChangedFired_CapturesEvent()
        {
            var plugin = new TestOutputPlugin();
            bool connectionFired = false;
            string? firedName = null;
            bool firedConnected = false;

            plugin.ConnectionChanged += (name, connected) =>
            {
                connectionFired = true;
                firedName = name;
                firedConnected = connected;
            };

            await plugin.StartAsync();

            Assert.True(connectionFired);
            Assert.Equal("TestPlugin", firedName);
            Assert.True(firedConnected);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task DetailedStatusChanged_CapturesHealthLevel()
        {
            var plugin = new TestOutputPlugin();
            OutputPluginHealthLevel capturedLevel = 0;
            string? capturedMessage = null;
            string? capturedErrorCode = null;

            plugin.DetailedStatusChanged += args =>
            {
                capturedLevel = args.HealthLevel;
                capturedMessage = args.Message;
                capturedErrorCode = args.ErrorCode;
            };

            await plugin.StartAsync();

            Assert.Equal(OutputPluginHealthLevel.Healthy, capturedLevel);
            Assert.Equal("TestPlugin started", capturedMessage);
            Assert.Equal(GatewayErrorCodes.None, capturedErrorCode);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task DetailedStatusChanged_OnError_IncludesExceptionInfo()
        {
            var plugin = new TestOutputPlugin { SendException = new SocketException() };
            OutputPluginHealthLevel capturedLevel = 0;
            Exception? capturedException = null;
            bool sawError = false;

            plugin.DetailedStatusChanged += args =>
            {
                capturedLevel = args.HealthLevel;
                capturedException = args.LastException;
                if (args.HealthLevel == OutputPluginHealthLevel.Error)
                {
                    sawError = true;
                }
            };

            await plugin.StartAsync();

            // SendAsync 会重新抛出异常，但事件在 throw 之前已触发
            try
            {
                await plugin.SendAsync(new Message { Topic = "test" });
            }
            catch (SocketException)
            {
                // 预期异常
            }

            // StopAsync 会再次触发 DetailedStatusChanged（Healthy），覆盖 capturedLevel
            // 因此用 sawError 确认 Error 事件确实被触发过
            Assert.True(sawError, "DetailedStatusChanged should have fired with Error level during send failure");
            Assert.IsType<SocketException>(capturedException);

            await plugin.StopAsync();
        }

        #endregion

        #region 测试辅助类

        private class TestOutputPlugin : OutputPluginBase
        {
            public override string Name => "TestPlugin";
            public override string ProtocolType => "Test";

            public int StartCallCount { get; private set; }
            public int SendCallCount { get; private set; }
            public int StopCallCount { get; private set; }

            public Exception? StartException { get; set; }
            public Exception? SendException { get; set; }
            public Exception? StopException { get; set; }

            protected override Task OnStartAsync(CancellationToken ct)
            {
                StartCallCount++;
                if (StartException != null) throw StartException;
                return Task.CompletedTask;
            }

            protected override Task OnSendAsync(Message message, CancellationToken cancellationToken)
            {
                SendCallCount++;
                if (SendException != null) throw SendException;
                return Task.CompletedTask;
            }

            protected override Task OnStopAsync()
            {
                StopCallCount++;
                if (StopException != null) throw StopException;
                return Task.CompletedTask;
            }

            // 暴露受保护方法供测试
            public int GetBackoffDelay(int failureStreak) => CalculateBackoffDelay(failureStreak);
        }

        private class CustomBackoffPlugin : TestOutputPlugin
        {
            protected override int BaseReconnectIntervalMs => 1000;
            protected override int MaxReconnectIntervalMs => 10000;
            protected override double BackoffMultiplier => 3.0;
        }

        #endregion
    }
}
#nullable restore
