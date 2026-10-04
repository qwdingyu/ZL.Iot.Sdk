namespace ZL.Framing.Tests;

/// <summary>
/// **结构性帧策略**（FixedLength / LengthField）的回归锁。
///
/// <para>与 Timeout 路径不同，结构性策略**不依赖空闲定时器**：数据够了就立即成帧，
/// 因此没有「早到丢帧」那类竞态（消费方实测 500/500 零丢失、0ms 即时成帧）。
/// 但这两条路径此前同样**零测试覆盖**——本类把它们的成帧语义钉住。</para>
/// </summary>
public class FrameAssemblerStructuralTests
{
    private static List<byte[]> Collect(Action<FrameAssembler> feed, ByteFramingOptions options, out List<FrameAssembleMode> modes)
    {
        var frames = new List<byte[]>();
        var modeList = new List<FrameAssembleMode>();
        using var assembler = new FrameAssembler(options, timeoutMs: 1000, (data, mode) =>
        {
            frames.Add(data);
            modeList.Add(mode);
        });
        feed(assembler);
        modes = modeList;
        return frames;
    }

    [Fact]
    public void 定长策略_一次投喂多帧_按长度切分且即时成帧()
    {
        var options = new ByteFramingOptions { Strategy = "FixedLength", FixedLength = 4 };

        var frames = Collect(a => a.Append(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), options, out var modes);

        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, frames[0]);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, frames[1]);
        Assert.All(modes, m => Assert.Equal(FrameAssembleMode.Decoded, m));
    }

    [Fact]
    public void 定长策略_分片到达_补齐后才成帧()
    {
        var options = new ByteFramingOptions { Strategy = "FixedLength", FixedLength = 4 };
        var frames = new List<byte[]>();

        using var assembler = new FrameAssembler(options, timeoutMs: 1000, (data, _) => frames.Add(data));
        assembler.Append(new byte[] { 1, 2 });
        Assert.Empty(frames);                       // 不足一帧：不产出
        assembler.Append(new byte[] { 3, 4 });
        Assert.Single(frames);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, frames[0]);
    }

    [Fact]
    public void 定长策略_带同步字_前导垃圾按恢复切掉后再成帧()
    {
        var options = new ByteFramingOptions
        {
            Strategy = "FixedLength",
            FixedLength = 4,
            SyncBytes = "AA55",
            ResyncPolicy = "DropOneByte",
        };

        var frames = Collect(a => a.Append(new byte[] { 0x00, 0xAA, 0x55, 0x01, 0x02, 0x03, 0x04 }), options, out var modes);

        // 约定：Recovery 会产出一个**空帧**占位（调用方按 mode 区分），随后才是真帧
        Assert.Contains(FrameAssembleMode.Recovery, modes);
        var realFrames = frames.Where(f => f.Length > 0).ToList();
        Assert.Single(realFrames);
        Assert.Equal(new byte[] { 0xAA, 0x55, 0x01, 0x02 }, realFrames[0]);
        // ⚠️ 不能断言「最后一次是 Decoded」：读完帧后缓冲区还剩尾部垃圾（[03 04]），
        // 同步字解码器会继续把无同步字的残留按 Recovery 丢掉——这是**正确**行为。
        Assert.Contains(FrameAssembleMode.Decoded, modes);
    }

    [Fact]
    public void 长度字段策略_含头长度_按长度成帧()
    {
        var options = new ByteFramingOptions
        {
            Strategy = "LengthField",
            LengthFieldOffset = 0,
            LengthFieldSize = 2,
            LengthFieldEndian = "Big",
            LengthFieldIncludesHeader = true,
        };

        // [00 05][A B C]：长度字段值 5 含头 ⇒ 整帧 5 字节
        var frames = Collect(a => a.Append(new byte[] { 0x00, 0x05, 0xAA, 0xBB, 0xCC }), options, out _);

        Assert.Single(frames);
        Assert.Equal(new byte[] { 0x00, 0x05, 0xAA, 0xBB, 0xCC }, frames[0]);
    }

    [Fact]
    public void 长度字段策略_不含头长度_总长为字段值加头部()
    {
        var options = new ByteFramingOptions
        {
            Strategy = "LengthField",
            LengthFieldOffset = 0,
            LengthFieldSize = 1,
            LengthFieldIncludesHeader = false,
        };

        // [03][A B C]：字段值 3 为载荷长度，不含头 ⇒ 总长 1 + 3 = 4
        var frames = Collect(a => a.Append(new byte[] { 0x03, 0xAA, 0xBB, 0xCC }), options, out _);

        Assert.Single(frames);
        Assert.Equal(new byte[] { 0x03, 0xAA, 0xBB, 0xCC }, frames[0]);
    }

    [Fact]
    public void 长度字段策略_长度非法_按恢复丢弃而不产出畸形帧()
    {
        var options = new ByteFramingOptions
        {
            Strategy = "LengthField",
            LengthFieldOffset = 0,
            LengthFieldSize = 2,
            LengthFieldIncludesHeader = true,
            MinFrameLength = 3,
            MaxFrameLength = 16,
            ResyncPolicy = "DropOneByte",
        };

        // 长度字段值 0 ⇒ 非法（< MinFrameLength）⇒ 必须走恢复，而不是发一个畸形帧
        var frames = Collect(a => a.Append(new byte[] { 0x00, 0x00, 0x01, 0x02, 0x03, 0x04 }), options, out var modes);

        Assert.Contains(FrameAssembleMode.Recovery, modes);
        // 非空帧只允许是「合法长度」的帧；不得出现长度 1..2 的畸形帧
        Assert.All(frames.Where(f => f.Length > 0), f => Assert.True(f.Length >= 3, $"不应产出畸形短帧（长度 {f.Length}）"));
    }
}
