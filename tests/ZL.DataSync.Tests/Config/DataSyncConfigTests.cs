using ZL.DataSync.Config;

namespace ZL.DataSync.Tests.Config;

/// <summary>
/// DataSyncConfig 单元测试。
/// </summary>
public class DataSyncConfigTests
{
    [Fact]
    public void Default_LocalDbPath_IsEmpty()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.Equal(string.Empty, config.LocalDbPath);
    }

    [Fact]
    public void Default_RemoteTargets_IsEmptyList()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.Empty(config.RemoteTargets);
    }

    [Fact]
    public void Default_BatchSize_Is100()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.Equal(100, config.BatchSize);
    }

    [Fact]
    public void Default_SyncIntervalSeconds_Is5()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.Equal(5, config.SyncIntervalSeconds);
    }

    [Fact]
    public void Default_MaxRetryCount_Is3()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.Equal(3, config.MaxRetryCount);
    }

    [Fact]
    public void Default_RetryBackoffSeconds_Is2()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.Equal(2, config.RetryBackoffSeconds);
    }

    [Fact]
    public void Default_EnableUpsert_IsTrue()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.True(config.EnableUpsert);
    }

    [Fact]
    public void Default_EnableCleanup_IsTrue()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.True(config.EnableCleanup);
    }

    [Fact]
    public void Default_DataRetentionDays_Is730()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.Equal(730, config.DataRetentionDays);
    }

    [Fact]
    public void Default_CleanupIntervalSeconds_Is3600()
    {
        // Arrange & Act
        var config = new DataSyncConfig();

        // Assert
        Assert.Equal(3600, config.CleanupIntervalSeconds);
    }

    [Fact]
    public void Can_Set_All_Properties()
    {
        // Arrange & Act
        var config = new DataSyncConfig
        {
            LocalDbPath = "/tmp/test.db",
            BatchSize = 50,
            SyncIntervalSeconds = 10,
            MaxRetryCount = 5,
            RetryBackoffSeconds = 3,
            EnableUpsert = false,
            EnableCleanup = false,
            DataRetentionDays = 365,
            CleanupIntervalSeconds = 1800
        };

        // Assert
        Assert.Equal("/tmp/test.db", config.LocalDbPath);
        Assert.Equal(50, config.BatchSize);
        Assert.Equal(10, config.SyncIntervalSeconds);
        Assert.Equal(5, config.MaxRetryCount);
        Assert.Equal(3, config.RetryBackoffSeconds);
        Assert.False(config.EnableUpsert);
        Assert.False(config.EnableCleanup);
        Assert.Equal(365, config.DataRetentionDays);
        Assert.Equal(1800, config.CleanupIntervalSeconds);
    }
}
