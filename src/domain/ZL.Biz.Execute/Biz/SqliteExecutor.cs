using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ZL.Iot.Interface;

namespace ZL.Biz.Execute.Biz
{
    /// <summary>
    /// 加固版 SQLite 数据库执行器
    /// 针对工业环境优化：解决事务死锁、WAL 并发模式、存储寿命保护 (Vacuum)
    /// </summary>
    public class SqliteExecutor : ISqlExecutor
    {
        private readonly ILogger<SqliteExecutor> _logger;
        private readonly string _connectionString;
        private SqliteConnection _activeConnection;
        private SqliteTransaction _activeTransaction;

        public string Dialect => "Sqlite";

        public SqliteExecutor(ILogger<SqliteExecutor> logger, string dbPath)
        {
            _logger = logger;
            // 工业级加固：
            // 1. Default Timeout=30: 增加繁忙超时，预防 "Database is locked"
            // 2. Journal Mode=WAL: 启用预写日志模式，允许并发读写，显著提升边缘侧吞吐量
            _connectionString = $"Data Source={dbPath};Cache=Shared;Default Timeout=30;Journal Mode=WAL";
            
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                
                // 1. 自动清理碎片：设置为 INCREMENTAL 配合定期 VACUUM，保护 Flash 寿命
                command.CommandText = "PRAGMA auto_vacuum = INCREMENTAL;";
                command.ExecuteNonQuery();
                
                // 2. 同步模式：NORMAL 在 WAL 模式下兼顾安全与性能
                command.CommandText = "PRAGMA synchronous = NORMAL;";
                command.ExecuteNonQuery();
                
                _logger.LogInformation("SQLite Executor initialized with industrial-grade pragmas (WAL, NORMAL, INCREMENTAL_VACUUM).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize SQLite pragmas. Database might still work but with default performance.");
            }
        }

        public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql, Dictionary<string, object> parameters = null)
        {
            bool isInternalConn = _activeConnection == null;
            var connection = isInternalConn ? new SqliteConnection(_connectionString) : _activeConnection;
            
            try 
            {
                if (isInternalConn) await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = sql;
                if (_activeTransaction != null) command.Transaction = _activeTransaction;

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }

                var results = new List<Dictionary<string, object>>();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }
                    results.Add(row);
                }
                return results;
            }
            finally
            {
                if (isInternalConn) await connection.DisposeAsync();
            }
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object> parameters = null)
        {
            bool isInternalConn = _activeConnection == null;
            var connection = isInternalConn ? new SqliteConnection(_connectionString) : _activeConnection;

            try
            {
                if (isInternalConn) await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = sql;
                if (_activeTransaction != null) command.Transaction = _activeTransaction;

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }

                return await command.ExecuteNonQueryAsync();
            }
            finally
            {
                if (isInternalConn) await connection.DisposeAsync();
            }
        }

        public async Task<int> ExecuteBatchNonQueryAsync(string sql, IEnumerable<Dictionary<string, object>> parameterList)
        {
            if (parameterList == null || !parameterList.Any()) return 0;

            bool isInternalConn = _activeConnection == null;
            var connection = isInternalConn ? new SqliteConnection(_connectionString) : _activeConnection;

            try
            {
                if (isInternalConn) await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = sql;
                if (_activeTransaction != null) command.Transaction = _activeTransaction;

                int totalAffected = 0;
                bool paramsInitialized = false;

                foreach (var parameters in parameterList)
                {
                    if (!paramsInitialized)
                    {
                        foreach (var kv in parameters)
                        {
                            var p = command.CreateParameter();
                            p.ParameterName = kv.Key;
                            command.Parameters.Add(p);
                        }
                        paramsInitialized = true;
                    }

                    foreach (var kv in parameters)
                    {
                        command.Parameters[kv.Key].Value = kv.Value ?? DBNull.Value;
                    }

                    totalAffected += await command.ExecuteNonQueryAsync();
                }

                return totalAffected;
            }
            finally
            {
                if (isInternalConn) await connection.DisposeAsync();
            }
        }

        public async Task<object> ExecuteScalarAsync(string sql, Dictionary<string, object> parameters = null)
        {
            bool isInternalConn = _activeConnection == null;
            var connection = isInternalConn ? new SqliteConnection(_connectionString) : _activeConnection;

            try
            {
                if (isInternalConn) await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = sql;
                if (_activeTransaction != null) command.Transaction = _activeTransaction;

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }

                return await command.ExecuteScalarAsync();
            }
            finally
            {
                if (isInternalConn) await connection.DisposeAsync();
            }
        }

        public bool Validate(string sql, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"EXPLAIN {sql}";
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public void BeginTransaction()
        {
            if (_activeTransaction != null) throw new InvalidOperationException("Transaction already in progress");
            _activeConnection = new SqliteConnection(_connectionString);
            _activeConnection.Open();
            _activeTransaction = _activeConnection.BeginTransaction();
        }

        public void CommitTransaction()
        {
            try {
                _activeTransaction?.Commit();
            }
            finally {
                CleanupTransaction();
            }
        }

        public void RollbackTransaction()
        {
            try {
                if (_activeTransaction != null && _activeTransaction.Connection != null)
                {
                    _activeTransaction.Rollback();
                    _logger.LogDebug("Sqlite transaction rolled back successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during Sqlite transaction rollback. Connection may be already closed.");
            }
            finally {
                CleanupTransaction();
            }
        }

        private void CleanupTransaction()
        {
            _activeTransaction?.Dispose();
            _activeTransaction = null;
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
    }
}
