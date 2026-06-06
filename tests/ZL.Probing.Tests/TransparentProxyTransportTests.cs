using ZL.Framing;
using ZL.Probing;
using System.Net.Sockets;

namespace ZL.Probing.Tests;

public class TransparentProxyTransportTests
{
    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        var mockTarget = new MockIByteTransport();
        Assert.Throws<ArgumentNullException>(() => new TransparentProxyTransport(null!, mockTarget));
    }

    [Fact]
    public void Constructor_NullTargetTransport_ThrowsArgumentNullException()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 5026,
            TargetHost = "localhost",
            TargetPort = 5025
        };
        Assert.Throws<ArgumentNullException>(() => new TransparentProxyTransport(config, null!));
    }

    [Fact]
    public void Constructor_WithValidArgs_CreatesSuccessfully()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 5026,
            TargetHost = "localhost",
            TargetPort = 5025
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        Assert.NotNull(proxy);
        Assert.False(proxy.IsOpen);
    }

    [Fact]
    public void ResourceName_ContainsPorts()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 9999,
            TargetHost = "target",
            TargetPort = 8888
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        Assert.Contains("9999", proxy.ResourceName);
        Assert.Contains("target", proxy.ResourceName);
        Assert.Contains("8888", proxy.ResourceName);
    }

    [Fact]
    public void FrameTimeoutMs_CalculatesFromConfig()
    {
        var config = new TransparentProxyConfig { FrameTimeoutMs = 50 };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        Assert.Equal(50, proxy.FrameTimeoutMs);
    }

    [Fact]
    public void FrameTimeoutMs_DefaultsTo30_WhenZero()
    {
        var config = new TransparentProxyConfig { FrameTimeoutMs = 0 };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        Assert.Equal(30, proxy.FrameTimeoutMs);
    }

    [Fact]
    public void FrameTimeoutMs_DefaultsTo30_WhenNegative()
    {
        var config = new TransparentProxyConfig { FrameTimeoutMs = -100 };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        Assert.Equal(30, proxy.FrameTimeoutMs);
    }

    [Fact]
    public void Open_MultipleCalls_IsIdempotent()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0, // 0 = random port
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        proxy.Open();
        Assert.True(proxy.IsOpen);

        var ex = Record.Exception(() => proxy.Open());
        Assert.Null(ex); // 不应抛异常
    }

    [Fact]
    public void Close_WhenAlreadyClosed_DoesNotThrow()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        var ex = Record.Exception(() => proxy.Close());
        Assert.Null(ex); // 不应抛异常
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        proxy.Dispose();
        var ex = Record.Exception(() => proxy.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Send_NullData_DoesNotThrow()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        var ex = Record.Exception(() => proxy.Send(null!));
        Assert.Null(ex);
    }

    [Fact]
    public void Send_EmptyData_DoesNotThrow()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        var ex = Record.Exception(() => proxy.Send(Array.Empty<byte>()));
        Assert.Null(ex);
    }

    [Fact]
    public void Send_WithNullSessionId_DoesNotThrow()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        var ex = Record.Exception(() => proxy.Send(new byte[] { 1, 2, 3 }, null!));
        Assert.Null(ex);
    }

    [Fact]
    public void Send_WithEmptySessionId_DoesNotThrow()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        var ex = Record.Exception(() => proxy.Send(new byte[] { 1, 2, 3 }, ""));
        Assert.Null(ex);
    }

    [Fact]
    public void Send_InSnifferMode_DoesNotForwardToTarget()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234,
            SnifferOnly = true
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        var testData = new byte[] { 1, 2, 3 };
        proxy.Send(testData);

        // 在 SnifferOnly 模式下，mockTarget.Send 不应被调用
        Assert.False(mockTarget.SendCalled);
    }

    [Fact]
    public void Send_InForwardMode_ForwardstoTarget()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234,
            SnifferOnly = false
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        var testData = new byte[] { 1, 2, 3 };
        proxy.Send(testData);

        Assert.True(mockTarget.SendCalled);
    }

    [Fact]
    public void DataReceived_EventAttached_SubscribesToEvents()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        int eventCount = 0;
        proxy.DataReceived += _ => eventCount++;

        // 事件应能被注册（不验证实际触发，因为没 Open）
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void FrameStatusChanged_EventAttached()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        int eventCount = 0;
        proxy.FrameStatusChanged += _ => eventCount++;

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void SessionStarted_EventAttached()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        int eventCount = 0;
        proxy.SessionStarted += _ => eventCount++;

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void SessionEnded_EventAttached()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        int eventCount = 0;
        proxy.SessionEnded += _ => eventCount++;

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void Framing_ReturnsConfigOrDefault()
    {
        var customFraming = new ByteFramingOptions { FixedLength = 256 };
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234,
            ByteFraming = customFraming
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        Assert.NotNull(proxy.Framing);
        Assert.Equal(256, proxy.Framing.FixedLength);
    }

    [Fact]
    public void Framing_ReturnsDefault_WhenNotConfigured()
    {
        var config = new TransparentProxyConfig
        {
            ListenMode = ListenMode.TcpServer,
            SourcePort = 0,
            TargetHost = "localhost",
            TargetPort = 1234
        };
        var mockTarget = new MockIByteTransport();
        var proxy = new TransparentProxyTransport(config, mockTarget);

        Assert.NotNull(proxy.Framing);
    }

    /// <summary>
    /// 模拟 IByteTransport，用于测试 TransparentProxyTransport 对目标传输的调用。
    /// </summary>
    private sealed class MockIByteTransport : IByteTransport
    {
        public bool IsOpen { get; private set; }
        public bool SendCalled { get; private set; }

        public string ResourceName => "MockTransport";

        public event Action<byte[]>? DataReceived;
        public event Action<FrameStatus>? FrameStatusChanged;

        public void Open() => IsOpen = true;
        public void Close() => IsOpen = false;

        public void Send(byte[] data)
        {
            SendCalled = true;
            DataReceived?.Invoke(data);
        }

        public void Dispose() { }
    }
}
