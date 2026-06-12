// ============================================================
//  FieldMapping 写入器 — 基于 ISqlExecutor 的通用实现
//  使用跨数据库兼容的 SQL 类型（INTEGER / BIGINT / REAL / TEXT / TIMESTAMP）
//  避免 MySQL 方言（AUTO_INCREMENT / ENGINE=InnoDB / CHARSET）
// ============================================================

using System.Data;
using Microsoft.Extensions.Logging;
using ZL.Iot.Interface;

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// 基于 ISqlExecutor 的 FieldMapping 写入器
/// 使用跨数据库兼容的 SQL，不依赖特定数据库方言
/// </summary>
public class SqlExecutorFieldMappingSink : IFieldMappingSink
{
    private readonly ISqlExecutor _sqlExecutor;
    private readonly ILogger _logger;

    public SqlExecutorFieldMappingSink(ISqlExecutor sqlExecutor, ILogger logger)
    {
        _sqlExecutor = sqlExecutor ?? throw new ArgumentNullException(nameof(sqlExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void EnsureTable(string tableName, List<FieldMappingRule> columns)
    {
        // 使用跨数据库兼容的 SQL 类型：
        // - INTEGER / BIGINT：所有数据库通用
        // - REAL：SQLite REAL = MySQL FLOAT/DOUBLE = SQL Server FLOAT = PostgreSQL REAL/DOUBLE
        // - TEXT：所有数据库通用
        // - TIMESTAMP：ISO 8601 文本格式，所有数据库通用
        var sql = $"CREATE TABLE IF NOT EXISTS {tableName} (";
        sql += "ID INTEGER PRIMARY KEY,";       // SQLite 自动递增；其他 DB 由应用或序列管理
        sql += "ProcessTime TEXT NOT NULL,";     // ISO 8601 文本，跨数据库兼容

        foreach (var col in columns)
        {
            var type = col.DataType?.ToLowerInvariant() switch
            {
                "int32" or "int" or "int16" or "short" => "INTEGER",
                "int64" or "long" => "BIGINT",
                "float" or "single" => "REAL",
                "double" or "float64" => "REAL",
                "datetime" => "TEXT",     // ISO 8601
                "bool" or "boolean" => "INTEGER",  // 0/1
                _ => "TEXT"
            };
            sql += $"{col.Name} {type},";
        }

        sql += "ProcessTime);"; // 逗号分隔处理完，最后用 ProcessTime 凑语法

        _sqlExecutor.ExecuteNonQueryAsync(sql).GetAwaiter().GetResult();
        _logger.LogInformation("[FieldMappingSink] 已确保表存在: {TableName}", tableName);
    }

    public void InsertRows(string tableName, List<Dictionary<string, object?>> rows)
    {
        foreach (var row in rows)
        {
            // 用参数化查询避免 SQL 注入
            var colNames = string.Join(", ", row.Keys);
            var colParams = string.Join(", ", row.Keys.Select(k => $"@{k}"));
            var sql = $"INSERT INTO {tableName} ({colNames}) VALUES ({colParams})";

            var parameters = row.ToDictionary(
                kv => kv.Key,
                kv => kv.Value switch
                {
                    DateTime dt => dt.ToString("O"),  // ISO 8601
                    DateTimeOffset dto => dto.ToString("O"),
                    _ => kv.Value ?? DBNull.Value
                });

            _sqlExecutor.ExecuteNonQueryAsync(sql, parameters).GetAwaiter().GetResult();
        }

        _logger.LogInformation("[FieldMappingSink] 已写入 {TableName}: {Rows} 行", tableName, rows.Count);
    }
}
