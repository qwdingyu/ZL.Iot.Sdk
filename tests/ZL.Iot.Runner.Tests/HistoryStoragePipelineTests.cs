using Microsoft.Extensions.Logging.Abstractions;
using ZL.Biz.Execute.Biz;
using ZL.Iot.Interface;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Runtime;
using ZL.Tag;

namespace ZL.Iot.Runner.Tests;

public sealed class HistoryStoragePipelineTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"runner_history_{Guid.NewGuid():N}.db");

    [Fact]
    public void TryEnqueue_WritesConfiguredTagHistory_ToSqlite()
    {
        var storage = new StorageOptions
        {
            TableName = "iot_tag_history",
            BatchSize = 2,
            FlushIntervalMs = 100,
            QueueCapacity = 10,
            Mappings =
            {
                new StorageMapping
                {
                    DeviceCode = "DEV-001",
                    TagId = "temperature",
                    TagType = "D"
                }
            }
        };
        using var executor = CreateExecutor();

        using (var pipeline = new HistoryStoragePipeline(
            storage,
            executor,
            NullLogger<HistoryStoragePipeline>.Instance))
        {
            Assert.True(pipeline.TryEnqueue("DEV-001", "temperature", 12.5, new TagItem
            {
                Id = "temperature",
                TagType = "D",
                DataTypeCode = "float"
            }));
            Assert.False(pipeline.TryEnqueue("DEV-001", "pressure", 5, new TagItem
            {
                Id = "pressure",
                TagType = "M",
                DataTypeCode = "int"
            }));
        }

        var rows = executor.ExecuteQueryAsync("SELECT device_code, tag_id, tag_type, data_type, value_text, value_number FROM iot_tag_history").GetAwaiter().GetResult();

        Assert.Single(rows);
        Assert.Equal("DEV-001", rows[0]["device_code"]);
        Assert.Equal("temperature", rows[0]["tag_id"]);
        Assert.Equal("D", rows[0]["tag_type"]);
        Assert.Equal("float", rows[0]["data_type"]);
        Assert.Equal("12.5", rows[0]["value_text"]);
        Assert.Equal(12.5, Convert.ToDouble(rows[0]["value_number"]));
    }

    [Fact]
    public async Task TryEnqueue_FlushesBeforeBatchFull_WhenIntervalElapsed()
    {
        var storage = new StorageOptions
        {
            TableName = "iot_tag_history",
            BatchSize = 10,
            FlushIntervalMs = 100,
            QueueCapacity = 10
        };
        using var executor = CreateExecutor();
        using var pipeline = new HistoryStoragePipeline(
            storage,
            executor,
            NullLogger<HistoryStoragePipeline>.Instance);

        Assert.True(pipeline.TryEnqueue("DEV-001", "temperature", 12.5, new TagItem
        {
            Id = "temperature",
            TagType = "M",
            DataTypeCode = "float"
        }));

        await Task.Delay(300);

        var rows = await executor.ExecuteQueryAsync("SELECT tag_id FROM iot_tag_history");
        Assert.Single(rows);
    }

    [Fact]
    public void Create_ExposesSameLocalExecutor_ForSqlAndTableStorage()
    {
        using var coordinator = RunnerStorageCoordinator.Create(new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = $"Data Source={_dbPath}",
            History = new StorageOptions
            {
                Enabled = true,
                TableName = "iot_tag_history"
            }
        }, NullLoggerFactory.Instance);

        Assert.NotNull(coordinator.SqlExecutor);
        Assert.NotNull(coordinator.TableStorage);
        Assert.Same(coordinator.SqlExecutor, coordinator.TableStorage);
        Assert.Equal("Sqlite", coordinator.SqlExecutor!.Dialect);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_dbPath);
        }
        catch
        {
        }
    }

    private SqlSugarExecutor CreateExecutor()
    {
        return new SqlSugarExecutor(
            NullLogger<SqlSugarExecutor>.Instance,
            SqlSugar.DbType.Sqlite,
            $"Data Source={_dbPath}");
    }
}
