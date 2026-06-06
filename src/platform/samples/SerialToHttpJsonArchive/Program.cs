using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Plugins;

namespace SerialToHttpJsonArchive;

/// <summary>
/// 展示 ProtocolGateway 的核心价值：
/// 1. 模拟串口上报原始 CSV 测量数据
/// 2. 通过 Pipeline Transformer 转成结构化 JSON
/// 3. 同时路由到 HTTP 接口与本地归档文件
/// 4. 输出结构化结果，便于验证桥接链路
/// </summary>
internal static class Program
{
    private static async Task Main()
    {
        using var cts = new CancellationTokenSource();
        using var listener = new HttpListener();

        var httpPort = 18081;
        var listenPrefix = $"http://127.0.0.1:{httpPort}/ingest/";
        var archivePath = Path.Combine(AppContext.BaseDirectory, "artifacts", "serial-http-archive.log");

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        listener.Prefixes.Add(listenPrefix);
        listener.Start();

        var captureTask = CaptureSingleRequestAsync(listener, cts.Token);

        Console.WriteLine("=== ProtocolGateway Demo: Serial CSV -> JSON -> HTTP + File Archive ===");
        Console.WriteLine($"HTTP Endpoint : {listenPrefix}");
        Console.WriteLine($"Archive File  : {archivePath}");
        Console.WriteLine("Raw Input     : SN-1001,23.5,24.1,22.8");
        Console.WriteLine();

        var sourceMessage = new Message
        {
            Topic = "COM3",
            Timestamp = DateTime.Now,
            Metadata =
            {
                ["Protocol"] = "Serial",
                ["PortName"] = "COM3",
                ["DeviceType"] = "TemperatureSensor"
            }
        };
        sourceMessage.SetTextContent("SN-1001,23.5,24.1,22.8");

        var pipeline = new MessagePipeline();
        pipeline.RegisterOutput(new HttpOutputPlugin(new HttpOutputConfig
        {
            Name = "http-api",
            Url = listenPrefix,
            Method = "POST",
            ContentType = "application/json",
            Timeout = 5000,
            Headers = new Dictionary<string, string>
            {
                ["X-Demo-Scenario"] = "serial-to-http-json-archive"
            }
        }));
        pipeline.RegisterOutput(new FileOutputPlugin(new FileOutputConfig
        {
            Name = "local-archive",
            FilePath = archivePath,
            Append = true,
            EncodingName = "UTF-8"
        }));

        // 使用 Pipeline Transformer 实现 CSV -> JSON 转换（替代已移除的 SerialToHttpConverter）
        pipeline.AddTransformer(async message =>
        {
            if (message.Metadata.TryGetValue("Protocol", out var proto) && proto == "Serial")
            {
                var rawData = message.GetTextContent();
                var parts = rawData?.Split(',');

                if (parts != null && parts.Length >= 2)
                {
                    var payload = new JsonObject
                    {
                        ["sn"] = parts[0].Trim(),
                        ["stationNo"] = "STATION-A01",
                        ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["values"] = null!
                    };

                    var values = new JsonArray();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        if (double.TryParse(parts[i].Trim(), out var value))
                        {
                            values.Add(new JsonObject
                            {
                                ["index"] = i,
                                ["value"] = value
                            });
                        }
                    }
                    payload["values"] = values;

                    var jsonContent = payload.ToJsonString();
                    message.SetJsonContent(jsonContent);
                    message.Metadata["Protocol"] = "BridgeResult";
                    message.Metadata["X-Device-Id"] = "device-sn-" + parts[0].Trim();
                }
            }

            return message;
        });

        pipeline.AddRouter(new RouteRule
        {
            Name = "fanout-http-and-file",
            Condition = _ => true,
            OutputNames = { "http-api", "local-archive" },
            ContinueMatching = false
        });

        await pipeline.StartAsync(cts.Token);
        await pipeline.ProcessAsync(sourceMessage);

        var httpPayload = await captureTask;
        await pipeline.StopAsync();

        cts.Cancel();
        listener.Stop();

        Console.WriteLine("[Converted JSON]");
        Console.WriteLine(sourceMessage.GetJsonContent());
        Console.WriteLine();
        Console.WriteLine("[HTTP Received Payload]");
        Console.WriteLine(httpPayload);
        Console.WriteLine();
        Console.WriteLine("[Archive File Content]");
        Console.WriteLine(await File.ReadAllTextAsync(archivePath));
        Console.WriteLine();
        Console.WriteLine("Result: 一个输入样本被桥接到 HTTP 与本地归档，体现协议转换、路由分发与审计留痕价值。");
    }

    private static async Task<string> CaptureSingleRequestAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        var context = await listener.GetContextAsync();
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        context.Response.StatusCode = 200;
        var responseBytes = Encoding.UTF8.GetBytes("ok");
        await context.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
        context.Response.Close();

        return body;
    }
}
