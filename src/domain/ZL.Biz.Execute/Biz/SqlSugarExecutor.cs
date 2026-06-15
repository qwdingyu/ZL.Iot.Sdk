using Microsoft.Extensions.Logging;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ZL.Iot.Interface;

namespace ZL.Biz.Execute.Biz;

/// <summary>
/// 基于 SqlSugar 的通用表存储执行器。
/// </summary>
public sealed class SqlSugarExecutor : ISqlExecutor, ITableStorageExecutor, IDisposable
{
    private readonly ILogger<SqlSugarExecutor> _logger;
    private readonly ISqlSugarClient _db;
    private readonly bool _ownsDb;

    public SqlSugarExecutor(ILogger<SqlSugarExecutor> logger, ISqlSugarClient db, bool ownsDb = false)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ownsDb = ownsDb;
    }

    public SqlSugarExecutor(ILogger<SqlSugarExecutor> logger, SqlSugar.DbType dbType, string connectionString)
        : this(logger, CreateClient(dbType, connectionString), ownsDb: true)
    {
    }

    public string Dialect => (_db.CurrentConnectionConfig?.DbType ?? SqlSugar.DbType.MySql) switch
    {
        SqlSugar.DbType.MySql => "MySql",
        SqlSugar.DbType.SqlServer => "SqlServer",
        SqlSugar.DbType.PostgreSQL => "PostgreSQL",
        SqlSugar.DbType.Oracle => "Oracle",
        SqlSugar.DbType.Sqlite => "Sqlite",
        _ => "Unknown"
    };

    public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql, Dictionary<string, object> parameters = null)
    {
        var dataTable = await _db.Ado.GetDataTableAsync(sql, ToSugarParameters(parameters)).ConfigureAwait(false);
        return DataTableToList(dataTable);
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object> parameters = null)
    {
        return await _db.Ado.ExecuteCommandAsync(sql, ToSugarParameters(parameters)).ConfigureAwait(false);
    }

    public async Task<int> ExecuteBatchNonQueryAsync(string sql, IEnumerable<Dictionary<string, object>> parameterList)
    {
        if (parameterList == null)
        {
            return 0;
        }

        var batches = parameterList as IReadOnlyCollection<Dictionary<string, object>> ?? parameterList.ToList();
        if (batches.Count == 0)
        {
            return 0;
        }

        // 整批包裹在单个事务中：任一条失败则整批回滚，避免部分写入导致数据不一致。
        // UseTranAsync 自动 Commit/Rollback 并管理连接生命周期（兼容 IsAutoCloseConnection）。
        var result = await _db.Ado.UseTranAsync(async () =>
        {
            var affected = 0;
            foreach (var parameters in batches)
            {
                affected += await _db.Ado
                    .ExecuteCommandAsync(sql, ToSugarParameters(parameters))
                    .ConfigureAwait(false);
            }

            return affected;
        }).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(result.ErrorException, "[SqlSugarExecutor] 批量执行失败已回滚: {Message}", result.ErrorMessage);
            throw new InvalidOperationException($"批量执行失败已回滚: {result.ErrorMessage}", result.ErrorException);
        }

        return result.Data;
    }

    public async Task<object> ExecuteScalarAsync(string sql, Dictionary<string, object> parameters = null)
    {
        return await _db.Ado.GetScalarAsync(sql, ToSugarParameters(parameters)).ConfigureAwait(false);
    }

    public bool Validate(string sql, out string errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(sql))
        {
            errorMessage = "SQL 语句不能为空";
            return false;
        }

        return true;
    }

    public void BeginTransaction() => _db.Ado.BeginTran();

    public void CommitTransaction() => _db.Ado.CommitTran();

    public void RollbackTransaction() => _db.Ado.RollbackTran();

    public Task EnsureTableAsync(string tableName, IReadOnlyList<TableColumnDefinition> columns)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("表名不能为空", nameof(tableName));
        }

        var normalizedColumns = NormalizeColumns(columns);
        if (normalizedColumns.Count == 0)
        {
            throw new InvalidOperationException($"表 {tableName} 没有可创建的列");
        }

        var existing = _db.DbMaintenance.IsAnyTable(tableName, false);
        if (!existing)
        {
            var dbColumns = BuildDbColumns(normalizedColumns, _db.CurrentConnectionConfig.DbType);
            _db.DbMaintenance.CreateTable(tableName, dbColumns);
            _logger.LogInformation("[SqlSugarExecutor] 已创建表 {TableName}", tableName);
            return Task.CompletedTask;
        }

        var existingColumns = _db.DbMaintenance.GetColumnInfosByTableName(tableName)
            .Select(x => x.DbColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var addColumns = normalizedColumns
            .Where(column => !existingColumns.Contains(column.Name))
            .ToList();

        foreach (var column in addColumns)
        {
            var dbColumn = BuildDbColumn(column, _db.CurrentConnectionConfig.DbType);
            _db.DbMaintenance.AddColumn(tableName, dbColumn);
        }

        if (addColumns.Count > 0)
        {
            _logger.LogInformation("[SqlSugarExecutor] 已补齐表 {TableName} 的 {Count} 个字段", tableName, addColumns.Count);
        }

        return Task.CompletedTask;
    }

    public async Task<int> InsertRowsAsync(string tableName, IReadOnlyList<Dictionary<string, object>> rows)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("表名不能为空", nameof(tableName));
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        var insertRows = rows
            .Select(row => new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return await _db.Insertable(insertRows)
            .AS(tableName)
            .ExecuteCommandAsync()
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_ownsDb)
        {
            _db.Dispose();
        }
    }

    private static ISqlSugarClient CreateClient(SqlSugar.DbType dbType, string connectionString)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            DbType = dbType,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
    }

    private static List<TableColumnDefinition> NormalizeColumns(IReadOnlyList<TableColumnDefinition> columns)
    {
        return columns
            .Where(column => !string.IsNullOrWhiteSpace(column.Name))
            .Select(column => new TableColumnDefinition
            {
                Name = column.Name.Trim(),
                DataType = string.IsNullOrWhiteSpace(column.DataType) ? "TEXT" : column.DataType.Trim(),
                IsPrimaryKey = column.IsPrimaryKey,
                IsIdentity = column.IsIdentity,
                IsNullable = column.IsNullable,
                Length = column.Length
            })
            .ToList();
    }

    private static List<DbColumnInfo> BuildDbColumns(IReadOnlyList<TableColumnDefinition> columns, SqlSugar.DbType dbType)
    {
        return columns.Select(column => BuildDbColumn(column, dbType)).ToList();
    }

    private static DbColumnInfo BuildDbColumn(TableColumnDefinition column, SqlSugar.DbType dbType)
    {
        var dataType = column.DataType?.Trim().ToLowerInvariant() switch
        {
            "bigint" => dbType == SqlSugar.DbType.Sqlite ? "INTEGER" : "BIGINT",
            "int" or "integer" or "int32" => "INT",
            "datetime" => dbType == SqlSugar.DbType.Sqlite ? "TEXT" : "DATETIME",
            "double" or "float" => dbType == SqlSugar.DbType.SqlServer ? "FLOAT" : "DOUBLE",
            "nvarchar" or "varchar" or "text" => dbType == SqlSugar.DbType.MySql ? "VARCHAR" : column.DataType,
            _ => column.DataType
        };

        return new DbColumnInfo
        {
            DbColumnName = column.Name,
            PropertyName = column.Name,
            DataType = dataType,
            IsPrimarykey = column.IsPrimaryKey,
            IsIdentity = column.IsIdentity,
            IsNullable = column.IsNullable,
            Length = column.Length
        };
    }

    private static SugarParameter[] ToSugarParameters(Dictionary<string, object> parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return Array.Empty<SugarParameter>();
        }

        return parameters.Select(kv => new SugarParameter(kv.Key, kv.Value ?? DBNull.Value)).ToArray();
    }

    private static List<Dictionary<string, object>> DataTableToList(DataTable dataTable)
    {
        var result = new List<Dictionary<string, object>>();
        foreach (DataRow row in dataTable.Rows)
        {
            var rowDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn col in dataTable.Columns)
            {
                rowDict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }
            result.Add(rowDict);
        }

        return result;
    }
}
