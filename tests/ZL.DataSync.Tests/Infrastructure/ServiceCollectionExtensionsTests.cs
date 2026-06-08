using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZL.DataSync;
using ZL.DataSync.Config;

namespace ZL.DataSync.Tests.Infrastructure;

/// <summary>
/// ServiceCollectionExtensions 单元测试。
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDataSync_RegistersConfigAndEngine()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDataSync(cfg =>
        {
            cfg.LocalDbPath = "/tmp/test.db";
            cfg.RemoteTargets = new List<RemoteTargetConfig>
            {
                new() { Name = "Test", Type = TargetType.MySql, ConnectionString = "server=test;" }
            };
        });

        var provider = services.BuildServiceProvider();

        // Assert
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
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            services.AddDataSyncFromConfig(config, "NonExistentSection"));
    }

    [Fact]
    public void AddDataSyncFromConfig_RegistersConfigAndEngine()
    {
        // Arrange
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

        // Act
        services.AddDataSyncFromConfig(configuration, "DataSync");
        var provider = services.BuildServiceProvider();

        // Assert
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
        // Arrange
        var services = new ServiceCollection();

        // Act - 只设置必要字段
        services.AddDataSync(cfg =>
        {
            cfg.LocalDbPath = "/tmp/test.db";
        });

        var provider = services.BuildServiceProvider();
        var config = provider.GetService<DataSyncConfig>();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(100, config!.BatchSize);           // 默认值
        Assert.Equal(5, config!.SyncIntervalSeconds);   // 默认值
        Assert.Equal(3, config!.MaxRetryCount);         // 默认值
        Assert.True(config!.EnableUpsert);               // 默认值
        Assert.True(config!.EnableCleanup);              // 默认值
    }

    [Fact]
    public void AddDataSync_EmptyRemoteTargets_IsAllowed()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDataSync(cfg =>
        {
            cfg.LocalDbPath = "/tmp/test.db";
            cfg.RemoteTargets = new();
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var config = provider.GetService<DataSyncConfig>();
        Assert.NotNull(config);
        Assert.Empty(config!.RemoteTargets);
    }
}
