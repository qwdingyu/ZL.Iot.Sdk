// ============================================================
//  FieldMapping 写入器 — 基于 ISqlExecutor 的通用实现
//  使用跨数据库兼容的 SQL 类型（INTEGER / BIGINT / REAL / TEXT / TIMESTAMP）
//  避免 MySQL 方言（AUTO_INCREMENT / ENGINE=InnoDB / CHARSET）
// ============================================================

using System.Data;
using Microsoft.Extensions.Logging;
using ZL.Biz.Execute.Sql;
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
        // 表名/列名直接拼接进 DDL，无法参数化，必须做标识符白名单校验防注入。
        // 复用 ZL.Biz.Execute.Sql.SafeSqlBuilder 的 IsValidIdentifier（^[a-zA-Z_][a-zA-Z0-9_]*$）。
        if (!SafeSqlBuilder.IsValidIdentifier(tableName))
        {
            throw new ArgumentException($"FieldMapping 目标表名非法（仅允许字母/数字/下划线，且不以数字开头）: {tableName}", nameof(tableName));
        }

        // 使用跨数据库兼容的 SQL 类型：
        // - INTEGER / BIGINT：所有数据库通用
        // - REAL：SQLite REAL = MySQL FLOAT/DOUBLE = SQL Server FLOAT = PostgreSQL REAL/DOUBLE
        // - TEXT：所有数据库通用
        // - TIMESTAMP：ISO 8601 文本格式，所有数据库通用
        var columnDefinitions = new List<string>
        {
            "ID INTEGER PRIMARY KEY",
            "ProcessTime TEXT NOT NULL"
        };

        foreach (var col in columns.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
        {
            if (string.Equals(col.Name, "ID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(col.Name, "ProcessTime", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 列名同样拼接进 DDL，必须校验。
            if (!SafeSqlBuilder.IsValidIdentifier(col.Name))
            {
                throw new ArgumentException($"FieldMapping 列名非法（仅允许字母/数字/下划线，且不以数字开头）: {col.Name}", nameof(columns));
            }

            var type = col.DataType?.ToLowerInvariant() switch
            {
                "int32" or "int" or "int16" or "short" => "INTEGER",
                "int64" or "long" => "BIGINT",
                "float" or "single" => "REAL",
                "double" or "float64" => "REAL",
                "datetime" => "TEXT",
                "bool" or "boolean" => "INTEGER",
                _ => "TEXT"
            };
            columnDefinitions.Add($"{col.Name} {type}");
        }

        var sql = $"CREATE TABLE IF NOT EXISTS {tableName} ({string.Join(", ", columnDefinitions)});";

        _sqlExecutor.ExecuteNonQueryAsync(sql).GetAwaiter().GetResult();
        _logger.LogInformation("[FieldMappingSink] 已确保表存在: {TableName}", tableName);
    }

    public void InsertRows(string tableName, List<Dictionary<string, object?>> rows)
    {
        // 表名拼接进 INSERT，必须校验。
        if (!SafeSqlBuilder.IsValidIdentifier(tableName))
        {
            throw new ArgumentException($"FieldMapping 目标表名非法（仅允许字母/数字/下划线，且不以数字开头）: {tableName}", nameof(tableName));
        }

        foreach (var row in rows)
        {
            // 列名拼接进 SQL，必须逐列校验；值已参数化（@key），不在注入面内。
            foreach (var colName in row.Keys)
            {
                if (!SafeSqlBuilder.IsValidIdentifier(colName))
                {
                    throw new ArgumentException($"FieldMapping 列名非法（仅允许字母/数字/下划线，且不以数字开头）: {colName}", nameof(rows));
                }
            }

            // 用参数化查询避免 SQL 注入（值部分）
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
