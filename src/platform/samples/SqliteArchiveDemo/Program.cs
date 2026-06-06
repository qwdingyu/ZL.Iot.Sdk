using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Plugins;

namespace SqliteArchiveDemo;

internal static class Program
{
    private static async Task Main()
    {
        var dataRoot = Path.Combine(AppContext.BaseDirectory, "artifacts");
        var dbPath = Path.Combine(dataRoot, "gateway-demo.db");
        Directory.CreateDirectory(dataRoot);

        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }

        Console.WriteLine("=== ProtocolGateway Demo: JSON -> SQLite Archive ===");
        Console.WriteLine($"SQLite File : {dbPath}");
        Console.WriteLine("Scenario    : 将统一消息写入 SQLite，模拟现场归档/追溯场景");
        Console.WriteLine();

        var message = new Message
        {
            Topic = "workshop/line-01/station-03",
            Timestamp = DateTime.Now,
            Metadata =
            {
                ["Protocol"] = "Json",
                ["DeviceId"] = "LINE01-ST03",
                ["BatchNo"] = "BATCH-20260419-01",
                ["Operator"] = "robot-arm-a"
            }
        };
        message.SetJsonContent(JsonSerializer.Serialize(new
        {
            deviceId = "LINE01-ST03",
            productCode = "P-AXIS-001",
            quality = "PASS",
            torque = 18.6,
            temperature = 42.3,
            collectedAt = DateTimeOffset.Now
        }));

        using var sqliteOutput = new SqliteOutputPlugin(new SqliteOutputConfig
        {
            Name = "sqlite-archive",
            ConnectionString = $"DataSource={dbPath}",
            TableName = "gateway_messages",
            AutoCreateDatabase = true
        });

        var pipeline = new MessagePipeline();
        pipeline.RegisterOutput(sqliteOutput);
        pipeline.AddRouter(new RouteRule
        {
            Name = "archive-all-json",
            Condition = _ => true,
            OutputNames = { "sqlite-archive" },
            ContinueMatching = false
        });

        await pipeline.StartAsync();
        await pipeline.ProcessAsync(message);
        await pipeline.StopAsync();

        Console.WriteLine("[Archived Message]");
        Console.WriteLine(message.GetJsonContent());
        Console.WriteLine();
        Console.WriteLine("[Verification]");
        Console.WriteLine(File.Exists(dbPath)
            ? "SQLite 数据库文件已生成，可用于后续查询/追溯。"
            : "SQLite 数据库文件未生成。");
        Console.WriteLine();
        Console.WriteLine("Result: 该 demo 展示了 ProtocolGateway 不只是转发器，也可以作为工业数据归档与审计留痕底座。");
    }
}
