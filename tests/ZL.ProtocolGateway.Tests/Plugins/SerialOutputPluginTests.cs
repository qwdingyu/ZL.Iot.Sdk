#nullable enable
using System;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins;

/// <summary>
/// SerialOutputPlugin 单元测试
/// 测试构造函数、配置验证、命名和协议类型属性。
/// Start/Stop 需要真实串口，不在单元测试中覆盖。
/// </summary>
public class SerialOutputPluginTests
{
    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SerialOutputPlugin(null!));
    }

    [Fact]
    public void Constructor_ValidConfig_SetsProperties()
    {
        var config = new SerialOutputConfig
        {
            Name = "test-serial",
            PortName = "COM3",
            BaudRate = 115200,
            DataBits = 8,
            StopBits = System.IO.Ports.StopBits.One,
            Parity = System.IO.Ports.Parity.None
        };

        using var plugin = new SerialOutputPlugin(config);
        Assert.Equal("test-serial", plugin.Name);
        Assert.Equal("Serial", plugin.ProtocolType);
        Assert.Equal(PluginStatus.Stopped, plugin.Status);
    }

    [Fact]
    public void Constructor_DefaultName_FromPortName()
    {
        var config = new SerialOutputConfig
        {
            PortName = "COM5"
        };

        using var plugin = new SerialOutputPlugin(config);
        Assert.Equal("Serial-COM5", plugin.Name);
    }

    [Fact]
    public void Constructor_EmptyName_DefaultsToPortName()
    {
        var config = new SerialOutputConfig
        {
            Name = "",
            PortName = "COM1"
        };

        using var plugin = new SerialOutputPlugin(config);
        Assert.Equal("Serial-COM1", plugin.Name);
    }

    [Fact]
    public void Constructor_InvalidConfig_ThrowsConfigValidationException()
    {
        var config = new SerialOutputConfig
        {
            Name = "bad",
            PortName = "", // invalid
            BaudRate = -1  // invalid
        };

        Assert.Throws<InvalidOperationException>(() => new SerialOutputPlugin(config));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-9600)]
    public void ConfigValidation_InvalidBaudRate_ProducesError(int baudRate)
    {
        var config = new SerialOutputConfig { BaudRate = baudRate, PortName = "COM1" };
        var errors = config.Validate();

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.PropertyName == nameof(SerialOutputConfig.BaudRate));
    }

    [Fact]
    public void ConfigValidation_EmptyPortName_ProducesError()
    {
        var config = new SerialOutputConfig { PortName = "", BaudRate = 9600 };
        var errors = config.Validate();

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.PropertyName == nameof(SerialOutputConfig.PortName));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData(null)]
    public void ConfigValidation_WhitespacePortName_ProducesError(string? portName)
    {
        var config = new SerialOutputConfig { PortName = portName, BaudRate = 9600 };
        var errors = config.Validate();

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.PropertyName == nameof(SerialOutputConfig.PortName));
    }

    [Fact]
    public void ConfigValidation_ValidConfig_NoErrors()
    {
        var config = new SerialOutputConfig
        {
            PortName = "COM1",
            BaudRate = 9600,
            DataBits = 8,
            StopBits = System.IO.Ports.StopBits.Two,
            Parity = System.IO.Ports.Parity.Even,
            Suffix = "\r\n",
            EncodingName = "UTF-8"
        };

        var errors = config.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Config_DefaultValues()
    {
        var config = new SerialOutputConfig();
        Assert.Equal("COM1", config.PortName);
        Assert.Equal(9600, config.BaudRate);
        Assert.Equal(8, config.DataBits);
        Assert.Equal(System.IO.Ports.StopBits.One, config.StopBits);
        Assert.Equal(System.IO.Ports.Parity.None, config.Parity);
        Assert.Equal("UTF-8", config.EncodingName);
        Assert.Equal(string.Empty, config.Suffix);
    }

    [Fact]
    public void Constructor_CustomEncoding_DoesNotThrow()
    {
        var config = new SerialOutputConfig
        {
            Name = "ascii-serial",
            PortName = "COM2",
            EncodingName = "ASCII"
        };

        using var plugin = new SerialOutputPlugin(config);
        Assert.Equal("ascii-serial", plugin.Name);
    }

    [Theory]
    [InlineData(System.IO.Ports.StopBits.One)]
    [InlineData(System.IO.Ports.StopBits.Two)]
    [InlineData(System.IO.Ports.StopBits.OnePointFive)]
    public void Config_AllStopBits_Valid(System.IO.Ports.StopBits stopBits)
    {
        var config = new SerialOutputConfig { PortName = "COM1", StopBits = stopBits };
        var errors = config.Validate();
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(System.IO.Ports.Parity.None)]
    [InlineData(System.IO.Ports.Parity.Odd)]
    [InlineData(System.IO.Ports.Parity.Even)]
    [InlineData(System.IO.Ports.Parity.Mark)]
    [InlineData(System.IO.Ports.Parity.Space)]
    public void Config_AllParityValues_Valid(System.IO.Ports.Parity parity)
    {
        var config = new SerialOutputConfig { PortName = "COM1", Parity = parity };
        var errors = config.Validate();
        Assert.Empty(errors);
    }
}
