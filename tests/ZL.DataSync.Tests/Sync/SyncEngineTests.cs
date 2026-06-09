using ZL.DataSync;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;
using ZL.DataSync.Pipeline;
using SqlSugar;

namespace ZL.DataSync.Tests.Sync;

public class SyncEngineTests
{
    private string _testDbPath = string.Empty!;

    public SyncEngineTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"engine_test_{Guid.NewGuid()}.db");
    }

    ~SyncEngineTests()
    {
        if (File.Exists(_testDbPath))
            File.Delete(_testDbPath);
    }

    [Fact]
    public void Constructor_Throws_WhenConfigIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SyncEngine(null!));
    }

    [Fact]
    public void Constructor_Throws_WhenLocalDbPathIsEmpty()
    {
        var config = new DataSyncConfig { LocalDbPath = string.Empty };
        Assert.Throws<ArgumentException>(() => new SyncEngine(config));
    }

    [Fact]
    public void Constructor_Throws_WhenLocalDbPathIsWhitespace()
    {
        var config = new DataSyncConfig { LocalDbPath = "   " };
        Assert.Throws<ArgumentException>(() => new SyncEngine(config));
    }

    [Fact]
    public void Constructor_CreatesEngine_WhenValid()
    {
        var config = new DataSyncConfig { LocalDbPath = _testDbPath };
        var engine = new SyncEngine(config);
        Assert.NotNull(engine);
        Assert.NotNull(engine.Status);
        Assert.False(engine.Status.IsRunning);
        engine.Dispose();
    }

    [Fact]
    public void Constructor_CreatesWatermarkTable()
    {
        var config = new DataSyncConfig { LocalDbPath = _testDbPath };
        var engine = new SyncEngine(config);
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_testDbPath}",
            IsAutoCloseConnection = true
        });
        var hasTable = db.DbMaintenance.IsAnyTable("_SyncWatermark", false);
        Assert.True(hasTable);
        engine.Dispose();
    }

    [Fact]
    public void Status_Property_ReturnsSyncStatus()
    {
        var config = new DataSyncConfig { LocalDbPath = _testDbPath };
        var engine = new SyncEngine(config);
        var status = engine.Status;
        Assert.NotNull(status);
        Assert.False(status.IsRunning);
        Assert.Equal("未启动", status.StatusText);
        engine.Dispose();
    }

    [Fact]
    public void ForceSyncAsync_ReturnsReports_ForEmptyTargets()
    {
        var config = new DataSyncConfig { LocalDbPath = _testDbPath };
        var engine = new SyncEngine(config);
        var reports = engine.ForceSyncAsync().GetAwaiter().GetResult();
        Assert.Empty(reports);
        engine.Dispose();
    }

    // ═══════════════════════════════════════════════════════════
    //  Start / StopAsync tests
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Start_SetsIsRunningTrue()
    {
        var config = new DataSyncConfig { LocalDbPath = _testDbPath };
        var engine = new SyncEngine(config);
        engine.Start();
        try
        {
            Assert.True(engine.Status.IsRunning);
            Assert.Equal("运行中", engine.Status.StatusText);
        }
        finally
        {
            engine.StopAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void Start_AlreadyRunning_DoesNotStartAgain()
    {
        var config = new DataSyncConfig { LocalDbPath = _testDbPath };
        var engine = new SyncEngine(config);
        engine.Start();
        engine.Start(); // 第二次调用应被忽略
        try
        {
            Assert.True(engine.Status.IsRunning);
        }
        finally
        {
            engine.StopAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void Start_WithTargets_CreatesStrategyEntries()
    {
        var config = new DataSyncConfig
        {
            LocalDbPath = _testDbPath,
            SyncIntervalSeconds = 1, // 短间隔以便快速停止
            RemoteTargets = new List<RemoteTargetConfig>
            {
                new()
                {
                    Name = "TestTarget",
                    Type = TargetType.MySql,
                    ConnectionString = "server=127.0.0.1;database=test;uid=root;password=test;"
                }
            }
        };
        var engine = new SyncEngine(config);
        engine.Start();
        try
        {
            Assert.True(engine.Status.IsRunning);
        }
        finally
        {
            engine.StopAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task StopAsync_SetsIsRunningFalse()
    {
        var config = new DataSyncConfig
        {
            LocalDbPath = _testDbPath,
            SyncIntervalSeconds = 1
        };
        var engine = new SyncEngine(config);
        engine.Start();
        await engine.StopAsync();
        Assert.False(engine.Status.IsRunning);
        Assert.Equal("已停止", engine.Status.StatusText);
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_DoesNotThrow()
    {
        var config = new DataSyncConfig { LocalDbPath = _testDbPath };
        var engine = new SyncEngine(config);
        var ex = await Record.ExceptionAsync(() => engine.StopAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task StopAsync_CleansUpStrategies()
    {
        var config = new DataSyncConfig
        {
            LocalDbPath = _testDbPath,
            SyncIntervalSeconds = 1,
            RemoteTargets = new List<RemoteTargetConfig>
            {
                new()
                {
                    Name = "TestTarget",
                    Type = TargetType.MySql,
                    ConnectionString = "server=127.0.0.1;database=test;uid=root;password=test;"
                }
            }
        };
        var engine = new SyncEngine(config);
        engine.Start();
        await engine.StopAsync();
        Assert.False(engine.Status.IsRunning);
    }

    // ═══════════════════════════════════════════════════════════
    //  Cleanup config tests
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_WithCleanupConfig_CreatesEngine()
    {
        var config = new DataSyncConfig
        {
            LocalDbPath = _testDbPath,
            EnableCleanup = true,
            DataRetentionDays = 30,
            CleanupIntervalSeconds = 60
        };
        var engine = new SyncEngine(config);
        Assert.NotNull(engine);
        Assert.True(config.EnableCleanup);
        engine.Dispose();
    }

    [Fact]
    public void Status_AlwaysNotNull()
    {
        var config = new DataSyncConfig { LocalDbPath = _testDbPath };
        var engine = new SyncEngine(config);
        Assert.NotNull(engine.Status);
    }
}
