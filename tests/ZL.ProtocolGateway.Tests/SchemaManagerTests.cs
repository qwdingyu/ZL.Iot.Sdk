// ============================================================
// 文件：SchemaManagerTests.cs
// 描述：SchemaManager 静态方法测试
// 修改日期：2026-06-05
// ============================================================

using System;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class SchemaManagerTests
    {
        [Fact]
        public void ValidateTableName_ValidName_DoesNotThrow()
        {
            // Valid table names should not throw
            SchemaManager.ValidateTableName("gateway_messages");
            SchemaManager.ValidateTableName("TestTable");
            SchemaManager.ValidateTableName("a");
            SchemaManager.ValidateTableName("_private");
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("1invalid")]
        [InlineData("table;DROP")]
        [InlineData("table--comment")]
        public void ValidateTableName_InvalidName_Throws(string? invalidName)
        {
            Assert.Throws<ArgumentException>(() => SchemaManager.ValidateTableName(invalidName!));
        }
    }
}
