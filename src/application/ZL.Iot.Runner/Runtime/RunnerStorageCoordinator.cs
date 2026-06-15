using System;
using System.IO;
using Microsoft.Extensions.Logging;
using ZL.Biz.Execute.Biz;
using ZL.DataSync;
using ZL.DataSync.Config;
using ZL.Iot.Interface;
using ZL.Iot.Runner.Configuration;
using ZL.Tag;

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// Runner 级共享存储协调器。
/// </summary>
public sealed class RunnerStorageCoordinator : IDisposable, IAsyncDisposable
{
    private readonly ILogger<RunnerStorageCoordinator> _logger;

    // P0-1：本地执行器用 SerializedSqlExecutor 包裹，把所有数据库访问串行化，
    // 避免历史消费者线程与多设备采集触发线程并发共享非线程安全的 SqlSugarClient。
    private readonly SerializedSqlExecutor<SqlSugarExecutor>? _localExecutor;
    private readonly HistoryStoragePipeline? _historyStorage;
    private readonly SyncEngine? _syncEngine;
    private bool _disposed;

    private RunnerStorageCoordinator(
        DataStorageOptions storage,
        SerializedSqlExecutor<SqlSugarExecutor>? localExecutor,
        HistoryStoragePipeline? historyStorage,
        SyncEngine? syncEngine,
        ILogger<RunnerStorageCoordinator> logger)
    {
        Storage = storage;
        _localExecutor = localExecutor;
        _historyStorage = historyStorage;
        _syncEngine = syncEngine;
        _logger = logger;
    }

    public DataStorageOptions Storage { get; }

    public ISqlExecutor? SqlExecutor => _localExecutor;

    public ITableStorageExecutor? TableStorage => _localExecutor;

    public static RunnerStorageCoordinator Create(DataStorageOptions? storage, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<RunnerStorageCoordinator>();
        var normalizedStorage = storage ?? new DataStorageOptions
        {
            Type = "Sqlite",
            ConnectionString = "Data Source=./data/iot_runner.db"
        };

        var localExecutor = CreateLocalExecutor(normalizedStorage, loggerFactory, logger);
        var historyStorage = normalizedStorage.History?.Enabled == true && localExecutor is not null
            ? new HistoryStoragePipeline(
                normalizedStorage.History,
                localExecutor,
                loggerFactory.CreateLogger<HistoryStoragePipeline>())
            : null;
        var syncEngine = CreateRemoteSyncEngine(normalizedStorage, logger);
        syncEngine?.Start();

        return new RunnerStorageCoordinator(normalizedStorage, localExecutor, historyStorage, syncEngine, logger);
    }

    public bool TryEnqueueHistory(string deviceCode, string tagId, object? value, TagItem? tag)
    {
        if (_disposed)
        {
            return false;
        }

        return _historyStorage?.TryEnqueue(deviceCode, tagId, value, tag) == true;
    }

    /// <summary>
    /// 异步释放（推荐路径）：宿主应优先 <c>await using</c> 走此路径，
    /// 真正 await 历史管线与远端同步引擎的停止，不阻塞、不 sync-over-async。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_historyStorage is not null)
        {
            await _historyStorage.StopAsync().ConfigureAwait(false);
        }

        if (_syncEngine is not null)
        {
            try
            {
                await _syncEngine.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Runner RemoteSync 停止异常");
            }
            finally
            {
                _syncEngine.Dispose();
            }
        }

