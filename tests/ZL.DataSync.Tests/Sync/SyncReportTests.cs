using ZL.DataSync;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;

namespace ZL.DataSync.Tests.Sync;

public class SyncReportTests
{
    [Fact]
    public void Ok_Success_ReturnsCorrectReport()
    {
        var report = SyncReport.Ok("test_table", 10, 10, "2025-01-01T00:00:00Z", 123.45);
        Assert.Equal("test_table", report.TableName);
        Assert.Equal(10, report.TargetCount);
        Assert.Equal(10, report.SyncedCount);
        Assert.Equal(0, report.FailedCount);
        Assert.True(report.Success);
        Assert.True(report.HasData);
        Assert.Equal("2025-01-01T00:00:00Z", report.LastWatermark);
        Assert.Equal(123.45, report.ElapsedMs);
    }

    [Fact]
    public void Ok_ZeroTarget_ReturnsCorrectReport()
    {
        var report = SyncReport.Ok("test_table", 0, 0, null, 0);
        Assert.Equal(0, report.TargetCount);
        Assert.False(report.HasData);
        Assert.True(report.Success);
    }

    [Fact]
    public void Fail_ReturnsCorrectReport()
    {
        var report = SyncReport.Fail("test_table", 10, "Connection timeout", 456.78);
        Assert.Equal("test_table", report.TableName);
        Assert.Equal(10, report.TargetCount);
        Assert.Equal(10, report.FailedCount);
        Assert.False(report.Success);
        Assert.True(report.HasData);
        Assert.Equal("Connection timeout", report.LastError);
    }

    [Fact]
    public void Success_Property_IsTrue_WhenFailedCountIsZero()
    {
        var report = SyncReport.Ok("table", 5, 5, null, 10);
        Assert.True(report.Success);
    }

    [Fact]
    public void Success_Property_IsFalse_WhenFailedCountGreaterThanZero()
    {
        var report = SyncReport.Fail("table", 5, "Error", 10);
        Assert.False(report.Success);
    }

    [Fact]
    public void HasData_Property_IsFalse_WhenTargetCountIsZero()
    {
        var report = SyncReport.Ok("table", 0, 0, null, 0);
        Assert.False(report.HasData);
    }

    [Fact]
    public void HasData_Property_IsTrue_WhenTargetCountGreaterThanZero()
    {
        var report = SyncReport.Ok("table", 1, 1, null, 10);
        Assert.True(report.HasData);
    }

    [Fact]
    public void Fail_ZeroTarget_SetsFailedCountToOne()
    {
        // 即使 target=0，有错误也标记为 1 条失败，避免 Success 误判
        var report = SyncReport.Fail("table", 0, "Error", 10);
        Assert.False(report.Success);
        Assert.False(report.HasData);
        Assert.Equal(1, report.FailedCount);
    }

    [Fact]
    public void Fail_NonZeroTarget_SetsFailedCountToTarget()
    {
        var report = SyncReport.Fail("table", 5, "Error", 10);
        Assert.False(report.Success);
        Assert.Equal(5, report.FailedCount);
    }

    [Fact]
    public void Ok_EmptyWatermark_IsAllowed()
    {
        var report = SyncReport.Ok("table", 0, 0, "", 0);
        Assert.True(report.Success);
        Assert.Equal("", report.LastWatermark);
    }

    [Fact]
    public void Timestamp_IsUtc()
    {
        var before = DateTime.UtcNow;
        var report = SyncReport.Ok("table", 1, 1, null, 10);
        var after = DateTime.UtcNow;

        Assert.True(report.Timestamp >= before && report.Timestamp <= after);
        Assert.True(report.Timestamp.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public void TableName_DefaultIsEmpty()
    {
        var report = SyncReport.Ok("table", 0, 0, null, 0);
        Assert.Equal("table", report.TableName);
    }
}

// 测试用 Mock Logger
internal sealed class TestLogger : IStructuredLogger
{
    public List<string> Messages { get; } = new();
    public IStructuredLogger ForSource(string source) => this;
    public void Info(string message) => Messages.Add($"INFO: {message}");
    public void Warning(string message) => Messages.Add($"WARN: {message}");
    public void Error(string message) => Messages.Add($"ERROR: {message}");
    public void Debug(string message) => Messages.Add($"DEBUG: {message}");
    public void Flush() { }
    public void Dispose() { }
}
