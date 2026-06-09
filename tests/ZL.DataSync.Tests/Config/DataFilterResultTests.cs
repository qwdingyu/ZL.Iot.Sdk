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

        Assert.True(result.shouldSync);
        Assert.NotNull(result.transformedRow);
        Assert.Equal("val", result.transformedRow!["key"]);
    }

    [Fact]
    public void DataFilterResult_WithOnlyShouldSync()
    {
        var result = new DataFilterResult(shouldSync: false);

        Assert.False(result.shouldSync);
        Assert.Null(result.transformedRow);
    }

    [Fact]
    public void DataFilterResult_TransformedRow_NullByDefault()
    {
        var result = new DataFilterResult(shouldSync: true);

        Assert.True(result.shouldSync);
        Assert.Null(result.transformedRow);
    }

    [Fact]
    public void DataFilterResult_DifferentShouldSyncAreNotEqual()
    {
        var r1 = new DataFilterResult(shouldSync: true);
        var r2 = new DataFilterResult(shouldSync: false);

        Assert.NotEqual(r1, r2);
    }
}
