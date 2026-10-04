namespace ZL.Framing.Tests;

/// <summary>
/// <see cref="StickyWindowBuffer"/> 的语义与**边界契约**。
///
/// <para>它是 <see cref="FrameAssembler"/> 的底层缓冲：所有字节流都经过它。
/// 它此前零测试覆盖，而「参数越界」这条路径**没有任何校验**——
/// 负偏移会静默读到**已消费的旧字节**、负长度会让**读指针倒退**（后续读取错乱），
/// 这类错误在断帧逻辑里表现为「偶发错帧」，极难归因。</para>
/// </summary>
public class StickyWindowBufferTests
{
    [Fact]
    public void 基本读写与可读字节数()
    {
        var buffer = new StickyWindowBuffer();
        buffer.Write(new byte[] { 1, 2, 3, 4 });

        Assert.Equal(4, buffer.ReadableBytes);
        Assert.Equal(1, buffer.GetByte(0));
        Assert.Equal(new byte[] { 2, 3 }, buffer.PeekBytes(1, 2));   // Peek 不移动读指针
        Assert.Equal(4, buffer.ReadableBytes);
        Assert.Equal(new byte[] { 1, 2 }, buffer.ReadBytes(2));
        Assert.Equal(2, buffer.ReadableBytes);
    }

    [Fact]
    public void 读指针推进后_写入触发压缩而不是无限增长()
    {
        var buffer = new StickyWindowBuffer(initialCapacity: 8, maxCapacity: 64);
        buffer.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        buffer.ReadBytes(6);

        // 再写 8 字节：剩余 2 + 8 = 10 > 8，必须压缩（而非丢弃已消费空间）
        buffer.Write(new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 });

        Assert.Equal(10, buffer.ReadableBytes);
        Assert.Equal(new byte[] { 7, 8, 9, 10 }, buffer.PeekBytes(0, 4));
    }

    [Fact]
    public void 超过最大容量_必须显式失败而不是静默丢数据()
    {
        var buffer = new StickyWindowBuffer(initialCapacity: 4, maxCapacity: 8);
        buffer.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.Throws<InvalidOperationException>(() => buffer.Write(new byte[] { 9 }));
    }

    [Fact]
    public void 读窗口不足_必须显式失败()
    {
        var buffer = new StickyWindowBuffer();
        buffer.Write(new byte[] { 1, 2 });

        Assert.Throws<IndexOutOfRangeException>(() => buffer.ReadBytes(3));
        Assert.Throws<IndexOutOfRangeException>(() => buffer.PeekBytes(1, 2));
        Assert.Throws<IndexOutOfRangeException>(() => buffer.Skip(3));
    }

    // ── 以下三条锁定「参数越界」的契约：负值必须被拒绝，不能静默读错/让读指针倒退 ──

    [Fact]
    public void 负偏移读取_必须被拒绝()
    {
        var buffer = new StickyWindowBuffer();
        buffer.Write(new byte[] { 1, 2, 3, 4 });
        buffer.ReadBytes(2);   // 读窗口移到 [3,4]；索引 0 已消费

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetByte(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.PeekBytes(-1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetUShort(-1, littleEndian: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetInt(-1, littleEndian: false));
    }

    [Fact]
    public void 负长度读取或跳过_必须被拒绝()
    {
        var buffer = new StickyWindowBuffer();
        buffer.Write(new byte[] { 1, 2, 3, 4 });

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadBytes(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.PeekBytes(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Skip(-1));

        // 关键：负 Skip 绝不能推进（更不能倒退）读指针
        Assert.Equal(4, buffer.ReadableBytes);
        Assert.Equal(1, buffer.GetByte(0));
    }

    [Fact]
    public void 查找_负偏移或越界偏移不得读到窗口外()
    {
        var buffer = new StickyWindowBuffer();
        buffer.Write(new byte[] { 0xAA, 0xBB, 0xAA, 0xBB });
        buffer.ReadBytes(2);   // 窗口 [0xAA, 0xBB]

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(new byte[] { 0xAA }, -1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(new byte[] { 0xAA }, 0, -1));

        // 偏移越过可读窗口：没有可匹配的数据 ⇒ -1（而不是去扫已消费区域）
        Assert.Equal(-1, buffer.IndexOf(new byte[] { 0xAA }, 10, 2));
        Assert.Equal(0, buffer.IndexOf(new byte[] { 0xAA }, 0, 2));
    }

    [Fact]
    public void 两种字节序的整数读取()
    {
        var big = new StickyWindowBuffer();
        big.Write(new byte[] { 0x12, 0x34, 0x56, 0x78 });
        Assert.Equal(0x1234, big.GetUShort(0, littleEndian: false));
        Assert.Equal(0x12345678, big.GetInt(0, littleEndian: false));
        Assert.Equal(0x3412, big.GetUShort(0, littleEndian: true));
        Assert.Equal(0x78563412, big.GetInt(0, littleEndian: true));
    }
}
