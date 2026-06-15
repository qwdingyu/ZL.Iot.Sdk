// ============================================================
//  串行化 SQL/表存储执行器装饰器（P0-1 并发止血）
//
//  背景：RunnerStorageCoordinator 全局只创建一个 SqlSugarExecutor，
//  其内部 ISqlSugarClient 非线程安全。但该实例被多个线程并发访问：
//    - HistoryStoragePipeline 单消费者线程（InsertRowsAsync）
//    - 每个 SingleDeviceRunner 的采集触发线程
//      （TriggerExecutor.ExecuteSql → ExecuteNonQueryAsync，
//       SqlExecutorFieldMappingSink → EnsureTable/InsertRows）
//
//  SqlSugarClient 多线程共享会导致连接状态错乱、命令交叉、间歇性
//  "no such table"/"connection is closed"。本装饰器用单一信号量把
//  所有数据库访问串行化，作为止血手段；最终方案是写入路径统一为
//  单消费者队列（见 docs/38 支柱 A）。
//
//  设计要点：
//   - 只包裹 Runner 侧实例，不改共享领域类 SqlSugarExecutor，
//     避免误伤云端可能存在的单线程高并发只读消费者。
//   - 信号量不可重入：每个公开方法只取一次锁，绝不在持锁时调用另一
//     个加锁方法（批量直接委托 inner 的批量实现，由 inner 内部循环）。
//   - 显式事务 API（Begin/Commit/Rollback）在 Runner 现场闭环中未被
//     使用；此处直接委托 inner 且不参与信号量，行为与改造前一致。
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Iot.Interface;

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// 把同一个底层执行器的所有数据库访问串行化的装饰器。
/// 同时实现 <see cref="ISqlExecutor"/> 与 <see cref="ITableStorageExecutor"/>，
/// 因此 SqlExecutor / TableStorage 两个视图仍指向同一序列化边界。
///
/// 【已被取代】这是 P0-1 的并发止血方案。P1-1 完成单写入器（<see cref="RunnerWriteQueue"/>）
/// 后，只有单一消费者线程访问底层执行器，不再有跨线程并发，本装饰器已退出 Runner 装配，
/// 仅保留类与单元测试作为串行化语义的参考。
/// </summary>
/// <typeparam name="TInner">
/// 被装饰的底层执行器，必须同时是 SQL 执行器与表存储执行器，且可释放
/// （拥有底层连接）。Runner 侧传入 SqlSugarExecutor。
/// </typeparam>
internal sealed class SerializedSqlExecutor<TInner> : ISqlExecutor, ITableStorageExecutor, IDisposable
    where TInner : ISqlExecutor, ITableStorageExecutor, IDisposable
{
    private readonly TInner _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SerializedSqlExecutor(TInner inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string Dialect => _inner.Dialect;

    // ── ISqlExecutor ───────────────────────────────────────────

    public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql, Dictionary<string, object> parameters = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await _inner.ExecuteQueryAsync(sql, parameters).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object> parameters = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await _inner.ExecuteNonQueryAsync(sql, parameters).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<int> ExecuteBatchNonQueryAsync(string sql, IEnumerable<Dictionary<string, object>> parameterList)
    {
        // inner 的批量实现内部循环调用 inner 自身（无锁），整体作为一个串行单元。
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await _inner.ExecuteBatchNonQueryAsync(sql, parameterList).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<object> ExecuteScalarAsync(string sql, Dictionary<string, object> parameters = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await _inner.ExecuteScalarAsync(sql, parameters).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    // 纯校验，不触碰数据库，无需串行化。
    public bool Validate(string sql, out string errorMessage) => _inner.Validate(sql, out errorMessage);

    // 显式事务：Runner 现场闭环未使用；直接委托，行为与改造前一致。
    public void BeginTransaction() => _inner.BeginTransaction();
    public void CommitTransaction() => _inner.CommitTransaction();
    public void RollbackTransaction() => _inner.RollbackTransaction();

    // ── ITableStorageExecutor ──────────────────────────────────

    public async Task EnsureTableAsync(string tableName, IReadOnlyList<TableColumnDefinition> columns)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await _inner.EnsureTableAsync(tableName, columns).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<int> InsertRowsAsync(string tableName, IReadOnlyList<Dictionary<string, object>> rows)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await _inner.InsertRowsAsync(tableName, rows).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.Dispose();
        _gate.Dispose();
    }
}
