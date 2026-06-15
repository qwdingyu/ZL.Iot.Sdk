using Microsoft.Extensions.Logging.Abstractions;
using ZL.Biz.Execute.Biz;
using ZL.Iot.Interface;
using ZL.Iot.Runner.Runtime;

namespace ZL.Iot.Runner.Tests;

/// <summary>
/// SqlExecutorFieldMappingSink 标识符注入加固测试。
/// 表名/列名拼接进 DDL/DML，必须经过 SafeSqlBuilder.IsValidIdentifier 白名单校验。
/// </summary>
public sealed class SqlExecutorFieldMappingSinkTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"runner_fieldmap_{Guid.NewGuid():N}.db");

    [Fact]
    public void EnsureTable_ValidIdentifiers_Succeeds()
    {
        using var executor = CreateExecutor();
        var sink = new SqlExecutorFieldMappingSink(executor, NullLogger.Instance);

        var columns = new List<FieldMappingRule>
        {
            new() { Name = "axis_no", DataType = "Int32" },
            new() { Name = "value_real", DataType = "double" }
        };

        // 合法标识符应建表成功，且可正常写入。
        sink.EnsureTable("axis_data", columns);
        sink.InsertRows("axis_data", new List<Dictionary<string, object?>>
        {
            new() { ["axis_no"] = 1, ["value_real"] = 3.14, ["ProcessTime"] = DateTime.UtcNow }
        });

        var rows = executor
            .ExecuteQueryAsync("SELECT axis_no, value_real FROM axis_data")
            .GetAwaiter().GetResult();

        Assert.Single(rows);
        Assert.Equal(1, Convert.ToInt32(rows[0]["axis_no"]));
    }

    [Theory]
    [InlineData("axis_data; DROP TABLE users")]   // 语句注入
    [InlineData("axis-data")]                       // 连字符非法
    [InlineData("1axis")]                            // 数字开头非法
    [InlineData("axis data")]                        // 空格非法
    [InlineData("axis`data")]                        // 反引号非法
    [InlineData("")]                                  // 空表名非法
    public void EnsureTable_InvalidTableName_Throws(string tableName)
    {
        using var executor = CreateExecutor();
        var sink = new SqlExecutorFieldMappingSink(executor, NullLogger.Instance);

        var ex = Assert.Throws<ArgumentException>(() =>
            sink.EnsureTable(tableName, new List<FieldMappingRule>
            {
                new() { Name = "value_real", DataType = "double" }
            }));

        Assert.Equal("tableName", ex.ParamName);
    }

    [Theory]
    [InlineData("col; DROP TABLE x")]
    [InlineData("bad-col")]
    [InlineData("2col")]
    public void EnsureTable_InvalidColumnName_Throws(string columnName)
    {
        using var executor = CreateExecutor();
        var sink = new SqlExecutorFieldMappingSink(executor, NullLogger.Instance);

        var ex = Assert.Throws<ArgumentException>(() =>
            sink.EnsureTable("axis_data", new List<FieldMappingRule>
            {
                new() { Name = columnName, DataType = "double" }
            }));

        Assert.Equal("columns", ex.ParamName);
    }

    [Fact]
    public void InsertRows_InvalidTableName_Throws()
    {
        using var executor = CreateExecutor();
        var sink = new SqlExecutorFieldMappingSink(executor, NullLogger.Instance);

        var ex = Assert.Throws<ArgumentException>(() =>
            sink.InsertRows("evil; DROP TABLE x", new List<Dictionary<string, object?>>
            {
                new() { ["value_real"] = 1.0 }
            }));

        Assert.Equal("tableName", ex.ParamName);
    }

    [Fact]
    public void InsertRows_InvalidColumnName_Throws()
    {
        using var executor = CreateExecutor();
        var sink = new SqlExecutorFieldMappingSink(executor, NullLogger.Instance);
        sink.EnsureTable("axis_data", new List<FieldMappingRule>
        {
            new() { Name = "value_real", DataType = "double" }
        });

        var ex = Assert.Throws<ArgumentException>(() =>
            sink.InsertRows("axis_data", new List<Dictionary<string, object?>>
            {
                new() { ["value_real); DROP TABLE x --"] = 1.0 }
            }));

        Assert.Equal("rows", ex.ParamName);
    }

    private SqlSugarExecutor CreateExecutor()
    {
        return new SqlSugarExecutor(
            NullLogger<SqlSugarExecutor>.Instance,
            SqlSugar.DbType.Sqlite,
            $"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
        }
    }
}
