// ============================================================
// 文件：IndustrialOutputPluginBaseTests.cs
// 描述：IndustrialOutputPluginBase 基类核心行为测试
//       连接循环、取消传播、失败追踪、指数退避
// 修改日期：2026-06-03
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class IndustrialOutputPluginBaseTests
    {
        #region 连接失败追踪

        [Fact]
        public async Task ConnectFailureStreak_IncrementsOnConnectionFailure()
        {
            var plugin = new FailingIndustrialPlugin(connectAttemptsBeforeSuccess: -1); // 永远失败
            var statusEvents = new List<OutputPluginStatusArgs>();
            plugin.DetailedStatusChanged += statusEvents.Add;

            await plugin.StartAsync();

            // 等待几次失败
            await Task.Delay(300);

            int streak = plugin.ConnectFailureStreak;
            Assert.True(streak > 0, $"Expected failure streak > 0, got {streak}");

            await plugin.StopAsync();
        }

        [Fact]
        public async Task ConnectFailureStreak_ResetsAfterSuccessfulConnection()
        {
            var plugin = new SucceedingIndustrialPlugin();
            await plugin.StartAsync();
            await Task.Delay(200); // 让连接成功

            Assert.Equal(0, plugin.ConnectFailureStreak);

            await plugin.StopAsync();
        }

        [Fact]
        public void ResetConnectFailureStreak_SetsToZero()
        {
            var plugin = new FailingIndustrialPlugin(connectAttemptsBeforeSuccess: -1);
            plugin.IncrementConnectFailureStreakForTest();
            plugin.IncrementConnectFailureStreakForTest();
            Assert.Equal(2, plugin.ConnectFailureStreak);

            plugin.ResetConnectFailureStreakForTest();
            Assert.Equal(0, plugin.ConnectFailureStreak);
        }

        [Fact]
        public void IncrementConnectFailureStreak_ReturnsNewValue()
        {
            var plugin = new FailingIndustrialPlugin(connectAttemptsBeforeSuccess: -1);
            Assert.Equal(0, plugin.ConnectFailureStreak);

            int val1 = plugin.IncrementConnectFailureStreakForTest();
            Assert.Equal(1, val1);

            int val2 = plugin.IncrementConnectFailureStreakForTest();
            Assert.Equal(2, val2);
        }

        #endregion

        #region 取消传播 — StopAsync 正确取消连接循环

        [Fact]
        public async Task StopAsync_CancelsConnectionLoop_Quickly()
        {
            var plugin = new BlockingIndustrialPlugin(); // TryConnectAsync 会阻塞直到取消
            await plugin.StartAsync();

            // 等待连接循环实际进入 TryConnectAsync
            await plugin.WaitForConnectAttemptAsync();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await plugin.StopAsync();
            stopwatch.Stop();

            // OnStopAsync 等待连接循环退出最多 10s，但取消应立即生效
            Assert.True(stopwatch.ElapsedMilliseconds < 5000,
                $"Stop took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
            Assert.True(plugin.WasCancelled, "CancellationToken should have been triggered");
        }

        [Fact]
        public async Task StopAsync_CancelsCtsToken()
        {
            var plugin = new BlockingIndustrialPlugin();
            await plugin.StartAsync();
            await plugin.WaitForConnectAttemptAsync();

            var tokenBeforeStop = plugin.CtsToken;
            Assert.NotNull(tokenBeforeStop);
            Assert.False(tokenBeforeStop.Value.IsCancellationRequested);

            await plugin.StopAsync();

            Assert.True(tokenBeforeStop.Value.IsCancellationRequested, "CtsToken should be cancelled after stop");
        }

        #endregion

        #region 指数退避

        [Fact]
        public async Task ConnectionLoop_UseExponentialBackoff()
        {
            var plugin = new TimedFailingIndustrialPlugin();
            await plugin.StartAsync();

            // 等待几次重连尝试
            await Task.Delay(800);

            await plugin.StopAsync();

            var delays = plugin.RecordedDelays;
            Assert.True(delays.Count >= 2, $"Expected at least 2 delay records, got {delays.Count}");

            // 验证退避递增：第二次延迟 >= 第一次
            if (delays.Count >= 2)
            {
                Assert.True(delays[1] >= delays[0],
                    $"Backoff should be non-decreasing: {delays[0]}ms, {delays[1]}ms");
            }
        }

        [Fact]
        public void CalculateBackoffDelay_BaseValues()
        {
            // failureStreak=0 → baseMs
            var d0 = OutputPluginBase.CalculateBackoffDelay(0, 1000, 60000, 2.0);
            Assert.InRange(d0, 500, 1000); // Equal Jitter: [base/2, base]

            // failureStreak=1 → baseMs (no extra multiplier yet)
            var d1 = OutputPluginBase.CalculateBackoffDelay(1, 1000, 60000, 2.0);
            Assert.InRange(d1, 500, 1000);

            // failureStreak=2 → baseMs * 2^1 = 2000
            var d2 = OutputPluginBase.CalculateBackoffDelay(2, 1000, 60000, 2.0);
            Assert.InRange(d2, 1000, 2000);

            // failureStreak=3 → baseMs * 2^2 = 4000
            var d3 = OutputPluginBase.CalculateBackoffDelay(3, 1000, 60000, 2.0);
            Assert.InRange(d3, 2000, 4000);
        }

        [Fact]
        public void CalculateBackoffDelay_CapsAtMax()
        {
            // Very high failure streak should cap at maxMs
            var d = OutputPluginBase.CalculateBackoffDelay(100, 1000, 5000, 2.0);
            Assert.InRange(d, 2500, 5000); // [max/2, max] with Equal Jitter
        }

        #endregion

        #region 状态转换

        [Fact]
        public async Task StartAsync_SetsRunningStatus()
        {
            var plugin = new SucceedingIndustrialPlugin();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);

            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StartAsync_Failure_SetsFatal()
        {
            var plugin = new StartFailingIndustrialPlugin();
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Fatal, plugin.Status);
        }

        [Fact]
        public async Task StopAsync_AlreadyStopped_IsNoop()
        {
            var plugin = new SucceedingIndustrialPlugin();
            // Don't start, just stop
            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        #endregion

        #region 状态事件通知

        [Fact]
        public async Task DetailedStatusChanged_FiresOnConnectionFailure()
        {
            var plugin = new FailingIndustrialPlugin(connectAttemptsBeforeSuccess: -1);
            var statusEvents = new List<OutputPluginStatusArgs>();
            plugin.DetailedStatusChanged += statusEvents.Add;

            await plugin.StartAsync();
            await Task.Delay(400);
            await plugin.StopAsync();

            Assert.NotEmpty(statusEvents);
            Assert.Contains(statusEvents, e => e.HealthLevel is OutputPluginHealthLevel.Warning or OutputPluginHealthLevel.Error);
        }

        [Fact]
        public async Task DetailedStatusChanged_IncludesErrorCode()
        {
            var plugin = new FailingIndustrialPlugin(connectAttemptsBeforeSuccess: -1);
            var statusEvents = new List<OutputPluginStatusArgs>();
            plugin.DetailedStatusChanged += statusEvents.Add;

            await plugin.StartAsync();
            await Task.Delay(400);
            await plugin.StopAsync();

            var errorEvent = statusEvents.FirstOrDefault(e => e.HealthLevel == OutputPluginHealthLevel.Error);
            if (errorEvent != null)
            {
                Assert.NotEqual(GatewayErrorCodes.None, errorEvent.ErrorCode);
            }
        }

        #endregion

        #region 错误分类

        [Fact]
        public async Task ClassifyError_Healthy_ReturnsNone()
        {
            var plugin = new SucceedingIndustrialPlugin();
            await plugin.StartAsync();
            await Task.Delay(100);

            var (code, msg, advice) = plugin.ClassifyErrorForTest(OutputPluginHealthLevel.Healthy, "ok");
            Assert.Equal(GatewayErrorCodes.None, code);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task ClassifyError_Error_ReturnsConnectionFailed()
        {
            var plugin = new SucceedingIndustrialPlugin();
            await plugin.StartAsync();

            var (code, msg, advice) = plugin.ClassifyErrorForTest(OutputPluginHealthLevel.Error, "failed");
            Assert.Equal(GatewayErrorCodes.ConnectionFailed, code);

            await plugin.StopAsync();
        }

        #endregion

        #region CtsToken 传播

        [Fact]
        public async Task CtsToken_IsAvailableAfterStart()
        {
            var plugin = new SucceedingIndustrialPlugin();
            Assert.Null(plugin.CtsToken);

            await plugin.StartAsync();
            Assert.NotNull(plugin.CtsToken);
            Assert.False(plugin.CtsToken.Value.IsCancellationRequested);

            await plugin.StopAsync();
            Assert.True(plugin.CtsToken.Value.IsCancellationRequested, "Token should be cancelled after stop");
        }

        #endregion
    }

    #region 测试用子类

    /// <summary>连接永远失败的插件</summary>
    public class FailingIndustrialPlugin : IndustrialOutputPluginBase
    {
        private readonly int _connectAttemptsBeforeSuccess;

        public FailingIndustrialPlugin(int connectAttemptsBeforeSuccess)
        {
            _connectAttemptsBeforeSuccess = connectAttemptsBeforeSuccess;
        }

        public override string Name => "TestFailing";
        public override string ProtocolType => "Test";

        private int _attemptCount;

        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                _attemptCount++;
                if (_connectAttemptsBeforeSuccess > 0 && _attemptCount > _connectAttemptsBeforeSuccess)
                {
                    await Task.Delay(100, ct);
                    return; // 成功
                }
                await Task.Delay(50, ct);
                throw new InvalidOperationException("Connection refused");
            }
        }

        protected override bool HasLiveConnection() => false;

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        // 暴露测试方法
        public int ConnectFailureStreak => base.ConnectFailureStreak;
        public void ResetConnectFailureStreakForTest() => base.ResetConnectFailureStreak();
        public int IncrementConnectFailureStreakForTest() => base.IncrementConnectFailureStreak();
        public CancellationToken? CtsToken => base.CtsToken;
        public (string, string, string) ClassifyErrorForTest(OutputPluginHealthLevel level, string msg)
            => base.ClassifyError(level, msg);
    }

    /// <summary>连接立即成功的插件</summary>
    public class SucceedingIndustrialPlugin : IndustrialOutputPluginBase
    {
        public override string Name => "TestSucceeding";
        public override string ProtocolType => "Test";

        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            await Task.Delay(50, ct); // 模拟成功连接
        }

        protected override bool HasLiveConnection() => true;

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        public int ConnectFailureStreak => base.ConnectFailureStreak;
        public CancellationToken? CtsToken => base.CtsToken;
        public (string, string, string) ClassifyErrorForTest(OutputPluginHealthLevel level, string msg)
            => base.ClassifyError(level, msg);
    }

    /// <summary>TryConnectAsync 会一直阻塞直到取消的插件（测试取消传播）</summary>
    public class BlockingIndustrialPlugin : IndustrialOutputPluginBase
    {
        public override string Name => "TestBlocking";
        public override string ProtocolType => "Test";
        public bool WasCancelled { get; private set; }
        private TaskCompletionSource<bool> _connectAttempted = new TaskCompletionSource<bool>();

        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            _connectAttempted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        public CancellationToken? CtsToken => base.CtsToken;

        public Task WaitForConnectAttemptAsync() => _connectAttempted.Task;
    }

    /// <summary>启动就失败的插件</summary>
    public class StartFailingIndustrialPlugin : IndustrialOutputPluginBase
    {
        public override string Name => "TestStartFailing";
        public override string ProtocolType => "Test";

        protected override Task OnBeforeConnectionLoopAsync()
            => throw new InvalidOperationException("Start failed");

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>记录重连延迟的失败插件</summary>
    public class TimedFailingIndustrialPlugin : IndustrialOutputPluginBase
    {
        public override string Name => "TestTimedFailing";
        public override string ProtocolType => "Test";
        public List<int> RecordedDelays { get; } = new List<int>();

        private int _attemptCount;

        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            _attemptCount++;
            await Task.Delay(10, ct);
            throw new InvalidOperationException("Connection refused");
        }

        protected override bool HasLiveConnection() => false;

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        protected override int BaseReconnectIntervalMs => 50;  // 快速退避
        protected override int MaxReconnectIntervalMs => 200;
        protected override double BackoffMultiplier => 2.0;

        // 记录每次退避延迟
        protected override string OnFormatFailureMessage(Exception ex, string failureKind, int streak)
        {
            var delay = base.CalculateBackoffDelay(streak);
            RecordedDelays.Add(delay);
            return base.OnFormatFailureMessage(ex, failureKind, streak);
        }
    }

    #endregion
}
