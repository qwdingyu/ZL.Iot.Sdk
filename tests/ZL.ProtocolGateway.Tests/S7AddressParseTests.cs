using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// S7 地址解析测试 - 使用公共 S7AddressParser 静态类
    /// 覆盖所有支持的地址格式：DB、M、I、Q 区域
    /// </summary>
    public class S7AddressParseTests
    {
        private static S7AddressParser.ParsedAddress Parse(string address)
        {
            return S7AddressParser.Parse(address);
        }

        #region DB 区域测试

        [Fact]
        public void ParseS7Address_DB1_DBW10_ParsesCorrectly()
        {
            var result = Parse("DB1.DBW10");

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area); // DB 区域
            Assert.Equal(10, result.ByteOffset);
            Assert.Equal(0, result.BitOffset);
            Assert.False(result.IsBit);
        }

        [Fact]
        public void ParseS7Address_DB1_DBB5_ParsesCorrectly()
        {
            var result = Parse("DB1.DBB5");

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
            Assert.Equal(5, result.ByteOffset);
            Assert.False(result.IsBit);
        }

        [Fact]
        public void ParseS7Address_DB1_DBX2_3_ParsesBitCorrectly()
        {
            var result = Parse("DB1.DBX2.3");

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
            Assert.Equal(2, result.ByteOffset);
            Assert.Equal(3, result.BitOffset);
            Assert.True(result.IsBit);
        }

        [Fact]
        public void ParseS7Address_DB1_DBX5_ParsesBitDefaultZero()
        {
            var result = Parse("DB1.DBX5");

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
            Assert.Equal(5, result.ByteOffset);
            Assert.Equal(0, result.BitOffset); // DBX 默认 bit=0
            Assert.True(result.IsBit);
        }

        [Fact]
        public void ParseS7Address_DB999_DBW0_ParsesLargeDb()
        {
            var result = Parse("DB999.DBW0");

            Assert.Equal(999, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
            Assert.Equal(0, result.ByteOffset);
        }

        [Fact]
        public void ParseS7Address_DB1_DBW0_Default()
        {
            var result = Parse("DB1.DBW0");

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
            Assert.Equal(0, result.ByteOffset);
        }

        #endregion

        #region M 区域（标记存储区）

        [Fact]
        public void ParseS7Address_M10_ParsesByte()
        {
            var result = Parse("M10");

            Assert.Equal(0, result.DbNumber);
            Assert.Equal((byte)0x83, result.Area); // M 区域
            Assert.Equal(10, result.ByteOffset);
            Assert.False(result.IsBit);
        }

        [Fact]
        public void ParseS7Address_M5_3_ParsesBit()
        {
            var result = Parse("M5.3");

            Assert.Equal(0, result.DbNumber);
            Assert.Equal((byte)0x83, result.Area);
            Assert.Equal(5, result.ByteOffset);
            Assert.Equal(3, result.BitOffset);
            Assert.True(result.IsBit);
        }

        [Fact]
        public void ParseS7Address_M0_ParsesZero()
        {
            var result = Parse("M0");

            Assert.Equal(0, result.DbNumber);
            Assert.Equal((byte)0x83, result.Area);
            Assert.Equal(0, result.ByteOffset);
            Assert.False(result.IsBit);
        }

        #endregion

        #region I 区域（输入）

        [Fact]
        public void ParseS7Address_I10_ParsesByte()
        {
            var result = Parse("I10");

            Assert.Equal(0, result.DbNumber);
            Assert.Equal((byte)0x81, result.Area); // I 区域
            Assert.Equal(10, result.ByteOffset);
            Assert.False(result.IsBit);
        }

        [Fact]
        public void ParseS7Address_I3_1_ParsesBit()
        {
            var result = Parse("I3.1");

            Assert.Equal(0, result.DbNumber);
            Assert.Equal((byte)0x81, result.Area);
            Assert.Equal(3, result.ByteOffset);
            Assert.Equal(1, result.BitOffset);
            Assert.True(result.IsBit);
        }

        #endregion

        #region Q 区域（输出）

        [Fact]
        public void ParseS7Address_Q8_ParsesByte()
        {
            var result = Parse("Q8");

            Assert.Equal(0, result.DbNumber);
            Assert.Equal((byte)0x82, result.Area); // Q 区域
            Assert.Equal(8, result.ByteOffset);
            Assert.False(result.IsBit);
        }

        [Fact]
        public void ParseS7Address_Q2_7_ParsesBit()
        {
            var result = Parse("Q2.7");

            Assert.Equal(0, result.DbNumber);
            Assert.Equal((byte)0x82, result.Area);
            Assert.Equal(2, result.ByteOffset);
            Assert.Equal(7, result.BitOffset);
            Assert.True(result.IsBit);
        }

        #endregion

        #region 边界情况

        [Fact]
        public void ParseS7Address_Null_ReturnsDefault()
        {
            var result = Parse(null!);

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
            Assert.Equal(0, result.ByteOffset);
        }

        [Fact]
        public void ParseS7Address_Empty_ReturnsDefault()
        {
            var result = Parse("");

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
        }

        [Fact]
        public void ParseS7Address_Whitespace_ReturnsDefault()
        {
            var result = Parse("   ");

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
        }

        [Fact]
        public void ParseS7Address_Lowercase_ConvertsToUpper()
        {
            var result = Parse("db1.dbw10");

            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
            Assert.Equal(10, result.ByteOffset);
        }

        [Fact]
        public void ParseS7Address_UnknownPrefix_ReturnsDefault()
        {
            var result = Parse("XYZ123");

            // 不匹配任何前缀，返回默认值 DB1.DBW0
            Assert.Equal(1, result.DbNumber);
            Assert.Equal((byte)0x84, result.Area);
            Assert.Equal(0, result.ByteOffset);
        }

        #endregion
    }
}
