using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Plugins;

namespace ModbusTcpQuickStart;

internal static class Program
{
    private static async Task Main()
    {
        using var cts = new CancellationTokenSource();
        int port = GetFreeTcpPort();
        var serverCaptured = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var serverTask = RunFakeModbusServerAsync(listener, serverCaptured, cts.Token);

        Console.WriteLine("=== ProtocolGateway Demo: JSON -> Modbus TCP ===");
        Console.WriteLine($"Fake Modbus Server : 127.0.0.1:{port}");
        Console.WriteLine("Scenario           : 将统一 JSON 写请求转换成 Modbus TCP 写寄存器报文");
        Console.WriteLine();

        using var output = new ModbusTcpOutputPlugin(new ModbusTcpOutputConfig
        {
            Name = "modbus-tcp-output",
            ServerIp = "127.0.0.1",
            Port = port,
            UnitId = 1,
            TimeoutMs = 3000
        });

        var pipeline = new MessagePipeline();
        pipeline.RegisterOutput(output);
        pipeline.AddRouter(new RouteRule
        {
            Name = "all-to-modbus",
            Condition = _ => true,
            OutputNames = { "modbus-tcp-output" },
            ContinueMatching = false
        });

        var message = new Message
        {
            Topic = "plc/write/register",
            Timestamp = DateTime.Now,
            Metadata =
            {
                ["Protocol"] = "Json",
                ["DeviceId"] = "PLC-LINE-A-01"
            }
        };
        message.SetJsonContent("{\"operation\":\"write\",\"registers\":[{\"address\":\"40001\",\"value\":\"123\"}]}");

        await pipeline.StartAsync(cts.Token);
        await pipeline.ProcessAsync(message);
        await pipeline.StopAsync();

        var request = await serverCaptured.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();
        await serverTask;

        Console.WriteLine("[Input JSON]");
        Console.WriteLine(message.GetJsonContent());
        Console.WriteLine();
        Console.WriteLine("[Captured Modbus TCP Frame]");
        Console.WriteLine(BitConverter.ToString(request));
        Console.WriteLine();
        Console.WriteLine("[Decoded Meaning]");
        Console.WriteLine($"Function Code : 0x{request[7]:X2} (06 = Write Single Register)");
        Console.WriteLine($"Address       : {BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(8, 2))}");
        Console.WriteLine($"Value         : {BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10, 2))}");
        Console.WriteLine();
        Console.WriteLine("Result: 该 demo 展示了 ProtocolGateway 可以把统一消息直接桥接为工业现场可消费的 Modbus TCP 写报文。");
    }

    private static async Task RunFakeModbusServerAsync(TcpListener listener, TaskCompletionSource<byte[]> captured, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            using var stream = client.GetStream();
            var request = await ReadModbusFrameAsync(stream, cancellationToken);
            captured.TrySetResult(request);
            await stream.WriteAsync(request, 0, request.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
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
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer, offset, length - offset, cancellationToken);
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
