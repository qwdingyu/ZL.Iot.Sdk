using Microsoft.Extensions.Logging.Abstractions;
using ZL.Biz.Execute.Biz;

namespace ZL.Iot.Runner.Tests;

/// <summary>
/// SqlSugarExecutor.ExecuteBatchNonQueryAsync 事务化测试（P2-3）。
/// 整批写入要么全部提交，要么全部回滚，不允许部分写入。
/// </summary>
public sealed class SqlSugarExecutorBatchTransactionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"runner_batchtx_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ExecuteBatch_AllValid_CommitsEveryRow()
    {
        using var executor = CreateExecutor();
        await executor.ExecuteNonQueryAsync(
            "CREATE TABLE batch_t (id INTEGER PRIMARY KEY, v TEXT NOT NULL)");

        var affected = await executor.ExecuteBatchNonQueryAsync(
            "INSERT INTO batch_t (id, v) VALUES (@id, @v)",
            new[]
            {
                new Dictionary<string, object> { ["id"] = 1, ["v"] = "a" },
                new Dictionary<string, object> { ["id"] = 2, ["v"] = "b" },
                new Dictionary<string, object> { ["id"] = 3, ["v"] = "c" }
            });

        Assert.Equal(3, affected);

        var rows = await executor.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM batch_t");
        Assert.Equal(3, Convert.ToInt32(rows[0]["cnt"]));
    }

    [Fact]
    public async Task ExecuteBatch_OneRowViolatesConstraint_RollsBackEntireBatch()
    {
        using var executor = CreateExecutor();
        await executor.ExecuteNonQueryAsync(
            "CREATE TABLE batch_t (id INTEGER PRIMARY KEY, v TEXT NOT NULL)");

        // 第二、三行复用 id=1，违反主键约束 → 整批必须回滚，第一行也不得留存。
        var batch = new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["v"] = "a" },
            new Dictionary<string, object> { ["id"] = 1, ["v"] = "dup" }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteBatchNonQueryAsync(
                "INSERT INTO batch_t (id, v) VALUES (@id, @v)", batch));

        // 关键断言：零部分写入。
        var rows = await executor.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM batch_t");
        Assert.Equal(0, Convert.ToInt32(rows[0]["cnt"]));
    }

    [Fact]
    public async Task ExecuteBatch_EmptyList_ReturnsZero_NoThrow()
    {
        using var executor = CreateExecutor();
        await executor.ExecuteNonQueryAsync(
            "CREATE TABLE batch_t (id INTEGER PRIMARY KEY, v TEXT NOT NULL)");

        var affected = await executor.ExecuteBatchNonQueryAsync(
            "INSERT INTO batch_t (id, v) VALUES (@id, @v)",
            Array.Empty<Dictionary<string, object>>());

        Assert.Equal(0, affected);
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
