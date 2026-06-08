using System.Data;
using System.Runtime.CompilerServices;
using SqlSugar;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;

namespace ZL.DataSync.Pipeline;

/// <summary>
/// 基于 SqlSugar 的数据库远程同步策略。
/// 读 SQLite → 批量拉取 → 批量写入远程（含 UPSERT）→ 批量标记同步。
/// </summary>
public sealed class DatabaseSyncStrategy : ISyncStrategy
{
    private readonly RemoteTargetConfig _target;
    private readonly bool _enableUpsert;
    private readonly IStructuredLogger _logger;
    private readonly SqlSugarScope _remoteScope;
    private readonly SqlSugar.DbType _remoteDbType;
    private readonly object _createTableLock = new();

    public DatabaseSyncStrategy(
        RemoteTargetConfig target,
        SqlSugarClient sharedLocalDb,
        bool enableUpsert,
        IStructuredLogger logger)
    {
        _target = target;
        _enableUpsert = enableUpsert;
        _logger = logger;
        _remoteDbType = MapDbType(target.Type);
        _remoteScope = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = target.ConnectionString,
            DbType = _remoteDbType,
            IsAutoCloseConnection = false  // P1-4 修复：启用连接池
        });
    }

    public string TargetName => _target.Name;

    public async Task<SyncReport> SyncTableAsync(
        string tableName,
        string? remoteTable,
        int batchSize,
        SqlSugarClient localDb,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string targetTable = string.IsNullOrWhiteSpace(remoteTable) ? tableName : remoteTable;

        // 1. 读取未同步数据 — 直接使用 Dictionary<string,object?> 避免 JSON 序列化
        var rows = await localDb.Queryable<Dictionary<string, object?>>()
            .AS(tableName)
            .Where("_Synced = 0")
            .OrderBy("ProcessTime")
            .Take(batchSize)
            .ToListAsync().ConfigureAwait(false);

        if (rows == null || rows.Count == 0)
            return SyncReport.Ok(tableName, 0, 0, null, sw.Elapsed.TotalMilliseconds);

        // 2. 确保远程表存在（加锁避免并发冲突）
        await EnsureTableAsync(targetTable, rows[0], ct).ConfigureAwait(false);

        // 3. 批量写入远程库（每 MaxRemoteBatchSize 条一批）
        const int MaxRemoteBatchSize = 50;
        int ok = 0;
        int fail = 0;
        DateTime? maxWatermark = null;
        var successRows = new List<Dictionary<string, object?>>(rows.Count);

        for (int i = 0; i < rows.Count; i += MaxRemoteBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = rows.Skip(i).Take(MaxRemoteBatchSize).ToList();

            // 3a. 批量写入远程
            try
            {
                // 过滤空行
                var validBatch = batch.Where(r => r != null && r.Count > 0).ToList();
                if (validBatch.Count > 0)
                {
                    if (_enableUpsert)
                    {
                        // UPSERT: 逐条尝试 UPDATE → INSERT（无主键冲突时 INSERT）
                        // 注意：如果远程表有唯一索引，改用 Insertable ... OnConflict 语义
                        // 这里用 Insertable，依赖远程库的唯一索引做幂等
                        await _remoteScope.Insertable(validBatch).AS(targetTable).ExecuteCommandAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await _remoteScope.Insertable(validBatch).AS(targetTable).ExecuteCommandAsync().ConfigureAwait(false);
                    }

                    // 写入成功后，累加成功行和最大水位
                    foreach (var row in validBatch)
                    {
                        ok++;
                        successRows.Add(row);
                        if (TryGetProcessTime(row, out var pt) && (maxWatermark == null || pt > maxWatermark.Value))
                            maxWatermark = pt;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"[{_target.Name}] 批量写入失败 {targetTable}: {ex.Message}");
                // 批量写入失败，逐条容错
                foreach (var row in batch)
                {
                    if (row == null || row.Count == 0) continue;
                    try
                    {
                        if (_enableUpsert)
                        {
                            await ExecuteUpsertAsync(targetTable, row, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await _remoteScope.Insertable(row).AS(targetTable).ExecuteCommandAsync().ConfigureAwait(false);
                        }
                        ok++;
                        successRows.Add(row);
                        if (TryGetProcessTime(row, out var pt) && (maxWatermark == null || pt > maxWatermark.Value))
                            maxWatermark = pt;
                    }
                    catch (Exception ex2)
                    {
                        fail++;
                        _logger.Warning($"[{_target.Name}] 单条写入失败 {targetTable}: {ex2.Message}");
                    }
                }
            }
        }

        // 4. 批量标记本地同步成功（先远程写入成功后，再统一标记 — 减少数据丢失风险）
        if (successRows.Count > 0)
        {
            try
            {
                await BatchMarkSyncedAsync(localDb, tableName, successRows, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"[{_target.Name}] 批量标记同步失败 {tableName}: {ex.Message}");
                // 标记失败不影响主流程，下次循环会继续
            }
        }

        int totalProcessed = ok + fail;
        return fail == 0 && totalProcessed > 0
            ? SyncReport.Ok(tableName, rows.Count, ok, maxWatermark?.ToString("o"), sw.Elapsed.TotalMilliseconds)
            : SyncReport.Fail(tableName, rows.Count, $"成功 {ok}/{rows.Count}, 失败 {fail}", sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// UPSERT 单行：先 UPDATE，影响 0 行则 INSERT。
    /// </summary>
    private async Task ExecuteUpsertAsync(string targetTable, Dictionary<string, object?> dict, CancellationToken ct)
    {
        if (dict.TryGetValue("Id", out var id) && id != null)
        {
            var updateResult = await _remoteScope.Updateable(dict)
                .AS(targetTable)
                .WhereColumns("Id")
                .ExecuteCommandAsync().ConfigureAwait(false);

            if (updateResult == 0)
            {
                // 更新 0 行 → 记录不存在，改为 INSERT
                await _remoteScope.Insertable(dict).AS(targetTable).ExecuteCommandAsync().ConfigureAwait(false);
            }
        }
        else
        {
            // 无主键 → 直接 INSERT
            await _remoteScope.Insertable(dict).AS(targetTable).ExecuteCommandAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 确保远程表存在：不存在则自动建表。
    /// 并发安全的建表检查（双重锁）。
    /// </summary>
    private async Task EnsureTableAsync(string targetTable, Dictionary<string, object?> sampleRow, CancellationToken ct)
    {
        // 快速路径：已存在则跳过
        if (_remoteScope.DbMaintenance.IsAnyTable(targetTable, false))
            return;

        lock (_createTableLock)
        {
            // 双重锁：另一个线程可能在等待锁期间完成了建表
            if (_remoteScope.DbMaintenance.IsAnyTable(targetTable, false))
                return;

            try
            {
                var cols = new List<string>();
                foreach (var kvp in sampleRow)
                {
                    if (kvp.Key.StartsWith("_")) continue; // 跳过内部字段
                    string sqlType = AdaptSqlType(kvp.Value?.GetType() ?? typeof(string), kvp.Value);
                    cols.Add($"{QuoteIdentifier(kvp.Key, _remoteDbType)} {sqlType}");
                }

                if (cols.Count == 0)
                {
                    _logger.Warning($"[{_target.Name}] 表 {targetTable} 没有有效列，跳过建表");
                    return;
                }

                string createSql = $"CREATE TABLE {QuoteIdentifier(targetTable, _remoteDbType)} ({string.Join(", ", cols)})";

                if (_remoteDbType == SqlSugar.DbType.MySql)
                    createSql += " ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";

                _remoteScope.Ado.ExecuteCommand(createSql);
                _logger.Info($"[{_target.Name}] 自动建表: {targetTable} ({cols.Count} 列)");
            }
            catch (Exception ex)
            {
                _logger.Warning($"[{_target.Name}] 建表失败 {targetTable}: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// 根据列类型推断 SQL 类型。
    /// </summary>
    private string AdaptSqlType(Type? clrType, object? sampleValue)
    {
        if (clrType == null || sampleValue == null || sampleValue == DBNull.Value)
            clrType = typeof(string);

        // 处理 Nullable<T>
        if (clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Nullable<>))
            clrType = clrType.GetGenericArguments()[0];

        string baseType = clrType.Name switch
        {
            "Boolean" or "Boolean?" => "TINYINT(1)",
            "Int32" or "Int16" or "Byte" or "Int32?" or "Int16?" or "Byte?" => "INT",
            "Int64" or "UInt32" or "UInt64" or "Int64?" => "BIGINT",
            "Single" or "Float" or "Decimal" or "Double" or "Single?" or "Double?" => "DOUBLE",
            "DateTime" or "DateTime?" => "DATETIME",
            _ => "TEXT"
        };

        if (_remoteDbType == SqlSugar.DbType.SqlServer)
        {
            if (baseType == "TINYINT(1)") return "BIT";
            if (baseType == "DATETIME") return "DATETIME2";
        }

        return baseType;
    }

    /// <summary>
    /// 批量标记本地记录为已同步。
    /// 使用 ProcessTime 作为定位键（支持无 Id 的表）。
    /// </summary>
    private async Task BatchMarkSyncedAsync(SqlSugarClient localDb, string tableName, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        // 按 ProcessTime 分组标记（ProcessTime 通常唯一）
        var processTimes = rows
            .Select(r => r.TryGetValue("ProcessTime", out var v) && v is DateTime dt ? dt : (DateTime?)null)
            .Where(t => t.HasValue && t.Value > DateTime.MinValue)
            .Distinct()
            .ToList();

        if (processTimes.Count == 0) return;

        // 构建 IN 参数的 SQL
        var paramNames = new List<string>();
        var parameters = new List<SugarParameter>();
        foreach (var t in processTimes)
        {
            var name = $"pt{parameters.Count}";
            paramNames.Add($"@{name}");
            parameters.Add(new SugarParameter(name, t.Value));
        }

        var sql = $"UPDATE {QuoteIdentifier(tableName, SqlSugar.DbType.Sqlite)} SET _Synced = 1, _SyncTime = @now WHERE ProcessTime IN ({string.Join(",", paramNames)})";
        parameters.Add(new SugarParameter("now", DateTime.UtcNow));

        await localDb.Ado.ExecuteCommandAsync(sql, parameters.ToArray()).ConfigureAwait(false);
    }

    private static bool TryGetProcessTime(Dictionary<string, object?> row, out DateTime pt)
    {
        pt = DateTime.MinValue;
        if (row.TryGetValue("ProcessTime", out var val) && val is DateTime d && d > DateTime.MinValue)
        {
            pt = d;
            return true;
        }
        return false;
    }

    private static string QuoteIdentifier(string name, SqlSugar.DbType dbType) =>
        dbType == SqlSugar.DbType.MySql
            ? $"`{name}`"
            : dbType == SqlSugar.DbType.SqlServer
                ? $"[{name}]"
                : $"`{name}`";

    private static SqlSugar.DbType MapDbType(TargetType type) => type switch
    {
        TargetType.MySql => SqlSugar.DbType.MySql,
        TargetType.SqlServer => SqlSugar.DbType.SqlServer,
        TargetType.PostgreSql => SqlSugar.DbType.PostgreSQL,
        TargetType.Oracle => SqlSugar.DbType.Oracle,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"不支持的目标类型: {type}")
    };

    public void Dispose()
    {
        _remoteScope?.Dispose();
    }
}
