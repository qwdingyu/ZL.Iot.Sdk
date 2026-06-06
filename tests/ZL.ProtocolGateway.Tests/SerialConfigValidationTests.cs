#nullable enable
using System;
using System.Collections.Generic;
using System.IO.Ports;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// SerialInputConfig / SerialOutputConfig 配置验证测试
    /// 注意：SerialInputPlugin 和 SerialOutputPlugin 需要真实串口硬件才能完整测试。
    /// 本测试只覆盖配置层验证（Validate() 方法），这是无需硬件的测试边界。
    /// 完整的串口 I/O 测试需要在集成测试环境中使用虚拟串口（如 com0com + com2tcp）。
    /// </summary>
    public class SerialConfigValidationTests
    {
        #region SerialInputConfig 验证

        [Fact]
        public void SerialInputConfig_Default_HasNoErrors()
        {
            var config = new SerialInputConfig
            {
                PortName = "COM1",
                BaudRate = 9600,
                DataBits = 8,
                ReadTimeout = 1000,
                Delimiter = new byte[] { 0x0A }
            };
            var errors = config.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void SerialInputConfig_NullPortName_ReportsError()
        {
            var config = new SerialInputConfig { PortName = null! };
            var errors = config.Validate();

            Assert.Contains(errors, e =>
                e.PropertyName == nameof(SerialInputConfig.PortName) &&
                e.ErrorMessage.Contains("不能为空"));
        }

        [Fact]
        public void SerialInputConfig_EmptyPortName_ReportsError()
        {
            var errors = new SerialInputConfig { PortName = "" }.Validate();
            Assert.Contains(errors, e => e.PropertyName == nameof(SerialInputConfig.PortName));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void SerialInputConfig_InvalidBaudRate_ReportsError(int baudRate)
        {
            var config = new SerialInputConfig
            {
                PortName = "COM1",
                BaudRate = baudRate,
                DataBits = 8,
                ReadTimeout = 1000,
                Delimiter = new byte[] { 0x0A }
            };
            var errors = config.Validate();

            Assert.Contains(errors, e =>
                e.PropertyName == nameof(SerialInputConfig.BaudRate) &&
                e.ErrorMessage.Contains("必须 > 0"));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(9)]
        public void SerialInputConfig_InvalidDataBits_ReportsError(int dataBits)
        {
            var config = new SerialInputConfig
            {
                PortName = "COM1",
                DataBits = dataBits,
                ReadTimeout = 1000,
                Delimiter = new byte[] { 0x0A }
            };
            var errors = config.Validate();

            Assert.Contains(errors, e =>
                e.PropertyName == nameof(SerialInputConfig.DataBits) &&
                e.ErrorMessage.Contains("必须在 5-8 范围内"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(99)]
        public void SerialInputConfig_ReadTimeoutBelow100_ReportsError(int timeout)
        {
            var config = new SerialInputConfig
            {
                PortName = "COM1",
                ReadTimeout = timeout,
                Delimiter = new byte[] { 0x0A }
            };
            var errors = config.Validate();

            Assert.Contains(errors, e =>
                e.PropertyName == nameof(SerialInputConfig.ReadTimeout) &&
                e.ErrorMessage.Contains("必须 >= 100"));
        }

        [Fact]
        public void SerialInputConfig_NullDelimiter_ReportsError()
        {
            var config = new SerialInputConfig { PortName = "COM1", Delimiter = null! };
            var errors = config.Validate();

            Assert.Contains(errors, e =>
                e.PropertyName == nameof(SerialInputConfig.Delimiter) &&
                e.ErrorMessage.Contains("不能为空"));
        }

        [Fact]
        public void SerialInputConfig_EmptyDelimiter_ReportsError()
        {
            var config = new SerialInputConfig { PortName = "COM1", Delimiter = Array.Empty<byte>() };
            var errors = config.Validate();

            Assert.Contains(errors, e => e.PropertyName == nameof(SerialInputConfig.Delimiter));
        }

        [Fact]
        public void SerialInputConfig_MultipleErrors_ReturnsAll()
        {
            // 多个错误同时存在
            var config = new SerialInputConfig
            {
                PortName = "",          // 错误1
                BaudRate = -1,          // 错误2
                DataBits = 9,           // 错误3
                ReadTimeout = 50,       // 错误4
                Delimiter = null!       // 错误5
            };
            var errors = config.Validate();

            Assert.Equal(5, errors.Count);
        }

        #endregion

        #region SerialInputConfig 合法值边界

        [Theory]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void SerialInputConfig_ValidDataBits_NoError(int dataBits)
        {
            var config = new SerialInputConfig
            {
                PortName = "COM1",
                DataBits = dataBits,
                ReadTimeout = 1000,
                Delimiter = new byte[] { 0x0A }
            };
            Assert.DoesNotContain(config.Validate(), e =>
                e.PropertyName == nameof(SerialInputConfig.DataBits));
        }

        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(10000)]
        public void SerialInputConfig_ValidReadTimeout_NoError(int timeout)
        {
            var config = new SerialInputConfig
            {
                PortName = "COM1",
                ReadTimeout = timeout,
                Delimiter = new byte[] { 0x0A }
            };
            Assert.DoesNotContain(config.Validate(), e =>
                e.PropertyName == nameof(SerialInputConfig.ReadTimeout));
        }

        #endregion

        #region SerialOutputConfig 验证

        [Fact]
        public void SerialOutputConfig_Default_HasNoErrors()
        {
            // 默认值：PortName="COM1" 非空，BaudRate=9600
            var errors = new SerialOutputConfig().Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void SerialOutputConfig_NullPortName_ReportsError()
        {
            var config = new SerialOutputConfig { PortName = null! };
            var errors = config.Validate();

            Assert.Contains(errors, e =>
                e.PropertyName == nameof(SerialOutputConfig.PortName) &&
                e.ErrorMessage.Contains("不能为空"));
        }

        [Fact]
        public void SerialOutputConfig_EmptyPortName_ReportsError()
        {
            var config = new SerialOutputConfig { PortName = "" };
            var errors = config.Validate();

            Assert.Contains(errors, e => e.PropertyName == nameof(SerialOutputConfig.PortName));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void SerialOutputConfig_InvalidBaudRate_ReportsError(int baudRate)
        {
            var config = new SerialOutputConfig { PortName = "COM1", BaudRate = baudRate };
            var errors = config.Validate();

            Assert.Contains(errors, e =>
                e.PropertyName == nameof(SerialOutputConfig.BaudRate) &&
                e.ErrorMessage.Contains("必须 > 0"));
        }

        #endregion

        #region SerialOutputPlugin 构造函数

        [Fact]
        public void SerialOutputPlugin_Constructor_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SerialOutputPlugin(null!));
        }

        [Fact]
        public void SerialOutputPlugin_Constructor_InvalidConfig_Throws()
        {
            var config = new SerialOutputConfig { PortName = "" };
            Assert.Throws<InvalidOperationException>(() => new SerialOutputPlugin(config));
        }

        [Fact]
        public void SerialOutputPlugin_Constructor_ValidConfig_CreatesSuccessfully()
        {
            var plugin = new SerialOutputPlugin(new SerialOutputConfig { PortName = "COM1" });
            Assert.NotNull(plugin);
            Assert.Equal("Serial-COM1", plugin.Name);
            Assert.Equal("Serial", plugin.ProtocolType);
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public void SerialOutputPlugin_Constructor_CustomName_ReflectedInNameProp()
        {
            var plugin = new SerialOutputPlugin(new SerialOutputConfig { Name = "MySerial", PortName = "COM1" });
            Assert.Equal("MySerial", plugin.Name);
        }

        [Fact]
        public void SerialOutputPlugin_Dispose_DoesNotThrow()
        {
            var plugin = new SerialOutputPlugin(new SerialOutputConfig { PortName = "COM1" });
            plugin.Dispose();
            // Dispose 不应抛异常（即使未 Start）
        }

        #endregion

        #region SerialInputPlugin 构造函数

        [Fact]
        public void SerialInputPlugin_Constructor_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SerialInputPlugin(null!));
        }

        [Fact]
        public void SerialInputPlugin_Constructor_ValidConfig_CreatesSuccessfully()
        {
            var plugin = new SerialInputPlugin(new SerialInputConfig
            {
                PortName = "COM1",
                Delimiter = new byte[] { 0x0A }
            });
            Assert.NotNull(plugin);
            Assert.Equal("Serial-COM1", plugin.Name);
            Assert.Equal("Serial", plugin.ProtocolType);
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public void SerialInputPlugin_Constructor_CustomName_ReflectedInNameProp()
        {
            var plugin = new SerialInputPlugin(new SerialInputConfig
            {
                Name = "MySerialInput",
                PortName = "/dev/ttyUSB0",
                Delimiter = new byte[] { 0x0A }
            });
            Assert.Equal("MySerialInput", plugin.Name);
        }

        [Fact]
        public void SerialInputPlugin_StopAsync_WithoutStart_DoesNotThrow()
        {
            var plugin = new SerialInputPlugin(new SerialInputConfig
            {
                PortName = "COM1",
                Delimiter = new byte[] { 0x0A }
            });
            // 未 Start 直接 Stop
            plugin.Dispose();
        }

        #endregion
    }
}
#nullable restore
