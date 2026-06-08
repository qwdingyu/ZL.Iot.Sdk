using ZL.DataSync;
using ZL.DataSync.Config;

namespace ZL.DataSync.Tests.Config;

/// <summary>
/// DataFilterResult 记录测试。
/// </summary>
public class DataFilterResultTests
{
    [Fact]
    public void DataFilterResult_CreatesWithAllFields()
    {
        var result = new DataFilterResult(
            shouldSync: true,
            transformedRow: new Dictionary<string, object?> { ["key"] = "val" });

        Assert.True(result.ShouldSync);
        Assert.NotNull(result.TransformedRow);
        Assert.Equal("val", result.TransformedRow!["key"]);
    }

    [Fact]
    public void DataFilterResult_WithOnlyShouldSync()
    {
        var result = new DataFilterResult(shouldSync: false);

        Assert.False(result.ShouldSync);
        Assert.Null(result.TransformedRow);
    }

    [Fact]
    public void DataFilterResult_TransformedRow_NullByDefault()
    {
        var result = new DataFilterResult(shouldSync: true);

        Assert.True(result.ShouldSync);
        Assert.Null(result.TransformedRow);
    }

    [Fact]
    public void DataFilterResult_Equality_SameValuesAreEqual()
    {
        var r1 = new DataFilterResult(shouldSync: true, transformedRow: new Dictionary<string, object?> { ["a"] = 1 });
        var r2 = new DataFilterResult(shouldSync: true, transformedRow: new Dictionary<string, object?> { ["a"] = 1 });

        Assert.Equal(r1, r2);
    }

    [Fact]
    public void DataFilterResult_DifferentShouldSyncAreNotEqual()
    {
        var r1 = new DataFilterResult(shouldSync: true);
        var r2 = new DataFilterResult(shouldSync: false);

        Assert.NotEqual(r1, r2);
    }
}
