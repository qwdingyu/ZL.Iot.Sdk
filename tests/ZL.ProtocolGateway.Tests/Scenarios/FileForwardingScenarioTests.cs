using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Scenarios
{
    public class FileForwardingScenarioTests : IDisposable
    {
        private TcpListener _echoServer;
        private CancellationTokenSource _echoCts;

        public void Dispose()
        {
            _echoCts?.Cancel();
            _echoServer?.Stop();
        }

        [Fact]
        public async Task FileInput_AppendedLine_ForwardsToTcpOutput()
        {
            var receivedMessages = new List<string>();
            var echoPort = 18110;
            StartEchoServer(echoPort, receivedMessages);
            await Task.Delay(150);

            var inputFile = Path.Combine(Path.GetTempPath(), $"protocol-gateway-input-{Guid.NewGuid():N}.log");
            await File.WriteAllTextAsync(inputFile, string.Empty);

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
            gateway.AddInput(new FileInputPlugin(new FileInputConfig
            {
                FilePath = inputFile,
                PollIntervalMs = 100,
                Delimiter = Encoding.UTF8.GetBytes("\n")
            }));

            await gateway.StartAsync();
            await Task.Delay(400);

            await File.AppendAllTextAsync(inputFile, "D100=42\n");
            await Task.Delay(800);

            Assert.NotEmpty(receivedMessages);
            Assert.Contains("D100=42", string.Join("", receivedMessages));

            await gateway.StopAsync();
        }

        private void StartEchoServer(int port, List<string> receivedMessages)
        {
            _echoCts = new CancellationTokenSource();
            _echoServer = new TcpListener(System.Net.IPAddress.Loopback, port);
            _echoServer.Start();

            _ = Task.Run(async () =>
            {
                while (!_echoCts.Token.IsCancellationRequested)
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
                                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, _echoCts.Token);
                                    if (read == 0) break;
                                    receivedMessages.Add(Encoding.UTF8.GetString(buffer, 0, read));
                                }
                            }
                        }, _echoCts.Token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }, _echoCts.Token);
        }
    }
}
