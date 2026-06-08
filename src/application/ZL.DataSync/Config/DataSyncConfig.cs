namespace ZL.DataSync.Config;

/// <summary>
/// 数据同步配置（不可变，线程安全）。
/// </summary>
public sealed class DataSyncConfig
{
    // ─── 本地 SQLite ────────────────────────────────────────────────

    /// <summary>本地 SQLite 文件路径</summary>
    public string LocalDbPath { get; init; } = string.Empty;

    // ─── 远程目标（多目标支持）──────────────────────────────────────

    /// <summary>
    /// 远程目标列表（支持同时同步到 MySQL、SQL Server 等多个目标）。
    /// 为空时仅写入本地 SQLite，不主动分发。
    /// </summary>
    public List<RemoteTargetConfig> RemoteTargets { get; set; } = new();

    // ─── 管道行为 ───────────────────────────────────────────────────

    /// <summary>
    /// 每次同步批次大小。默认 100。
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// 同步循环间隔（秒）。默认 5 秒。
    /// </summary>
    public int SyncIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// 失败后最大重试次数。默认 3。
    /// </summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// 失败后初始退避时间（秒）。默认 2 秒，按指数退避。
    /// </summary>
    public int RetryBackoffSeconds { get; init; } = 2;

    /// <summary>
    /// 是否在每表内保持精确的去重保证（基于业务主键的 UPSERT 语义）。
    /// 默认 true。设为 false 可提升性能（仅 INSERT，依赖远程库的幂等性）。
    /// </summary>
    public bool EnableUpsert { get; init; } = true;

    /// <summary>
    /// 是否启用数据清理。默认 true。
    /// </summary>
    public bool EnableCleanup { get; init; } = true;

    /// <summary>
    /// 已同步数据的保留天数。默认 730 天（2年）。
    /// </summary>
    public int DataRetentionDays { get; init; } = 730;

    /// <summary>
    /// 数据清理检查间隔（秒）。默认 3600（1小时）。
    /// </summary>
    public int CleanupIntervalSeconds { get; init; } = 3600;
}
