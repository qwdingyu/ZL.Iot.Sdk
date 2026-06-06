using ZL.Framing;
using ZL.Probing;

namespace ZL.Probing.Tests;

public class TransparentProxyConfigTests
{
    [Fact]
    public void DefaultConfig_HasCorrectDefaults()
    {
        var config = new TransparentProxyConfig();

        Assert.Equal(ListenMode.TcpServer, config.ListenMode);
        Assert.Null(config.SourcePort);
        Assert.Null(config.SourcePortName);
        Assert.Equal("localhost", config.TargetHost);
        Assert.Null(config.TargetPort);
        Assert.Null(config.TargetPortName);
        Assert.False(config.SnifferOnly);
        Assert.Null(config.LogFile);
        Assert.Null(config.SessionLogDir);
        Assert.Equal("UTF-8", config.EncodingName);
        Assert.Equal(30, config.FrameTimeoutMs);
        Assert.Null(config.ByteFraming);
    }

    [Fact]
    public void SetTcpServerMode_Fields()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 5026,
            TargetHost = "192.168.1.100",
            TargetPort = 5025
        };

        Assert.Equal(ListenMode.TcpServer, config.ListenMode);
        Assert.Equal(5026, config.SourcePort);
        Assert.Equal("192.168.1.100", config.TargetHost);
        Assert.Equal(5025, config.TargetPort);
    }

    [Fact]
    public void SetTcpClientMode_Fields()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpClient,
            TargetHost = "target.example.com",
            TargetPort = 8080
        };

        Assert.Equal(ListenMode.TcpClient, config.ListenMode);
        Assert.Equal("target.example.com", config.TargetHost);
        Assert.Equal(8080, config.TargetPort);
    }

    [Fact]
    public void SetSerialMode_Fields()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.Serial,
            SourcePortName = "/dev/ttyUSB0",
            TargetPortName = "/dev/ttyACM0"
        };

        Assert.Equal(ListenMode.Serial, config.ListenMode);
        Assert.Equal("/dev/ttyUSB0", config.SourcePortName);
        Assert.Equal("/dev/ttyACM0", config.TargetPortName);
    }

    [Fact]
    public void SnifferOnly_Mode_DoesNotForward()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 9999,
            TargetHost = "localhost",
            TargetPort = 8888,
            SnifferOnly = true
        };

        Assert.True(config.SnifferOnly);
    }

    [Fact]
    public void WithLogFileAndSessionLogDir_SetsPaths()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 7777,
            TargetHost = "localhost",
            TargetPort = 6666,
            LogFile = "/tmp/proxy_log.jsonl",
            SessionLogDir = "/tmp/sessions/"
        };

        Assert.Equal("/tmp/proxy_log.jsonl", config.LogFile);
        Assert.Equal("/tmp/sessions/", config.SessionLogDir);
    }

    [Fact]
    public void WithCustomEncoding_SetsEncodingName()
    {
        var config = new TransparentProxyConfig
        {
            EncodingName = "ISO-8859-1"
        };

        Assert.Equal("ISO-8859-1", config.EncodingName);
    }

    [Fact]
    public void WithCustomFrameTimeout_SetsTimeout()
    {
        var config = new TransparentProxyConfig
        {
            FrameTimeoutMs = 50
        };

        Assert.Equal(50, config.FrameTimeoutMs);
    }

    [Fact]
    public void WithCustomByteFraming_SetsOptions()
    {
        var framing = new ByteFramingOptions
        {
            Strategy = "LengthField",
            FixedLength = 128
        };

        var config = new TransparentProxyConfig
        {
            ByteFraming = framing
        };

        Assert.NotNull(config.ByteFraming);
        Assert.Equal("LengthField", config.ByteFraming.Strategy);
        Assert.Equal(128, config.ByteFraming.FixedLength);
    }
}

public class ListenModeTests
{
    [Theory]
    [InlineData(ListenMode.TcpServer, "TcpServer")]
    [InlineData(ListenMode.TcpClient, "TcpClient")]
    [InlineData(ListenMode.Serial, "Serial")]
    public void ListenMode_ValuesAreDefined(ListenMode mode, string expectedName)
    {
        Assert.True(Enum.IsDefined(typeof(ListenMode), mode));
    }
}
