using SqlSugar;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;
using ZL.DataSync.Pipeline;

namespace ZL.DataSync;

/// <summary>
/// 数据同步引擎：后台运行，自动发现本地表，按策略分发到多个远程目标。
/// 支持断网续传、指数退避重试、水位线追踪、过期数据清理。
/// </summary>
public sealed class SyncEngine : IDisposable
{
    private readonly DataSyncConfig _config;
    private readonly IStructuredLogger _logger;
    private readonly WatermarkStore _watermark;
    private readonly CancellationTokenSource _cts = new();
    private readonly SyncStatus _status = new();
    private bool _disposed;

    // 共享的本地 SQLite 连接（复用，避免频繁创建）
    private readonly SqlSugarClient _localDb;

    // 每个目标的策略缓存 + 任务
    private readonly object _strategyLock = new();
    private readonly Dictionary<string, (ISyncStrategy Strategy, Task Task)> _targetEntries = new();

    // 已发现的本地表
    private HashSet<string> _discoveredTables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>同步状态查询</summary>
    public SyncStatus Status => _status;

    /// <summary>
    /// 创建同步引擎。
    /// </summary>
    /// <param name="config">同步配置</param>
    /// <param name="logger">日志（null 时使用 DebugLogger）</param>
    public SyncEngine(DataSyncConfig config, IStructuredLogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(_config.LocalDbPath))
            throw new ArgumentException("LocalDbPath 不能为空", nameof(config));

        _logger = logger ?? new Infrastructure.DebugLogger();

