using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class ModbusRtuOutputPluginTests
    {
        [Fact]
        public async Task SendAsync_UsesInjectedTransportAndWritesFrame()
        {
            var transport = new FakeModbusRtuTransport();
            var plugin = new ModbusRtuOutputPlugin(new ModbusRtuOutputConfig
            {
                PortName = "COM_TEST",
                UnitId = 1
            }, transport);

            await plugin.StartAsync();
            var message = new Message();
            message.SetJsonContent(@"{""registers"":[{""address"":""40001"",""value"":""77""}]}");
            await plugin.SendAsync(message);
            await plugin.StopAsync();

            Assert.Single(transport.Requests);
            Assert.Equal((byte)0x06, transport.Requests[0][1]);
        }

        private sealed class FakeModbusRtuTransport : IModbusRtuTransport
        {
            public List<byte[]> Requests { get; } = new();

            public Task OpenAsync(ModbusRtuOutputConfig config, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<byte[]> SendAndReceiveAsync(byte[] request, int timeoutMs, CancellationToken cancellationToken = default)
            {
                Requests.Add((byte[])request.Clone());
                return Task.FromResult((byte[])request.Clone());
            }

            public Task CloseAsync() => Task.CompletedTask;

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
