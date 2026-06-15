using Microsoft.Extensions.Logging.Abstractions;
using ZL.Biz.Execute.Biz;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Runtime;

namespace ZL.Iot.Runner.Tests;

/// <summary>
/// RunnerWriteQueue 统一写入队列测试（P1-1 支柱 A）。
/// 验证历史 / 规则 SQL / FieldMapping 三类命令都能正确串行落库，
/// 以及 FieldMapping 反馈写回（OnCommitted）在落库成功后触发。
/// </summary>
public sealed class RunnerWriteQueueTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"runner_wq_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task HistoryCommand_PersistsToHistoryTable_WithEventTime()
    {
        using var executor = CreateExecutor();
        var options = new StorageOptions { TableName = "iot_tag_history", BatchSize = 2, FlushIntervalMs = 100 };

        await using (var queue = new RunnerWriteQueue(options, executor, executor, NullLogger<RunnerWriteQueue>.Instance))
        {
            Assert.True(queue.TryEnqueue(new HistoryWriteCommand
            {
                DeviceCode = "DEV-1", TagId = "temp", TagType = "D", Value = 12.5, DataType = "float", EventTime = DateTimeOffset.Now
            }));
            Assert.True(queue.TryEnqueue(new HistoryWriteCommand
            {
                DeviceCode = "DEV-1", TagId = "press", TagType = "D", Value = 7, DataType = "int", EventTime = DateTimeOffset.Now
            }));
        } // DisposeAsync flush

        var rows = await executor.ExecuteQueryAsync(
            "SELECT device_code, tag_id, value_text, value_number, event_time, process_time FROM iot_tag_history ORDER BY tag_id");

        Assert.Equal(2, rows.Count);
        Assert.Equal("press", rows[0]["tag_id"]);
        Assert.Equal("temp", rows[1]["tag_id"]);
        Assert.Equal(12.5, Convert.ToDouble(rows[1]["value_number"]));
        // event_time 与 process_time 两列都应落库（采集时刻 vs 落库时刻分离）。
        Assert.NotNull(rows[1]["event_time"]);
        Assert.NotNull(rows[1]["process_time"]);
    }

    [Fact]
    public async Task RawSqlCommand_ExecutesAgainstDatabase()
    {
        using var executor = CreateExecutor();
        await executor.ExecuteNonQueryAsync("CREATE TABLE alerts (id INTEGER PRIMARY KEY, msg TEXT NOT NULL)");
        var options = new StorageOptions { TableName = "iot_tag_history", FlushIntervalMs = 100 };

        await using (var queue = new RunnerWriteQueue(options, executor, executor, NullLogger<RunnerWriteQueue>.Instance))
        {
            Assert.True(queue.TryEnqueue(new RawSqlCommand
            {
                BizCode = "E1", ExeType = "M", Sql = "INSERT INTO alerts (msg) VALUES ('over-temp')"
            }));
        }

        var rows = await executor.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM alerts");
        Assert.Equal(1, Convert.ToInt32(rows[0]["cnt"]));
    }

    [Fact]
    public async Task TableInsertCommand_PersistsRows_AndFiresFeedbackAfterCommit()
    {
        using var executor = CreateExecutor();
        var options = new StorageOptions { TableName = "iot_tag_history", FlushIntervalMs = 100 };

        var feedbackFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rowsPersistedAtFeedback = -1;

        await using (var queue = new RunnerWriteQueue(options, executor, executor, NullLogger<RunnerWriteQueue>.Instance))
        {
            Assert.True(queue.TryEnqueue(new TableInsertCommand
            {
                DeviceCode = "DEV-1",
                TableName = "axis_data",
                Columns = new List<FieldMappingRule>
                {
                    new() { Name = "axis_no", DataType = "Int32" },
                    new() { Name = "value_real", DataType = "double" }
                },
                Rows = new List<Dictionary<string, object?>>
                {
                    new() { ["axis_no"] = 1, ["value_real"] = 3.14, ["ProcessTime"] = DateTime.UtcNow }
                },
                OnCommitted = () =>
                {
                    // 回调时数据应已落库 → 此处能查到 1 行（防丢数据语义）。
                    var r = executor.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM axis_data").GetAwaiter().GetResult();
                    rowsPersistedAtFeedback = Convert.ToInt32(r[0]["cnt"]);
                    feedbackFired.TrySetResult(true);
                }
            }));

            // 等待回调（落库后触发），最多 5s。
            var done = await Task.WhenAny(feedbackFired.Task, Task.Delay(5000));
            Assert.True(ReferenceEquals(done, feedbackFired.Task), "反馈回调未在超时内触发");
        }

        Assert.Equal(1, rowsPersistedAtFeedback); // 回调触发时数据已落库
    }

    [Fact]
    public async Task TableInsertCommand_InvalidTableName_DoesNotCrash_FeedbackNotFired()
    {
        using var executor = CreateExecutor();
        var options = new StorageOptions { TableName = "iot_tag_history", FlushIntervalMs = 100 };
        var feedbackFired = false;

        await using (var queue = new RunnerWriteQueue(options, executor, executor, NullLogger<RunnerWriteQueue>.Instance))
        {
            // 非法表名：sink 会抛 ArgumentException，被消费者捕获；落库失败 → 不触发反馈。
            Assert.True(queue.TryEnqueue(new TableInsertCommand
            {
                DeviceCode = "DEV-1",
                TableName = "axis; DROP TABLE x",
                Columns = new List<FieldMappingRule> { new() { Name = "v", DataType = "double" } },
                Rows = new List<Dictionary<string, object?>> { new() { ["v"] = 1.0 } },
                OnCommitted = () => { feedbackFired = true; }
            }));

            await Task.Delay(500); // 给消费者处理时间
        }

        Assert.False(feedbackFired); // 落库失败不反馈（防止 PLC 误以为采集成功）
    }

    private SqlSugarExecutor CreateExecutor()
    {
        return new SqlSugarExecutor(
            NullLogger<SqlSugarExecutor>.Instance,
            SqlSugar.DbType.Sqlite,
            $"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
        }
    }
}
