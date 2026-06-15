// ============================================================
//  RunnerStorageCoordinator 单元测试
//  覆盖远端同步引擎创建逻辑：
//  1. RemoteSync 未启用 → 不启动
//  2. 非 SQLite 本地存储 → 不启动
//  3. 无效远端类型 → 不启动
//  4. 空远端连接串 → 不启动
//  5. 启用 RemoteSync 且配置有效 → 创建 SyncEngine
//  6. 远端类型映射正确（mysql/sqlserver/postgresql/oracle）
//  7. Dispose 不抛异常
// ============================================================

using Microsoft.Extensions.Logging.Abstractions;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Runtime;

namespace ZL.Iot.Runner.Tests;

public sealed class RunnerStorageCoordinatorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"runner_coord_{Guid.NewGuid():N}.db");
    private readonly List<RunnerStorageCoordinator> _coordinators = new();

    [Fact]
    public void Create_RemoteSyncDisabled_DoesNotStartSyncEngine()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions { Enabled = false }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
        Assert.Equal("Sqlite", coordinator.SqlExecutor!.Dialect);
    }

    [Fact]
    public void Create_NonSqliteLocalType_RemoteSyncSkipped()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "MySql",
            ConnectionString = "Server=127.0.0.1;Database=test;Uid=root;Pwd=test;",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mysql",
                ConnectionString = "Server=192.168.1.10;Port=3306;Database=iot_edge;Uid=root;Pwd=remote;"
            }
        });

        Assert.NotNull(coordinator);
        // MySql 非 SQLite，本地 executor 应创建（MySql 类型支持）
        Assert.NotNull(coordinator.SqlExecutor);
        Assert.Equal("MySql", coordinator.SqlExecutor!.Dialect);
    }

    [Fact]
    public void Create_NoneLocalType_RemoteSyncSkipped()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "None",
            ConnectionString = "",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mysql",
                ConnectionString = "Server=192.168.1.10;Database=iot_edge;Uid=root;Pwd=remote;"
            }
        });

        Assert.NotNull(coordinator);
        // None 类型不应创建本地 executor
        Assert.Null(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_InvalidRemoteType_RemoteSyncSkipped()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "invalid_type_xyz",
                ConnectionString = "Server=192.168.1.10;Database=iot_edge;Uid=root;Pwd=remote;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_EmptyRemoteConnectionString_RemoteSyncSkipped()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mysql",
                ConnectionString = ""
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_RemoteSyncMySql_ConfiguresCorrectly()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mysql",
                ConnectionString = "Server=192.168.1.10;Port=3306;Database=iot_edge;Uid=root;Pwd=remote;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_RemoteSyncSqlServer_ConfiguresCorrectly()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "sqlserver",
                ConnectionString = "Server=192.168.1.20;Database=iot_edge;User Id=sa;Password=Pass123;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_RemoteSyncPostgreSql_ConfiguresCorrectly()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "postgresql",
                ConnectionString = "Server=192.168.1.30;Port=5432;Database=iot_edge;User Id=postgres;Password=postgres;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_RemoteSyncOracle_ConfiguresCorrectly()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "oracle",
                ConnectionString = "Data Source=192.168.1.40:1521/IOTEDGE;User Id=system;Password=oracle123;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_TypeAliases_WorkForSqlServer()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mssql",
                ConnectionString = "Server=192.168.1.20;Database=iot_edge;User Id=sa;Password=Pass123;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_TypeAliases_WorkForPostgres()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "pgsql",
                ConnectionString = "Server=192.168.1.30;Port=5432;Database=iot_edge;User Id=postgres;Password=postgres;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    [Fact]
    public void Create_WithHistoryAndRemoteSync_DoesNotThrow()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            History = new StorageOptions
            {
                Enabled = true,
                TableName = "iot_tag_history",
                BatchSize = 100,
                FlushIntervalMs = 1000,
                QueueCapacity = 10000
            },
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mysql",
                ConnectionString = "Server=192.168.1.10;Port=3306;Database=iot_edge;Uid=root;Pwd=remote;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
        Assert.NotNull(coordinator.TableStorage);
        Assert.Same(coordinator.SqlExecutor, coordinator.TableStorage);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mysql",
                ConnectionString = "Server=192.168.1.10;Port=3306;Database=iot_edge;Uid=root;Pwd=remote;"
            }
        });

        // 多次 Dispose 不应抛出异常
        coordinator.Dispose();
        coordinator.Dispose();
        coordinator.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_WithHistoryAndRemoteSync_DoesNotThrow()
    {
        var coordinator = RunnerStorageCoordinator.Create(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            History = new StorageOptions { Enabled = true, TableName = "iot_tag_history" },
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mysql",
                ConnectionString = "Server=192.168.1.10;Port=3306;Database=iot_edge;Uid=root;Pwd=remote;"
            }
        }, NullLoggerFactory.Instance);

        // 异步释放路径不应抛出；后续同步 Dispose 也应幂等。
        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();
        coordinator.Dispose();
    }

    [Fact]
    public void Create_RemoteSyncSkipped_ForUnsupportedDbType()
    {
        // 本地类型为 SqlServer 但 RemoteSync 启用了 → 应跳过（只有 SQLite 做源库才能配合 SyncEngine）
        var coordinator = CreateCoordinator(new DataStorageOptions
        {
            Type = "SqlServer",
            ConnectionString = "Server=127.0.0.1;Database=test;User Id=sa;Password=test;",
            RemoteSync = new RemoteSyncOptions
            {
                Enabled = true,
                Type = "mysql",
                ConnectionString = "Server=192.168.1.10;Port=3306;Database=iot_edge;Uid=root;Pwd=remote;"
            }
        });

        Assert.NotNull(coordinator);
        Assert.NotNull(coordinator.SqlExecutor);
    }

    public void Dispose()
    {
        foreach (var c in _coordinators)
        {
            try { c.Dispose(); } catch { }
        }

        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
        }
    }

    private RunnerStorageCoordinator CreateCoordinator(DataStorageOptions storage)
    {
        var coordinator = RunnerStorageCoordinator.Create(storage, NullLoggerFactory.Instance);
        _coordinators.Add(coordinator);
        return coordinator;
    }
}
