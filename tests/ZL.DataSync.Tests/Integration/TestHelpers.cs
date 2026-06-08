using SqlSugar;

namespace ZL.DataSync.Tests.Integration;

/// <summary>
/// 测试辅助工具类。
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// 检查 MySQL 是否可用。
    /// </summary>
    public static bool IsMySqlAvailable()
    {
        try
        {
            using var conn = new SqlSugarClient(new ConnectionConfig
            {
                DbType = SqlSugar.DbType.MySql,
                ConnectionString = "server=127.0.0.1;uid=root;password=mes;charset=utf8mb4;",
                IsAutoCloseConnection = true
            });
            conn.Ado.ExecuteCommand("SELECT 1");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查 SQLite 是否可用。
    /// </summary>
    public static bool IsSqliteAvailable()
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"sqlite_test_{Guid.NewGuid()}.db");
            using var conn = new SqlSugarClient(new ConnectionConfig
            {
                DbType = SqlSugar.DbType.Sqlite,
                ConnectionString = $"Data Source={path}",
                IsAutoCloseConnection = true
            });
            conn.Ado.ExecuteCommand("SELECT 1");
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
