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
}
