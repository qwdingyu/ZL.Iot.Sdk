namespace ZL.DataSync;

/// <summary>
/// 同步报告：一次同步循环的结果。
/// </summary>
public sealed class SyncReport
{
    public DateTime Timestamp { get; init; }
    public string TableName { get; init; } = string.Empty;
    public int TargetCount { get; init; }       // 目标库中待同步的记录数
    public int SyncedCount { get; init; }        // 实际成功同步的数量
    public int FailedCount { get; init; }        // 失败的数量
    public string? LastError { get; init; }
    public string? LastWatermark { get; init; }  // 上次成功同步的水位值
    public double ElapsedMs { get; init; }

    /// <summary>
    /// 同步成功：有数据待同步且没有失败。
    /// 无数据待同步（TargetCount == 0）不算成功。
    /// </summary>
    public bool Success => TargetCount > 0 && FailedCount == 0;

    public bool HasData => TargetCount > 0;

    public static SyncReport Ok(string tableName, int target, int synced, string? watermark, double elapsedMs)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            TableName = tableName,
            TargetCount = target,
            SyncedCount = synced,
            FailedCount = 0,
            LastWatermark = watermark,
            ElapsedMs = elapsedMs
        };

    public static SyncReport Fail(string tableName, int target, string? error, double elapsedMs)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            TableName = tableName,
            TargetCount = target,
            SyncedCount = 0,
            FailedCount = target > 0 ? target : 0,
            LastError = error,
            ElapsedMs = elapsedMs
        };
}

/// <summary>
/// 同步引擎运行状态。
/// </summary>
public sealed class SyncStatus
{
    public bool IsRunning { get; set; }
    public int TotalTables { get; set; }
    public int TotalSynced { get; set; }
    public int TotalFailed { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public DateTime? LastStartTime { get; set; }
    public int FailStreak { get; set; }  // 连续失败次数
    public string? LastError { get; set; }
    public string? StatusText { get; set; }  // 用于 UI 展示的状态文本

    /// <summary>
    /// 健康：未运行 或 从未连续失败过。
    /// </summary>
    public bool IsHealthy => !IsRunning || FailStreak == 0;

    public void Reset()
    {
        TotalTables = 0;
        TotalSynced = 0;
        TotalFailed = 0;
        LastSyncTime = null;
        LastStartTime = null;
        FailStreak = 0;
        LastError = null;
        StatusText = null;
    }
}
