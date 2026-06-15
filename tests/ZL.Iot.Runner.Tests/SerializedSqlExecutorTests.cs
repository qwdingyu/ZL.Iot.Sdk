using System.Collections.Concurrent;
using ZL.Biz.Execute.Biz;
using ZL.Iot.Interface;
using ZL.Iot.Runner.Runtime;

namespace ZL.Iot.Runner.Tests;

/// <summary>
/// SerializedSqlExecutor 并发止血测试（P0-1）。
///
/// 两类验证：
/// 1. 用探测 fake 证明装饰器把对底层执行器的并发访问串行化为 1。
/// 2. 用真实 SqlSugarExecutor（SQLite）证明多线程并发写入数据正确、无丢失。
/// </summary>
public sealed class SerializedSqlExecutorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"runner_serialized_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ConcurrentCalls_AreSerialized_MaxConcurrencyIsOne()
    {
        var probe = new ConcurrencyProbeExecutor();
        using var serialized = new SerializedSqlExecutor<ConcurrencyProbeExecutor>(probe);

        // 100 个线程同时打 INSERT + 表存储，装饰器必须把它们串行化。
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            int n = i;
            tasks.Add(Task.Run(() => serialized.ExecuteNonQueryAsync($"INSERT {n}")));
            tasks.Add(Task.Run(() => serialized.InsertRowsAsync("t", new List<Dictionary<string, object>>())));
        }

        await Task.WhenAll(tasks);

        // 底层观测到的最大并发数必须为 1 → 证明 SqlSugarClient 不会被并发触碰。
        Assert.Equal(1, probe.MaxObservedConcurrency);
        Assert.Equal(200, probe.TotalCalls);
    }

    [Fact]
    public async Task ConcurrentWrites_RealSqlite_AllRowsPersisted()
    {
        using var inner = new SqlSugarExecutor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlSugarExecutor>.Instance,
            SqlSugar.DbType.Sqlite,
            $"Data Source={_dbPath}");
        using var serialized = new SerializedSqlExecutor<SqlSugarExecutor>(inner);

        await serialized.ExecuteNonQueryAsync(
            "CREATE TABLE IF NOT EXISTS concurrent_probe (id INTEGER PRIMARY KEY, src TEXT NOT NULL)");

        // 50 个线程并发插入，每个插入一行。
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            int n = i;
            tasks.Add(Task.Run(() => serialized.ExecuteNonQueryAsync(
                "INSERT INTO concurrent_probe (src) VALUES (@src)",
                new Dictionary<string, object> { ["src"] = $"thread-{n}" })));
        }

        await Task.WhenAll(tasks);

        var rows = await serialized.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM concurrent_probe");
        Assert.Equal(50, Convert.ToInt32(rows[0]["cnt"]));
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

    /// <summary>
    /// 探测底层被触碰时的并发度：进入时 +1，离开时 -1，记录峰值。
    /// 内部刻意 await 一小段，放大并发窗口，使未串行化时一定会观测到 &gt;1。
    /// </summary>
    private sealed class ConcurrencyProbeExecutor : ISqlExecutor, ITableStorageExecutor, IDisposable
    {
        private int _current;
        private int _max;
        private int _total;

        public int MaxObservedConcurrency => Volatile.Read(ref _max);
        public int TotalCalls => Volatile.Read(ref _total);

        public string Dialect => "Probe";

        private async Task EnterAsync()
        {
            Interlocked.Increment(ref _total);
            int now = Interlocked.Increment(ref _current);
            UpdateMax(now);
            await Task.Delay(2).ConfigureAwait(false); // 放大并发窗口
            Interlocked.Decrement(ref _current);
        }

        private void UpdateMax(int candidate)
        {
            int prev;
            do
            {
                prev = Volatile.Read(ref _max);
                if (candidate <= prev) return;
            }
            while (Interlocked.CompareExchange(ref _max, candidate, prev) != prev);
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object> parameters = null)
        {
            await EnterAsync();
            return 1;
        }

        public async Task<int> InsertRowsAsync(string tableName, IReadOnlyList<Dictionary<string, object>> rows)
        {
            await EnterAsync();
            return rows.Count;
        }

        public async Task EnsureTableAsync(string tableName, IReadOnlyList<TableColumnDefinition> columns)
            => await EnterAsync();

        public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql, Dictionary<string, object> parameters = null)
        {
            await EnterAsync();
            return new List<Dictionary<string, object>>();
        }

        public async Task<int> ExecuteBatchNonQueryAsync(string sql, IEnumerable<Dictionary<string, object>> parameterList)
        {
            await EnterAsync();
            return 0;
        }

        public async Task<object> ExecuteScalarAsync(string sql, Dictionary<string, object> parameters = null)
        {
            await EnterAsync();
            return null;
        }

        public bool Validate(string sql, out string errorMessage) { errorMessage = null; return true; }
        public void BeginTransaction() { }
        public void CommitTransaction() { }
        public void RollbackTransaction() { }
        public void Dispose() { }
    }
}
