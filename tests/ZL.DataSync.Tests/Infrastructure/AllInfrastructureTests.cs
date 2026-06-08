using ZL.DataSync;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ZL.DataSync.Tests.Infrastructure;

public class WatermarkStoreTests : IDisposable
{
    private readonly string _dbPath;
    private WatermarkStore? _store;

    public WatermarkStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"wal_test_{Guid.NewGuid()}.db");
        _store = new WatermarkStore(_dbPath);
        _store.EnsureTable();
    }

    public void Dispose()
    {
        _store?.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void WriteAndReadWatermark()
    {
        var store = new WatermarkStore(_dbPath);
        store.EnsureTable();
        store.WriteWatermark("test_table", "target1", "2025-01-01T00:00:00Z");
        var result = store.ReadWatermark("test_table", "target1");
        Assert.Equal("2025-01-01T00:00:00Z", result);
        store.Dispose();
    }

    [Fact]
    public void ReadWatermark_ReturnsNull_ForNonExistingKey()
    {
        var store = new WatermarkStore(_dbPath);
        store.EnsureTable();
        var result = store.ReadWatermark("nonexistent", "target1");
        Assert.Null(result);
        store.Dispose();
    }

    [Fact]
    public void WriteWatermark_UpdatesExistingValue()
    {
        var store = new WatermarkStore(_dbPath);
        store.EnsureTable();
        store.WriteWatermark("test_table", "target1", "initial");
        store.WriteWatermark("test_table", "target1", "updated");
        var result = store.ReadWatermark("test_table", "target1");
        Assert.Equal("updated", result);
        store.Dispose();
    }

    [Fact]
    public void WriteWatermark_SupportsMultipleKeys()
    {
        var store = new WatermarkStore(_dbPath);
        store.EnsureTable();
        store.WriteWatermark("table1", "target1", "wm1");
        store.WriteWatermark("table1", "target2", "wm2");
        store.WriteWatermark("table2", "target1", "wm3");
        Assert.Equal("wm1", store.ReadWatermark("table1", "target1"));
        Assert.Equal("wm2", store.ReadWatermark("table1", "target2"));
        Assert.Equal("wm3", store.ReadWatermark("table2", "target1"));
        store.Dispose();
    }

    [Fact]
    public void GetLastSyncTime_ReturnsTimestamp()
    {
        var store = new WatermarkStore(_dbPath);
        store.EnsureTable();
        store.WriteWatermark("test_table", "target1", "2025-01-01T00:00:00Z");
        var result = store.GetLastSyncTime("test_table", "target1");
        Assert.NotNull(result);
        Assert.True(result > DateTime.MinValue);
        store.Dispose();
    }

    [Fact]
    public void GetLastSyncTime_ReturnsNull_ForNonExistingKey()
    {
        var store = new WatermarkStore(_dbPath);
        store.EnsureTable();
        var result = store.GetLastSyncTime("nonexistent", "target1");
        Assert.Null(result);
        store.Dispose();
    }

    [Fact]
    public void WriteWatermark_HandlesEmptyValues()
    {
        var store = new WatermarkStore(_dbPath);
        store.EnsureTable();
        store.WriteWatermark("test_table", "target1", "");
        var result = store.ReadWatermark("test_table", "target1");
        Assert.Equal("", result);
        store.Dispose();
    }
}

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDataSync_RegistersConfigAndEngine()
    {
        var services = new ServiceCollection();
        services.AddDataSync(cfg =>
        {
            cfg.LocalDbPath = "/tmp/test.db";
            cfg.RemoteTargets = new List<RemoteTargetConfig>
            {
                new() { Name = "Test", Type = TargetType.MySql, ConnectionString = "server=test;" }
            };
        });
        var provider = services.BuildServiceProvider();
        var config = provider.GetService<DataSyncConfig>();
        Assert.NotNull(config);
        Assert.Equal("/tmp/test.db", config!.LocalDbPath);
        Assert.Single(config.RemoteTargets);
        var engine = provider.GetService<SyncEngine>();
        Assert.NotNull(engine);
    }

    [Fact]
    public void AddDataSyncFromConfig_Throws_WhenSectionDoesNotExist()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentException>(() =>
            services.AddDataSyncFromConfig(config, "NonExistentSection"));
    }

    [Fact]
    public void AddDataSyncFromConfig_RegistersConfigAndEngine()
    {
        var services = new ServiceCollection();
        var configData = new Dictionary<string, string>
        {
            ["DataSync:LocalDbPath"] = "/tmp/test_from_config.db",
            ["DataSync:BatchSize"] = "200",
            ["DataSync:SyncIntervalSeconds"] = "10"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
        services.AddDataSyncFromConfig(configuration, "DataSync");
        var provider = services.BuildServiceProvider();
        var resolvedConfig = provider.GetService<DataSyncConfig>();
        Assert.NotNull(resolvedConfig);
        Assert.Equal("/tmp/test_from_config.db", resolvedConfig!.LocalDbPath);
        Assert.Equal(200, resolvedConfig.BatchSize);
        Assert.Equal(10, resolvedConfig.SyncIntervalSeconds);
        var engine = provider.GetService<SyncEngine>();
        Assert.NotNull(engine);
    }

    [Fact]
    public void AddDataSync_Defaults_ArePreserved()
    {
        var services = new ServiceCollection();
        services.AddDataSync(cfg => { cfg.LocalDbPath = "/tmp/test.db"; });
        var provider = services.BuildServiceProvider();
        var config = provider.GetService<DataSyncConfig>();
        Assert.NotNull(config);
        Assert.Equal(100, config!.BatchSize);
        Assert.Equal(5, config!.SyncIntervalSeconds);
        Assert.Equal(3, config!.MaxRetryCount);
        Assert.True(config!.EnableUpsert);
        Assert.True(config!.EnableCleanup);
    }

    [Fact]
    public void AddDataSync_EmptyRemoteTargets_IsAllowed()
    {
        var services = new ServiceCollection();
        services.AddDataSync(cfg => { cfg.LocalDbPath = "/tmp/test.db"; });
        var provider = services.BuildServiceProvider();
        var config = provider.GetService<DataSyncConfig>();
        Assert.NotNull(config);
        Assert.Empty(config!.RemoteTargets);
    }
}
