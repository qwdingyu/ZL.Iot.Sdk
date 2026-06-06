using System;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Scenarios
{
    public class SqliteForwardingScenarioTests
    {
        [Fact]
        public async Task SqliteOutputPlugin_InsertsMessageIntoConfiguredTable()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"protocol-gateway-{Guid.NewGuid():N}.db");
            var plugin = new DatabaseOutputPlugin(new DatabaseOutputConfig
            {
                Provider = "Sqlite",
                ConnectionString = $"DataSource={dbPath}",
                TableName = "gateway_messages"
            });

            await plugin.StartAsync();
            await plugin.SendAsync(new Message
            {
                Topic = "plc/watch/test",
                ContentType = "json",
                Timestamp = DateTime.Now,
                Payload = System.Text.Encoding.UTF8.GetBytes(@"{""value"":1}"),
                Metadata = { ["Protocol"] = "UnitTest" }
            });
            await plugin.StopAsync();

            using var connection = new SqliteConnection($"DataSource={dbPath}");
            await connection.OpenAsync();
            var count = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM gateway_messages");
            Assert.Equal(1, count);
        }
    }
}
