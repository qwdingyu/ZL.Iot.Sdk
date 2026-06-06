// ============================================================
// 文件：ConfigValidationTests.cs
// 描述：ConfigValidation 工具类独立单元测试
// 修改日期：2026-06-03
// ============================================================

using System;
using System.Collections.Generic;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class ConfigValidationTests
    {
        #region IsValidIpAddress

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("192.168.1.1")]
        [InlineData("10.0.0.1")]
        [InlineData("0.0.0.0")]
        [InlineData("255.255.255.255")]
        [InlineData("::1")]
        [InlineData("2001:db8::1")]
        [InlineData("fe80::1")]
        public void IsValidIpAddress_ValidAddresses_ReturnsTrue(string ip)
        {
            Assert.True(ConfigValidation.IsValidIpAddress(ip));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("256.0.0.1")]
        [InlineData("abc.def.ghi.jkl")]
        [InlineData("192.168.1.1.1")]
        [InlineData("localhost")]
        [InlineData("192.168.1.-1")]
        public void IsValidIpAddress_InvalidAddresses_ReturnsFalse(string? ip)
        {
            Assert.False(ConfigValidation.IsValidIpAddress(ip));
        }

        #endregion

        #region IsValidPort

        [Theory]
        [InlineData(1)]
        [InlineData(80)]
        [InlineData(443)]
        [InlineData(502)]
        [InlineData(1883)]
        [InlineData(8080)]
        [InlineData(65535)]
        public void IsValidPort_ValidPorts_ReturnsTrue(int port)
        {
            Assert.True(ConfigValidation.IsValidPort(port));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-65535)]
        [InlineData(65536)]
        [InlineData(int.MaxValue)]
        public void IsValidPort_InvalidPorts_ReturnsFalse(int port)
        {
            Assert.False(ConfigValidation.IsValidPort(port));
        }

        #endregion

        #region IsValidTimeout

        [Theory]
        [InlineData(100, 100)]
        [InlineData(1000, 100)]
        [InlineData(30000, 100)]
        [InlineData(500, 500)]
        [InlineData(int.MaxValue, 1)]
        public void IsValidTimeout_ValidTimeouts_ReturnsTrue(int timeoutMs, int minMs)
        {
            Assert.True(ConfigValidation.IsValidTimeout(timeoutMs, minMs));
        }

        [Theory]
        [InlineData(99, 100)]
        [InlineData(0, 100)]
        [InlineData(-1, 100)]
        [InlineData(499, 500)]
        [InlineData(int.MinValue, 100)]
        public void IsValidTimeout_InvalidTimeouts_ReturnsFalse(int timeoutMs, int minMs)
        {
            Assert.False(ConfigValidation.IsValidTimeout(timeoutMs, minMs));
        }

        [Fact]
        public void IsValidTimeout_DefaultMinIs100()
        {
            Assert.True(ConfigValidation.IsValidTimeout(100));
            Assert.False(ConfigValidation.IsValidTimeout(99));
        }

        #endregion

        #region IsValidReconnectInterval

        [Theory]
        [InlineData(500)]
        [InlineData(1000)]
        [InlineData(30000)]
        public void IsValidReconnectInterval_Valid_ReturnsTrue(int ms, int minMs = 500)
        {
            Assert.True(ConfigValidation.IsValidReconnectInterval(ms, minMs));
        }

        [Theory]
        [InlineData(499)]
        [InlineData(100)]
        [InlineData(0)]
        [InlineData(-1)]
        public void IsValidReconnectInterval_Invalid_ReturnsFalse(int ms)
        {
            Assert.False(ConfigValidation.IsValidReconnectInterval(ms));
        }

        [Fact]
        public void IsValidReconnectInterval_DefaultMinIs500()
        {
            Assert.True(ConfigValidation.IsValidReconnectInterval(500));
            Assert.False(ConfigValidation.IsValidReconnectInterval(499));
        }

        #endregion

        #region IsValidErrorThreshold

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(100)]
        [InlineData(int.MaxValue)]
        public void IsValidErrorThreshold_Valid_ReturnsTrue(int threshold)
        {
            Assert.True(ConfigValidation.IsValidErrorThreshold(threshold));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(int.MinValue)]
        public void IsValidErrorThreshold_Invalid_ReturnsFalse(int threshold)
        {
            Assert.False(ConfigValidation.IsValidErrorThreshold(threshold));
        }

        #endregion

        #region ThrowIfInvalid

        [Fact]
        public void ThrowIfInvalid_NullErrors_DoesNotThrow()
        {
                        ConfigValidation.ThrowIfInvalid(null!);
        }

        [Fact]
        public void ThrowIfInvalid_EmptyErrors_DoesNotThrow()
        {
                        ConfigValidation.ThrowIfInvalid(Array.Empty<ConfigValidationError>());
        }

        [Fact]
        public void ThrowIfInvalid_SingleError_ThrowsWithMessage()
        {
            var errors = new List<ConfigValidationError>
            {
                new ConfigValidationError("Port", "must be between 1 and 65535")
            };

            var ex = Assert.Throws<InvalidOperationException>(() => ConfigValidation.ThrowIfInvalid(errors));
            Assert.Contains("[Port] must be between 1 and 65535", ex.Message);
        }

        [Fact]
        public void ThrowIfInvalid_MultipleErrors_ThrowsWithAllMessages()
        {
            var errors = new List<ConfigValidationError>
            {
                new ConfigValidationError("Host", "cannot be empty"),
                new ConfigValidationError("Port", "must be between 1 and 65535"),
                new ConfigValidationError("Timeout", "must be at least 100ms")
            };

            var ex = Assert.Throws<InvalidOperationException>(() => ConfigValidation.ThrowIfInvalid(errors));
            Assert.Contains("[Host] cannot be empty", ex.Message);
            Assert.Contains("[Port] must be between 1 and 65535", ex.Message);
            Assert.Contains("[Timeout] must be at least 100ms", ex.Message);
            Assert.Contains("; ", ex.Message);
        }

        #endregion
    }
}
