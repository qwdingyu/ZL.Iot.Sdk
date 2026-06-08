using ZL.DataSync;

namespace ZL.DataSync.Tests.Sync;

/// <summary>
/// SyncStatus 单元测试。
/// </summary>
public class SyncStatusTests
{
    [Fact]
    public void IsHealthy_IsTrue_WhenNotRunning()
    {
        // Arrange
        var status = new SyncStatus { IsRunning = false, FailStreak = 5 };

        // Assert
        Assert.True(status.IsHealthy);
    }

    [Fact]
    public void IsHealthy_IsTrue_WhenRunningButNoFailStreak()
    {
        // Arrange
        var status = new SyncStatus { IsRunning = true, FailStreak = 0 };

        // Assert
        Assert.True(status.IsHealthy);
    }

    [Fact]
    public void IsHealthy_IsFalse_WhenRunningWithFailStreak()
    {
        // Arrange
        var status = new SyncStatus { IsRunning = true, FailStreak = 3 };

        // Assert
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        // Arrange
        var status = new SyncStatus
        {
            TotalTables = 5,
            TotalSynced = 100,
            TotalFailed = 10,
            LastSyncTime = DateTime.UtcNow,
            LastStartTime = DateTime.UtcNow,
            FailStreak = 3,
            LastError = "Some error"
        };

        // Act
        status.Reset();

        // Assert
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
        // Arrange & Act
        var status = new SyncStatus();

        // Assert
        Assert.Equal("未启动", status.StatusText);
    }
}
