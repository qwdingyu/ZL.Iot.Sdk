using ZL.DataProcessing;

namespace ZL.DataProcessing.Tests;

/// <summary>
/// HexCodec 回归锁（2026-10-06，自源码语义逐条推导；此前该类零覆盖）。
/// </summary>
public class HexCodecTests
{
    [Theory]
    [InlineData("aa bb-cc", "AABBCC")]
    [InlineData("  xG12 ", "12")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_过滤非HEX字符并大写(string? input, string expected)
    {
        Assert.Equal(expected, HexCodec.Normalize(input!));
    }

    [Fact]
    public void ToBytes_常规与分隔符混排()
    {
        Assert.Equal(new byte[] { 0xAA, 0x55, 0x02 }, HexCodec.ToBytes("AA 55 02"));
        Assert.Equal(new byte[] { 0xAA, 0x55 }, HexCodec.ToBytes("aa-55"));
    }

    [Fact]
    public void ToBytes_奇数长度补前导零()
    {
        Assert.Equal(new byte[] { 0x0A }, HexCodec.ToBytes("A"));
        // ⚠️ 空格先被 Normalize 剔除、拼回后**整体**判奇偶——"A B" 实为 "AB"（一个字节 0xAB），
        // 不是按字段各自补零。首版测试在这里推错了，跑挂后对照源码修正。
        Assert.Equal(new byte[] { 0xAB }, HexCodec.ToBytes("A B"));
    }

    [Fact]
    public void ToBytes_空或全非法字符返回空数组()
    {
        Assert.Empty(HexCodec.ToBytes(""));
        Assert.Empty(HexCodec.ToBytes("zz"));
        Assert.Empty(HexCodec.ToBytes(null!));
    }

    [Theory]
    [InlineData(new byte[] { 0xAA, 0x55 }, "", "AA55")]
    [InlineData(new byte[] { 0xAA, 0x55 }, " ", "AA 55")]
    public void ToString_分隔符可控(byte[] data, string separator, string expected)
    {
        Assert.Equal(expected, HexCodec.ToString(data, separator));
    }

    [Fact]
    public void ToString_空或null返回空串()
    {
        Assert.Equal(string.Empty, HexCodec.ToString(Array.Empty<byte>()));
        Assert.Equal(string.Empty, HexCodec.ToString(null!));
    }

    [Fact]
    public void TryStripChecksum_剥离末尾N字节为校验和()
    {
        bool ok = HexCodec.TryStripChecksum("AA5502EF", 2, out string stripped, out byte[] checksum);
        Assert.True(ok);
        Assert.Equal("AA55", stripped);
        Assert.Equal(new byte[] { 0x02, 0xEF }, checksum);
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData("AA55", 2)]
    [InlineData("AA5502EF", 0)]
    [InlineData("AA5502EF", -1)]
    public void TryStripChecksum_非法输入返回false(string input, int checksumLen)
    {
        bool ok = HexCodec.TryStripChecksum(input, checksumLen, out string stripped, out byte[] checksum);
        Assert.False(ok);
        Assert.Empty(checksum);
    }

    [Fact]
    public void 编解码往返一致()
    {
        byte[] original = { 0x00, 0x01, 0xFE, 0xFF, 0x80 };
        Assert.Equal(original, HexCodec.ToBytes(HexCodec.ToString(original)));
    }
}
