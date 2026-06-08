using ZL.DataSync.Config;
using ZL.DataSync.Pipeline;
using ZL.DataSync.Infrastructure;
using SqlSugar;
using ZL.DataSync.Tests.Sync;

namespace ZL.DataSync.Tests.Pipeline;

public class HttpSyncStrategyTests
{
    private readonly string _testDbPath;
    private readonly TestLogger _logger = new();

    public HttpSyncStrategyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"http_test_{Guid.NewGuid()}.db");
    }

    ~HttpSyncStrategyTests()
    {
        if (File.Exists(_testDbPath))
            File.Delete(_testDbPath);
    }

    [Fact]
    public void TargetName_ReturnsCorrectValue()
    {
        var config = new HttpUploadConfig { Endpoint = "http://test.com/api" };
        var strategy = new HttpSyncStrategy(config, "TestTarget", _logger);
        Assert.Equal("TestTarget", strategy.TargetName);
        strategy.Dispose();
    }

    [Fact]
    public async Task SyncTableAsync_ReturnsOk_WhenNoData()
    {
        var config = new HttpUploadConfig { Endpoint = "http://test.com/api", TimeoutSeconds = 5 };
        using var strategy = new HttpSyncStrategy(config, "TestTarget", _logger);
        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_testDbPath}",
            IsAutoCloseConnection = true
        });
        localDb.Ado.ExecuteCommand("CREATE TABLE IF NOT EXISTS `empty_table` (Id INTEGER PRIMARY KEY, ProcessTime DATETIME, _Synced INTEGER DEFAULT 0)");
        var report = await strategy.SyncTableAsync("empty_table", null, 100, localDb, CancellationToken.None);
        Assert.True(report.Success);
        Assert.Equal(0, report.TargetCount);
        Assert.Equal(0, report.SyncedCount);
    }

    [Fact]
    public async Task SyncTableAsync_ReturnsFail_WhenEndpointNotConfigured()
    {
        var config = new HttpUploadConfig { Endpoint = "" };
        using var strategy = new HttpSyncStrategy(config, "TestTarget", _logger);
        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_testDbPath}",
            IsAutoCloseConnection = true
        });
        localDb.Ado.ExecuteCommand("CREATE TABLE IF NOT EXISTS `data_table` (Id INTEGER PRIMARY KEY, ProcessTime DATETIME, _Synced INTEGER DEFAULT 0)");
        localDb.Ado.ExecuteCommand("INSERT INTO data_table (Id, ProcessTime, _Synced) VALUES (1, datetime('now'), 0)");
        var report = await strategy.SyncTableAsync("data_table", null, 100, localDb, CancellationToken.None);
        Assert.False(report.Success);
        Assert.NotNull(report.LastError);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var config = new HttpUploadConfig { Endpoint = "http://test.com/api" };
        var strategy = new HttpSyncStrategy(config, "TestTarget", _logger);
        strategy.Dispose();
        strategy.Dispose(); // 多次不应抛异常
    }

    [Fact]
    public void Constructor_ThrowsOnNullConfig()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HttpSyncStrategy(null!, "test", _logger));
    }

    [Fact]
    public void Constructor_ThrowsOnNullTargetName()
    {
        var config = new HttpUploadConfig { Endpoint = "http://localhost" };
        Assert.Throws<ArgumentNullException>(() =>
            new HttpSyncStrategy(config, null!, _logger));
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        var config = new HttpUploadConfig { Endpoint = "http://localhost" };
        Assert.Throws<ArgumentNullException>(() =>
            new HttpSyncStrategy(config, "test", null!));
    }

    [Fact]
    public async Task SyncTableAsync_TableEndpoint_UsesPerTableEndpoint()
    {
        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_testDbPath}",
            IsAutoCloseConnection = false
        });
        localDb.Ado.ExecuteCommand("CREATE TABLE IF NOT EXISTS `special_table` (Id INTEGER PRIMARY KEY, ProcessTime DATETIME, _Synced INTEGER DEFAULT 0)");
        localDb.Ado.ExecuteCommand("INSERT INTO special_table (Id, ProcessTime, _Synced) VALUES (1, datetime('now'), 0)");

        var config = new HttpUploadConfig
        {
            Endpoint = "http://default-endpoint.local/upload",
            TableEndpoints = new Dictionary<string, string>
            {
                { "special_table", "http://special-endpoint.local/upload" }
            }
        };
        using var strategy = new HttpSyncStrategy(config, "test", _logger);
        var report = await strategy.SyncTableAsync("special_table", null, 100, localDb, CancellationToken.None);

        // 因为没有真实 HTTP 服务器，应返回失败
        Assert.False(report.Success);
        // 但不应是 "未配置 HTTP Endpoint" 错误
        Assert.DoesNotContain("未配置 HTTP Endpoint", report.LastError);
    }

    [Fact]
    public async Task SyncTableAsync_MissingEndpoint_ReturnsFail()
    {
        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_testDbPath}",
            IsAutoCloseConnection = false
        });
        localDb.Ado.ExecuteCommand("CREATE TABLE IF NOT EXISTS `data_table2` (Id INTEGER PRIMARY KEY, ProcessTime DATETIME, _Synced INTEGER DEFAULT 0)");
        localDb.Ado.ExecuteCommand("INSERT INTO data_table2 (Id, ProcessTime, _Synced) VALUES (1, datetime('now'), 0)");

        // 没有配置 Endpoint，也没有 TableEndpoints
        var config = new HttpUploadConfig { TableEndpoints = new Dictionary<string, string>() };
        using var strategy = new HttpSyncStrategy(config, "test", _logger);
        var report = await strategy.SyncTableAsync("data_table2", null, 100, localDb, CancellationToken.None);

        Assert.False(report.Success);
        Assert.NotNull(report.LastError);
        Assert.Contains("未配置 HTTP Endpoint", report.LastError);
    }
}
