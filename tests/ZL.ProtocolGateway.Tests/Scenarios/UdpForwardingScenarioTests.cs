using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Scenarios
{
    public class UdpForwardingScenarioTests
    {
        [Fact]
        public async Task UdpOutputPlugin_SendsDatagram_ListenerReceivesPayload()
        {
            int port = GetFreeUdpPort();
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));

            var plugin = new UdpOutputPlugin(new UdpOutputConfig
            {
                Name = "udp-out-test",
                ServerIp = "127.0.0.1",
                Port = port
            });

            await plugin.StartAsync();
            var msg = new Message();
            msg.SetTextContent("PLCSIM_UDP_OUT");
            await plugin.SendAsync(msg);

            UdpReceiveResult result = await listener.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
            string text = Encoding.UTF8.GetString(result.Buffer);
            Assert.Equal("PLCSIM_UDP_OUT", text);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task UdpInputPlugin_ReceivesDatagram_HandlerGetsMessage()
        {
            int port = GetFreeUdpPort();
            var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);

            var plugin = new UdpInputPlugin(new UdpInputConfig
            {
                Name = "udp-in-test",
                LocalIp = "127.0.0.1",
                Port = port
            });

            await plugin.StartAsync(msg =>
            {
                tcs.TrySetResult(msg);
                return Task.CompletedTask;
            });

            using (var sender = new UdpClient())
            {
                byte[] payload = Encoding.UTF8.GetBytes("PLCSIM_UDP_IN");
                await sender.SendAsync(payload, payload.Length, "127.0.0.1", port);
            }

            Message received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("Udp", received.Metadata["Protocol"]);
            Assert.Contains("127.0.0.1", received.Metadata["Source"]);
            Assert.Equal("PLCSIM_UDP_IN", Encoding.UTF8.GetString(received.Payload));

            await plugin.StopAsync();
        }

        private static int GetFreeUdpPort()
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        }
    }
}
