using ZL.Shared.Utils;

namespace ZL.Shared.Tests;

/// <summary>
/// StringDistance（Levenshtein）回归锁 —— 此前该类零覆盖，
/// 而它是协议模板 "Did you mean" 提示的相似度来源。
/// 期望值均为编辑距离的教科书定义（含经典 kitten/sitting=3）。
/// </summary>
public class StringDistanceTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("a", "b", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("123456", "1234567", 1)]
    public void Levenshtein_教科书定义(string s, string t, int expected)
    {
        Assert.Equal(expected, StringDistance.Levenshtein(s, t));
    }

    [Fact]
    public void Levenshtein_参数对调结果一致()
    {
        Assert.Equal(StringDistance.Levenshtein("kitten", "sitting"), StringDistance.Levenshtein("sitting", "kitten"));
    }

    [Fact]
    public void Similarity_完全相同为1()
    {
        Assert.Equal(1.0, StringDistance.Similarity("ABCD", "ABCD"), 10);
        Assert.Equal(1.0, StringDistance.Similarity("", ""), 10);
    }

    [Fact]
    public void Similarity_完全不同且等长为0()
    {
        Assert.Equal(0.0, StringDistance.Similarity("abc", "xyz"), 10);
    }

    [Fact]
    public void Similarity_一半字符不同为半()
    {
        // "abcd" vs "abxy"：距离 2，最大长度 4 → 0.5
        Assert.Equal(0.5, StringDistance.Similarity("abcd", "abxy"), 10);
    }

    [Fact]
    public void Similarity_结果落在0到1之间()
    {
        double v = StringDistance.Similarity("kitten", "sitting");
        Assert.InRange(v, 0.0, 1.0);
    }
}
