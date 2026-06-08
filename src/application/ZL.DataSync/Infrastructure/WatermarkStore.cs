using SqlSugar;

namespace ZL.DataSync.Infrastructure;

/// <summary>
/// 水位线存储：按表名 + 目标名追踪每个表的最后同步水位。
/// 水位线可以是时间戳（ProcessTime）或自增 ID（Id）。
/// 存储在本地 SQLite 的 _SyncWatermark 表内。
/// </summary>
internal sealed class WatermarkStore : IDisposable
{
    private readonly SqlSugarClient _localDb;
    private bool _disposed;

    /// <summary>
    /// 使用外部共享的 SqlSugarClient。
    /// </summary>
    public WatermarkStore(SqlSugarClient sharedLocalDb)
    {
        _localDb = sharedLocalDb;
    }

    /// <summary>
    /// 确保水位线表存在。
    /// </summary>
    public void EnsureTable()
    {
        if (!_localDb.DbMaintenance.IsAnyTable("_SyncWatermark", false))
        {
            _localDb.Ado.ExecuteCommand(
                "CREATE TABLE \"_SyncWatermark\" (" +
                "\"TableName\" TEXT NOT NULL, " +
                "\"TargetName\" TEXT NOT NULL, " +
                "\"WatermarkType\" TEXT NOT NULL DEFAULT 'DateTime', " +
                "\"WatermarkValue\" TEXT NOT NULL, " +
                "\"LastSyncTime\" TEXT, " +
                "PRIMARY KEY (TableName, TargetName)" +
                ")");
        }
    }

    /// <summary>
    /// 读取指定表+目标的水位线。
    /// </summary>
    public string? ReadWatermark(string tableName, string targetName)
    {
        try
        {
            var result = _localDb.Ado.SqlQuery<WatermarkRow>(
                "SELECT WatermarkValue FROM _SyncWatermark WHERE TableName = ? AND TargetName = ?",
                new SugarParameter("p0", tableName),
                new SugarParameter("p1", targetName)
            );

            if (result != null && result.Count > 0)
                return result[0]?.WatermarkValue;

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 写入水位线。
    /// </summary>
    public void WriteWatermark(string tableName, string targetName, string watermarkValue)
    {
        try
        {
            var existing = _localDb.Ado.SqlQuery<int>(
                "SELECT COUNT(*) FROM _SyncWatermark WHERE TableName = ? AND TargetName = ?",
                new SugarParameter("p0", tableName),
                new SugarParameter("p1", targetName)
            );

            var hasRow = existing != null && existing.Count > 0 && existing[0] > 0;

            if (hasRow)
            {
                _localDb.Ado.ExecuteCommand(
                    "UPDATE _SyncWatermark SET WatermarkValue = ?, LastSyncTime = ? " +
                    "WHERE TableName = ? AND TargetName = ?",
                    new SugarParameter("p0", watermarkValue),
                    new SugarParameter("p1", DateTime.UtcNow.ToString("O")),
                    new SugarParameter("p2", tableName),
                    new SugarParameter("p3", targetName)
                );
            }
            else
            {
                _localDb.Ado.ExecuteCommand(
                    "INSERT INTO _SyncWatermark (TableName, TargetName, WatermarkType, WatermarkValue, LastSyncTime) " +
                    "VALUES (?, ?, 'DateTime', ?, ?)",
                    new SugarParameter("p0", tableName),
                    new SugarParameter("p1", targetName),
                    new SugarParameter("p2", watermarkValue),
                    new SugarParameter("p3", DateTime.UtcNow.ToString("O"))
                );
            }
        }
        catch
        {
            // 写入失败不影响同步主流程
        }
    }

    /// <summary>
    /// 读取指定表+目标的最后同步时间（用于清理已同步的过期数据）。
    /// </summary>
    public DateTime? GetLastSyncTime(string tableName, string targetName)
    {
        try
        {
            var result = _localDb.Ado.SqlQuery<DateTime?>(
                "SELECT LastSyncTime FROM _SyncWatermark WHERE TableName = ? AND TargetName = ?",
                new SugarParameter("p0", tableName),
                new SugarParameter("p1", targetName)
            );

            if (result != null && result.Count > 0)
                return result[0];

            return null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class WatermarkRow
    {
        public string? WatermarkValue { get; set; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 注意：不再直接 _localDb.Dispose()，因为它是共享的
        // 由 SyncEngine 负责释放
    }
}
