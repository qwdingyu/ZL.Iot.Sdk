using SqlSugar;

namespace ZL.DataSync.Pipeline;

/// <summary>
/// 同步策略接口：定义一次表同步的最小契约。
/// 数据库类型（MySQL/SQL Server）和 HTTP API 各实现一个。
/// </summary>
public interface ISyncStrategy : IDisposable
{
    /// <summary>目标名称</summary>
    string TargetName { get; }

    /// <summary>
    /// 同步单表数据到目标。
    /// </summary>
    /// <param name="tableName">本地表名</param>
    /// <param name="remoteTable">远程表名映射（可为 null，默认同本地表名）</param>
    /// <param name="batchSize">批次大小</param>
    /// <param name="localDb">本地 SQLite 连接（由引擎提供，复用连接）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>同步报告</returns>
    Task<SyncReport> SyncTableAsync(
        string tableName,
        string? remoteTable,
        int batchSize,
        SqlSugarClient localDb,
        CancellationToken ct);
}
