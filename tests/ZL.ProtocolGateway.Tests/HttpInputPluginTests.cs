using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class HttpInputPluginTests
    {
        [Fact]
        public async Task HttpInputPlugin_PostJson_HandlerReceivesJsonMessage()
        {
            var messages = new List<Message>();
            var plugin = new HttpInputPlugin(new HttpInputConfig
            {
                Port = 18130,
                PathPrefix = "/ingest"
            });

            await plugin.StartAsync(msg =>
            {
                messages.Add(msg);
                return Task.CompletedTask;
            });

            using (var client = new HttpClient())
            {
                var content = new StringContent(@"{""value"":42}", Encoding.UTF8, "application/json");
                var response = await client.PostAsync("http://127.0.0.1:18130/ingest/data", content);
                Assert.True(response.IsSuccessStatusCode);
            }

            await Task.Delay(300);
            await plugin.StopAsync();

            Assert.Single(messages);
            Assert.Equal("/ingest/data", messages[0].Topic);
            Assert.Equal("json", messages[0].ContentType);
            Assert.Equal("{\"value\":42}", Encoding.UTF8.GetString(messages[0].Payload));
            Assert.Equal("POST", messages[0].Metadata["Method"]);
            Assert.Equal("Http", messages[0].Metadata["Protocol"]);
        }

        [Fact]
        public async Task HttpInputPlugin_GetRequest_ReturnsMethodNotAllowed()
        {
            var messages = new List<Message>();
            var plugin = new HttpInputPlugin(new HttpInputConfig
            {
                Port = 18131,
                PathPrefix = "/ingest"
            });

            await plugin.StartAsync(msg =>
            {
                messages.Add(msg);
                return Task.CompletedTask;
            });

            using (var client = new HttpClient())
            {
                var response = await client.GetAsync("http://127.0.0.1:18131/ingest");
                Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, response.StatusCode);
            }

            await Task.Delay(200);
            await plugin.StopAsync();

            Assert.Empty(messages);
        }

        [Fact]
        public async Task FileInputPlugin_ReadFromEnd_OnlyProcessesNewLines()
        {
            var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"file-input-{System.Guid.NewGuid():N}.log");
            await System.IO.File.WriteAllTextAsync(filePath, "history-1\nhistory-2\n");

            var messages = new List<Message>();
            var plugin = new FileInputPlugin(new FileInputConfig
            {
                FilePath = filePath,
                PollIntervalMs = 100,
                ReadFromEnd = true
            });

            await plugin.StartAsync(msg =>
            {
                messages.Add(msg);
                return Task.CompletedTask;
            });

            await Task.Delay(250);
            Assert.Empty(messages);

            await System.IO.File.AppendAllTextAsync(filePath, "tail-1\n");
            await Task.Delay(400);
            await plugin.StopAsync();

            Assert.Single(messages);
            Assert.Equal("tail-1\n", Encoding.UTF8.GetString(messages[0].Payload));
        }
    }
}
