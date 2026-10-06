using ZL.Collections.Collections;

namespace ZL.Collections.Tests;

/// <summary>
/// FixedSizeRingBuffer 回归锁（2026-10-06，自源码语义逐条推导；此前该库零覆盖）。
/// 语义：满时覆盖最旧项；GetNewest 按时间正序返回最近 N 条。
/// </summary>
public class FixedSizeRingBufferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 构造_容量必须大于0(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedSizeRingBuffer<int>(capacity));
    }

    [Fact]
    public void 未满_按写入顺序全量返回()
    {
        var buf = new FixedSizeRingBuffer<int>(3);
        buf.Append(1);
        buf.Append(2);
        Assert.Equal(new[] { 1, 2 }, buf.GetNewest(10));
    }

    [Fact]
    public void 满_恰好容量不丢数据()
    {
        var buf = new FixedSizeRingBuffer<int>(3);
        buf.Append(1);
        buf.Append(2);
        buf.Append(3);
        Assert.Equal(new[] { 1, 2, 3 }, buf.GetNewest(10));
    }

    [Fact]
    public void 溢出_覆盖最旧项且跨越写指针环绕()
    {
        var buf = new FixedSizeRingBuffer<int>(3);
        for (int i = 1; i <= 5; i++) buf.Append(i);
        Assert.Equal(new[] { 3, 4, 5 }, buf.GetNewest(10));
    }

    [Fact]
    public void GetNewest_请求条数多于现有_返回现有全部()
    {
        var buf = new FixedSizeRingBuffer<int>(5);
        buf.Append(1);
        Assert.Equal(new[] { 1 }, buf.GetNewest(3));
    }

    [Fact]
    public void GetNewest_请求最近两条_为时间正序的最新两条()
    {
        var buf = new FixedSizeRingBuffer<int>(3);
        for (int i = 1; i <= 5; i++) buf.Append(i);
        Assert.Equal(new[] { 4, 5 }, buf.GetNewest(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void GetNewest_非正条数返回空(int count)
    {
        var buf = new FixedSizeRingBuffer<int>(3);
        buf.Append(1);
        Assert.Empty(buf.GetNewest(count));
    }

    [Fact]
    public void 空缓冲_返回空()
    {
        var buf = new FixedSizeRingBuffer<int>(3);
        Assert.Empty(buf.GetNewest(5));
    }

    [Fact]
    public void 长时间滚动_写入1万条后仍保持最近N条正序()
    {
        var buf = new FixedSizeRingBuffer<int>(7);
        for (int i = 1; i <= 10000; i++) buf.Append(i);
        Assert.Equal(new[] { 9994, 9995, 9996, 9997, 9998, 9999, 10000 }, buf.GetNewest(7));
    }
}
