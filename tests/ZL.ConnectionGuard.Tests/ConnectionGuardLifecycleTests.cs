using ZL.ConnectionGuard;
using ZL.ConnectionGuard.Models;
using Guard = ZL.ConnectionGuard.ConnectionGuard;

namespace ZL.ConnectionGuard.Tests;

/// <summary>
/// <see cref="Guard"/> 的**生命周期确定性**门禁。
///
/// <para>该类的自述设计目标是「确定性停机、线程安全、异常隔离、可观测性」。
/// 本类把前两条变成可断言的事实：</para>
/// <list type="number">
///   <item><b>停机确定</b>：<c>StopAsync()</c> 返回时，通道必须**已经关闭**——
///     否则调用方（重启、释放资源、断言）拿到的是一个「看起来停了、实际还占着」的对象；</item>
///   <item><b>关闭不并发</b>：看门狗/发送失败触发的重连不得**并发**调用 <c>CloseAsync</c>——
///     适配器接口没有可重入约定，并发关闭可能让底层句柄二次释放或状态错乱。</item>
/// </list>
/// </summary>
public class ConnectionGuardLifecycleTests
{
    [Fact(Timeout = 30_000)]
    public async Task 停机确定_StopAsync返回时通道必须已关闭()
    {
        var adapter = new FakeChannelAdapter { CloseDelayMs = 300 };
        using var guard = new Guard(adapter, new ConnectionGuardOptions
        {
            MaintenanceLoopDelayMs = 20,
            DeviceDeadTimeoutMs = 0,        // 关掉看门狗，本用例只看停机
            HeartbeatIntervalMs = 600_000,  // 关掉心跳
        });

        guard.Start();
        await WaitUntilAsync(() => adapter.IsConnected, 3000);

        await guard.StopAsync();

        // ⚠️ 关键断言：StopAsync 返回的**那一刻**就必须已经关闭
        Assert.True(adapter.CloseCount > 0 && !adapter.IsConnected,
            $"StopAsync 返回时通道必须已关闭（确定性停机）；实际 CloseCount={adapter.CloseCount}、IsConnected={adapter.IsConnected}");
    }

    [Fact(Timeout = 30_000)]
    public async Task 看门狗重连_不得并发关闭通道()
    {
        var adapter = new FakeChannelAdapter { CloseDelayMs = 250 };
        using var guard = new Guard(adapter, new ConnectionGuardOptions
        {
            MaintenanceLoopDelayMs = 10,
            DeviceDeadTimeoutMs = 50,        // 很快判死，逼出重连
            WatchdogWarmupMs = 0,
            WatchdogRequiresSend = false,
            ReconnectMinDelayMs = 10,
            ReconnectMaxDelayMs = 20,
            HeartbeatIntervalMs = 600_000,
        });

        guard.Start();
        await Task.Delay(1200);              // 让看门狗触发多轮

        Assert.True(adapter.ConcurrentClosePeak <= 1,
            $"CloseAsync 被并发调用（峰值 {adapter.ConcurrentClosePeak}，总次数 {adapter.CloseCount}）——"
            + "适配器接口没有可重入约定，重连必须串行化关闭");
    }

    [Fact(Timeout = 30_000)]
    public async Task 连接后可发送且数据事件可达()
    {
        var adapter = new FakeChannelAdapter();
        using var guard = new Guard(adapter, new ConnectionGuardOptions
        {
            MaintenanceLoopDelayMs = 20,
            DeviceDeadTimeoutMs = 0,
            HeartbeatIntervalMs = 600_000,
        });

        var received = new List<byte[]>();
        guard.OnDataReceived += data => { lock (received) received.Add(data); };

        guard.Start();
        await WaitUntilAsync(() => adapter.IsConnected, 3000);

        Assert.True(await guard.SendAsync(new byte[] { 0x01, 0x02 }), "连接后发送应成功");
        Assert.Single(adapter.Sent);

        adapter.EmitData(new byte[] { 0x09 });
        await WaitUntilAsync(() => { lock (received) return received.Count > 0; }, 2000);
        lock (received)
        {
            Assert.Equal(new byte[] { 0x09 }, Assert.Single(received));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }
}
