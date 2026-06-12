using ZL.DataSync;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;
using ZL.DataSync.Pipeline;
using SqlSugar;
using Xunit.Abstractions;

namespace ZL.DataSync.Tests.Integration;

/// <summary>
/// 端到端集成测试：使用真实 SQLite 和 MySQL。
///
/// 设计原则：
/// - 每个测试使用唯一的 SQLite 文件和独立的 MySQL 表名，避免竞争
/// - 测试目标明确：Test1=配置创建，Test2=建表+同步，Test3=空数据，
///   Test4=分批限制，Test5=_Synced 标记验证
/// - MySQL 连接字符串集中管理，密码通过环境变量 DATASYNC_MYSQL_PWD 覆盖（默认 mes）
/// </summary>
public class DatabaseSyncIntegrationTests : IDisposable
{
    private readonly string _sqlitePath;
    private readonly string _mySqlDb;
    private readonly string _mySqlConnectionString;

    private readonly ITestOutputHelper? _output;

    public DatabaseSyncIntegrationTests(ITestOutputHelper? output = null)
    {
        _output = output;
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"e2e_{GetType().Name}_{Guid.NewGuid():N}.db");
        // MySQL 数据库名限制 64 字符（63 安全），用 hash 截短
        var rawName = $"zldatasync_{GetType().Name}_{Guid.NewGuid():N}";
        _mySqlDb = rawName.Length > 63 ? $"zldatasync_{Math.Abs(GetType().Name.GetHashCode()):X8}_{Guid.NewGuid():N}"[..63] : rawName;
        var pwd = Environment.GetEnvironmentVariable("DATASYNC_MYSQL_PWD") ?? "mes";
        _mySqlConnectionString = $"server=127.0.0.1;database={_mySqlDb};uid=root;password={pwd};charset=utf8mb4;Allow User Variables=True;";
    }

    public void Dispose()
    {
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);

        var pwd = Environment.GetEnvironmentVariable("DATASYNC_MYSQL_PWD") ?? "mes";
        try
        {
            using var setupDb = new SqlSugarClient(new ConnectionConfig
            {
                DbType = SqlSugar.DbType.MySql,
                ConnectionString = $"server=127.0.0.1;uid=root;password={pwd};charset=utf8mb4;",
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
            var pwd = Environment.GetEnvironmentVariable("DATASYNC_MYSQL_PWD") ?? "mes";
            using var conn = new SqlSugarClient(new ConnectionConfig
            {
                DbType = SqlSugar.DbType.MySql,
                ConnectionString = $"server=127.0.0.1;uid=root;password={pwd};charset=utf8mb4;",
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

        var pwd = Environment.GetEnvironmentVariable("DATASYNC_MYSQL_PWD") ?? "mes";
        using var setupDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.MySql,
            ConnectionString = $"server=127.0.0.1;uid=root;password={pwd};charset=utf8mb4;",
            IsAutoCloseConnection = true
        });

        _output?.WriteLine($"[Setup] Creating database {_mySqlDb}...");
        setupDb.Ado.ExecuteCommand($"CREATE DATABASE IF NOT EXISTS `{_mySqlDb}` DEFAULT CHARACTER SET utf8mb4");
        _output?.WriteLine($"[Setup] Done.");
    }

    /// <summary>
    /// 创建 SQLite 表并插入测试数据。
    /// 表结构：Id (PK AUTOINCREMENT), StationCode, BarCode, ProcessTime, _Synced, _SyncTime
    /// </summary>
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
        // 目标：验证 DataSyncConfig 可正常设置所有属性
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
        // 目标：验证数据库策略能自动建表并同步数据，正确标记 _Synced
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
        var report = strategy.SyncTableAsync("test_table", "synced_data", 100, localDb, CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(report.Success, $"同步失败: {report.LastError}");
        Assert.True(report.HasData);
        Assert.Equal(5, report.TargetCount);
        Assert.Equal(5, report.SyncedCount);
        Assert.Equal(0, report.FailedCount);

        // 验证 MySQL 远程表中有数据
        using var mySqlDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.MySql,
            ConnectionString = _mySqlConnectionString,
            IsAutoCloseConnection = true
        });
        Assert.Equal(5, mySqlDb.Queryable<dynamic>().AS("synced_data").Count());

        // 验证本地 _Synced 已更新
        Assert.Equal(5, localDb.Queryable<dynamic>().AS("test_table").Where("_Synced = 1").Count());
    }

    [Fact]
    public void Test3_DatabaseSyncStrategy_HandlesNoData()
    {
        // 目标：验证空表场景返回成功但无数据
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
        // 目标：验证 batchSize 参数限制单次同步上限
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
    public void Test5_DatabaseSyncStrategy_SyncedMark_Verification()
    {
        // 目标：验证 _Synced 标记在第一次同步后正确更新，第二次同步无新数据
        if (!IsMySqlAvailable())
            throw new Exception("MySQL 不可用，跳过集成测试");

        SetupMySqlDatabase();
        SetupSQLite("sync_mark_table", 3);

        var target = new RemoteTargetConfig
        {
            Name = "MySQL-SyncMark",
            Type = TargetType.MySql,
            ConnectionString = _mySqlConnectionString,
            TableMappings = new Dictionary<string, string>
            {
                { "sync_mark_table", "sync_mark_data" }
            }
        };

        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_sqlitePath}",
            IsAutoCloseConnection = false
        });

        using var strategy = new DatabaseSyncStrategy(target, new TestLogger());

        // 第一次同步：3 条记录
        var report1 = strategy.SyncTableAsync("sync_mark_table", "sync_mark_data", 100, localDb, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(report1.Success);
        Assert.Equal(3, report1.SyncedCount);

        // 验证 _Synced = 1 的记录数
        var syncedCount = localDb.Queryable<dynamic>().AS("sync_mark_table").Where("_Synced = 1").Count();
        Assert.Equal(3, syncedCount);

        // 验证 MySQL 端有 3 条
        using var mySqlDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.MySql,
            ConnectionString = _mySqlConnectionString,
            IsAutoCloseConnection = true
        });
        Assert.Equal(3, mySqlDb.Queryable<dynamic>().AS("sync_mark_data").Count());

        // 第二次同步：无新数据
        var report2 = strategy.SyncTableAsync("sync_mark_table", "sync_mark_data", 100, localDb, CancellationToken.None).GetAwaiter().GetResult();
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
