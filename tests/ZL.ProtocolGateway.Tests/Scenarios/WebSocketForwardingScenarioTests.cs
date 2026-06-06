using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Scenarios
{
    public class WebSocketForwardingScenarioTests : IDisposable
    {
        private TcpListener _webSocketServer;
        private TcpListener _echoServer;
        private CancellationTokenSource _cts;

        public void Dispose()
        {
            _cts?.Cancel();
            _webSocketServer?.Stop();
            _echoServer?.Stop();
        }

        [Fact]
        public async Task WebSocketInput_ReceivesFrame_ForwardsToTcpOutput()
        {
            var receivedMessages = new List<string>();
            var wsPort = 18120;
            var echoPort = 18121;

            StartEchoServer(echoPort, receivedMessages);
            StartWebSocketServer(wsPort, "HELLO_WS");
            await Task.Delay(150);

            var pipeline = new ResilientMessagePipeline();
            var output = new TcpOutputPlugin(new TcpOutputConfig
            {
                Name = "EchoTarget",
                ServerIp = "127.0.0.1",
                Port = echoPort
            });
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = _ => true, OutputNames = { output.Name } });

            var gateway = new GatewayService(pipeline);
            gateway.AddInput(new WebSocketInputPlugin(new WebSocketInputConfig
            {
                Url = $"ws://127.0.0.1:{wsPort}/ws",
                ReconnectIntervalMs = 5000
            }));

            await gateway.StartAsync();
            await Task.Delay(1200);

            Assert.NotEmpty(receivedMessages);
            Assert.Contains("HELLO_WS", string.Join("", receivedMessages));

            await gateway.StopAsync();
        }

        private void StartWebSocketServer(int port, string payload)
        {
            _cts = new CancellationTokenSource();
            _webSocketServer = new TcpListener(IPAddress.Loopback, port);
            _webSocketServer.Start();

            _ = Task.Run(async () =>
            {
                using (var client = await _webSocketServer.AcceptTcpClientAsync())
                using (var stream = client.GetStream())
                {
                    var request = await ReadHttpHeadersAsync(stream, _cts.Token);
                    var webSocketKey = ExtractWebSocketKey(request);
                    await WriteHandshakeResponseAsync(stream, webSocketKey, _cts.Token);
                    await WriteTextFrameAsync(stream, payload, _cts.Token);
                    await Task.Delay(200, _cts.Token);
                }
            }, _cts.Token);
        }

        private void StartEchoServer(int port, List<string> receivedMessages)
        {
            if (_cts == null)
            {
                _cts = new CancellationTokenSource();
            }

            _echoServer = new TcpListener(IPAddress.Loopback, port);
            _echoServer.Start();

            _ = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _echoServer.AcceptTcpClientAsync();
                        _ = Task.Run(async () =>
                        {
                            using (client)
                            using (var stream = client.GetStream())
                            {
                                var buffer = new byte[1024];
                                while (client.Connected)
                                {
                                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                                    if (read == 0) break;
                                    receivedMessages.Add(Encoding.UTF8.GetString(buffer, 0, read));
                                }
                            }
                        }, _cts.Token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }, _cts.Token);
        }

        private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[1];
                int matched = 0;
                while (matched < 4)
                {
                    int read = await stream.ReadAsync(buffer, 0, 1, cancellationToken);
                    if (read == 0)
                    {
                        throw new IOException("WebSocket handshake aborted");
                    }

                    ms.WriteByte(buffer[0]);
                    matched = buffer[0] switch
                    {
                        (byte)'\r' when matched == 0 || matched == 2 => matched + 1,
                        (byte)'\n' when matched == 1 || matched == 3 => matched + 1,
                        _ => 0
                    };
                }

                return Encoding.ASCII.GetString(ms.ToArray());
            }
        }

        private static string ExtractWebSocketKey(string requestText)
        {
            var lines = requestText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring("Sec-WebSocket-Key:".Length).Trim();
                }
            }

            throw new InvalidOperationException("Missing Sec-WebSocket-Key");
        }

        private static async Task WriteHandshakeResponseAsync(NetworkStream stream, string webSocketKey, CancellationToken cancellationToken)
        {
            var acceptKey = Convert.ToBase64String(SHA1.Create().ComputeHash(
                Encoding.ASCII.GetBytes(webSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

            var response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

            var bytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static async Task WriteTextFrameAsync(NetworkStream stream, string payload, CancellationToken cancellationToken)
        {
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x81);
                ms.WriteByte((byte)payloadBytes.Length);
                ms.Write(payloadBytes, 0, payloadBytes.Length);
                var bytes = ms.ToArray();
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
    }
}
