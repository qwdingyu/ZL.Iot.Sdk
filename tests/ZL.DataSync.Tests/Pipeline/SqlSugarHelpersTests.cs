using SqlSugar;
using ZL.DataSync.Infrastructure;
using ZL.DataSync.Pipeline;

namespace ZL.DataSync.Tests.Pipeline;

/// <summary>
/// SqlSugarHelpers 单元测试。
/// </summary>
public class SqlSugarHelpersTests
{
    // 本地测试用 Logger
    private sealed class TestLogger : IStructuredLogger
    {
        public List<string> Messages { get; } = new();
        public IStructuredLogger ForSource(string source) => this;
        public void Info(string message) => Messages.Add($"INFO: {message}");
        public void Warning(string message) => Messages.Add($"WARN: {message}");
        public void Error(string message) => Messages.Add($"ERROR: {message}");
        public void Debug(string message) => Messages.Add($"DEBUG: {message}");
        public void Flush() { }
        public void Dispose() { }
    }

    // ═══════════════════════════════════════════════════════════
    //  QuoteIdentifier
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void QuoteIdentifier_MySql_UsesBackticks()
    {
        var result = SqlSugarHelpers.QuoteIdentifier("my_table", SqlSugar.DbType.MySql);
        Assert.Equal("`my_table`", result);
    }

    [Fact]
    public void QuoteIdentifier_SQLServer_UsesDoubleQuotes()
    {
        var result = SqlSugarHelpers.QuoteIdentifier("my_table", SqlSugar.DbType.SqlServer);
        Assert.Equal("\"my_table\"", result);
    }

    [Fact]
    public void QuoteIdentifier_PostgreSQL_UsesDoubleQuotes()
    {
        var result = SqlSugarHelpers.QuoteIdentifier("my_table", SqlSugar.DbType.PostgreSQL);
        Assert.Equal("\"my_table\"", result);
    }

    [Fact]
    public void QuoteIdentifier_EscapesDoubleQuotes()
    {
        var result = SqlSugarHelpers.QuoteIdentifier("table\"name", SqlSugar.DbType.SqlServer);
        Assert.Equal("\"table\"\"name\"", result);
    }

    // ═══════════════════════════════════════════════════════════
    //  MapDbType
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void MapDbType_MySql_ReturnsSqlSugarMySql()
    {
        Assert.Equal(SqlSugar.DbType.MySql, SqlSugarHelpers.MapDbType(Config.TargetType.MySql));
    }

    [Fact]
    public void MapDbType_SQLServer_ReturnsSqlSugarSqlServer()
    {
        Assert.Equal(SqlSugar.DbType.SqlServer, SqlSugarHelpers.MapDbType(Config.TargetType.SqlServer));
    }

    [Fact]
    public void MapDbType_PostgreSql_ReturnsSqlSugarPostgreSQL()
    {
        Assert.Equal(SqlSugar.DbType.PostgreSQL, SqlSugarHelpers.MapDbType(Config.TargetType.PostgreSql));
    }

    [Fact]
    public void MapDbType_Oracle_ReturnsSqlSugarOracle()
    {
        Assert.Equal(SqlSugar.DbType.Oracle, SqlSugarHelpers.MapDbType(Config.TargetType.Oracle));
    }

    [Fact]
    public void MapDbType_Unknown_DefaultsToMySql()
    {
        // 使用反射创建一个不存在的枚举值来测试默认分支
        var unknownType = (Config.TargetType)999;
        Assert.Equal(SqlSugar.DbType.MySql, SqlSugarHelpers.MapDbType(unknownType));
    }

    // ═══════════════════════════════════════════════════════════
    //  TryGetProcessTime
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void TryGetProcessTime_WithValidTime_ReturnsTrue()
    {
        var row = new Dictionary<string, object?> { ["ProcessTime"] = DateTime.UtcNow };
        Assert.True(SqlSugarHelpers.TryGetProcessTime(row, out var pt));
        Assert.True(pt > DateTime.MinValue);
    }

    [Fact]
    public void TryGetProcessTime_MissingKey_ReturnsFalse()
    {
        var row = new Dictionary<string, object?> { ["Other"] = "value" };
        Assert.False(SqlSugarHelpers.TryGetProcessTime(row, out _));
    }

    [Fact]
    public void TryGetProcessTime_MinValue_ReturnsFalse()
    {
        var row = new Dictionary<string, object?> { ["ProcessTime"] = DateTime.MinValue };
        Assert.False(SqlSugarHelpers.TryGetProcessTime(row, out _));
    }

