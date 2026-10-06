using System.Text.RegularExpressions;
using ZL.DataProcessing;
using Xunit;

namespace ZL.DataProcessing.Tests;

/// <summary>
/// PatternMatcher 是 ZL.DataProcessing 中模式匹配的核心
/// 测试覆盖：文本正则构建、捕获组、二进制匹配（Fixed/Any/Range/Many）
/// </summary>
public class PatternMatcherTests
{
    #region BuildTextRegex

    [Fact]
    public void BuildTextRegex_NullOrEmpty_ShouldReturnEmptyPattern()
    {
        var regex = PatternMatcher.BuildTextRegex("");
        Assert.False(PatternMatcher.TryMatch("anything", regex, out _));
    }

    [Fact]
    public void BuildTextRegex_Literal_ShouldMatchExactly()
    {
        var regex = PatternMatcher.BuildTextRegex("HELLO");
        Assert.True(PatternMatcher.TryMatch("HELLO", regex, out var groups));
        Assert.Empty(groups);
    }

    [Fact]
    public void BuildTextRegex_Literal_CaseInsensitive()
    {
        var regex = PatternMatcher.BuildTextRegex("hello");
        Assert.True(PatternMatcher.TryMatch("HELLO", regex, out _));
    }

    [Fact]
    public void BuildTextRegex_WithPlaceholder_ShouldCapture()
    {
        var regex = PatternMatcher.BuildTextRegex("TEMP {value}");
        Assert.True(PatternMatcher.TryMatch("TEMP 42.5", regex, out var groups));
        Assert.Equal("42.5", groups[0]);
    }

    [Fact]
    public void BuildTextRegex_MultiplePlaceholders_ShouldCaptureAll()
    {
        var regex = PatternMatcher.BuildTextRegex("SET {a} {b}");
        Assert.True(PatternMatcher.TryMatch("SET 10 20", regex, out var groups));
        Assert.Equal("10", groups[0]);
        Assert.Equal("20", groups[1]);
    }

    [Fact]
    public void BuildTextRegex_EscapesSpecialChars()
    {
        // Dot should be literal, not regex wildcard
        var regex = PatternMatcher.BuildTextRegex("V1.0");
        Assert.True(PatternMatcher.TryMatch("V1.0", regex, out _));
        Assert.False(PatternMatcher.TryMatch("V1X0", regex, out _));
    }

    #endregion

    #region TryMatch

    [Fact]
    public void TryMatch_NullInput_ShouldReturnFalse()
    {
        var regex = PatternMatcher.BuildTextRegex("hello");
        Assert.False(PatternMatcher.TryMatch(null!, regex, out _));
    }

    [Fact]
    public void TryMatch_NullRegex_ShouldReturnFalse()
    {
        Assert.False(PatternMatcher.TryMatch("hello", null!, out _));
    }

    [Fact]
    public void TryMatch_NoCapture_ShouldReturnEmptyGroups()
    {
        var regex = PatternMatcher.BuildTextRegex("EXACT");
        Assert.True(PatternMatcher.TryMatch("EXACT", regex, out var groups));
        Assert.Empty(groups);
    }

    #endregion

    #region IsBinaryMatch

    [Fact]
    public void IsBinaryMatch_NullData_ShouldReturnFalse()
    {
        Assert.False(PatternMatcher.IsBinaryMatch(null!, "AB CD"));
    }

    [Fact]
    public void IsBinaryMatch_EmptyPattern_ShouldReturnFalse()
    {
        Assert.False(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB }, ""));
    }

    [Fact]
    public void IsBinaryMatch_FixedExact_ShouldMatch()
    {
        Assert.True(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB, 0xCD }, "AB CD"));
    }

    [Fact]
    public void IsBinaryMatch_FixedMismatch_ShouldNotMatch()
    {
        Assert.False(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB, 0xEF }, "AB CD"));
    }

    [Fact]
    public void IsBinaryMatch_AnyWildcard_ShouldMatchAnyByte()
    {
        Assert.True(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB, 0xFF }, "AB ??"));
        Assert.True(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB, 0x00 }, "AB ??"));
    }

    [Fact]
    public void IsBinaryMatch_AnyManyWildcard_ShouldMatchAnyLength()
    {
        // ** matches zero or more bytes
        Assert.True(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB }, "AB **"));
        Assert.True(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB, 0x01, 0x02 }, "AB **"));
    }

    [Fact]
    public void IsBinaryMatch_AnyRange_ShouldMatchWithinRange()
    {
        // ??[1-3] matches 1 to 3 bytes
        Assert.True(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB, 0x01 }, "AB ??[1-3]"));
        Assert.True(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB, 0x01, 0x02, 0x03 }, "AB ??[1-3]"));
        // Too few bytes for minimum range
        Assert.False(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB }, "AB ??[1-3]"));
    }

    [Fact]
    public void IsBinaryMatch_CaseInsensitiveHex()
    {
        Assert.True(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB, 0xCD }, "ab cd"));
    }

    [Fact]
    public void IsBinaryMatch_InvalidPattern_ShouldReturnFalse()
    {
        Assert.False(PatternMatcher.IsBinaryMatch(new byte[] { 0xAB }, "ZZ ZZ"));
    }

    #endregion
}