        // P2-3 修复：复用 SqlSugarClient 而非每次创建新连接
        _localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={_config.LocalDbPath}",
            IsAutoCloseConnection = false  // 复用连接
        });

        _watermark = new WatermarkStore(_localDb);
        _watermark.EnsureTable();
        _status.Reset();
    }

    /// <summary>
    /// 启动同步引擎。后台开始所有目标的同步循环。
    /// </summary>
    public void Start()
    {
        if (_status.IsRunning)
        {
            _logger.Warning("同步引擎已在运行中");
            return;
        }

        _status.IsRunning = true;
        _status.LastStartTime = DateTime.UtcNow;
        _status.StatusText = "启动中";

        // 发现本地表
        DiscoverLocalTables();

        // 为每个目标创建策略并启动同步循环
        foreach (var target in _config.RemoteTargets)
        {
            var strategy = CreateStrategy(target);
            var task = RunTargetLoopAsync(target, strategy, _cts.Token);
            lock (_strategyLock)
            {
                _targetEntries[target.Name] = (strategy, task);
            }
            _logger.Info($"[{target.Name}] 同步循环已启动，间隔 {_config.SyncIntervalSeconds}s");
        }

        // 启动清理循环（如果启用）
        if (_config.EnableCleanup)
        {
            _ = Task.Run(() => CleanupLoopAsync(_cts.Token), _cts.Token);
            _logger.Info($"数据清理已启动，间隔 {_config.CleanupIntervalSeconds}s，保留 {_config.DataRetentionDays} 天");
        }

        _status.StatusText = "运行中";
        _logger.Info($"同步引擎启动完成，{_discoveredTables.Count} 个表, {_config.RemoteTargets.Count} 个目标");
    }

    /// <summary>
    /// 停止同步引擎。等待所有目标完成当前同步后优雅退出。
    /// </summary>
    public async Task StopAsync()
    {
        if (!_status.IsRunning) return;

        _logger.Info("正在停止同步引擎...");
        _cts.Cancel();

        List<Task> runningTasks;
        lock (_strategyLock)
        {
            runningTasks = _targetEntries.Values.Select(e => e.Task).ToList();
        }

        foreach (var task in runningTasks)
        {
            try { await Task.WhenAny(task, Task.Delay(10000)).ConfigureAwait(false); }
            catch { }
        }

        // 清理策略
        List<ISyncStrategy> strategies;
        lock (_strategyLock)
        {
            strategies = _targetEntries.Values.Select(e => e.Strategy).ToList();
            _targetEntries.Clear();
        }

        foreach (var s in strategies) s.Dispose();

        _status.IsRunning = false;
        _status.StatusText = "已停止";
        _logger.Info("同步引擎已停止");
    }

    /// <summary>
    /// 手动触发一次同步（非周期性的，用于 UI 按钮等场景）。
    /// </summary>
    public async Task<Dictionary<string, SyncReport>> ForceSyncAsync()
    {
        var reports = new Dictionary<string, SyncReport>();

        foreach (var target in _config.RemoteTargets)
        {
            ISyncStrategy strategy;
            lock (_strategyLock)
            {
                strategy = CreateStrategy(target);
            }

            try
            {
                var report = await SyncAllTablesAsync(target, strategy, CancellationToken.None).ConfigureAwait(false);
                reports[target.Name] = report;
            }
            catch (Exception ex)
            {
                reports[target.Name] = SyncReport.Fail(target.Name, 0, ex.Message, 0);
            }
            finally
            {
                strategy.Dispose();
            }
        }

        return reports;
    }

    // ═══════════════════════════════════════════════════════════════
    //  核心循环
    // ═══════════════════════════════════════════════════════════════

    private async Task RunTargetLoopAsync(RemoteTargetConfig target, ISyncStrategy strategy, CancellationToken ct)
    {
        int failStreak = 0;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.SyncIntervalSeconds), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var report = await SyncAllTablesAsync(target, strategy, ct).ConfigureAwait(false);
                if (report.TargetCount > 0)
                {
                    failStreak = 0;
                    _status.FailStreak = failStreak;
                    _status.TotalSynced += report.SyncedCount;
                    _status.TotalFailed += report.FailedCount;
                    _status.LastSyncTime = DateTime.UtcNow;
                    _status.StatusText = report.Success ? "同步中" : $"失败({report.FailedCount})";
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                failStreak++;
                _status.FailStreak = failStreak;
                _status.LastError = ex.Message;
                _status.StatusText = $"异常({failStreak})";

                int wait = Math.Min(
                    (int)Math.Pow(2, Math.Min(failStreak, 8)) * _config.RetryBackoffSeconds,
                    300);

                _logger.Warning($"[{target.Name}] 同步失败: {ex.Message}，{wait}s后重试");
                try { await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            _logger.Debug($"[{target.Name}] 同步循环完成: {sw.ElapsedMilliseconds}ms");
        }

        strategy.Dispose();
    }

    /// <summary>
    /// 同步所有已发现的表到一个目标。
    /// </summary>
    private async Task<SyncReport> SyncAllTablesAsync(RemoteTargetConfig target, ISyncStrategy strategy, CancellationToken ct)
    {
        int totalTarget = 0;
        int totalOk = 0;
        int totalFail = 0;
        string? lastError = null;
        DateTime? lastWatermark = null;

        foreach (var table in _discoveredTables)
        {
            if (ct.IsCancellationRequested) break;

            string? remoteTable = target.TableMappings.TryGetValue(table, out var alias) ? alias : null;
            var report = await strategy.SyncTableAsync(table, remoteTable, _config.BatchSize, _localDb, ct).ConfigureAwait(false);

            totalTarget += report.TargetCount;
            totalOk += report.SyncedCount;
            totalFail += report.FailedCount;

            if (!report.Success && string.IsNullOrEmpty(lastError))
                lastError = report.LastError;

            if (report.LastWatermark != null && DateTime.TryParse(report.LastWatermark, out var wm))
                lastWatermark ??= wm;
        }

        if (totalFail == 0 && totalOk > 0)
        {
            return SyncReport.Ok($"[{target.Name}] 汇总", totalTarget, totalOk, lastWatermark?.ToString("O") ?? "success", 0);
        }

        return SyncReport.Fail($"[{target.Name}] 汇总", totalTarget, lastError ?? "未知错误", 0);
    }

    // ═══════════════════════════════════════════════════════════════
    //  清理
    // ═══════════════════════════════════════════════════════════════

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.CleanupIntervalSeconds), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;

            try
            {
                await CleanupSyncedDataAsync(_config.DataRetentionDays, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.Warning($"数据清理异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 清理已同步的过期数据（分批删除，P1-2 修复）。
    /// </summary>
    private async Task CleanupSyncedDataAsync(int retentionDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        int totalDeleted = 0;
        const int MaxCleanupBatchSize = 5000;

        foreach (var table in _discoveredTables)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                bool hasSynced = HasColumn(_localDb, table, "_Synced");
                if (!hasSynced) continue;

                // 分批删除：每次删除最多 MaxCleanupBatchSize 条
                int deleted = 0;
                do
                {
                    var ids = await _localDb.Queryable<dynamic>()
                        .AS(table)
                        .Where("_Synced = 1 AND ProcessTime < @cutoff", cutoff)
                        .Select("Id")
                        .Take(MaxCleanupBatchSize)
                        .ToListAsync().ConfigureAwait(false);

                    if (ids == null || ids.Count == 0) break;
                    var idList = ids
                        .OfType<IDictionary<string, object?>>()
                        .Select(d => d.ContainsKey("Id") ? d["Id"] : null)
                        .Where(id => id != null)
                        .Distinct()
                        .ToList();
                    if (idList.Count == 0) break;

                    // 构建参数化 DELETE
                    var parameters = new List<SugarParameter>();
                    var conditions = new List<string>();
                    for (int i = 0; i < idList.Count; i++)
                    {
                        conditions.Add($"@id{i}");
                        parameters.Add(new SugarParameter($"@id{i}", idList[i]));
                    }

                    int rowsAffected = await _localDb.Ado.ExecuteCommandAsync(
                        $"DELETE FROM [{table}] WHERE Id IN ({string.Join(",", conditions)})",
                        parameters.ToArray()
                    ).ConfigureAwait(false);

                    totalDeleted += rowsAffected;
                } while (deleted < MaxCleanupBatchSize * 100); // 最多循环 100 轮（50 万条/表）
            }
            catch (Exception ex)
            {
                _logger.Warning($"清理表 {table} 异常: {ex.Message}");
            }
        }

        if (totalDeleted > 0)
            _logger.Info($"数据清理完成: 删除 {totalDeleted} 条记录");
    }

    private static bool HasColumn(SqlSugarClient db, string table, string colName)
    {
        try
        {
            var cols = db.Ado.SqlQuery<ColumnInfoRow>($"PRAGMA table_info(\"{table}\")");
            return cols != null && cols.Any(r => r.Name == colName);
        }
        catch
        {
            return false;
        }
    }

    private sealed class ColumnInfoRow
    {
        public string? Name { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  表发现 & 策略创建
    // ═══════════════════════════════════════════════════════════════

    private void DiscoverLocalTables()
    {
        try
        {
            // 排除所有下划线开头的系统表（如 _SyncWatermark）
            var tables = _localDb.Ado.SqlQuery<TableInfoRow>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE '_%' ORDER BY name");

            if (tables != null)
            {
                _discoveredTables = new HashSet<string>(
                    tables.Select(t => t.Name ?? string.Empty)
                        .Where(n => !string.IsNullOrWhiteSpace(n)),
                    StringComparer.OrdinalIgnoreCase);
                _logger.Info($"发现 {_discoveredTables.Count} 个本地业务表: {string.Join(", ", _discoveredTables)}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"发现本地表失败: {ex.Message}");
        }
    }

    private ISyncStrategy CreateStrategy(RemoteTargetConfig target)
    {
        return target.Type switch
        {
            TargetType.MySql or TargetType.SqlServer or TargetType.PostgreSql or TargetType.Oracle =>
                new DatabaseSyncStrategy(target, _localDb, _config.EnableUpsert, _logger),
            TargetType.Http =>
                new HttpSyncStrategy(target.HttpConfig ?? throw new InvalidOperationException($"HTTP 目标 {target.Name} 缺少 HttpConfig"),
                    target.Name, _logger),
            _ => throw new ArgumentOutOfRangeException(nameof(target.Type), $"不支持的目标类型: {target.Type}")
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        _localDb?.Dispose();
        _watermark?.Dispose();
    }

    private sealed class TableInfoRow
    {
        public string? Name { get; set; }
    }
}
