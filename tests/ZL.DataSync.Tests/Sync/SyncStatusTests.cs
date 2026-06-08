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

    // ═══════════════════════════════════════════════════════════
    //  AddSynced / AddFailed
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AddSynced_IncrementsCounter()
    {
        // Arrange
        var status = new SyncStatus();

        // Act & Assert
        Assert.Equal(0, status.TotalSynced);
        status.AddSynced(5);
        Assert.Equal(5, status.TotalSynced);
        status.AddSynced(10);
        Assert.Equal(15, status.TotalSynced);
    }

    [Fact]
    public void AddFailed_IncrementsCounter()
    {
        // Arrange
        var status = new SyncStatus();

        // Act & Assert
        Assert.Equal(0, status.TotalFailed);
        status.AddFailed(3);
        Assert.Equal(3, status.TotalFailed);
        status.AddFailed(7);
        Assert.Equal(10, status.TotalFailed);
    }

    [Fact]
    public void TotalSynced_Setter_ExchangeValue()
    {
        // Arrange
        var status = new SyncStatus();

        // Act
        status.TotalSynced = 42;

        // Assert
        Assert.Equal(42, status.TotalSynced);
    }

    [Fact]
    public void TotalFailed_Setter_ExchangeValue()
    {
        // Arrange
        var status = new SyncStatus();

        // Act
        status.TotalFailed = 99;

        // Assert
        Assert.Equal(99, status.TotalFailed);
    }

    [Fact]
    public void AddSynced_CanBeCombinedWithSet()
    {
        // Arrange
        var status = new SyncStatus();
        status.TotalSynced = 10;

        // Act & Assert
        status.AddSynced(5);
        Assert.Equal(15, status.TotalSynced);
    }

    // ═══════════════════════════════════════════════════════════
    //  Reset edge cases
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Reset_ClearsStatusText()
    {
        // Arrange
        var status = new SyncStatus { StatusText = "running" };

        // Act
        status.Reset();

        // Assert
        Assert.Equal("未启动", status.StatusText);
    }

    [Fact]
    public void Reset_ClearsLastError()
    {
        // Arrange
        var status = new SyncStatus { LastError = "error" };

        // Act
        status.Reset();

        // Assert
        Assert.Null(status.LastError);
    }

    [Fact]
    public void Reset_ClearsLastSyncTime()
    {
        // Arrange
        var status = new SyncStatus { LastSyncTime = DateTime.UtcNow };

        // Act
        status.Reset();

        // Assert
        Assert.Null(status.LastSyncTime);
    }

    [Fact]
    public void Reset_ClearsLastStartTime()
    {
        // Arrange
        var status = new SyncStatus { LastStartTime = DateTime.UtcNow };

        // Act
        status.Reset();

        // Assert
        Assert.Null(status.LastStartTime);
    }

    // ═══════════════════════════════════════════════════════════
    //  IsHealthy edge cases
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsHealthy_MultipleFailures_ReturnsFalse()
    {
        // Arrange
        var status = new SyncStatus { IsRunning = true, FailStreak = 5 };

        // Assert
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public void IsHealthy_AfterReset_ReturnsTrue()
    {
        // Arrange
        var status = new SyncStatus { IsRunning = true, FailStreak = 5 };

        // Act
        status.Reset();

        // Assert — 重置后 IsRunning 仍为 true，FailStreak = 0，所以 IsHealthy = true
        Assert.True(status.IsHealthy);
    }

    // ═══════════════════════════════════════════════════════════
    //  Default values
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Default_LastError_IsNull()
    {
        var status = new SyncStatus();
        Assert.Null(status.LastError);
    }

    [Fact]
    public void Default_LastSyncTime_IsNull()
    {
        var status = new SyncStatus();
        Assert.Null(status.LastSyncTime);
    }

    [Fact]
    public void Default_LastStartTime_IsNull()
    {
        var status = new SyncStatus();
        Assert.Null(status.LastStartTime);
    }

    [Fact]
    public void Default_FailStreak_IsZero()
    {
        var status = new SyncStatus();
        Assert.Equal(0, status.FailStreak);
    }

    [Fact]
    public void Default_TotalTables_IsZero()
    {
        var status = new SyncStatus();
        Assert.Equal(0, status.TotalTables);
    }
}
