using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZL.Biz.Execute.Sql;

namespace ZL.Biz.Execute.Tests
{
    /// <summary>
    /// SafeSqlBuilder 测试 - P2.2 验证
    /// </summary>
    public class SafeSqlBuilderTests
    {
        [Fact]
        public void Test_BuildSelect_Basic()
        {
            var builder = new SafeSqlBuilder();
            var result = builder.BuildSelect("users");

            Assert.True(result.Success);
            Assert.Contains("SELECT * FROM [users]", result.Sql);
            Assert.Contains("LIMIT 1000", result.Sql);
            Assert.Equal(SqlExecutionMode.Safe, result.ExecutionMode);
        }

        [Fact]
        public void Test_BuildSelect_WithColumns()
        {
            var builder = new SafeSqlBuilder();
            var result = builder.BuildSelect("users", new[] { "id", "name", "email" });

            Assert.True(result.Success);
            Assert.Contains("[id]", result.Sql);
            Assert.Contains("[name]", result.Sql);
            Assert.Contains("[email]", result.Sql);
        }

        [Fact]
        public void Test_BuildSelect_WithWhereConditions()
        {
            var builder = new SafeSqlBuilder();
            var where = new Dictionary<string, object> { { "status", 1 }, { "deleted", false } };
            var result = builder.BuildSelect("users", whereConditions: where);

            Assert.True(result.Success);
            Assert.Contains("WHERE", result.Sql);
            Assert.Equal(2, result.Parameters.Count);
            Assert.Contains(1, result.Parameters.Values);
            Assert.Contains(false, result.Parameters.Values);
        }

        [Fact]
        public void Test_BuildSelect_InvalidTableName()
        {
            var builder = new SafeSqlBuilder();
            var result = builder.BuildSelect("users; DROP TABLE users;--");

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public void Test_BuildSelect_TableNotInWhitelist()
        {
            var options = new SqlBuildOptions
            {
                AllowedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "allowed_table" }
            };
            var builder = new SafeSqlBuilder(options);
            var result = builder.BuildSelect("forbidden_table");

            Assert.False(result.Success);
            Assert.Contains("不在白名单中", result.ErrorMessage);
        }

        [Fact]
        public void Test_BuildInsert_NotAllowed()
        {
            var builder = new SafeSqlBuilder();
            var data = new Dictionary<string, object> { { "name", "test" } };
            var result = builder.BuildInsert("users", data);

            Assert.False(result.Success);
            Assert.Contains("未启用", result.ErrorMessage);
        }

        [Fact]
        public void Test_BuildInsert_Allowed()
        {
            var options = new SqlBuildOptions { AllowInsert = true };
            var builder = new SafeSqlBuilder(options);
            var data = new Dictionary<string, object> { { "name", "test" }, { "age", 25 } };
            var result = builder.BuildInsert("users", data);

            Assert.True(result.Success);
            Assert.Contains("INSERT INTO [users]", result.Sql);
            Assert.Equal(2, result.Parameters.Count);
        }

        [Fact]
        public void Test_BuildUpdate_NotAllowed()
        {
            var builder = new SafeSqlBuilder();
            var data = new Dictionary<string, object> { { "name", "updated" } };
            var where = new Dictionary<string, object> { { "id", 1 } };
            var result = builder.BuildUpdate("users", data, where);

            Assert.False(result.Success);
        }

        [Fact]
        public void Test_BuildUpdate_Allowed()
        {
            var options = new SqlBuildOptions { AllowUpdate = true };
            var builder = new SafeSqlBuilder(options);
            var data = new Dictionary<string, object> { { "name", "updated" } };
            var where = new Dictionary<string, object> { { "id", 1 } };
            var result = builder.BuildUpdate("users", data, where);

            Assert.True(result.Success);
            Assert.Contains("UPDATE [users] SET", result.Sql);
            Assert.Contains("WHERE", result.Sql);
            Assert.Equal(2, result.Parameters.Count);
        }

        [Fact]
        public void Test_BuildDelete_NotAllowed()
        {
            var builder = new SafeSqlBuilder();
            var where = new Dictionary<string, object> { { "id", 1 } };
            var result = builder.BuildDelete("users", where);

            Assert.False(result.Success);
        }

        [Fact]
        public void Test_BuildDelete_Allowed()
        {
            var options = new SqlBuildOptions { AllowDelete = true };
            var builder = new SafeSqlBuilder(options);
            var where = new Dictionary<string, object> { { "id", 1 } };
            var result = builder.BuildDelete("users", where);

            Assert.True(result.Success);
            Assert.Contains("DELETE FROM [users]", result.Sql);
            Assert.Contains("WHERE", result.Sql);
        }