    [Fact]
    public void TryGetProcessTime_NotDateTime_ReturnsFalse()
    {
        var row = new Dictionary<string, object?> { ["ProcessTime"] = "not a date" };
        Assert.False(SqlSugarHelpers.TryGetProcessTime(row, out _));
    }

    // ═══════════════════════════════════════════════════════════
    //  ConvertToDictionary
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ConvertToDictionary_Null_ReturnsNull()
    {
        Assert.Null(SqlSugarHelpers.ConvertToDictionary(null!));
    }

    [Fact]
    public void ConvertToDictionary_AlreadyDictionary_ReturnsCopy()
    {
        var original = new Dictionary<string, object?> { ["key"] = "value" };
        var result = SqlSugarHelpers.ConvertToDictionary(original as dynamic);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Count);
        Assert.Equal("value", result["key"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  FilterValidRows
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FilterValidRows_NullInput_ReturnsEmptyList()
    {
        var result = SqlSugarHelpers.FilterValidRows(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void FilterValidRows_EmptyInput_ReturnsEmptyList()
    {
        var result = SqlSugarHelpers.FilterValidRows(new List<Dictionary<string, object?>>());
        Assert.Empty(result);
    }

    [Fact]
    public void FilterValidRows_FiltersNullRows()
    {
        var rows = new List<object?>
        {
            new Dictionary<string, object?> { ["key"] = "val" },
            null!,
            new Dictionary<string, object?> { ["key2"] = "val2" }
        };
        var result = SqlSugarHelpers.FilterValidRows(rows as dynamic);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterValidRows_FiltersEmptyDictionaries()
    {
        var rows = new List<object?>
        {
            new Dictionary<string, object?>(),
            new Dictionary<string, object?> { ["key"] = "val" }
        };
        var result = SqlSugarHelpers.FilterValidRows(rows as dynamic);
        Assert.Single(result);
    }

    // ═══════════════════════════════════════════════════════════
    //  ExtractValidRows
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ExtractValidRows_ReturnsSubset()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["a"] = 1 },
            new() { ["b"] = 2 },
            new() { ["c"] = 3 }
        };
        var result = SqlSugarHelpers.ExtractValidRows(rows, 0, 2);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ExtractValidRows_SkipsNullRows()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["a"] = 1 },
            null!,
            new() { ["c"] = 3 }
        };
        var result = SqlSugarHelpers.ExtractValidRows(rows, 0, 3);
        Assert.Single(result);
    }