        _localExecutor?.Dispose();
    }

    /// <summary>
    /// 同步释放（兼容路径）：保留给 <c>using</c> 与 IDisposable 链。
    /// 远端同步引擎的停止改为在线程池线程上执行并设超时上限，
    /// 脱离调用方可能持有的 SynchronizationContext（如 WinForm UI 线程），
    /// 从根上规避 sync-over-async 死锁，同时避免无限阻塞。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _historyStorage?.Dispose();
        if (_syncEngine is not null)
        {
            try
            {
                // Task.Run 把异步停止派发到线程池线程，不在调用方上下文上等待，避免经典死锁。
                if (!Task.Run(() => _syncEngine.StopAsync()).Wait(TimeSpan.FromSeconds(10)))
                {
                    _logger.LogWarning("Runner RemoteSync 停止超时（10s），继续释放");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Runner RemoteSync 停止异常");
            }
            finally
            {
                _syncEngine.Dispose();
            }
        }
        _localExecutor?.Dispose();
    }

    private static SerializedSqlExecutor<SqlSugarExecutor>? CreateLocalExecutor(
        DataStorageOptions storage,
        ILoggerFactory loggerFactory,
        ILogger<RunnerStorageCoordinator> logger)
    {
        if (string.Equals(storage.Type, "None", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Runner DataStorage 未启用，本地历史/FieldMapping/SQL 将写入 JSONL 降级文件");
            return null;
        }

        var dbType = ResolveSqlSugarDbType(storage.Type);
        if (dbType is null)
        {
            logger.LogWarning("Runner DataStorage 类型 {Type} 暂未接入本地数据库执行", storage.Type);
            return null;
        }

        var connectionString = ResolveConnectionString(storage.Type, storage.ConnectionString);
        logger.LogInformation("Runner 本地数据库已启用: Type={Type}", storage.Type);
        var sqlSugar = new SqlSugarExecutor(loggerFactory.CreateLogger<SqlSugarExecutor>(), dbType.Value, connectionString);
        // 串行化包装：止住多线程共享 SqlSugarClient 的并发缺陷（见 SerializedSqlExecutor 注释）。
        return new SerializedSqlExecutor<SqlSugarExecutor>(sqlSugar);
    }

    private static SqlSugar.DbType? ResolveSqlSugarDbType(string? storageType)
    {
        return storageType?.Trim().ToLowerInvariant() switch
        {
            "sqlite" => SqlSugar.DbType.Sqlite,
            "mysql" => SqlSugar.DbType.MySql,
            "sqlserver" or "mssql" => SqlSugar.DbType.SqlServer,
            _ => null
        };
    }

    private static string ResolveConnectionString(string? storageType, string? connectionString)
    {
        if (string.Equals(storageType, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return $"Data Source={ResolveSqlitePath(connectionString)}";
        }

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString.Trim();
        }

        throw new InvalidOperationException($"Runner DataStorage 类型 {storageType} 必须配置连接字符串");
    }

    private static string ResolveSqlitePath(string? connectionString)
    {
        var raw = string.IsNullOrWhiteSpace(connectionString)
            ? "./data/iot_runner.db"
            : connectionString.Trim();

        const string dataSourcePrefix = "Data Source=";
        if (raw.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[dataSourcePrefix.Length..].Split(';', 2)[0].Trim();
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = "./data/iot_runner.db";
        }

        return Path.GetFullPath(raw, AppContext.BaseDirectory);
    }

    // ═══════════════════════════════════════════════════════════════
    //  远端同步引擎
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 创建远端同步引擎。
    /// 仅当本地存储为 Sqlite 且 RemoteSync.Enabled=true 且远端类型/连接串有效时启动；
    /// 否则明确日志降级，不影响本地采集。
    /// </summary>
    private static SyncEngine? CreateRemoteSyncEngine(
        DataStorageOptions storage,
        ILogger<RunnerStorageCoordinator> logger)
    {
        // 只有本地存储为 SQLite 才能配合 SyncEngine（它内部使用 SQLite 作为源库）
        if (!string.Equals(storage.Type, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Runner RemoteSync 跳过：本地存储类型 {Type} 非 SQLite", storage.Type);
            return null;
        }

        var remoteSync = storage.RemoteSync;
        if (remoteSync?.Enabled != true)
        {
            logger.LogInformation("Runner RemoteSync 未启用，跳过");
            return null;
        }

        // 解析远端类型
        var targetType = ResolveTargetType(remoteSync.Type);
        if (targetType is null)
        {
            logger.LogWarning("Runner RemoteSync 跳过：远端类型 {Type} 无效或不受支持", remoteSync.Type);
            return null;
        }

        // 检查远端连接串
        if (string.IsNullOrWhiteSpace(remoteSync.ConnectionString))
        {
            logger.LogWarning("Runner RemoteSync 跳过：远端连接字符串为空，Type={Type}", remoteSync.Type);
            return null;
        }

        // 复用现有 ResolveSqlitePath 解析本地 SQLite 文件路径
        var localDbPath = ResolveSqlitePath(storage.ConnectionString);
        var config = new DataSyncConfig
        {
            LocalDbPath = localDbPath,
            RemoteTargets = new List<RemoteTargetConfig>
            {
                new()
                {
                    Name = $"RemoteSync-{remoteSync.Type}",
                    Type = targetType.Value,
                    ConnectionString = remoteSync.ConnectionString.Trim()
                }
            }
        };

        var syncLogger = new RunnerSyncLogger(logger);
        logger.LogInformation(
            "Runner RemoteSync 已启用: LocalDbPath={LocalDb}, RemoteType={Type}",
            localDbPath, remoteSync.Type);

        return new SyncEngine(config, syncLogger);
    }

    /// <summary>
    /// 将配置字符串映射为 ZL.DataSync 的 TargetType。
    /// 支持：mysql/sqlserver/mssql/postgresql/postgres/pgsql/oracle。
    /// 不支持的字符串返回 null。
    /// </summary>
    private static DataSync.Config.TargetType? ResolveTargetType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "mysql" => DataSync.Config.TargetType.MySql,
            "sqlserver" or "mssql" => DataSync.Config.TargetType.SqlServer,
            "postgresql" or "postgres" or "pgsql" => DataSync.Config.TargetType.PostgreSql,
            "oracle" => DataSync.Config.TargetType.Oracle,
            _ => null
        };
    }

    /// <summary>
    /// IStructuredLogger 适配器，将 SyncEngine 的内部日志通过 Runner 的 ILogger 输出，
    /// 复用 Runner 的日志配置（控制台/文件等）。
    /// </summary>
    private sealed class RunnerSyncLogger : DataSync.Infrastructure.IStructuredLogger
    {
        private readonly ILogger _logger;
        private readonly string? _source;

        public RunnerSyncLogger(ILogger logger, string? source = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _source = source;
        }

        public DataSync.Infrastructure.IStructuredLogger ForSource(string source)
            => new RunnerSyncLogger(_logger, source);

        public void Info(string message)
            => _logger.LogInformation("[SyncEngine] {Source}{Msg}", FormatSource(), message);

        public void Warning(string message)
            => _logger.LogWarning("[SyncEngine] {Source}{Msg}", FormatSource(), message);

        public void Error(string message)
            => _logger.LogError("[SyncEngine] {Source}{Msg}", FormatSource(), message);

        public void Debug(string message)
            => _logger.LogDebug("[SyncEngine] {Source}{Msg}", FormatSource(), message);

        public void Flush() { }
        public void Dispose() { }

        private string FormatSource()
            => string.IsNullOrWhiteSpace(_source) ? "" : $"[{_source}] ";
    }
}