        [Fact]
        public void Test_BuildRaw_SafeModeBlocked()
        {
            var builder = new SafeSqlBuilder();
            var result = builder.BuildRaw("SELECT * FROM users");

            Assert.False(result.Success);
            Assert.Contains("仅在兼容模式下允许", result.ErrorMessage);
        }

        [Fact]
        public void Test_BuildRaw_CompatibilityMode()
        {
            var options = new SqlBuildOptions { ExecutionMode = SqlExecutionMode.Compatibility };
            var builder = new SafeSqlBuilder(options);
            var result = builder.BuildRaw("SELECT * FROM users WHERE id = 1");

            Assert.True(result.Success);
            Assert.Equal(SqlExecutionMode.Compatibility, result.ExecutionMode);
        }

        [Fact]
        public void Test_MaxRowsLimit()
        {
            var options = new SqlBuildOptions { MaxRows = 100 };
            var builder = new SafeSqlBuilder(options);
            var result = builder.BuildSelect("users");

            Assert.True(result.Success);
            Assert.Contains("LIMIT 100", result.Sql);
        }

        [Fact]
        public void Test_OrderBy()
        {
            var builder = new SafeSqlBuilder();
            var result = builder.BuildSelect("users", orderBy: "created_at", ascending: false);

            Assert.True(result.Success);
            Assert.Contains("ORDER BY [created_at] DESC", result.Sql);
        }

        [Fact]
        public void Test_InvalidColumnNameFiltered()
        {
            var builder = new SafeSqlBuilder();
            var columns = new[] { "valid_col", "invalid;column", "another_valid" };
            var result = builder.BuildSelect("users", columns);

            Assert.True(result.Success);
            Assert.Contains("[valid_col]", result.Sql);
            Assert.Contains("[another_valid]", result.Sql);
            Assert.DoesNotContain("invalid;column", result.Sql);
        }

        [Theory]
        [InlineData("users")]
        [InlineData("iot_tags")]
        [InlineData("edge_config")]
        [InlineData("_private_table")]
        [InlineData("Table123")]
        public void Test_ValidTableNames(string tableName)
        {
            var result = SafeSqlBuilder.IsValidIdentifier(tableName);
            Assert.True(result);
        }

        [Theory]
        [InlineData("users; DROP TABLE users;--")]
        [InlineData("users' OR '1'='1")]
        [InlineData("users/*comment*/")]
        [InlineData("users--comment")]
        [InlineData("123table")]
        [InlineData("")]
        public void Test_InvalidTableNames_ReturnFalse(string tableName)
        {
            var result = SafeSqlBuilder.IsValidIdentifier(tableName);
            Assert.False(result);
        }

        [Fact]
        public void Test_NullTableName_ReturnFalse()
        {
            var result = SafeSqlBuilder.IsValidIdentifier(null);
            Assert.False(result);
        }

        [Fact]
        public void Test_SqlInjection_AttemptBlocked()
        {
            var builder = new SafeSqlBuilder();
            var maliciousInput = "users; DROP TABLE users;--";
            var result = builder.BuildSelect(maliciousInput);

            Assert.False(result.Success);
            Assert.Contains("无效", result.ErrorMessage);
        }

        [Fact]
        public void Test_ParameterInjection_Blocked()
        {
            var builder = new SafeSqlBuilder();
            var where = new Dictionary<string, object>
            {
                { "name", "admin' OR '1'='1" },  // 注入尝试在值中
                { "status", 1 }
            };
            var result = builder.BuildSelect("users", whereConditions: where);

            Assert.True(result.Success);
            // 参数应该被正确保留，因为它们是值而非列名
            Assert.Equal(2, result.Parameters.Count);
            Assert.Contains("@p0", result.Sql);
            Assert.Equal("admin' OR '1'='1", result.Parameters["@p0"]);
        }

        [Fact]
        public void Test_WhereClauseInjection_Blocked()
        {
            var builder = new SafeSqlBuilder();
            // 尝试通过列名注入
            var where = new Dictionary<string, object>
            {
                { "id=1; DROP TABLE users;--", 1 }
            };
            var result = builder.BuildSelect("users", whereConditions: where);

            Assert.True(result.Success);
            // 非法列名应该被过滤掉
            Assert.DoesNotContain("DROP TABLE", result.Sql);
        }
    }
}