    [Fact]
    public void ExtractValidRows_SkipsEmptyRows()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["a"] = 1 },
            new(),
            new() { ["c"] = 3 }
        };
        var result = SqlSugarHelpers.ExtractValidRows(rows, 0, 3);
        Assert.Equal(2, result.Count);
    }

    // ═══════════════════════════════════════════════════════════
    //  CollectColumnList
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CollectColumnList_ExcludesUnderscoreColumns()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "test", ["_Synced"] = 0, ["_SyncTime"] = DateTime.UtcNow }
        };
        var result = SqlSugarHelpers.CollectColumnList(rows);
        Assert.Single(result);
        Assert.Equal("Name", result[0]);
    }

    [Fact]
    public void CollectColumnList_DeduplicatesColumns()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "test", ["Value"] = 1 },
            new() { ["Name"] = "test2", ["Value"] = 2 }
        };
        var result = SqlSugarHelpers.CollectColumnList(rows);
        Assert.Equal(2, result.Count);
        Assert.Contains("Name", result);
        Assert.Contains("Value", result);
    }

    // ═══════════════════════════════════════════════════════════
    //  BuildBatchInsertSql
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildBatchInsertSql_GeneratesCorrectSql()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice", ["Age"] = 30 },
            new() { ["Name"] = "Bob", ["Age"] = 25 }
        };
        var (sql, parameters) = SqlSugarHelpers.BuildBatchInsertSql("users", SqlSugar.DbType.MySql, rows);

        Assert.Contains("INSERT INTO `users`", sql);
        Assert.Contains("`Name`, `Age`", sql);
        Assert.Contains("VALUES", sql);
        Assert.Equal(4, parameters.Count); // 2 rows × 2 columns
    }

    [Fact]
    public void BuildBatchInsertSql_HandlesNullValues()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice", ["Age"] = null }
        };
        var (sql, parameters) = SqlSugarHelpers.BuildBatchInsertSql("users", SqlSugar.DbType.MySql, rows);

        Assert.Contains("NULL", sql);
        Assert.Single(parameters);
    }

    [Fact]
    public void BuildBatchInsertSql_SQLServer_UsesDoubleQuotes()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice" }
        };
        var (sql, _) = SqlSugarHelpers.BuildBatchInsertSql("users", SqlSugar.DbType.SqlServer, rows);

        Assert.Contains("\"users\"", sql);
    }

    // ═══════════════════════════════════════════════════════════
    //  BuildSingleInsertSql
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSingleInsertSql_GeneratesCorrectSql()
    {
        var row = new Dictionary<string, object?> { ["Name"] = "Alice", ["Age"] = 30 };
        var (sql, parameters) = SqlSugarHelpers.BuildSingleInsertSql("users", SqlSugar.DbType.MySql, row);

        Assert.Contains("INSERT INTO `users`", sql);
        Assert.Contains("`Name`, `Age`", sql);
        Assert.Equal(2, parameters.Count);
    }

    [Fact]
    public void BuildSingleInsertSql_ExcludesUnderscoreColumns()
    {
        var row = new Dictionary<string, object?>
        {
            ["Name"] = "Alice",
            ["_Synced"] = 0
        };
        var (sql, parameters) = SqlSugarHelpers.BuildSingleInsertSql("users", SqlSugar.DbType.MySql, row);

        Assert.DoesNotContain("_Synced", sql);
        Assert.Single(parameters);
    }

    [Fact]
    public void BuildSingleInsertSql_EmptyRow_ReturnsEmpty()
    {
        var row = new Dictionary<string, object?>();
        var (sql, parameters) = SqlSugarHelpers.BuildSingleInsertSql("users", SqlSugar.DbType.MySql, row);

        Assert.Empty(sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void BuildSingleInsertSql_HandlesDBNull()
    {
        var row = new Dictionary<string, object?> { ["Name"] = DBNull.Value };
        var (sql, parameters) = SqlSugarHelpers.BuildSingleInsertSql("users", SqlSugar.DbType.MySql, row);

        Assert.Contains("DBNull.Value", sql);
    }

    // ═══════════════════════════════════════════════════════════
    //  AdaptSqlType
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AdaptSqlType_Int_ReturnsBigint()
    {
        Assert.Equal("BIGINT", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, typeof(int), 42));
    }

    [Fact]
    public void AdaptSqlType_Long_ReturnsBigint()
    {
        Assert.Equal("BIGINT", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, typeof(long), 42L));
    }

    [Fact]
    public void AdaptSqlType_Double_ReturnsDouble()
    {
        Assert.Equal("DOUBLE", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, typeof(double), 3.14));
    }

    [Fact]
    public void AdaptSqlType_Bool_MySql_ReturnsTinyint()
    {
        Assert.Equal("TINYINT(1)", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, typeof(bool), true));
    }

    [Fact]
    public void AdaptSqlType_Bool_SQLServer_ReturnsBit()
    {
        Assert.Equal("BIT", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.SqlServer, typeof(bool), true));
    }

    [Fact]
    public void AdaptSqlType_DateTime_ReturnsDatetime()
    {
        Assert.Equal("DATETIME", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, typeof(DateTime), DateTime.UtcNow));
    }

    [Fact]
    public void AdaptSqlType_ByteArr_ReturnsBlob()
    {
        Assert.Equal("BLOB", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, typeof(byte[]), new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void AdaptSqlType_NullType_ReturnsText()
    {
        Assert.Equal("TEXT", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, null!, null));
    }

    [Fact]
    public void AdaptSqlType_DbNullValue_ReturnsText()
    {
        Assert.Equal("TEXT", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, typeof(string), DBNull.Value));
    }

    [Fact]
    public void AdaptSqlType_String_ReturnsText()
    {
        Assert.Equal("TEXT", SqlSugarHelpers.AdaptSqlType(SqlSugar.DbType.MySql, typeof(string), "hello"));
    }

    // ═══════════════════════════════════════════════════════════
    //  BuildMarkSyncedSql (IDs)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildMarkSyncedSql_WithIds_GeneratesCorrectSql()
    {
        var ids = new List<object?> { 1, 2, 3 };
        var (sql, parameters) = SqlSugarHelpers.BuildMarkSyncedSql("test_table", SqlSugar.DbType.MySql, ids, null);

        Assert.Contains("UPDATE `test_table` SET _Synced = 1", sql);
        Assert.Contains("WHERE Id IN", sql);
        Assert.Equal(4, parameters.Count); // 3 IDs + 1 @now
    }

    // ═══════════════════════════════════════════════════════════
    //  BuildMarkSyncedSql (ProcessTime fallback)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildMarkSyncedSql_WithProcessTimes_GeneratesCorrectSql()
    {
        var times = new List<DateTime> { DateTime.UtcNow, DateTime.UtcNow.AddHours(-1) };
        var (sql, parameters) = SqlSugarHelpers.BuildMarkSyncedSql("test_table", SqlSugar.DbType.MySql, null, times);

        Assert.Contains("UPDATE `test_table` SET _Synced = 1", sql);
        Assert.Contains("WHERE ProcessTime IN", sql);
        Assert.Equal(3, parameters.Count); // 2 times + 1 @now
    }

    // ═══════════════════════════════════════════════════════════
    //  BuildMarkSyncedSql (empty)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildMarkSyncedSql_EmptyInputs_ReturnsEmpty()
    {
        var (sql, parameters) = SqlSugarHelpers.BuildMarkSyncedSql("test_table", SqlSugar.DbType.MySql, new List<object?>(), new List<DateTime>());

        Assert.Empty(sql);
        Assert.Empty(parameters);
    }

    // ═══════════════════════════════════════════════════════════
    //  BuildCreateTableSql
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildCreateTableSql_ExcludesUnderscoreColumns()
    {
        var sample = new Dictionary<string, object?>
        {
            ["Name"] = "test",
            ["_Synced"] = 0,
            ["_SyncTime"] = DateTime.UtcNow
        };
        var logger = new TestLogger();
        var sql = SqlSugarHelpers.BuildCreateTableSql("test_table", SqlSugar.DbType.MySql, sample, "test-target", logger);

        Assert.DoesNotContain("_Synced", sql);
        Assert.DoesNotContain("_SyncTime", sql);
        Assert.Contains("`Name`", sql);
    }

    [Fact]
    public void BuildCreateTableSql_MySql_IncludesEngineClause()
    {
        var sample = new Dictionary<string, object?> { ["Name"] = "test" };
        var logger = new TestLogger();
        var sql = SqlSugarHelpers.BuildCreateTableSql("test_table", SqlSugar.DbType.MySql, sample, "test-target", logger);

        Assert.Contains("ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", sql);
    }

    [Fact]
    public void BuildCreateTableSql_SQLServer_ExcludesEngineClause()
    {
        var sample = new Dictionary<string, object?> { ["Name"] = "test" };
        var logger = new TestLogger();
        var sql = SqlSugarHelpers.BuildCreateTableSql("test_table", SqlSugar.DbType.SqlServer, sample, "test-target", logger);

        Assert.DoesNotContain("ENGINE", sql);
    }

    [Fact]
    public void BuildCreateTableSql_AllColumnsInferred()
    {
        var sample = new Dictionary<string, object?>
        {
            ["Name"] = "test",
            ["Age"] = 30,
            ["Score"] = 95.5,
            ["Active"] = true,
            ["Created"] = DateTime.UtcNow
        };
        var logger = new TestLogger();
        var sql = SqlSugarHelpers.BuildCreateTableSql("test_table", SqlSugar.DbType.MySql, sample, "test-target", logger);

        Assert.Contains("`Name` TEXT", sql);
        Assert.Contains("`Age` BIGINT", sql);
        Assert.Contains("`Score` DOUBLE", sql);
        Assert.Contains("`Active` TINYINT(1)", sql);
        Assert.Contains("`Created` DATETIME", sql);
    }

    [Fact]
    public void BuildCreateTableSql_NoValidColumns_ReturnsEmpty()
    {
        var sample = new Dictionary<string, object?>
        {
            ["_Synced"] = 0,
            ["_SyncTime"] = DateTime.UtcNow
        };
        var logger = new TestLogger();
        var sql = SqlSugarHelpers.BuildCreateTableSql("test_table", SqlSugar.DbType.MySql, sample, "test-target", logger);

        Assert.Empty(sql);
        Assert.Single(logger.Messages);
        Assert.Contains("没有有效列", logger.Messages[0]);
    }

    // ═══════════════════════════════════════════════════════════
    //  Constants
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SyncColumn_ConstantValue()
    {
        Assert.Equal("_Synced", SqlSugarHelpers.SyncColumn);
    }

    [Fact]
    public void SyncTimeColumn_ConstantValue()
    {
        Assert.Equal("_SyncTime", SqlSugarHelpers.SyncTimeColumn);
    }

    [Fact]
    public void ProcessTimeColumn_ConstantValue()
    {
        Assert.Equal("ProcessTime", SqlSugarHelpers.ProcessTimeColumn);
    }

    [Fact]
    public void IdColumn_ConstantValue()
    {
        Assert.Equal("Id", SqlSugarHelpers.IdColumn);
    }
}
