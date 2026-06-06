// ============================================================
// 文件：DeadLetterStore.cs
// 描述：死信队列 SQLite 持久化存储
// 功能：将内存死信队列溢出时自动落盘，重启后可恢复诊断信息
// 修改日期：2026-06-05
// ============================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 死信消息持久化存储。
    /// 使用独立 SQLite 文件（gateway_deadletters.db），与数据输出插件的数据库隔离。
    /// 内存队列满时自动落盘，防止重启后丢失诊断信息。
    /// 
    /// P0 修复：SqliteConnection 不是线程安全的，所有 DB 操作通过 _dbLock 串行化。
    /// 启用 WAL 模式提升并发读取性能。
    /// </summary>
    public class DeadLetterStore : IDisposable, IAsyncDisposable
    {
        private const string DefaultDbPath = "gateway_deadletters.db";
        private const int DefaultMaxRows = 5000;
        private const int DefaultRetentionHours = 72; // 3 天

        private readonly string _dbPath;
        private readonly int _maxRows;
        private readonly int _retentionHours;

        // P0 修复：_dbLock 串行化所有 DB 操作（不仅是连接创建），
        // 因为 SqliteConnection 不支持并发异步操作。
        private readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);
        private SqliteConnection? _connection;
        private int _disposed;

        /// <summary>
        /// 死信持久化条目
        /// </summary>
        public class DeadLetterEntry
        {
            public long Id { get; set; }
            public string Topic { get; set; } = string.Empty;
            public string? ContentType { get; set; }
            public string? PayloadText { get; set; }
            public string? PayloadHex { get; set; }
            public string? ExceptionMessage { get; set; }
            public string? ExceptionType { get; set; }
            public string? OutputName { get; set; }
            public int RetryCount { get; set; }
            public string FailedAt { get; set; } = string.Empty; // UTC ISO 8601
            public string? TraceId { get; set; }
        }

        /// <summary>
        /// 创建死信持久化存储。
        /// </summary>
        /// <param name="dbPath">SQLite 文件路径，默认 gateway_deadletters.db</param>
        /// <param name="maxRows">最大持久化行数，超出时删除最旧记录（默认 5000）</param>
        /// <param name="retentionHours">保留小时数，0 表示不限制（默认 72 = 3 天）</param>
        public DeadLetterStore(string? dbPath = null, int maxRows = DefaultMaxRows, int retentionHours = DefaultRetentionHours)
        {
            _dbPath = dbPath ?? DefaultDbPath;
            _maxRows = maxRows;
            _retentionHours = retentionHours;
        }

        /// <summary>
        /// 获取或创建数据库连接，并启用 WAL 模式。
        /// P0 修复：连接创建和 WAL 启用在 _dbLock 保护下完成。
        /// </summary>
        private async ValueTask<SqliteConnection> GetConnectionAsync()
        {
            if (_connection != null) return _connection;
            await _dbLock.WaitAsync();
            try
            {
                if (_connection != null) return _connection; // 双重检查
                var conn = new SqliteConnection($"Data Source={_dbPath};Cache=Shared;");
                await conn.OpenAsync();
                // P0 修复：启用 WAL 模式，允许并发读取（虽然写入仍串行化）
                await ((IDbConnection)conn).ExecuteAsync("PRAGMA journal_mode=WAL");
                await EnsureTableAsync(conn);
                _connection = conn;
                return conn;
            }
            finally { _dbLock.Release(); }
        }

        private static async Task EnsureTableAsync(IDbConnection connection)
        {
            var exists = await ((IDbConnection)connection).QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DeadLetters'");

            if (exists == 0)
            {
                await ((IDbConnection)connection).ExecuteAsync(@"
                    CREATE TABLE DeadLetters (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Topic TEXT NOT NULL,
                        ContentType TEXT,
                        PayloadText TEXT,
                        PayloadHex TEXT,
                        ExceptionMessage TEXT,
                        ExceptionType TEXT,
                        OutputName TEXT,
                        RetryCount INTEGER NOT NULL DEFAULT 0,
                        FailedAt TEXT NOT NULL,
                        TraceId TEXT
                    )");
                await ((IDbConnection)connection).ExecuteAsync("CREATE INDEX idx_DeadLetters_FailedAt ON DeadLetters (FailedAt)");
                await ((IDbConnection)connection).ExecuteAsync("CREATE INDEX idx_DeadLetters_Topic ON DeadLetters (Topic)");
            }
        }

        /// <summary>
        /// 异步添加死信记录。
        /// P0 修复：整个操作在 _dbLock 保护下执行，确保 SqliteConnection 不被并发使用。
        /// </summary>
        public async Task AddAsync(DeadLetterEntry entry)
        {
            if (entry == null) return;

            var conn = await GetConnectionAsync();

            await _dbLock.WaitAsync();
            try
            {
                await ((IDbConnection)conn).ExecuteAsync(
                    @"INSERT INTO DeadLetters (Topic, ContentType, PayloadText, PayloadHex, ExceptionMessage, ExceptionType, OutputName, RetryCount, FailedAt, TraceId)
                      VALUES (@Topic, @ContentType, @PayloadText, @PayloadHex, @ExceptionMessage, @ExceptionType, @OutputName, @RetryCount, @FailedAt, @TraceId)",
                    new
                    {
                        entry.Topic,
                        entry.ContentType,
                        PayloadText = Truncate(entry.PayloadText, 4096),
                        PayloadHex = Truncate(entry.PayloadHex, 2048),
                        ExceptionMessage = Truncate(entry.ExceptionMessage, 2048),
                        entry.ExceptionType,
                        entry.OutputName,
                        entry.RetryCount,
                        FailedAt = entry.FailedAt,
                        entry.TraceId
                    });

                // 容量控制：超过 maxRows 时删除最旧记录
                await EnforceCapacityAsync(conn);

                // 时间轮转：删除过期记录
                if (_retentionHours > 0)
                {
                    await PurgeOldRowsAsync(conn);
                }
            }
            finally { _dbLock.Release(); }
        }

        /// <summary>
        /// 获取所有死信记录（按时间倒序）。
        /// P0 修复：在 _dbLock 保护下执行，避免与 AddAsync 并发使用同一连接。
        /// </summary>
        public async Task<IReadOnlyList<DeadLetterEntry>> GetAllAsync(int limit = 100)
        {
            var conn = await GetConnectionAsync();

            await _dbLock.WaitAsync();
            try
            {
                var rows = await ((IDbConnection)conn).QueryAsync<DeadLetterEntry>(
                    "SELECT * FROM DeadLetters ORDER BY Id DESC LIMIT @Limit",
                    new { Limit = limit });
                return rows.ToList().AsReadOnly();
            }
            finally { _dbLock.Release(); }
        }

        /// <summary>
        /// 获取死信记录总数。
        /// </summary>
        public async Task<int> GetCountAsync()
        {
            var conn = await GetConnectionAsync();

            await _dbLock.WaitAsync();
            try
            {
                return await ((IDbConnection)conn).QuerySingleAsync<int>("SELECT COUNT(*) FROM DeadLetters");
            }
            finally { _dbLock.Release(); }
        }

        /// <summary>
        /// 清空所有死信记录。
        /// </summary>
        public async Task ClearAsync()
        {
            var conn = await GetConnectionAsync();

            await _dbLock.WaitAsync();
            try
            {
                await ((IDbConnection)conn).ExecuteAsync("DELETE FROM DeadLetters");
            }
            finally { _dbLock.Release(); }
        }

        /// <summary>
        /// 强制持久化 — 确保所有 WAL 数据同步到主数据库文件。
        /// <para>SQLite WAL 模式下，数据写入 WAL 文件后不一定立即同步到主库。
        /// 优雅关闭时调用此方法执行 CHECKPOINT，确保数据落盘。</para>
        /// </summary>
        /// <param name="ct">取消令牌</param>
        public async Task ForcePersistAsync(CancellationToken ct = default)
        {
            if (_disposed == 1) return;

            var conn = await GetConnectionAsync();
            await _dbLock.WaitAsync(ct);
            try
            {
                // WAL CHECKPOINT PASSIVE：将 WAL 中的数据同步到主数据库文件。
                // PASSIVE 模式：如果无法获取锁则跳过，不阻塞其他读取者。
                await ((IDbConnection)conn).ExecuteAsync("PRAGMA wal_checkpoint(PASSIVE)");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GatewayLog.Warn("DeadLetterStore", $"ForcePersist checkpoint failed: {ex.Message}");
            }
            finally { _dbLock.Release(); }
        }

        private async Task EnforceCapacityAsync(SqliteConnection conn)
        {
            if (_maxRows <= 0) return;

            long count = await ((IDbConnection)conn).QuerySingleAsync<long>("SELECT COUNT(*) FROM DeadLetters");
            if (count > _maxRows)
            {
                long deleteBatch = Math.Max(100, (count - _maxRows) / 5);
                await ((IDbConnection)conn).ExecuteAsync(
                    "DELETE FROM DeadLetters WHERE Id IN (SELECT Id FROM DeadLetters ORDER BY FailedAt ASC LIMIT @deleteBatch)",
                    new { deleteBatch });
            }
        }

        private async Task PurgeOldRowsAsync(SqliteConnection conn)
        {
            await ((IDbConnection)conn).ExecuteAsync(
                "DELETE FROM DeadLetters WHERE datetime(FailedAt) < datetime('now', '-' || @Hours || ' hours')",
                new { Hours = _retentionHours });
        }

        private static string? Truncate(string? value, int maxLen)
        {
            if (value == null) return null;
            return value.Length > maxLen ? value.Substring(0, maxLen) : value;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            try { _dbLock.Wait(); }
            catch (ObjectDisposedException) { }
            try
            {
                _connection?.Dispose();
                _connection = null;
            }
            finally
            {
                _dbLock.Dispose();
            }
        }

        /// <summary>
        /// 异步释放资源 — 等待 _dbLock 后安全关闭连接。
        /// 优先使用此方法而非同步 Dispose，避免在未完成异步 DB 操作时关闭连接。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _connection?.Dispose();
                _connection = null;
            }
            finally
            {
                _dbLock.Dispose();
            }
        }
    }
}
