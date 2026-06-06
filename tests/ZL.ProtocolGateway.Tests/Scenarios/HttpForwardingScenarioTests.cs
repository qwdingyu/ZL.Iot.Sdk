using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Scenarios
{
    public class HttpForwardingScenarioTests : System.IDisposable
    {
        private TcpListener _echoServer;
        private CancellationTokenSource _echoCts;

        public void Dispose()
        {
            _echoCts?.Cancel();
            _echoServer?.Stop();
        }

        [Fact]
        public async Task HttpInput_PostBody_ForwardsToTcpOutput()
        {
            var receivedMessages = new List<string>();
            var echoPort = 18140;
            StartEchoServer(echoPort, receivedMessages);
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
            gateway.AddInput(new HttpInputPlugin(new HttpInputConfig
            {
                Port = 18141,
                PathPrefix = "/webhook/"
            }));

            await gateway.StartAsync();
            await Task.Delay(400);

            using (var client = new HttpClient())
            {
                var content = new StringContent("{\"station\":\"A1\",\"value\":88}", Encoding.UTF8, "application/json");
                var response = await client.PostAsync("http://127.0.0.1:18141/webhook/data", content);
                Assert.True(response.IsSuccessStatusCode);
            }

            await Task.Delay(800);
            Assert.NotEmpty(receivedMessages);
            Assert.Contains("\"station\":\"A1\"", string.Join("", receivedMessages));
            Assert.Contains("\"value\":88", string.Join("", receivedMessages));

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
