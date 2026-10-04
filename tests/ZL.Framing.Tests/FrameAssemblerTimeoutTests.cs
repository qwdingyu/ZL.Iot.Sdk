using System.Threading;

namespace ZL.Framing.Tests;

/// <summary>
/// <see cref="FrameAssembler"/> **Timeout 路径**的回归锁 —— 这个库此前没有测试项目，
/// 「定时器早到丢帧」缺陷因此长期存活（消费方只能用旁路绕开）。
///
/// <para><b>缺陷</b>：Timeout 策略下，空闲定时器「早到」（回调时 idle 还差零点几毫秒不到阈值）
/// 时直接 <c>return</c> 而**不重排**，而定时器周期是 <c>Infinite</c>——
/// 缓冲区里的帧**从此永远不会被冲刷**：调用方只看到「无响应」，且无任何日志。</para>
///
/// <para><b>为什么是压测而不是单发</b>：这是时序竞态（定时器粒度/时钟），单发用例不可靠。
/// 实测损失率（1ms 超时）：整块投喂约 1.40%（28/2000）、分片投喂约 13.55%（271/2000）；
/// 修复后两者均 0/2000。本用例按轮数压测，任一轮丢帧即失败。</para>
/// </summary>
public class FrameAssemblerTimeoutTests
{
    private const int TimeoutMs = 1;
    private static readonly byte[] Payload = { 0x11, 0x22, 0x33, 0x44 };

    [Fact]
    public void Timeout策略_定时器早到_整块投喂不得丢帧()
    {
        int dropped = CountDrops(rounds: 100, fragmented: false);
        Assert.True(dropped == 0,
            $"整块投喂丢帧 {dropped}/100：Timeout 路径定时器「早到」后必须**重排剩余时间**再冲刷，"
            + "否则帧永远留在缓冲区（调用方只看到无响应，且无日志）");
    }

    [Fact]
    public void Timeout策略_定时器早到_分片投喂不得丢帧()
    {
        int dropped = CountDrops(rounds: 60, fragmented: true);
        Assert.True(dropped == 0,
            $"分片投喂丢帧 {dropped}/60：分片是真实链路的常态（TCP/串口按块到达），"
            + "该路径的损失率高于整块投喂（实测 13.55% vs 1.40%）");
    }

    [Fact]
    public void Timeout策略_空闲后按Emit冲刷_内容与顺序正确()
    {
        var received = new List<(byte[] Data, FrameAssembleMode Mode)>();
        var options = new ByteFramingOptions { Strategy = "Timeout", TimeoutAction = "Emit" };
        using var assembler = new FrameAssembler(options, TimeoutMs, (data, mode) => { lock (received) received.Add((data, mode)); });

        assembler.Append(Payload);
        WaitUntil(() => { lock (received) return received.Count > 0; }, TimeoutMs * 50);

        lock (received)
        {
            var single = Assert.Single(received);
            Assert.Equal(FrameAssembleMode.Timeout, single.Mode);
            Assert.Equal(Payload, single.Data);
        }
    }

    [Fact]
    public void Timeout策略_TimeoutAction为Clear_空闲后清空且不发帧()
    {
        int emitted = 0;
        var options = new ByteFramingOptions { Strategy = "Timeout", TimeoutAction = "Clear" };
        using var assembler = new FrameAssembler(options, TimeoutMs, (_, _) => Interlocked.Increment(ref emitted));

        assembler.Append(Payload);
        Thread.Sleep(TimeoutMs * 30);

        Assert.Equal(0, emitted);   // Clear：丢弃而不是发帧
    }

    [Fact]
    public void 未配置策略时_超时为0_按块即时投递()
    {
        var received = new List<FrameAssembleMode>();
        var options = new ByteFramingOptions { Strategy = "Timeout" };
        using var assembler = new FrameAssembler(options, timeoutMs: 0, (_, mode) => { lock (received) received.Add(mode); });

        assembler.Append(Payload);

        lock (received)
        {
            Assert.Equal(FrameAssembleMode.Chunk, Assert.Single(received));   // 无超时 ⇒ 不缓冲
        }
    }

    private static int CountDrops(int rounds, bool fragmented)
    {
        int dropped = 0;
        for (int i = 0; i < rounds; i++)
        {
            // 收集**全部** Timeout 帧：分片投喂时定时器可能在两次 Append 之间触发，
            // 合法地拆成多帧——不变式是「一个字节都不能丢」，而不是「必须恰好一帧」。
            var collected = new List<byte[]>();
            var options = new ByteFramingOptions { Strategy = "Timeout" };
            using (var assembler = new FrameAssembler(options, TimeoutMs, (data, mode) =>
            {
                if (mode == FrameAssembleMode.Timeout) { lock (collected) collected.Add(data); }
            }))
            {
                if (fragmented)
                {
                    assembler.Append(Payload[..1]);
                    Thread.Sleep(0);
                    assembler.Append(Payload[1..]);
                }
                else
                {
                    assembler.Append(Payload);
                }

                WaitUntil(() => { lock (collected) return collected.Count > 0; }, TimeoutMs * 50);
                // 给可能的后续帧足够时间（分片投喂时定时器可能拆帧；也可能因定时器早到被推迟）
                if (fragmented) Thread.Sleep(100);
            }

            byte[] flushed;
            int frameCount;
            lock (collected)
            {
                frameCount = collected.Count;
                if (collected.Count == 0) { dropped++; continue; }
                flushed = collected.SelectMany(x => x).ToArray();
            }
            // 关键不变式：拼接后必须与原始载荷**逐字节一致**（不丢、不错序、不重复）
            Assert.True(flushed.SequenceEqual(Payload),
                $"第 {i} 轮丢字节：收到 {frameCount} 帧，拼接后 {flushed.Length} 字节 "
                + $"[{string.Join(",", flushed.Select(b => b.ToString()))}]，期望 {Payload.Length} 字节 "
                + $"[{string.Join(",", Payload.Select(b => b.ToString()))}]");
        }
        return dropped;
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
        }
    }
}
