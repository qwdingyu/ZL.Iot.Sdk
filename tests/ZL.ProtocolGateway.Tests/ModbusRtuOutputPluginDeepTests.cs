#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins
{
    /// <summary>
    /// ModbusRtuOutputPlugin 深度测试 — 使用 Mock Transport 测试完整读写流程
    /// </summary>
    public class ModbusRtuOutputPluginDeepTests
    {
        #region 基本启动/停止

        [Fact]
        public async Task StartAsync_WithMockTransport_CallsOpen()
        {
            var transport = new MockRtuTransport();
            var config = new ModbusRtuOutputConfig { PortName = "COM1", BaudRate = 9600 };
            var plugin = new ModbusRtuOutputPlugin(config, transport);

            await plugin.StartAsync();

            Assert.True(transport.OpenCalled);
            Assert.Equal(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task StopAsync_CallsTransportClose()
        {
            var transport = new MockRtuTransport();
            var config = new ModbusRtuOutputConfig { PortName = "COM1" };
            var plugin = new ModbusRtuOutputPlugin(config, transport);

            await plugin.StartAsync();
            await plugin.StopAsync();

            Assert.True(transport.CloseCalled);
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        #endregion

        #region 完整写入流程

        [Fact]
        public async Task SendAsync_ValidJson_SendsCorrectRtuFrame()
        {
            var transport = new MockRtuTransport();
            var config = new ModbusRtuOutputConfig { PortName = "COM1", UnitId = 1 };
            var plugin = new ModbusRtuOutputPlugin(config, transport);

            await plugin.StartAsync();

            var msg = new Message();
            msg.SetJsonContent("{\"registers\":[{\"address\":\"40001\",\"value\":\"100\"}]}");

            // 构建 RTU 响应（与请求相同的数据部分 + CRC）
            var write = new ModbusWriteOperation(address: 0, value: 100, isCoil: false, unitId: 1);
            transport.NextResponse = ModbusWriteSupport.BuildRtuWriteRequest(write);

            await plugin.SendAsync(msg);

            Assert.Single(transport.SentRequests);
            var sentFrame = transport.SentRequests.ToArray()[0];
            Assert.Equal(8, sentFrame.Length);
            Assert.Equal(1, sentFrame[0]); // Unit ID
            Assert.Equal(0x06, sentFrame[1]); // Function code: Write Single Register

            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_CoilWrite_SendsCoilFrame()
        {
            var transport = new MockRtuTransport();
            var config = new ModbusRtuOutputConfig { PortName = "COM1", UnitId = 1 };
            var plugin = new ModbusRtuOutputPlugin(config, transport);

            await plugin.StartAsync();

            var msg = new Message();
            msg.SetJsonContent("{\"registers\":[{\"address\":\"M1\",\"value\":\"true\"}]}");

            var write = new ModbusWriteOperation(address: 0, value: 0xFF00, isCoil: true, unitId: 1);
            transport.NextResponse = ModbusWriteSupport.BuildRtuWriteRequest(write);

            await plugin.SendAsync(msg);

            var sentFrame = transport.SentRequests.ToArray()[0];
            Assert.Equal(0x05, sentFrame[1]); // Function code: Write Single Coil

            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_TransportThrows_ExceptionPropagates()
        {
            var transport = new MockRtuTransport
            {
                SendException = new InvalidOperationException("serial error")
            };
            var config = new ModbusRtuOutputConfig { PortName = "COM1" };
            var plugin = new ModbusRtuOutputPlugin(config, transport);

            await plugin.StartAsync();

            var msg = new Message();
            msg.SetJsonContent("{\"registers\":[{\"address\":\"40001\",\"value\":\"1\"}]}");

            await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.SendAsync(msg));

            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_BadResponse_ValidationThrows()
        {
            var transport = new MockRtuTransport();
            var config = new ModbusRtuOutputConfig { PortName = "COM1", UnitId = 1 };
            var plugin = new ModbusRtuOutputPlugin(config, transport);

            await plugin.StartAsync();

            var msg = new Message();
            msg.SetJsonContent("{\"registers\":[{\"address\":\"40001\",\"value\":\"1\"}]}");

            // 返回错误的响应（长度不对）
            transport.NextResponse = new byte[] { 1, 0x06, 0, 0, 0, 1 }; // 6 bytes, not 8

            await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.SendAsync(msg));

            await plugin.StopAsync();
        }

        #endregion

        #region 边界情况

        [Fact]
        public async Task SendAsync_NullMessage_IsNoop()
        {
            var transport = new MockRtuTransport();
            var config = new ModbusRtuOutputConfig { PortName = "COM1" };
            var plugin = new ModbusRtuOutputPlugin(config, transport);

            await plugin.StartAsync();
            await plugin.SendAsync(null!);
            Assert.Empty(transport.SentRequests);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task SendAsync_NoRegisters_IsNoop()
        {
            var transport = new MockRtuTransport();
            var config = new ModbusRtuOutputConfig { PortName = "COM1" };
            var plugin = new ModbusRtuOutputPlugin(config, transport);

            await plugin.StartAsync();

            var msg = new Message();
            msg.SetJsonContent("{\"notRegisters\":[]}");

            await plugin.SendAsync(msg);
            Assert.Empty(transport.SentRequests);

            await plugin.StopAsync();
        }

        #endregion

        #region Mock Transport

        private class MockRtuTransport : IModbusRtuTransport
        {
            public bool OpenCalled { get; private set; }
            public bool CloseCalled { get; private set; }
            public ConcurrentBag<byte[]> SentRequests { get; } = new();
            public byte[]? NextResponse { get; set; }
            public Exception? SendException { get; set; }

            public Task OpenAsync(ModbusRtuOutputConfig config, CancellationToken ct = default)
            {
                OpenCalled = true;
                return Task.CompletedTask;
            }

            public Task<byte[]> SendAndReceiveAsync(byte[] request, int timeoutMs, CancellationToken ct = default)
            {
                SentRequests.Add(request);
                if (SendException != null) throw SendException;
                return Task.FromResult(NextResponse ?? new byte[8]);
            }

            public Task CloseAsync()
            {
                CloseCalled = true;
                return Task.CompletedTask;
            }

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        #endregion
    }
}
#nullable restore
