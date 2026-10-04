using ZL.Framing;

namespace ZL.Probing.Tests;

/// <summary>
/// FrameAssembler「Timeout 策略定时器早到丢帧」回归锁（2026-10-04 修复）。
/// 缺陷：定时器「早到」（触发时 idle 还差零点几毫秒不到阈值）时直接 return 而**不重排**，
/// 而本类定时器周期是 Infinite——缓冲区里的帧从此永远不会被冲刷：调用方只看到「无响应」，
/// 且无任何日志。修复 = 早到时重排剩余时间后再冲刷。
/// 该缺陷是时序竞态，单发用例不可靠，这里按小超时多轮循环压测：
/// 修复后任何一轮都不允许丢帧；修复前 1ms 超时下实测约 8%/轮，50 轮漏检概率 &lt;1%。
/// </summary>
public class FrameAssemblerTimeoutFlushTests
{
    private const int Rounds = 50;
    private const int TimeoutMs = 1;
    private static readonly byte[] Payload = { 0x01, 0x02, 0x03 };

    [Fact]
    public void TimeoutStrategy_EarlyTimerFire_StillFlushesEveryRound()
    {
        int dropped = 0;
        for (int i = 0; i < Rounds; i++)
        {
            byte[]? flushed = null;
            var options = new ByteFramingOptions { Strategy = "Timeout" };
            var assembler = new FrameAssembler(options, TimeoutMs, (data, mode) =>
            {
                if (mode == FrameAssembleMode.Timeout)
                {
                    flushed = data;
                }
            });

            assembler.Append(Payload);

            // 单轮正常 1~2ms 内就该冲刷，循环在拿到帧的瞬间退出；这里的上限只兜两个场景：
            // ① 代码回退成「早到不重排」→ 该轮永远等不到，5s 后计一次丢帧（50 轮最坏 250s，可接受）；
            // ② xUnit 并行跑其它用例时线程池繁忙，回调被推迟——修好过的代码这只是**晚到**不是丢，
            //    上限必须给得足够宽，否则会制造假失败（首轮 40ms 上限就因此误报过一次）。
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (flushed == null && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(1);
            }

            assembler.Dispose();

            if (flushed == null)
            {
                dropped++;
                continue;
            }

            Assert.Equal(Payload, flushed);
        }

        Assert.Equal(0, dropped);
    }
}
