using ZL.DataSync;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;

namespace ZL.DataSync.Tests.Config;

public class DataSyncConfigTests
{
    [Fact]
    public void Default_LocalDbPath_IsEmpty()
    {
        var config = new DataSyncConfig();
        Assert.Equal(string.Empty, config.LocalDbPath);
    }

    [Fact]
    public void Default_RemoteTargets_IsEmptyList()
    {
        var config = new DataSyncConfig();
        Assert.Empty(config.RemoteTargets);
    }

    [Fact]
    public void Default_BatchSize_Is100() => Assert.Equal(100, new DataSyncConfig().BatchSize);

    [Fact]
    public void Default_SyncIntervalSeconds_Is5() => Assert.Equal(5, new DataSyncConfig().SyncIntervalSeconds);

    [Fact]
    public void Default_MaxRetryCount_Is3() => Assert.Equal(3, new DataSyncConfig().MaxRetryCount);

    [Fact]
    public void Default_RetryBackoffSeconds_Is2() => Assert.Equal(2, new DataSyncConfig().RetryBackoffSeconds);

    [Fact]
    public void Default_EnableUpsert_IsTrue() => Assert.True(new DataSyncConfig().EnableUpsert);

    [Fact]
    public void Default_EnableCleanup_IsTrue() => Assert.True(new DataSyncConfig().EnableCleanup);

    [Fact]
    public void Default_DataRetentionDays_Is730() => Assert.Equal(730, new DataSyncConfig().DataRetentionDays);

    [Fact]
    public void Default_CleanupIntervalSeconds_Is3600() => Assert.Equal(3600, new DataSyncConfig().CleanupIntervalSeconds);

    [Fact]
    public void Can_Set_All_Properties()
    {
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

public class RemoteTargetConfigTests
{
    [Fact]
    public void Default_Name_IsEmpty()
    {
        var config = new RemoteTargetConfig();
        Assert.Equal(string.Empty, config.Name);
    }

    [Fact]
    public void Default_Type_IsMySql() => Assert.Equal(TargetType.MySql, new RemoteTargetConfig().Type);

    [Fact]
    public void Default_ConnectionString_IsEmpty()
    {
        var config = new RemoteTargetConfig();
        Assert.Equal(string.Empty, config.ConnectionString);
    }

    [Fact]
    public void Default_TableMappings_IsEmptyDictionary()
    {
        var config = new RemoteTargetConfig();
        Assert.Empty(config.TableMappings);
    }

    [Fact]
    public void Default_HttpConfig_IsNull()
    {
        var config = new RemoteTargetConfig();
        Assert.Null(config.HttpConfig);
    }

    [Fact]
    public void Can_Set_All_Properties()
    {
        var httpConfig = new HttpUploadConfig
        {
            Endpoint = "http://example.com/api",
            TimeoutSeconds = 60,
            DeviceName = "TestDevice",
            Type = "1"
        };

        var config = new RemoteTargetConfig
        {
            Name = "TestTarget",
            Type = TargetType.Http,
            ConnectionString = "server=test;database=test;user=root;password=test;",
            HttpConfig = httpConfig
        };

        Assert.Equal("TestTarget", config.Name);
        Assert.Equal(TargetType.Http, config.Type);
        Assert.Equal("server=test;database=test;user=root;password=test;", config.ConnectionString);
        Assert.NotNull(config.HttpConfig);
        Assert.Equal("http://example.com/api", config.HttpConfig!.Endpoint);
        Assert.Equal(60, config.HttpConfig!.TimeoutSeconds);
        Assert.Equal("TestDevice", config.HttpConfig!.DeviceName);
        Assert.Equal("1", config.HttpConfig!.Type);
    }
}

public class HttpUploadConfigTests
{
    [Fact]
    public void Default_Endpoint_IsEmpty()
    {
        var config = new HttpUploadConfig();
        Assert.Equal(string.Empty, config.Endpoint);
    }

    [Fact]
    public void Default_TableEndpoints_IsEmptyDictionary()
    {
        var config = new HttpUploadConfig();
        Assert.Empty(config.TableEndpoints);
    }

    [Fact]
    public void Default_TimeoutSeconds_Is30() => Assert.Equal(30, new HttpUploadConfig().TimeoutSeconds);

    [Fact]
    public void Default_Headers_IsEmptyDictionary()
    {
        var config = new HttpUploadConfig();
        Assert.Empty(config.Headers);
    }

    [Fact]
    public void Default_BodyTemplate_IsNull()
    {
        var config = new HttpUploadConfig();
        Assert.Null(config.BodyTemplate);
    }

    [Fact]
    public void Default_DeviceName_IsNull()
    {
        var config = new HttpUploadConfig();
        Assert.Null(config.DeviceName);
    }

    [Fact]
    public void Default_Type_IsNull()
    {
        var config = new HttpUploadConfig();
        Assert.Null(config.Type);
    }
}
