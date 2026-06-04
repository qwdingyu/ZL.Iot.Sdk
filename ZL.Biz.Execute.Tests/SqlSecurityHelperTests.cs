using Xunit;
using ZL.Biz.Execute;

namespace ZL.Biz.Execute.Tests
{
    /// <summary>
    /// SqlSecurityHelper 单元测试
    /// </summary>
    public class SqlSecurityHelperTests
    {
        #region ContainsDangerousSql 测试

        [Theory]
        [InlineData("DROP TABLE users", true)]
        [InlineData("DELETE FROM users", true)]
        [InlineData("' OR '1'='1", true)]   // OR 关键字
        [InlineData("'; DROP TABLE users;--", true)]
        [InlineData("UNION SELECT * FROM", true)]
        [InlineData("Normal text", false)]  // OR 在 "Normal" 中不是单词边界
        [InlineData("Hello World", false)]
        [InlineData("12345", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ContainsDangerousSql_ShouldDetectSqlKeywords(string? input, bool expected)
        {
            // Act
            var result = SqlSecurityHelper.ContainsDangerousSql(input!);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ContainsDangerousSql_ShouldBeCaseInsensitive()
        {
            // Arrange
            var dangerousInputs = new[] { "drop", "DROP", "Drop", "DeLeTe", "UNION", "union" };

            // Act & Assert
            foreach (var input in dangerousInputs)
            {
                Assert.True(SqlSecurityHelper.ContainsDangerousSql(input), $"Should detect: {input}");
            }
        }

        #endregion

        #region EscapeSqlString 测试

        [Theory]
        [InlineData("normal", "normal")]
        [InlineData("O'Brien", "O''Brien")]
        [InlineData("it's", "it''s")]
        [InlineData("test'value", "test''value")]
        [InlineData("", "")]
        [InlineData(null, null)]
        public void EscapeSqlString_ShouldEscapeSingleQuotes(string? input, string? expected)
        {
            // Act
            var result = SqlSecurityHelper.EscapeSqlString(input!);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void EscapeSqlString_WithMultipleQuotes_ShouldEscapeAll()
        {
            // Arrange
            var input = "test'''value";

            // Act
            var result = SqlSecurityHelper.EscapeSqlString(input);

            // Assert
            Assert.Equal("test''''''value", result);
        }

        #endregion

        #region RemoveSqlComments 测试

        [Theory]
        [InlineData("SELECT * FROM users", "SELECT * FROM users")]
        [InlineData("SELECT * FROM users--comment", "SELECT * FROM users ")]
        [InlineData("SELECT /* comment */ * FROM users", "SELECT   * FROM users")]
        [InlineData("SELECT * FROM users/*comment*/", "SELECT * FROM users ")]
        [InlineData("SELECT * FROM users--", "SELECT * FROM users ")]
        public void RemoveSqlComments_ShouldRemoveSqlComments(string input, string expected)
        {
            // Act
            var result = SqlSecurityHelper.RemoveSqlComments(input);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region IsExpressionSafe 测试

        [Theory]
        [InlineData("(1 + 2) > 0", true)]
        [InlineData("?x? > 100", true)]
        [InlineData("IIF(?x? > 0, true, false)", true)]
        [InlineData("Abs(?x?) > 100", true)]
        [InlineData("Sqrt(4) > 2", true)]
        [InlineData("DROP TABLE users", false)]
        [InlineData("Process.Start('cmd')", false)]
        [InlineData("System.IO.File", false)]
        public void IsExpressionSafe_ShouldValidateExpression(string expression, bool expected)
        {
            // Act
            var result = SqlSecurityHelper.IsExpressionSafe(expression);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1=1 OR System.Diagnostics.Process.Start", false)]  // System.* 访问
        [InlineData("' OR 1=1 --", false)]  // OR 关键字，ContainsDangerousSql 会拒绝
        [InlineData("?x? = '1'; DROP TABLE", false)]  // DROP TABLE 关键字
        public void IsExpressionSafe_ShouldDetectSystemAccess(string expression, bool expected)
        {
            // Act
            var result = SqlSecurityHelper.IsExpressionSafe(expression);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region ValidateAndEscape 测试

        [Fact]
        public void ValidateAndEscape_WithValidString_ShouldEscape()
        {
            // Arrange
            var input = "O'Brien";

            // Act
            var result = SqlSecurityHelper.ValidateAndEscape("test", input);

            // Assert
            Assert.Equal("O''Brien", result);
        }

        [Fact]
        public void ValidateAndEscape_WithDangerousContent_ShouldThrow()
        {
            // Arrange
            var input = "'; DROP TABLE users;--";

            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                SqlSecurityHelper.ValidateAndEscape("test", input));
        }

        [Fact]
        public void ValidateAndEscape_WithNumericValue_ShouldValidateAsNumber()
        {
            // Arrange
            var input = "123.45";

            // Act
            var result = SqlSecurityHelper.ValidateAndEscape("test", input, isNumeric: true);

            // Assert
            Assert.Equal("123.45", result);
        }

        [Fact]
        public void ValidateAndEscape_WithNonNumericValue_ShouldThrow()
        {
            // Arrange
            var input = "not-a-number";

            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                SqlSecurityHelper.ValidateAndEscape("test", input, isNumeric: true));
        }

        #endregion

        #region GetDangerousKeywords 测试

        [Fact]
        public void GetDangerousKeywords_ShouldReturnAllFoundKeywords()
        {
            // Arrange
            var input = "DROP and DELETE and UPDATE";

            // Act
            var result = SqlSecurityHelper.GetDangerousKeywords(input);

            // Assert
            Assert.Contains("DROP", result);
            Assert.Contains("DELETE", result);
            Assert.Contains("UPDATE", result);
        }

        [Fact]
        public void GetDangerousKeywords_WithNoDangerousContent_ShouldReturnEmptyList()
        {
            // Arrange
            var input = "normal text";

            // Act
            var result = SqlSecurityHelper.GetDangerousKeywords(input);

            // Assert
            Assert.Empty(result);
        }

        #endregion
    }
}
