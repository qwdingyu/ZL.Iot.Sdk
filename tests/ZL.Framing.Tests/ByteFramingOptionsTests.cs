namespace ZL.Framing.Tests;

/// <summary>
/// <see cref="ByteFramingOptions"/> 的归一化与策略映射。
///
/// <para><b>为什么这些断言重要</b>：策略是**字符串**驱动的（配置来自 JSON），
/// 未知/空字符串的落点决定了实际行为。映射必须与消费方的判据**同源**
/// （消费方用 <c>options.GetStrategy()</c> 判断是否走超时路径，而不是自己比字符串），
/// 否则两侧会把同一个未知策略理解成不同策略——那正是「静默丢帧」类缺陷的土壤。</para>
/// </summary>
public class ByteFramingOptionsTests
{
    [Fact]
    public void 策略映射_未知或空字符串一律落到Timeout()
    {
        Assert.Equal(ByteFramingStrategy.Timeout, new ByteFramingOptions { Strategy = "" }.GetStrategy());
        Assert.Equal(ByteFramingStrategy.Timeout, new ByteFramingOptions { Strategy = "  " }.GetStrategy());
        Assert.Equal(ByteFramingStrategy.Timeout, new ByteFramingOptions { Strategy = "NoSuchStrategy" }.GetStrategy());

        Assert.Equal(ByteFramingStrategy.FixedLength, new ByteFramingOptions { Strategy = "fixedlength" }.GetStrategy());
        Assert.Equal(ByteFramingStrategy.LengthField, new ByteFramingOptions { Strategy = "LENGTHFIELD" }.GetStrategy());
        Assert.Equal(ByteFramingStrategy.LengthFieldWithChecksum,
            new ByteFramingOptions { Strategy = "LengthFieldChecksum" }.GetStrategy());
    }

    [Fact]
    public void 超时动作映射_未知落到Hold()
    {
        Assert.Equal(TimeoutMode.Hold, new ByteFramingOptions { TimeoutAction = "" }.GetTimeoutMode());
        Assert.Equal(TimeoutMode.Hold, new ByteFramingOptions { TimeoutAction = "Whatever" }.GetTimeoutMode());
        Assert.Equal(TimeoutMode.Emit, new ByteFramingOptions { TimeoutAction = "emit" }.GetTimeoutMode());
        Assert.Equal(TimeoutMode.Clear, new ByteFramingOptions { TimeoutAction = "CLEAR" }.GetTimeoutMode());
    }

    [Fact]
    public void 重同步策略映射_未知落到ScanForSync()
    {
        Assert.Equal(ResyncMode.ScanForSync, new ByteFramingOptions { ResyncPolicy = "" }.GetResyncMode());
        Assert.Equal(ResyncMode.ScanForSync, new ByteFramingOptions { ResyncPolicy = "Unknown" }.GetResyncMode());
        Assert.Equal(ResyncMode.DropOneByte, new ByteFramingOptions { ResyncPolicy = "droponebyte" }.GetResyncMode());
    }

    [Fact]
    public void 归一化_非法取值被收敛到安全默认()
    {
        var options = new ByteFramingOptions
        {
            FixedLength = -5,
            LengthFieldOffset = -1,
            LengthFieldSize = 7,          // 只允许 1..4
            MinFrameLength = 0,
            MaxFrameLength = 0,
            LengthFieldEndian = "  ",
            BufferInitialCapacity = -1,
            BufferMaxCapacity = -1,
            MaxResyncSkip = -3,
        };

        options.Normalize();

        Assert.Equal(0, options.FixedLength);
        Assert.Equal(0, options.LengthFieldOffset);
        Assert.Equal(2, options.LengthFieldSize);
        Assert.Equal(1, options.MinFrameLength);
        Assert.Equal(4096, options.MaxFrameLength);
        Assert.Equal("Big", options.LengthFieldEndian);
        Assert.Equal(4096, options.BufferInitialCapacity);
        Assert.Equal(1024 * 1024, options.BufferMaxCapacity);
        Assert.Equal(2048, options.MaxResyncSkip);
    }

    [Fact]
    public void 归一化_Max小于Min时以Min为准_初始容量不超过上限()
    {
        var options = new ByteFramingOptions { MinFrameLength = 100, MaxFrameLength = 10, BufferInitialCapacity = 4096, BufferMaxCapacity = 1024 };
        options.Normalize();

        Assert.Equal(100, options.MaxFrameLength);
        Assert.Equal(1024, options.BufferInitialCapacity);
    }

    [Fact]
    public void 同步字解析_容忍空格逗号与0x前缀_奇数位左补零()
    {
        Assert.Equal(new byte[] { 0xAA, 0x55 }, new ByteFramingOptions { SyncBytes = "AA 55" }.GetSyncBytes());
        Assert.Equal(new byte[] { 0xAA, 0x55 }, new ByteFramingOptions { SyncBytes = "0xAA,0x55" }.GetSyncBytes());
        Assert.Equal(new byte[] { 0x0A, 0x55 }, new ByteFramingOptions { SyncBytes = "A55" }.GetSyncBytes());
        Assert.Empty(new ByteFramingOptions { SyncBytes = "" }.GetSyncBytes());
    }
}
