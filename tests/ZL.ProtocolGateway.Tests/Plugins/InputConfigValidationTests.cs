#nullable enable
using System.Collections.Generic;
using System.Linq;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins;

/// <summary>
/// 输入插件配置验证测试 — 所有 Validate() 方法均为纯函数，零基础设施依赖
/// </summary>
public class InputConfigValidationTests
{
    #region FileInput 配置验证

    [Fact]
    public void FileInput_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new FileInputConfig
        {
            FilePath = "/data/input.csv",
            PollIntervalMs = 1000,
            Delimiter = new byte[] { 0x0A }
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void FileInput_Validate_EmptyFilePath_ReturnsError()
    {
        var cfg = new FileInputConfig { FilePath = "", PollIntervalMs = 1000, Delimiter = new byte[] { 0x0A } };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "FilePath");
    }

    [Fact]
    public void FileInput_Validate_PollIntervalTooSmall_ReturnsError()
    {
        var cfg = new FileInputConfig { FilePath = "/f", PollIntervalMs = 10 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "PollIntervalMs");
    }

    [Fact]
    public void FileInput_Validate_EmptyDelimiter_ReturnsError()
    {
        var cfg = new FileInputConfig { FilePath = "/f", PollIntervalMs = 1000, Delimiter = System.Array.Empty<byte>() };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Delimiter");
    }

    #endregion

    #region TcpInput 配置验证

