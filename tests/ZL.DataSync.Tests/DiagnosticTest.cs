using System.Data;
using SqlSugar;
using Xunit;
using Xunit.Abstractions;

namespace ZL.DataSync.Tests;

public class DiagnosticTest
{
    private readonly ITestOutputHelper _output;
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), "diag_test_shared.db");

    public DiagnosticTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Diagnostic only - run individually. Uses shared SQLite file.")]
    public void Diag_SqlQuery_Dictionary_And_MySQL_Insert()
    {
        // Setup SQLite
        using var localDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = $"Data Source={_sqlitePath}",
            IsAutoCloseConnection = true
        });

        localDb.Ado.ExecuteCommand(@"CREATE TABLE test_table (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StationCode TEXT,
            BarCode TEXT,
            ProcessTime DATETIME,
            _Synced INTEGER DEFAULT 0,
            _SyncTime DATETIME
        )");

        for (int i = 1; i <= 3; i++)
        {
            localDb.Ado.ExecuteCommand(
                "INSERT INTO test_table (StationCode, BarCode, ProcessTime, _Synced, _SyncTime) VALUES (@sc, @bc, @pt, 0, NULL)",
                new SugarParameter("@sc", $"STATION_{i}"),
                new SugarParameter("@bc", $"BC_{i}"),
                new SugarParameter("@pt", DateTime.UtcNow.AddMinutes(-i))
            );
        }

        // Test 1: SqlQuery<Dictionary>
        var rows = localDb.Ado.SqlQuery<Dictionary<string, object?>>(
            "SELECT * FROM test_table WHERE _Synced = 0 ORDER BY ProcessTime LIMIT 100");
        _output.WriteLine($"Rows count (Dictionary): {rows?.Count ?? 0}");
        if (rows?.Count > 0)
        {
            foreach (var row in rows)
            {
                _output.WriteLine($"  Row keys: [{string.Join(", ", row.Keys)}] count={row.Count}");
            }
        }

        // Test 2: SqlQuery<dynamic>
        var dynRows = localDb.Ado.SqlQuery<dynamic>(
            "SELECT * FROM test_table WHERE _Synced = 0 ORDER BY ProcessTime LIMIT 100");
        _output.WriteLine($"Rows count (dynamic): {dynRows?.Count ?? 0}");
        if (dynRows?.Count > 0)
        {
            foreach (var row in dynRows)
            {
                var obj = row as object;
                var dictCount = 0;
                if (obj is IDictionary<string, object?> d)
                {
                    dictCount = d.Count;
                    _output.WriteLine($"  Dynamic row keys: [{string.Join(", ", d.Keys)}]");
                }
                _output.WriteLine($"  Dynamic row dict count: {dictCount}");
            }
        }

        // Test 3: Convert to Dictionary and insert to MySQL
        var convertedRows = dynRows?.Select(row =>
        {
            var obj = row as object;
            return obj is IDictionary<string, object?> d
                ? new Dictionary<string, object?>(d)
                : null;
        }).Where(d => d != null && d.Count > 0).ToList()
        ?? new List<Dictionary<string, object?>>();

        _output.WriteLine($"Converted rows: {convertedRows.Count}");
        foreach (var row in convertedRows)
        {
            _output.WriteLine($"  Row keys: [{string.Join(", ", row.Keys)}]");
        }

        // Test 4: Create DB and insert to MySQL
        using var setupDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.MySql,
            ConnectionString = "server=127.0.0.1;uid=root;password=mes;charset=utf8mb4;",
            IsAutoCloseConnection = true
        });
        setupDb.Ado.ExecuteCommand("CREATE DATABASE IF NOT EXISTS `zldatasync_test` DEFAULT CHARACTER SET utf8mb4");

        using var mySqlDb = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.MySql,
            ConnectionString = "server=127.0.0.1;database=zldatasync_test;uid=root;password=mes;charset=utf8mb4;Allow User Variables=True;",
            IsAutoCloseConnection = true
        });

        mySqlDb.Ado.ExecuteCommand("DROP TABLE IF EXISTS `synced_data`");
        mySqlDb.Ado.ExecuteCommand(@"CREATE TABLE `synced_data` (
            `Id` BIGINT PRIMARY KEY AUTO_INCREMENT,
            `StationCode` TEXT,
            `BarCode` TEXT,
            `ProcessTime` DATETIME,
            `_Synced` INT DEFAULT 0,
            `_SyncTime` DATETIME
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Manual INSERT via Ado
        var colList = new List<string> { "StationCode", "BarCode", "ProcessTime" };
        var paramCount = 0;
        var rowTemplates = new List<string>();
        foreach (var row in convertedRows)
        {
            var placeholders = new List<string>();
            foreach (var col in colList)
            {
                if (row.TryGetValue(col, out var value) && value != null)
                {
                    placeholders.Add($"@p{paramCount}");
                    paramCount++;
                }
                else
                {
                    placeholders.Add("NULL");
                }
            }
            rowTemplates.Add($"({string.Join(",", placeholders)})");
        }

        var colQuoted = string.Join(", ", colList.Select(c => $"`{c}`"));
        var sql = $"INSERT INTO `synced_data` ({colQuoted}) VALUES {string.Join(", ", rowTemplates)}";
        _output.WriteLine($"MySQL SQL: {sql}");

        var parameters = new List<SugarParameter>();
        foreach (var row in convertedRows)
        {
            foreach (var col in colList)
            {
                if (row.TryGetValue(col, out var value) && value != null)
                    parameters.Add(new SugarParameter($"p{parameters.Count}", value));
            }
        }
        _output.WriteLine($"MySQL Parameters: [{string.Join(", ", parameters.Select(p => $"{p.ParameterName}={p.Value}"))}]");

        var result = mySqlDb.Ado.ExecuteCommand(sql, parameters.ToArray());
        _output.WriteLine($"MySQL rows affected: {result}");

        var count = mySqlDb.Ado.SqlQuery<int>("SELECT COUNT(*) FROM `synced_data`")[0];
        _output.WriteLine($"MySQL count: {count}");

        Assert.Equal(3, count);
    }
}
