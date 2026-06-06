#nullable enable
using System;
using System.Reflection;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins
{
    /// <summary>
    /// 三菱 MC 设备地址解析深度测试
    /// ParseDeviceAddress 是 private 方法，通过反射调用
    /// </summary>
    public class MitsubishiMcDeviceAddressTests
    {
        private static readonly MethodInfo _parseMethod;

        static MitsubishiMcDeviceAddressTests()
        {
            var pluginType = typeof(MitsubishiMcOutputPlugin);
            _parseMethod = pluginType.GetMethod("ParseDeviceAddress",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ParseDeviceAddress method not found");
        }

        private static (byte deviceCode, int address) ParseDeviceAddress(string addressStr)
        {
            // 用默认配置创建一个实例（不需要真实连接）
            var plugin = new MitsubishiMcOutputPlugin(new MitsubishiMcOutputConfig { ServerIp = "127.0.0.1", Port = 5000 });
            var result = _parseMethod.Invoke(plugin, new object?[] { addressStr });
            return ((byte, int))result!;
        }

        // ========== 所有设备码映射测试 ==========

        [Theory]
        [InlineData("D100", 0xA8, 100)]   // D区 - 数据寄存器
        [InlineData("X10", 0x9C, 10)]    // X区 - 输入
        [InlineData("Y20", 0x9D, 20)]    // Y区 - 输出
        [InlineData("M100", 0x90, 100)]  // M区 - 辅助继电器
        [InlineData("L50", 0x92, 50)]    // L区 - 锁存继电器
        [InlineData("W200", 0xB4, 200)]  // W区 - 链接寄存器
        [InlineData("B30", 0xA0, 30)]    // B区 - 链接继电器
        [InlineData("F10", 0x93, 10)]    // F区 - 报警器
        [InlineData("V20", 0x94, 20)]    // V区 - 边沿继电器
        [InlineData("S100", 0x98, 100)]  // S区 - 步进继电器
        [InlineData("Z500", 0xAF, 500)]  // Z区 - 文件寄存器
        [InlineData("R1000", 0xAF, 1000)]// R区 - 文件寄存器（同Z）
        public void ParseDeviceAddress_AllDeviceCodes_MapsCorrectly(string address, byte expectedCode, int expectedAddr)
        {
            var (code, addr) = ParseDeviceAddress(address);
            Assert.Equal(expectedCode, code);
            Assert.Equal(expectedAddr, addr);
        }

        // ========== 小写字母测试 ==========

        [Theory]
        [InlineData("d100", 0xA8, 100)]
        [InlineData("x10", 0x9C, 10)]
        [InlineData("y20", 0x9D, 20)]
        [InlineData("m100", 0x90, 100)]
        [InlineData("z500", 0xAF, 500)]
        public void ParseDeviceAddress_Lowercase_MapsCorrectly(string address, byte expectedCode, int expectedAddr)
        {
            var (code, addr) = ParseDeviceAddress(address);
            Assert.Equal(expectedCode, code);
            Assert.Equal(expectedAddr, addr);
        }

        // ========== 边界与异常输入 ==========

        [Fact]
        public void ParseDeviceAddress_Null_ReturnsDefaultD()
        {
            var (code, addr) = ParseDeviceAddress(null!);
            Assert.Equal((byte)0xA8, code); // 默认 D区
            Assert.Equal(0, addr);
        }

        [Fact]
        public void ParseDeviceAddress_Empty_ReturnsDefaultD()
        {
            var (code, addr) = ParseDeviceAddress("");
            Assert.Equal((byte)0xA8, code);
            Assert.Equal(0, addr);
        }

        [Fact]
        public void ParseDeviceAddress_OnlyLetter_NoNumber_ReturnsZeroAddress()
        {
            var (code, addr) = ParseDeviceAddress("D");
            Assert.Equal((byte)0xA8, code);
            Assert.Equal(0, addr); // int.TryParse("") 返回 false → 0
        }

        [Fact]
        public void ParseDeviceAddress_NonNumericSuffix_ReturnsZeroAddress()
        {
            var (code, addr) = ParseDeviceAddress("Dabc");
            Assert.Equal((byte)0xA8, code);
            Assert.Equal(0, addr); // int.TryParse("abc") 返回 false → 0
        }

        [Fact]
        public void ParseDeviceAddress_UnknownLetter_DefaultsToD()
        {
            var (code, addr) = ParseDeviceAddress("K999");
            Assert.Equal((byte)0xA8, code); // 未知字母 → 默认 D区
            Assert.Equal(999, addr);
        }

        [Fact]
        public void ParseDeviceAddress_ZeroAddress_IsValid()
        {
            var (code, addr) = ParseDeviceAddress("D0");
            Assert.Equal((byte)0xA8, code);
            Assert.Equal(0, addr);
        }

        [Fact]
        public void ParseDeviceAddress_LargeAddress_PreservesValue()
        {
            var (code, addr) = ParseDeviceAddress("D65535");
            Assert.Equal((byte)0xA8, code);
            Assert.Equal(65535, addr);
        }

        // ========== 工业场景：D区大数据地址 ==========

        [Fact]
        public void ParseDeviceAddress_IndustrialLargeDAddress_ParsesCorrectly()
        {
            // 三菱 Q 系列 D 区地址可达 65535
            var (code, addr) = ParseDeviceAddress("D65000");
            Assert.Equal((byte)0xA8, code);
            Assert.Equal(65000, addr);
        }
    }
}
#nullable restore
