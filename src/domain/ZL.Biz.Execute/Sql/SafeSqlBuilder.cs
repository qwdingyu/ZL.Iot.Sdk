using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZL.Biz.Execute.Sql
{
    /// <summary>
    /// SQL 执行模式
    /// </summary>
    public enum SqlExecutionMode
    {
        /// <summary>
        /// 安全模式（默认）- 仅允许参数化查询
        /// </summary>
        Safe,
        /// <summary>
        /// 兼容模式 - 允许动态 SQL（需要显式启用）
        /// </summary>
        Compatibility
    }

    /// <summary>
    /// SQL 构建选项
    /// </summary>
    public class SqlBuildOptions
    {
        public SqlExecutionMode ExecutionMode { get; set; } = SqlExecutionMode.Safe;
        public HashSet<string> AllowedTables { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AllowedColumns { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int MaxRows { get; set; } = 1000;
        public bool AllowDelete { get; set; } = false;
        public bool AllowUpdate { get; set; } = false;
        public bool AllowInsert { get; set; } = false;
    }

    /// <summary>
    /// SQL 构建结果
    /// </summary>
    public class SqlBuildResult
    {
        public string Sql { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public SqlExecutionMode ExecutionMode { get; set; }
    }

    /// <summary>
    /// 安全 SQL 构建器 - P2.2 实现
    /// 默认使用参数化查询，动态 SQL 作为兼容模式
    /// </summary>
    public class SafeSqlBuilder
    {
        private readonly SqlBuildOptions _options;
        private static readonly Regex ValidIdentifierPattern = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        public SafeSqlBuilder() : this(new SqlBuildOptions()) { }
        public SafeSqlBuilder(SqlBuildOptions options) { _options = options ?? new SqlBuildOptions(); }

        /// <summary>
        /// 验证标识符是否合法（字母、数字、下划线，以字母或下划线开头）
        /// </summary>
        public static bool IsValidIdentifier(string identifier) =>
            !string.IsNullOrEmpty(identifier) && ValidIdentifierPattern.IsMatch(identifier);

        private SqlBuildResult Error(string msg) => new SqlBuildResult { Success = false, ErrorMessage = msg };
        private SqlBuildResult Ok(string sql, Dictionary<string, object> parameters, SqlExecutionMode mode) =>
            new SqlBuildResult { Success = true, Sql = sql, Parameters = parameters, ExecutionMode = mode };

        /// <summary>
        /// 构建 SELECT 查询（安全参数化）
        /// </summary>
        public SqlBuildResult BuildSelect(string tableName, IEnumerable<string> columns = null,
            Dictionary<string, object> whereConditions = null, string orderBy = null, bool ascending = true)
        {
            if (!IsValidIdentifier(tableName)) return Error("表名无效");
            if (_options.AllowedTables.Count > 0 && !_options.AllowedTables.Contains(tableName))
                return Error($"表 '{tableName}' 不在白名单中");

            var sql = new StringBuilder("SELECT ");
            var parameters = new Dictionary<string, object>();

            if (columns == null || !columns.Any()) sql.Append("*");
            else
            {
                var validCols = columns.Where(IsValidIdentifier).ToList();
                if (validCols.Count == 0) return Error("没有有效列名");
                sql.Append(string.Join(", ", validCols.Select(c => $"[{c}]")));
            }

            sql.Append($" FROM [{tableName}]");

            if (whereConditions != null && whereConditions.Count > 0)
            {
                var clauses = new List<string>();
                int i = 0;
                foreach (var kvp in whereConditions.Where(x => IsValidIdentifier(x.Key)))
                {
                    var p = $"@p{i++}";
                    clauses.Add($"[{kvp.Key}] = {p}");
                    parameters[p] = kvp.Value;
                }
                if (clauses.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", clauses));
            }

            if (!string.IsNullOrEmpty(orderBy) && IsValidIdentifier(orderBy))
                sql.Append($" ORDER BY [{orderBy}] {(ascending ? "ASC" : "DESC")}");
            sql.Append($" LIMIT {_options.MaxRows}");

            return Ok(sql.ToString(), parameters, SqlExecutionMode.Safe);
        }

        /// <summary>
        /// 构建 INSERT 语句
        /// </summary>
        public SqlBuildResult BuildInsert(string tableName, Dictionary<string, object> data)
        {
            if (!_options.AllowInsert) return Error("INSERT 操作未启用");
            if (!IsValidIdentifier(tableName)) return Error("表名无效");
            if (data == null || data.Count == 0) return Error("数据为空");

            var validCols = data.Keys.Where(IsValidIdentifier).ToList();
            if (validCols.Count == 0) return Error("没有有效列名");

            var parameters = new Dictionary<string, object>();
            var paramNames = new List<string>();
            int i = 0;
            foreach (var col in validCols) { var p = $"@p{i++}"; paramNames.Add(p); parameters[p] = data[col]; }

            var sql = $"INSERT INTO [{tableName}] ({string.Join(", ", validCols.Select(c => $"[{c}]"))}) VALUES ({string.Join(", ", paramNames)})";
            return Ok(sql, parameters, SqlExecutionMode.Safe);
        }

        /// <summary>
        /// 构建 UPDATE 语句
        /// </summary>
        public SqlBuildResult BuildUpdate(string tableName, Dictionary<string, object> data, Dictionary<string, object> whereConditions)
        {
            if (!_options.AllowUpdate) return Error("UPDATE 操作未启用");
            if (!IsValidIdentifier(tableName)) return Error("表名无效");
            if (data == null || data.Count == 0) return Error("数据为空");

            var validCols = data.Keys.Where(IsValidIdentifier).ToList();
            if (validCols.Count == 0) return Error("没有有效列名");

            var parameters = new Dictionary<string, object>();
            var setClauses = new List<string>();
            int i = 0;
            foreach (var col in validCols) { var p = $"@p{i++}"; setClauses.Add($"[{col}] = {p}"); parameters[p] = data[col]; }

            var sql = new StringBuilder($"UPDATE [{tableName}] SET {string.Join(", ", setClauses)}");

            if (whereConditions != null && whereConditions.Count > 0)
            {
                var clauses = new List<string>();
                foreach (var kvp in whereConditions.Where(x => IsValidIdentifier(x.Key)))
                { var p = $"@p{i++}"; clauses.Add($"[{kvp.Key}] = {p}"); parameters[p] = kvp.Value; }
                if (clauses.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", clauses));
            }

            return Ok(sql.ToString(), parameters, SqlExecutionMode.Safe);
        }

        /// <summary>
        /// 构建 DELETE 语句
        /// </summary>
        public SqlBuildResult BuildDelete(string tableName, Dictionary<string, object> whereConditions)
        {
            if (!_options.AllowDelete) return Error("DELETE 操作未启用");
            if (!IsValidIdentifier(tableName)) return Error("表名无效");

            var sql = new StringBuilder($"DELETE FROM [{tableName}]");
            var parameters = new Dictionary<string, object>();

            if (whereConditions != null && whereConditions.Count > 0)
            {
                var clauses = new List<string>();
                int i = 0;
                foreach (var kvp in whereConditions.Where(x => IsValidIdentifier(x.Key)))
                { var p = $"@p{i++}"; clauses.Add($"[{kvp.Key}] = {p}"); parameters[p] = kvp.Value; }
                if (clauses.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", clauses));
            }

            return Ok(sql.ToString(), parameters, SqlExecutionMode.Safe);
        }

        /// <summary>
        /// 兼容模式：执行原始 SQL（仅当 ExecutionMode = Compatibility 时允许）
        /// </summary>
        public SqlBuildResult BuildRaw(string sql)
        {
            if (_options.ExecutionMode != SqlExecutionMode.Compatibility)
                return Error("原始 SQL 仅在兼容模式下允许，当前为安全模式");
            return Ok(sql, new Dictionary<string, object>(), SqlExecutionMode.Compatibility);
        }
    }
}
