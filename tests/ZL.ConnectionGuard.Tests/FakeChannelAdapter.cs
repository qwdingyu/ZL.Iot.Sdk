using System.Collections.Concurrent;
using ZL.ConnectionGuard;
using ZL.ConnectionGuard.Models;

namespace ZL.ConnectionGuard.Tests;

/// <summary>
/// 记录开/关行为的假适配器：用来把「停机是否确定」「关闭是否被并发调用」变成可断言的事实。
/// </summary>
internal sealed class FakeChannelAdapter : IChannelAdapter
{
    private int _closing;

    public string ChannelId => "fake:0";
    public bool IsConnected { get; private set; }
    public bool FailOpen { get; set; }

    /// <summary>CloseAsync 的人为耗时——用来放大「关闭在飞时状态仍是已连接」的窗口。</summary>
    public int CloseDelayMs { get; set; }

    /// <summary>CloseAsync 被调用的总次数。</summary>
    public int CloseCount;

    /// <summary>同时处于 CloseAsync 内的峰值（&gt;1 表示被并发调用；适配器不保证可重入）。</summary>
    public int ConcurrentClosePeak;

    /// <summary>OpenAsync 被调用的总次数。</summary>
    public int OpenCount;

    public ConcurrentQueue<byte[]> Sent { get; } = new();

    public event Action<byte[]>? OnDataReceived;

    public Task OpenAsync(CancellationToken token)
    {
        Interlocked.Increment(ref OpenCount);
        if (FailOpen) throw new IOException("fake open failure");
        IsConnected = true;
        return Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        int now = Interlocked.Increment(ref _closing);
        InterlockedMax(ref ConcurrentClosePeak, now);
        try
        {
            if (CloseDelayMs > 0) await Task.Delay(CloseDelayMs).ConfigureAwait(false);
            IsConnected = false;
        }
        finally
        {
            Interlocked.Decrement(ref _closing);
            Interlocked.Increment(ref CloseCount);
        }
    }

    public Task SendAsync(byte[] data, CancellationToken token)
    {
        Sent.Enqueue(data);
        return Task.CompletedTask;
    }

    /// <summary>模拟设备主动上报（触发 OnDataReceived）。</summary>
    public void EmitData(byte[] data) => OnDataReceived?.Invoke(data);

    public void Dispose() => IsConnected = false;

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            Interlocked.CompareExchange(ref target, value, current);
        }
    }
}
