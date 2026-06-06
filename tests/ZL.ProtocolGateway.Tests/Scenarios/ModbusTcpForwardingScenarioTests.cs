using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Scenarios
{
    public class ModbusTcpForwardingScenarioTests : IDisposable
    {
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public void Dispose()
        {
            _cts?.Cancel();
            _listener?.Stop();
        }

        [Fact]
        public async Task ModbusTcpOutputPlugin_WriteHoldingRegister_SendsFunction06()
        {
            int port = GetFreeTcpPort();
            var captured = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            StartModbusServer(port, captured);

            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig
            {
                ServerIp = "127.0.0.1",
                Port = port,
                UnitId = 1
            });

            await plugin.StartAsync();
            var msg = new Message();
            msg.SetJsonContent(@"{""operation"":""write"",""registers"":[{""address"":""40001"",""value"":""123""}]}");
            await plugin.SendAsync(msg);
            await plugin.StopAsync();

            var request = await captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(0x06, request[7]);
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(8, 2)));
            Assert.Equal((ushort)123, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10, 2)));
        }

        [Fact]
        public async Task ModbusTcpOutputPlugin_WriteCoil_SendsFunction05()
        {
            int port = GetFreeTcpPort();
            var captured = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            StartModbusServer(port, captured);

            var plugin = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig
            {
                ServerIp = "127.0.0.1",
                Port = port,
                UnitId = 1
            });

            await plugin.StartAsync();
            var msg = new Message();
            msg.SetJsonContent(@"{""operation"":""write"",""registers"":[{""address"":""M10"",""value"":""true""}]}");
            await plugin.SendAsync(msg);
            await plugin.StopAsync();

            var request = await captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(0x05, request[7]);
            Assert.Equal((ushort)9, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(8, 2)));
            Assert.Equal((ushort)0xFF00, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10, 2)));
        }

        private void StartModbusServer(int port, TaskCompletionSource<byte[]> captured)
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();

            _ = Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                using var stream = client.GetStream();
                var request = await ReadModbusFrameAsync(stream, _cts.Token);
                captured.TrySetResult(request);
                await stream.WriteAsync(request, 0, request.Length, _cts.Token);
                await stream.FlushAsync(_cts.Token);
            }, _cts.Token);
        }

        private static async Task<byte[]> ReadModbusFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var header = await ReadExactAsync(stream, 7, cancellationToken);
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
            var body = await ReadExactAsync(stream, length - 1, cancellationToken);
            var frame = new byte[7 + body.Length];
            Buffer.BlockCopy(header, 0, frame, 0, 7);
            Buffer.BlockCopy(body, 0, frame, 7, body.Length);
            return frame;
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
        {
            var buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset, cancellationToken);
                if (read == 0)
                {
                    throw new InvalidOperationException("Unexpected Modbus TCP stream close");
                }

                offset += read;
            }

            return buffer;
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
