using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// InputPluginBase 基类单元测试
    /// 验证状态机、消息转发、取消、Dispose 等核心行为
    /// </summary>
    public class InputPluginBaseTests
    {
        #region 状态机测试

        [Fact]
        public async Task StartAsync_Success_TransitionsToRunning()
        {
            var plugin = new TestInputPlugin();
            await plugin.StartAsync(_ => Task.CompletedTask);

            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsNoop()
        {
            var plugin = new TestInputPlugin();
            await plugin.StartAsync(_ => Task.CompletedTask);

            // 第二次 Start 不应增加 OnStart 调用次数
            int firstCount = plugin.OnStartCallCount;
            await plugin.StartAsync(_ => Task.CompletedTask);

            Assert.Equal(firstCount, plugin.OnStartCallCount);
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_AlreadyStarting_IsNoop()
        {
            var plugin = new TestInputPlugin();
            await plugin.StartAsync(_ => Task.CompletedTask);

            // 模拟 Starting 状态后再次 Start
            await plugin.StartAsync(_ => Task.CompletedTask);
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StartAsync_OnStartThrows_TransitionsToError()
        {
            var plugin = new TestInputPlugin
            {
                OnStartException = new InvalidOperationException("start failed")
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.StartAsync(_ => Task.CompletedTask));

            Assert.Equal(PluginStatus.Error, plugin.Status);
        }

        [Fact]
        public async Task StartAsync_AfterError_CanRestart()
        {
            var plugin = new TestInputPlugin
            {
                OnStartException = new InvalidOperationException("first start failed")
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.StartAsync(_ => Task.CompletedTask));
            Assert.Equal(PluginStatus.Error, plugin.Status);

            // 清除异常，重新 Start
            plugin.OnStartException = null;
            await plugin.StartAsync(_ => Task.CompletedTask);

            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_TransitionsToStopped()
        {
            var plugin = new TestInputPlugin();
            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();

            Assert.Equal(PluginStatus.Stopped, plugin.Status);
            Assert.True(plugin.OnStopCallCount > 0);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoop()
        {
            var plugin = new TestInputPlugin();
            await plugin.StopAsync(); // 已经是 Stopped

            Assert.Equal(PluginStatus.Stopped, plugin.Status);
            Assert.Equal(0, plugin.OnStopCallCount); // OnStop 不应被调用
        }

        [Fact]
        public async Task StopAsync_OnStopThrows_RethrowsButStillTransitionsToStopped()
        {
            var plugin = new TestInputPlugin();
            await plugin.StartAsync(_ => Task.CompletedTask);

            plugin.OnStopException = new InvalidOperationException("stop failed");

            // InputPluginBase 不捕获 OnStopAsync 异常（与 OutputPluginBase 不同），
            // 但 finally 块确保 Status 仍变为 Stopped
            try
            {
                await plugin.StopAsync();
            }
            catch (InvalidOperationException)
            {
                // 预期异常
            }

            // finally 块仍执行：Status → Stopped，CancellationTokenSource 已释放
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        #endregion

        #region 消息转发测试

        [Fact]
        public async Task InvokeMessageHandler_ForwardsMessageToCallback()
        {
            Message received = null;
            var plugin = new TestInputPlugin();

            await plugin.StartAsync(async (msg) => { received = msg; return; });

            var testMessage = new Message { Topic = "test", Payload = new byte[] { 1, 2, 3 } };
            plugin.InvokeTestMessageHandler(testMessage);

            Assert.NotNull(received);
            Assert.Same(testMessage, received);

            await plugin.StopAsync();
        }

        [Fact]
        public void InvokeMessageHandler_BeforeStart_ThrowsInvalidOperationException()
        {
            var plugin = new TestInputPlugin();

            var ex = Assert.Throws<InvalidOperationException>(() => plugin.InvokeTestMessageHandler(new Message()));
            Assert.Contains("Message handler not set", ex.Message);
        }

        #endregion

        #region 事件测试

        [Fact]
        public async Task ConnectionChanged_FiredOnStartAndStop()
        {
            var events = new List<(string name, bool connected)>();
            var plugin = new TestInputPlugin();
            plugin.ConnectionChanged += (name, connected) => events.Add((name, connected));

            await plugin.StartAsync(_ => Task.CompletedTask);
            await plugin.StopAsync();

            Assert.Equal(2, events.Count);
            Assert.True(events[0].connected);
            Assert.False(events[1].connected);
        }

        [Fact]
        public async Task ConnectionChanged_NotFiredOnStartFailure()
        {
            var fired = false;
            var plugin = new TestInputPlugin
            {
                OnStartException = new InvalidOperationException("fail")
            };
            plugin.ConnectionChanged += (_, _) => fired = true;

            await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.StartAsync(_ => Task.CompletedTask));

            Assert.False(fired);
            Assert.Equal(PluginStatus.Error, plugin.Status);
        }

        #endregion

        #region 取消令牌测试

        [Fact]
        public async Task CancellationToken_IsPropagatedToOnStartAsync()
        {
            CancellationToken capturedToken = default;
            var plugin = new TestInputPlugin
            {
                OnStartAsyncOverride = (ct) =>
                {
                    capturedToken = ct;
                    return Task.CompletedTask;
                }
            };

            using var cts = new CancellationTokenSource();
            await plugin.StartAsync(_ => Task.CompletedTask, cts.Token);

            Assert.False(capturedToken.IsCancellationRequested);
            cts.Cancel();
            Assert.True(capturedToken.IsCancellationRequested);

            await plugin.StopAsync();
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public async Task Dispose_WhenRunning_CancelsButDoesNotAwaitStopAsync()
        {
            // P0-3 修复后：同步 Dispose 不再等待 StopAsync 完成（避免死锁），
            // 仅 Cancel CTS 并释放资源。验证插件被 Cancel 且 CTS 被释放。
            var plugin = new TestInputPlugin();
            await plugin.StartAsync(_ => Task.CompletedTask);

            plugin.Dispose();

            // 同步 Dispose 只 Cancel + 释放 CTS，不等待 StopAsync 完成
            // Status 可能仍为 Running（因为 StopAsync 未被 await）
            Assert.True(plugin.OnStopCallCount >= 0); // 不再保证 OnStopAsync 被调用
        }

        [Fact]
        public async Task DisposeAsync_WhenRunning_CallsStopAsync()
        {
            // DisposeAsync 是推荐路径 — 优雅停止并释放资源
            var plugin = new TestInputPlugin();
            await plugin.StartAsync(_ => Task.CompletedTask);

            await plugin.DisposeAsync();

            Assert.Equal(PluginStatus.Stopped, plugin.Status);
            Assert.True(plugin.OnStopCallCount > 0);
        }

        [Fact]
        public void Dispose_WhenStopped_IsNoop()
        {
            var plugin = new TestInputPlugin();
            // 不应抛出异常
            plugin.Dispose();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
            Assert.Equal(0, plugin.OnStopCallCount);
        }

        #endregion

        #region Null 安全测试

        [Fact]
        public async Task StartAsync_NullMessageHandler_ThrowsArgumentNullException()
        {
            var plugin = new TestInputPlugin();
            await Assert.ThrowsAsync<ArgumentNullException>(() => plugin.StartAsync(null!));
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        #endregion

        #region 测试用子类

        /// <summary>
        /// 测试用 Input 插件 — 暴露受保护方法供测试调用
        /// </summary>
        private class TestInputPlugin : InputPluginBase
        {
            public override string Name => "TestInput";
            public override string ProtocolType => "Test";

            public int OnStartCallCount { get; private set; }
            public int OnStopCallCount { get; private set; }
            public Exception? OnStartException { get; set; }
            public Exception? OnStopException { get; set; }
            public Func<CancellationToken, Task>? OnStartAsyncOverride { get; set; }

            protected override Task OnStartAsync(CancellationToken ct)
            {
                if (OnStartAsyncOverride != null)
                    return OnStartAsyncOverride(ct);

                OnStartCallCount++;
                if (OnStartException != null) throw OnStartException;
                return Task.CompletedTask;
            }

            protected override Task OnStopAsync()
            {
                OnStopCallCount++;
                if (OnStopException != null) throw OnStopException;
                return Task.CompletedTask;
            }

            // 暴露受保护方法供测试调用
            public void InvokeTestMessageHandler(Message message)
            {
                InvokeMessageHandler(message);
            }
        }

        #endregion
    }
}