    [Fact]
    public void TcpInput_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new TcpInputConfig
        {
            LocalIp = "0.0.0.0",
            Port = 502,
            MaxMessageSize = 1024,
            Delimiter = new byte[] { 0x0D, 0x0A }
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void TcpInput_Validate_InvalidLocalIp_ReturnsError()
    {
        var cfg = new TcpInputConfig { LocalIp = "bad" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "LocalIp");
    }

    [Fact]
    public void TcpInput_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new TcpInputConfig { LocalIp = "0.0.0.0", Port = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    [Fact]
    public void TcpInput_Validate_MaxMessageSizeZero_ReturnsError()
    {
        var cfg = new TcpInputConfig { LocalIp = "0.0.0.0", Port = 502, MaxMessageSize = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "MaxMessageSize");
    }

    [Fact]
    public void TcpInput_Validate_EmptyDelimiter_ReturnsError()
    {
        var cfg = new TcpInputConfig { LocalIp = "0.0.0.0", Port = 502, Delimiter = System.Array.Empty<byte>() };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Delimiter");
    }

    #endregion

    #region UdpInput 配置验证

    [Fact]
    public void UdpInput_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new UdpInputConfig { LocalIp = "0.0.0.0", Port = 5000 };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void UdpInput_Validate_InvalidIp_ReturnsError()
    {
        var cfg = new UdpInputConfig { LocalIp = "invalid" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "LocalIp");
    }

    [Fact]
    public void UdpInput_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new UdpInputConfig { LocalIp = "0.0.0.0", Port = 99999 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    #endregion

    #region HttpInput 配置验证

    [Fact]
    public void HttpInput_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new HttpInputConfig
        {
            LocalIp = "0.0.0.0",
            Port = 8080,
            MaxBodySizeBytes = 4096,
            PathPrefix = "/api",
            AcceptedMethods = new List<string> { "POST" }
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void HttpInput_Validate_InvalidLocalIp_ReturnsError()
    {
        var cfg = new HttpInputConfig { LocalIp = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "LocalIp");
    }

    [Fact]
    public void HttpInput_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new HttpInputConfig { LocalIp = "0.0.0.0", Port = -1 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    [Fact]
    public void HttpInput_Validate_MaxBodySizeZero_ReturnsError()
    {
        var cfg = new HttpInputConfig { LocalIp = "0.0.0.0", Port = 8080, MaxBodySizeBytes = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "MaxBodySizeBytes");
    }

    [Fact]
    public void HttpInput_Validate_EmptyMethods_ReturnsError()
    {
        var cfg = new HttpInputConfig
        {
            LocalIp = "0.0.0.0", Port = 8080, AcceptedMethods = new List<string>()
        };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "AcceptedMethods");
    }

    [Fact]
    public void HttpInput_Validate_EmptyPathPrefix_ReturnsError()
    {
        var cfg = new HttpInputConfig
        {
            LocalIp = "0.0.0.0", Port = 8080, AcceptedMethods = new List<string> { "POST" }, PathPrefix = ""
        };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "PathPrefix");
    }

    #endregion

    #region WebSocketInput 配置验证

    [Fact]
    public void WebSocketInput_Validate_ValidWssUrl_ReturnsNoErrors()
    {
        var cfg = new WebSocketInputConfig
        {
            Url = "wss://server.example.com/ws",
            ReconnectIntervalMs = 5000,
            BufferSize = 4096
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void WebSocketInput_Validate_EmptyUrl_ReturnsError()
    {
        var cfg = new WebSocketInputConfig { Url = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Url");
    }

    [Fact]
    public void WebSocketInput_Validate_HttpUrl_ReturnsError()
    {
        var cfg = new WebSocketInputConfig { Url = "http://example.com" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Url");
    }

    [Fact]
    public void WebSocketInput_Validate_InvalidReconnectInterval_ReturnsError()
    {
        var cfg = new WebSocketInputConfig { Url = "ws://host", ReconnectIntervalMs = 100 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ReconnectIntervalMs");
    }

    [Fact]
    public void WebSocketInput_Validate_BufferSizeZero_ReturnsError()
    {
        var cfg = new WebSocketInputConfig { Url = "ws://host", BufferSize = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "BufferSize");
    }

    #endregion

    #region MqttInput 配置验证

    [Fact]
    public void MqttInput_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new MqttInputConfig
        {
            Server = "mqtt.example.com",
            Port = 1883,
            Topics = new List<string> { "sensor/#" }
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void MqttInput_Validate_EmptyServer_ReturnsError()
    {
        var cfg = new MqttInputConfig { Server = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Server");
    }

    [Fact]
    public void MqttInput_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new MqttInputConfig { Server = "host", Port = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    [Fact]
    public void MqttInput_Validate_EmptyTopics_ReturnsError()
    {
        var cfg = new MqttInputConfig { Server = "host", Port = 1883, Topics = new List<string>() };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Topics");
    }

    [Fact]
    public void MqttInput_Validate_NullTopics_ReturnsError()
    {
        var cfg = new MqttInputConfig { Server = "host", Port = 1883, Topics = null };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Topics");
    }

    #endregion

    #region SerialInput 配置验证

    [Fact]
    public void SerialInput_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new SerialInputConfig
        {
            PortName = "COM1",
            BaudRate = 115200,
            DataBits = 8,
            ReadTimeout = 1000,
            Delimiter = new byte[] { 0x0A }
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void SerialInput_Validate_EmptyPortName_ReturnsError()
    {
        var cfg = new SerialInputConfig { PortName = "", BaudRate = 9600, DataBits = 8, ReadTimeout = 1000 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "PortName");
    }

    [Fact]
    public void SerialInput_Validate_InvalidBaudRate_ReturnsError()
    {
        var cfg = new SerialInputConfig { PortName = "COM1", BaudRate = 0, DataBits = 8, ReadTimeout = 1000 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "BaudRate");
    }

    [Fact]
    public void SerialInput_Validate_InvalidDataBits_ReturnsError()
    {
        var cfg = new SerialInputConfig { PortName = "COM1", BaudRate = 9600, DataBits = 9, ReadTimeout = 1000 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "DataBits");
    }

    [Fact]
    public void SerialInput_Validate_ReadTimeoutTooSmall_ReturnsError()
    {
        var cfg = new SerialInputConfig { PortName = "COM1", BaudRate = 9600, DataBits = 8, ReadTimeout = 10 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ReadTimeout");
    }

    [Fact]
    public void SerialInput_Validate_EmptyDelimiter_ReturnsError()
    {
        var cfg = new SerialInputConfig
        {
            PortName = "COM1", BaudRate = 9600, DataBits = 8, ReadTimeout = 1000,
            Delimiter = System.Array.Empty<byte>()
        };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Delimiter");
    }

    [Fact]
    public void SerialInput_Validate_MultipleErrors_ReturnsAll()
    {
        var cfg = new SerialInputConfig
        {
            PortName = "",
            BaudRate = 0,
            DataBits = 4,
            ReadTimeout = 10
        };
        var errors = cfg.Validate();
        Assert.True(errors.Count >= 3, $"Expected >=3 errors, got {errors.Count}");
    }

    #endregion
}
