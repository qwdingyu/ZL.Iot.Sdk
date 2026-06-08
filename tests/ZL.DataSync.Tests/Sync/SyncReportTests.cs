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
}

public class SyncStatusTests
{
    [Fact]
    public void IsHealthy_IsTrue_WhenNotRunning()
    {
        var status = new SyncStatus { IsRunning = false, FailStreak = 5 };
        Assert.True(status.IsHealthy);
    }

    [Fact]
    public void IsHealthy_IsTrue_WhenRunningButNoFailStreak()
    {
        var status = new SyncStatus { IsRunning = true, FailStreak = 0 };
        Assert.True(status.IsHealthy);
    }

    [Fact]
    public void IsHealthy_IsFalse_WhenRunningWithFailStreak()
    {
        var status = new SyncStatus { IsRunning = true, FailStreak = 3 };
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        var status = new SyncStatus
        {
            TotalTables = 5, TotalSynced = 100, TotalFailed = 10,
            LastSyncTime = DateTime.UtcNow, LastStartTime = DateTime.UtcNow,
            FailStreak = 3, LastError = "Some error"
        };
        status.Reset();
        Assert.Equal(0, status.TotalTables);
        Assert.Equal(0, status.TotalSynced);
        Assert.Equal(0, status.TotalFailed);
        Assert.Null(status.LastSyncTime);
        Assert.Null(status.LastStartTime);
        Assert.Equal(0, status.FailStreak);
        Assert.Null(status.LastError);
    }

    [Fact]
    public void Default_StatusText_IsNotStarted()
    {
        var status = new SyncStatus();
        Assert.Equal("未启动", status.StatusText);
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
