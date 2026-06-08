using ZL.DataSync.Config;
using ZL.DataSync.Pipeline;
using ZL.DataSync.Infrastructure;
using SqlSugar;

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
        localDb.Ado.ExecuteCommand("CREATE TABLE IF NOT EXISTS `empty_table` (Id INTEGER PRIMARY KEY)");
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
        localDb.Insertable(new Dictionary<string, object?>
        {
            ["Id"] = 1, ["ProcessTime"] = DateTime.UtcNow, ["_Synced"] = false
        }).AS("data_table").ExecuteCommand();
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
}
