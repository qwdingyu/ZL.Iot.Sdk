using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Plugins;

namespace QuickStartInMemory;

/// <summary>
/// 展示最小闭环：Message -> Pipeline Transformer (JSON 字段映射) -> FileOutputPlugin
/// 不再使用已移除的 IProtocolConverter，改用 Pipeline.AddTransformer。
/// </summary>
internal static class Program
{
    private static async Task Main()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "artifacts", "quickstart-output.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        Console.WriteLine("=== ProtocolGateway QuickStart: JSON Transform -> File ===");
        Console.WriteLine("这个 demo 不需要打开端口、不需要数据库、不需要外部服务。它展示最小闭环：Message -> Pipeline Transformer -> FileOutputPlugin。");
        Console.WriteLine();

        var input = new Message
        {
            Topic = "plc/line-a/temperature",
            Metadata =
            {
                ["Protocol"] = "Json",
                ["DeviceId"] = "PLC-LINE-A-01"
            }
        };
        input.SetJsonContent("{\"device\":\"PLC-LINE-A-01\",\"temperature\":36.8,\"pressure\":1.24}");

        var pipeline = new MessagePipeline();
        pipeline.RegisterOutput(new FileOutputPlugin(new FileOutputConfig
        {
            Name = "json-file-output",
            FilePath = outputPath,
            Append = true,
            EncodingName = "UTF-8"
        }));

        // 使用 Pipeline Transformer 实现 JSON 字段重命名映射
        pipeline.AddTransformer(async message =>
        {
            if (message.ContentType != "json")
                return message;

            try
            {
                using var doc = JsonDocument.Parse(message.GetJsonContent());
                var root = doc.RootElement;
                var output = new JsonObject();

                // 字段映射：device -> deviceId, temperature -> temperatureCelsius, pressure -> pressureMpa
                var fieldMapping = new Dictionary<string, string>
                {
                    ["device"] = "deviceId",
                    ["temperature"] = "temperatureCelsius",
                    ["pressure"] = "pressureMpa"
                };

                foreach (var mapping in fieldMapping)
                {
                    if (root.TryGetProperty(mapping.Key, out var value))
                    {
                        output[mapping.Value] = JsonNode.Parse(value.GetRawText());
                    }
                }

                output["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var jsonContent = output.ToJsonString();
                message.SetJsonContent(jsonContent);
                message.Metadata["Protocol"] = "NormalizedJson";
            }
            catch
            {
                // JSON 解析失败，保持原样
            }

            return message;
        });

        pipeline.AddRouter(new RouteRule
        {
            Name = "all-normalized-json-to-file",
            Condition = message => message.ContentType == "json",
            OutputNames = { "json-file-output" },
            ContinueMatching = false
        });

        await pipeline.StartAsync();
        await pipeline.ProcessAsync(input);
        await pipeline.StopAsync();

        Console.WriteLine("[Input JSON]");
        Console.WriteLine("{\"device\":\"PLC-LINE-A-01\",\"temperature\":36.8,\"pressure\":1.24}");
        Console.WriteLine();
        Console.WriteLine("[Output File]");
        Console.WriteLine(outputPath);
        Console.WriteLine(await File.ReadAllTextAsync(outputPath));
        Console.WriteLine("Result: 5 分钟内即可理解统一消息、Pipeline Transformer、路由、输出插件的核心模型。");
    }
}
