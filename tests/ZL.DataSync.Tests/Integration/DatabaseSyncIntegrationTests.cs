using ZL.DataSync;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;
using ZL.DataSync.Pipeline;
using SqlSugar;
using Xunit.Abstractions;

namespace ZL.DataSync.Tests.Integration;

/// <summary>
/// 端到端集成测试：使用真实 SQLite 和 MySQL。
/// </summary>
public class DatabaseSyncIntegrationTests : IDisposable
{
    private readonly string _sqlitePath;
    private readonly string _mySqlDb = "zldatasync_test";
    private readonly string _mySqlConnectionString;
    private bool _mySqlDbCreated;

    private readonly ITestOutputHelper? _output;

    public DatabaseSyncIntegrationTests(ITestOutputHelper? output = null)
    {
        _output = output;
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"e2e_test_{Guid.NewGuid()}.db");
        _mySqlConnectionString = $"server=127.0.0.1;database={_mySqlDb};uid=root;password=mes;charset=utf8mb4;Allow User Variables=True;";
    }

    public void Dispose()
    {
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);
        try
        {
            using var setupDb = new SqlSugarClient(new ConnectionConfig
            {
                DbType = SqlSugar.DbType.MySql,
                ConnectionString = "server=127.0.0.1;uid=root;password=mes;charset=utf8mb4;",
                IsAutoCloseConnection = true
            });
            setupDb.Ado.ExecuteCommand($"DROP DATABASE IF EXISTS `{_mySqlDb}`");
        }
        catch { }
    }

    private bool IsMySqlAvailable()
    {
        try
        {
            using var conn = new SqlSugarClient(new ConnectionConfig
            {
                DbType = SqlSugar.DbType.MySql,
                ConnectionString = "server=127.0.0.1;uid=root;password=mes;charset=utf8mb4;",
                IsAutoCloseConnection = true
            });
            conn.Ado.ExecuteCommand("SELECT 1");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SetupMySqlDatabase()
    {
        if (_mySqlConnectionString == null) return;

        if (!IsMySqlAvailable())
            throw new Exception("MySQL 不可用，跳过集成测试");

        using var setupDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.MySql,
            ConnectionString = "server=127.0.0.1;uid=root;password=mes;charset=utf8mb4;",
            IsAutoCloseConnection = true
        });
        
        _output?.WriteLine($"[Setup] Creating database {_mySqlDb}...");
        setupDb.Ado.ExecuteCommand($"CREATE DATABASE IF NOT EXISTS `{_mySqlDb}` DEFAULT CHARACTER SET utf8mb4");
        _output?.WriteLine($"[Setup] Database created.");
        
        // 验证数据库确实存在
        var verify = setupDb.Ado.SqlQuery<string>($"SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '{_mySqlDb}'");
        _output?.WriteLine($"[Setup] DB verify after CREATE: {(verify?.Count > 0 ? "EXISTS" : "NOT FOUND")}");
        
        _mySqlDbCreated = true;
    }

    private void SetupSQLite(string tableName, int recordCount = 5)
    {
        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_sqlitePath}",
            IsAutoCloseConnection = true
        });
        localDb.Ado.ExecuteCommand($"CREATE TABLE IF NOT EXISTS `{tableName}` (" +
            "`Id` INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "`StationCode` TEXT, " +
            "`BarCode` TEXT, " +
            "`ProcessTime` DATETIME, " +
            "`_Synced` INTEGER DEFAULT 0, " +
            "`_SyncTime` DATETIME" +
            ")");

        for (int i = 1; i <= recordCount; i++)
        {
            localDb.Ado.ExecuteCommand(
                $"INSERT INTO `{tableName}` (`StationCode`, `BarCode`, `ProcessTime`, `_Synced`, `_SyncTime`) VALUES (@sc, @bc, @pt, 0, NULL)",
                new SugarParameter("@sc", $"STATION_{i}"),
                new SugarParameter("@bc", $"BC_{i}"),
                new SugarParameter("@pt", DateTime.UtcNow.AddMinutes(-i))
            );
        }
    }

    [Fact]
    public void Test1_Configuration_CanBeCreated()
    {
        var config = new DataSyncConfig
        {
            LocalDbPath = _sqlitePath,
            BatchSize = 100,
            SyncIntervalSeconds = 5,
            RemoteTargets = new List<RemoteTargetConfig>
            {
                new()
                {
                    Name = "MySQL-Test",
                    Type = TargetType.MySql,
                    ConnectionString = _mySqlConnectionString,
                    TableMappings = new Dictionary<string, string>
                    {
                        { "test_table", "test_table" }
                    }
                }
            }
        };

        Assert.Equal(_sqlitePath, config.LocalDbPath);
        Assert.Equal(100, config.BatchSize);
        Assert.Single(config.RemoteTargets);
        Assert.Equal("MySQL-Test", config.RemoteTargets[0].Name);
        Assert.Equal(TargetType.MySql, config.RemoteTargets[0].Type);
    }

    [Fact]
    public void Test2_DatabaseSyncStrategy_CreatesTableAndSyncsData()
    {
        if (!IsMySqlAvailable())
            throw new Exception("MySQL 不可用，跳过集成测试");

        SetupMySqlDatabase();
        SetupSQLite("test_table", 5);

        var target = new RemoteTargetConfig
        {
            Name = "MySQL-Test",
            Type = TargetType.MySql,
            ConnectionString = _mySqlConnectionString,
            TableMappings = new Dictionary<string, string>
            {
                { "test_table", "synced_data" }
            }
        };

        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_sqlitePath}",
            IsAutoCloseConnection = false
        });

        using var strategy = new DatabaseSyncStrategy(target, new TestLogger());

        _output?.WriteLine("[Test2] Starting SyncTableAsync...");
        var report = strategy.SyncTableAsync("test_table", "synced_data", 100, localDb, CancellationToken.None).GetAwaiter().GetResult();

        _output?.WriteLine($"[Test2] Report: Success={report.Success}, Synced={report.SyncedCount}, Failed={report.FailedCount}, Error={report.LastError}");
        if (!report.Success) _output?.WriteLine($"[Test2] Logger messages: {string.Join("; ", strategy.Logger.Messages)}");

        Assert.True(report.Success, $"同步失败: {report.LastError}");
        Assert.True(report.HasData);
        Assert.Equal(5, report.TargetCount);
        Assert.Equal(5, report.SyncedCount);
        Assert.Equal(0, report.FailedCount);

        // 验证 MySQL 中数据已写入
        using var mySqlDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.MySql,
            ConnectionString = _mySqlConnectionString,
            IsAutoCloseConnection = true
        });
        var mysqlCount = mySqlDb.Queryable<dynamic>().AS("synced_data").Count();
        Assert.Equal(5, mysqlCount);

        // 验证本地 _Synced 标记已更新
        var syncedCount = localDb.Queryable<dynamic>().AS("test_table").Where("_Synced = 1").Count();
        Assert.Equal(5, syncedCount);
    }

    [Fact]
    public void Test3_DatabaseSyncStrategy_HandlesNoData()
    {
        if (!IsMySqlAvailable())
            throw new Exception("MySQL 不可用，跳过集成测试");

        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_sqlitePath}",
            IsAutoCloseConnection = false
        });
        localDb.Ado.ExecuteCommand("CREATE TABLE IF NOT EXISTS `empty_table` (Id INTEGER PRIMARY KEY, ProcessTime DATETIME, _Synced INTEGER DEFAULT 0)");

        var target = new RemoteTargetConfig
        {
            Name = "MySQL-Empty",
            Type = TargetType.MySql,
            ConnectionString = _mySqlConnectionString
        };

        using var strategy = new DatabaseSyncStrategy(target, new TestLogger());
        var report = strategy.SyncTableAsync("empty_table", null, 100, localDb, CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(report.Success);
        Assert.False(report.HasData);
        Assert.Equal(0, report.TargetCount);
    }

    [Fact]
    public void Test4_DatabaseSyncStrategy_BatchSize_LimitsRecords()
    {
        if (!IsMySqlAvailable())
            throw new Exception("MySQL 不可用，跳过集成测试");

        SetupMySqlDatabase();
        SetupSQLite("batch_table", 20);

        var target = new RemoteTargetConfig
        {
            Name = "MySQL-Batch",
            Type = TargetType.MySql,
            ConnectionString = _mySqlConnectionString
        };

        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_sqlitePath}",
            IsAutoCloseConnection = false
        });

        using var strategy = new DatabaseSyncStrategy(target, new TestLogger());

        var report = strategy.SyncTableAsync("batch_table", null, 5, localDb, CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(report.Success);
        Assert.True(report.SyncedCount <= 5, $"批次大小应为 5，实际同步 {report.SyncedCount}");
    }

    [Fact]
    public void Test5_DatabaseSyncStrategy_Upsert_Works()
    {
        if (!IsMySqlAvailable())
            throw new Exception("MySQL 不可用，跳过集成测试");

        SetupMySqlDatabase();
        SetupSQLite("upsert_table", 3);

        var target = new RemoteTargetConfig
        {
            Name = "MySQL-Upsert",
            Type = TargetType.MySql,
            ConnectionString = _mySqlConnectionString,
            TableMappings = new Dictionary<string, string>
            {
                { "upsert_table", "upsert_data" }
            }
        };

        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_sqlitePath}",
            IsAutoCloseConnection = false
        });

        using var strategy = new DatabaseSyncStrategy(target, new TestLogger());

        // 第一次同步
        var report1 = strategy.SyncTableAsync("upsert_table", "upsert_data", 100, localDb, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(report1.Success);
        Assert.Equal(3, report1.SyncedCount);

        // 再次同步（应该没有新数据）
        var report2 = strategy.SyncTableAsync("upsert_table", "upsert_data", 100, localDb, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(report2.Success);
        Assert.Equal(0, report2.TargetCount);
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
