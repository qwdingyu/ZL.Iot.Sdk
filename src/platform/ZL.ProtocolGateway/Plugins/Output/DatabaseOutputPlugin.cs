using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Microsoft.Data.SqlClient;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    public class DatabaseOutputConfig
    {
        public string Name { get; set; }
        public string Provider { get; set; } = "Sqlite";
        public string ConnectionString { get; set; } = "DataSource=protocol-gateway.db";
        public string TableName { get; set; } = "gateway_messages";
        public bool AutoCreateDatabase { get; set; } = true;

        /// <summary>
        /// 数据保留小时数 — 0 表示禁用时间轮转清理（默认 168 = 7 天）
        /// <para>7×24 运行场景下，无清理的表会在 30 天内增长到数 GB。</para>
        /// </summary>
        public int RetentionHours { get; set; } = 168;

        /// <summary>
        /// 最大行数 — 0 表示禁用容量控制（默认 1000000）
        /// </summary>
        public long MaxRows { get; set; } = 1_000_000;

        /// <summary>
        /// 自动清理间隔（分钟）— 0 表示禁用自动清理（默认 60）
        /// </summary>
        public int AutoPurgeIntervalMinutes { get; set; } = 60;

        /// <summary>
        /// 是否启用批量写入（默认 true）。开启后将多条记录合并为单次批量 INSERT，性能提升 20-100×。
        /// </summary>
        public bool EnableBatchWrite { get; set; } = true;

        /// <summary>
        /// 批量写入最大条数（默认 100），达到此数量立即刷新。
        /// </summary>
        public int MaxBatchSize { get; set; } = 100;

        /// <summary>
        /// 批量刷新间隔（毫秒，默认 1000）。即使未满 BatchSize，超时也刷新。
        /// </summary>
        public int BatchFlushIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 合法的数据库 Provider 名称
        /// </summary>
        private static readonly HashSet<string> ValidProviders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Sqlite", "MySql", "SqlServer", "PostgreSQL", "Mssql", "Postgres", "PG"
        };

        /// <summary>
        /// 验证配置合法性
        /// </summary>
        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(ConnectionString))
                errors.Add(new ConfigValidationError(nameof(ConnectionString), "连接字符串不能为空"));
            if (string.IsNullOrWhiteSpace(TableName))
                errors.Add(new ConfigValidationError(nameof(TableName), "表名不能为空"));
            if (!string.IsNullOrWhiteSpace(Provider) && !ValidProviders.Contains(Provider))
                errors.Add(new ConfigValidationError(nameof(Provider), $"不支持的数据库 Provider: {Provider}（支持: Sqlite/MySql/SqlServer/PostgreSQL）"));
            if (RetentionHours < 0)
                errors.Add(new ConfigValidationError(nameof(RetentionHours), "保留小时数不能为负数"));
            if (MaxRows < 0)
                errors.Add(new ConfigValidationError(nameof(MaxRows), "最大行数不能为负数"));
            if (AutoPurgeIntervalMinutes < 0)
                errors.Add(new ConfigValidationError(nameof(AutoPurgeIntervalMinutes), "自动清理间隔不能为负数"));
            return errors;
        }
    }

    /// <summary>
    /// 数据库输出插件 - 使用 Dapper 微 ORM 将消息写入关系型数据库
    /// 继承 OutputPluginBase，消除状态管理样板代码
    /// </summary>
    public class DatabaseOutputPlugin : OutputPluginBase
    {
        private readonly DatabaseOutputConfig _config;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private DbConnection _connection;
        private readonly string _insertSql; // 构造时生成并缓存，避免热路径重复拼接
        private Timer _purgeTimer; // 自动清理定时器
        private SchemaManager.DatabaseProvider _provider; // 缓存 provider，避免重复解析

        // 批量写入缓冲
        private readonly object _batchLock = new();
        private List<GatewayDatabaseRecord> _batchBuffer = new(100);
        private Timer? _batchFlushTimer;
        private int _batchCount;

        public DatabaseOutputPlugin(DatabaseOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
            // 构造时验证表名并缓存 INSERT SQL，避免热路径重复拼接和校验
            SchemaManager.ValidateTableName(_config.TableName);
            _insertSql = $@"INSERT INTO {_config.TableName} (Topic, ContentType, PayloadText, PayloadHex, MetadataJson, CreatedAt)
                            VALUES (@Topic, @ContentType, @PayloadText, @PayloadHex, @MetadataJson, @CreatedAt)";
        }

        public override string Name => string.IsNullOrWhiteSpace(_config.Name)
            ? $"{NormalizeProvider(_config.Provider)}-{_config.TableName}"
            : _config.Name;

        public override string ProtocolType => NormalizeProvider(_config.Provider);

        /// <summary>
        /// 启动：初始化数据库连接并自动创建表结构（Dapper + SchemaManager）
        /// 异常由基类自动捕获并触发 Fatal 状态通知
        /// </summary>
        protected override async Task OnStartAsync(CancellationToken ct)
        {
            _provider = ResolveProvider(_config.Provider);
            var cs = NormalizeConnectionString(_config.ConnectionString, _config.Provider);

            _connection = _provider switch
            {
                SchemaManager.DatabaseProvider.Sqlite => new SqliteConnection(cs),
                SchemaManager.DatabaseProvider.MySql => new MySqlConnection(cs),
                SchemaManager.DatabaseProvider.SqlServer => new SqlConnection(cs),
                SchemaManager.DatabaseProvider.PostgreSQL => new NpgsqlConnection(cs),
                _ => throw new NotSupportedException($"Unsupported database provider: {_config.Provider}")
            };

            await _connection.OpenAsync(ct);

            // 自动建库（仅 SQLite 支持）
            if (_config.AutoCreateDatabase && IsAutoCreateDatabaseSupported(_config.Provider))
            {
                // SQLite 连接文件时自动创建数据库文件，无需额外操作
            }

            // 自动建表
            await SchemaManager.EnsureTableAsync(_connection, _provider, _config.TableName, ct);

            // 启动自动清理定时器（防止数据库无限增长）
            if (_config.AutoPurgeIntervalMinutes > 0)
            {
                var interval = TimeSpan.FromMinutes(_config.AutoPurgeIntervalMinutes);
                _purgeTimer = new Timer(PurgeCallback, null, interval, interval);
                GatewayLog.Info(Name, $"Auto-purge timer started: every {_config.AutoPurgeIntervalMinutes}m, retention={_config.RetentionHours}h, maxRows={_config.MaxRows}");
            }
        }

        /// <summary>
        /// 停止：刷新批量缓冲区，释放数据库连接和写入锁
        /// </summary>
        protected override async Task OnStopAsync()
        {
            // 先停止自动清理定时器
            _purgeTimer?.Dispose();
            _purgeTimer = null;

            // 刷新批量缓冲区中剩余的数据
            await FlushBatchAsync();

            // 先等待所有正在进行的写入完成，再释放资源
            // 注意：不在此处 Dispose _writeLock，避免 OnSendAsync 中正在 WaitAsync 的线程拿到 ObjectDisposedException
            await _writeLock.WaitAsync();
            try
            {
                _connection?.Dispose();
                _connection = null;
            }
            finally
            {
                _writeLock.Release();
            }
            // _writeLock 由基类 Dispose 路径清理（或在此处释放但不 Dispose，由 GC 回收）
            // 不在此处调用 SetConnectionState，由基类 StopAsync finally 统一处理
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _purgeTimer?.Dispose();
                _batchFlushTimer?.Dispose();
                _writeLock?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// 自动清理回调 — 定期删除过期数据，防止数据库无限增长。
        /// 使用 _writeLock 避免与写入并发冲突。
        /// </summary>
        private void PurgeCallback(object state)
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                return;

            // 使用 fire-and-forget 模式，避免 Timer 回调阻塞
            _ = Task.Run(async () =>
            {
                await _writeLock.WaitAsync();
                try
                {
                    if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                        return;

                    int deleted = await SchemaManager.PurgeOldRowsAsync(
                        _connection, _provider, _config.TableName,
                        _config.RetentionHours, _config.MaxRows);

                    if (deleted > 0)
                    {
                        GatewayLog.Info(Name, $"Auto-purge: deleted {deleted} rows from {_config.TableName}");
                    }
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn(Name, $"Auto-purge failed: {ex.Message}", ex);
                }
                finally
                {
                    _writeLock.Release();
                }
            });
        }

        /// <summary>
        /// 发送：将消息写入数据库表。
        /// 支持批量写入模式（EnableBatchWrite=true）：先将记录加入缓冲区，达到 MaxBatchSize 或超时后批量 INSERT。
        /// 批量写入相比逐行 INSERT 性能提升 20-100×。
        /// </summary>
        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null)
            {
                return;
            }

            var row = new GatewayDatabaseRecord
            {
                Topic = message.Topic ?? string.Empty,
                ContentType = message.ContentType ?? "binary",
                PayloadText = ResolvePayloadText(message),
                PayloadHex = message.GetHexContent() ?? string.Empty,
                MetadataJson = JsonSerializer.Serialize(message.Metadata ?? new Dictionary<string, string>()),
                WritesJson = message.Writes?.Count > 0
                    ? JsonSerializer.Serialize(message.Writes)
                    : string.Empty,
                CreatedAt = message.Timestamp == default ? DateTime.Now : message.Timestamp
            };

            if (_config.EnableBatchWrite)
            {
                bool shouldFlush = false;
                lock (_batchLock)
                {
                    _batchBuffer.Add(row);
                    _batchCount++;
                    shouldFlush = _batchCount >= _config.MaxBatchSize;

                    // 首次入缓冲时启动定时器
                    if (!shouldFlush && _batchFlushTimer == null)
                    {
                        _batchFlushTimer = new Timer(_ => FlushBatchAsync(), null, TimeSpan.FromMilliseconds(_config.BatchFlushIntervalMs), Timeout.InfiniteTimeSpan);
                    }
                }

                if (shouldFlush)
                {
                    await FlushBatchAsync();
                }
            }
            else
            {
                // 非批量模式：逐行 INSERT
                await _writeLock.WaitAsync(cancellationToken);
                try
                {
                    if (_connection == null)
                    {
                        throw new InvalidOperationException("Database connection not initialized");
                    }

                    // Dapper 2.1.35 ExecuteAsync 无 CancellationToken 参数。
                    await ((IDbConnection)_connection).ExecuteAsync(_insertSql, row);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
        }

        /// <summary>
        /// 将缓冲区中的记录批量写入数据库。
        /// 使用 Dapper 的批量参数执行，一次 SQL 完成所有 INSERT。
        /// </summary>
        private async Task FlushBatchAsync()
        {
            List<GatewayDatabaseRecord> toFlush;
            lock (_batchLock)
            {
                if (_batchCount == 0)
                    return;

                toFlush = _batchBuffer;
                _batchBuffer = new List<GatewayDatabaseRecord>(Math.Max(_config.MaxBatchSize, 100));
                _batchCount = 0;

                _batchFlushTimer?.Dispose();
                _batchFlushTimer = null;
            }

            await _writeLock.WaitAsync();
            try
            {
                if (_connection == null)
                {
                    throw new InvalidOperationException("Database connection not initialized");
                }

                // Dapper 批量执行：传入 IEnumerable 自动批量 INSERT
                DbTransaction transaction = null;
                try
                {
                    transaction = await _connection.BeginTransactionAsync();
                    await ((IDbConnection)_connection).ExecuteAsync(_insertSql, toFlush, transaction);
                    await transaction.CommitAsync();
                    transaction = null; // committed successfully, skip rollback
                }
                catch (Exception ex)
                {
                    if (transaction != null)
                    {
                        try { await transaction.RollbackAsync(); } catch { }
                    }
                    GatewayLog.Warn(Name, $"Batch flush failed ({toFlush.Count} rows): {ex.Message}", ex);
                    throw;
                }
                finally
                {
                    transaction?.Dispose();
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public static SchemaManager.DatabaseProvider ResolveProvider(string provider)
        {
            return NormalizeProvider(provider) switch
            {
                "Sqlite" => SchemaManager.DatabaseProvider.Sqlite,
                "MySql" => SchemaManager.DatabaseProvider.MySql,
                "SqlServer" => SchemaManager.DatabaseProvider.SqlServer,
                "PostgreSQL" => SchemaManager.DatabaseProvider.PostgreSQL,
                _ => throw new NotSupportedException($"Unsupported database provider: {provider}")
            };
        }

        public static string NormalizeProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return "Sqlite";
            }

            return provider.Trim().ToLowerInvariant() switch
            {
                "sqlite" => "Sqlite",
                "mysql" => "MySql",
                "sqlserver" => "SqlServer",
                "mssql" => "SqlServer",
                "postgresql" => "PostgreSQL",
                "postgres" => "PostgreSQL",
                "pg" => "PostgreSQL",
                _ => provider.Trim()
            };
        }

        public static bool IsAutoCreateDatabaseSupported(string provider)
        {
            return NormalizeProvider(provider) == "Sqlite";
        }

        public static string NormalizeConnectionString(string connectionString, string provider)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return NormalizeProvider(provider) == "Sqlite"
                    ? "DataSource=protocol-gateway.db"
                    : string.Empty;
            }

            return connectionString;
        }

        private static string ResolvePayloadText(Message message)
        {
            if (message.Payload == null || message.Payload.Length == 0)
            {
                return string.Empty;
            }

            // null-safe 比较：ContentType 为 null 时视为 binary，不尝试 UTF8 解码
            if (string.Equals(message.ContentType, "binary", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(message.Payload);
        }

        /// <summary>
        /// 数据库记录 POCO — 仅用于 Dapper 参数映射，不再需要 ORM 属性。
        /// </summary>
        private class GatewayDatabaseRecord
        {
            public long Id { get; set; }
            public string Topic { get; set; }
            public string ContentType { get; set; }
            public string PayloadText { get; set; }
            public string PayloadHex { get; set; }
            public string MetadataJson { get; set; }
            public string WritesJson { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }

    public sealed class SqliteOutputConfig : DatabaseOutputConfig
    {
        public SqliteOutputConfig()
        {
            Provider = "Sqlite";
        }
    }

    public sealed class SqliteOutputPlugin : DatabaseOutputPlugin
    {
        public SqliteOutputPlugin(SqliteOutputConfig config) : base(config)
        {
        }
    }
}
