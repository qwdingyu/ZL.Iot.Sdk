using ZL.DataSync;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;
using SqlSugar;

namespace ZL.DataSync.Tests.Infrastructure;

/// <summary>
/// WatermarkStore 单元测试（使用真实 SQLite 内存数据库）。
/// </summary>
public class WatermarkStoreTests : IDisposable
{
    private readonly SqlSugarClient _localDb;
    private readonly string _dbPath;
    private WatermarkStore? _store;

    public WatermarkStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"wal_test_{Guid.NewGuid()}.db");
        _localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={_dbPath}",
            IsAutoCloseConnection = false
        });
        _store = new WatermarkStore(_localDb);
        _store.EnsureTable();
    }

    public void Dispose()
    {
        _store?.Dispose();
        _localDb.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void EnsureTable_CreatesTable_IfNotExists()
    {
        // Arrange & Act - 构造时调用 EnsureTable
        // Assert - 不抛异常即成功
        Assert.NotNull(_store);
    }

    [Fact]
    public void WriteWatermark_StoresValue()
    {
        // Arrange
        var wmStore = new WatermarkStore(_localDb);
        wmStore.EnsureTable();

        // Act
        wmStore.WriteWatermark("test_table", "target1", "2025-01-01T00:00:00Z");

        // Assert
        var result = wmStore.ReadWatermark("test_table", "target1");
        Assert.Equal("2025-01-01T00:00:00Z", result);

        wmStore.Dispose();
    }

    [Fact]
    public void ReadWatermark_ReturnsNull_ForNonExistingKey()
    {
        // Arrange
        var wmStore = new WatermarkStore(_localDb);
        wmStore.EnsureTable();

        // Act
        var result = wmStore.ReadWatermark("nonexistent", "target1");

        // Assert
        Assert.Null(result);

        wmStore.Dispose();
    }

    [Fact]
    public void WriteWatermark_UpdatesExistingValue()
    {
        // Arrange
        var wmStore = new WatermarkStore(_localDb);
        wmStore.EnsureTable();
        wmStore.WriteWatermark("test_table", "target1", "initial");

        // Act
        wmStore.WriteWatermark("test_table", "target1", "updated");

        // Assert
        var result = wmStore.ReadWatermark("test_table", "target1");
        Assert.Equal("updated", result);

        wmStore.Dispose();
    }

    [Fact]
    public void WriteWatermark_SupportsMultipleKeys()
    {
        // Arrange
        var wmStore = new WatermarkStore(_localDb);
        wmStore.EnsureTable();

        // Act
        wmStore.WriteWatermark("table1", "target1", "wm1");
        wmStore.WriteWatermark("table1", "target2", "wm2");
        wmStore.WriteWatermark("table2", "target1", "wm3");

        // Assert
        Assert.Equal("wm1", wmStore.ReadWatermark("table1", "target1"));
        Assert.Equal("wm2", wmStore.ReadWatermark("table1", "target2"));
        Assert.Equal("wm3", wmStore.ReadWatermark("table2", "target1"));

        wmStore.Dispose();
    }

    [Fact]
    public void GetLastSyncTime_ReturnsTimestamp()
    {
        // Arrange
        var wmStore = new WatermarkStore(_localDb);
        wmStore.EnsureTable();
        wmStore.WriteWatermark("test_table", "target1", "2025-01-01T00:00:00Z");

        // Act
        var result = wmStore.GetLastSyncTime("test_table", "target1");

        // Assert
        Assert.NotNull(result);
        Assert.True(result > DateTime.MinValue);

        wmStore.Dispose();
    }

    [Fact]
    public void GetLastSyncTime_ReturnsNull_ForNonExistingKey()
    {
        // Arrange
        var wmStore = new WatermarkStore(_localDb);
        wmStore.EnsureTable();

        // Act
        var result = wmStore.GetLastSyncTime("nonexistent", "target1");

        // Assert
        Assert.Null(result);

        wmStore.Dispose();
    }

    [Fact]
    public void WriteWatermark_HandlesEmptyValues()
    {
        // Arrange
        var wmStore = new WatermarkStore(_localDb);
        wmStore.EnsureTable();

        // Act
        wmStore.WriteWatermark("test_table", "target1", "");

        // Assert
        var result = wmStore.ReadWatermark("test_table", "target1");
        Assert.Equal("", result);

        wmStore.Dispose();
    }
}
