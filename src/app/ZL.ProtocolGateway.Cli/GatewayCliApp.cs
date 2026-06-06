using System.Text.Json;
using System.Text.Json.Nodes;
using ZL.ProtocolGateway.Plugins;

namespace ZL.ProtocolGateway.Cli;

public static class GatewayCliApp
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = GatewayCliOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            if (options.ListScenarios)
            {
                PrintScenarioList();
                return 0;
            }

            GatewayCliRequest request;
            if (!string.IsNullOrWhiteSpace(options.ConfigPath))
            {
                request = GatewayCliRequestLoader.LoadFromFile(options.ConfigPath!);
                GatewayCliRequestFactory.ApplyOverrides(request, options);
            }
            else if (!string.IsNullOrWhiteSpace(options.ScenarioId))
            {
                request = GatewayCliRequestFactory.FromOptions(options);
            }
            else
            {
                request = GatewayInteractivePrompt.BuildInteractiveRequest();
            }

            GatewayCliRequestFactory.Normalize(request);

            if (options.DoctorOnly)
            {
                var report = GatewayCliDoctorRunner.Run(request, options);
                PrintDoctorReport(report);
                return report.HasErrors ? 2 : 0;
            }

            GatewayCliRequestValidator.Validate(request);

            if (!string.IsNullOrWhiteSpace(options.SaveConfigPath))
            {
                var savedPath = GatewayCliRequestLoader.SaveToFile(request, options.SaveConfigPath!);
                Console.WriteLine($"[Config] 已导出标准化配置: {savedPath}");
            }

            PrintRunSummary(request, options);

            if (options.ValidateOnly)
            {
                Console.WriteLine("[Validate] 配置校验通过。未执行运行时预检与网关启动。");
                return 0;
            }

            if (options.PrecheckOnly)
            {
                var report = GatewayCliPrecheckRunner.Run(request);
                PrintPrecheckReport(report);
                return report.HasErrors ? 2 : 0;
            }

            if (options.DryRun)
            {
                PrintDryRunConfig(request);
                Console.WriteLine("[DryRun] 已跳过 GatewayService 实际启动，仅输出最终配置与模拟摘要。");
                return 0;
            }

            await GatewaySimulationRunner.RunAsync(request, cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProtocolGateway.Cli] 运行失败: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ProtocolGateway.Cli - 无头协议转换 Host");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj");
        Console.WriteLine("  dotnet run --project ... -- --list-scenarios");
        Console.WriteLine("  dotnet run --project ... -- --scenario serial-to-http --payload \"SN123,10.5,20.3\" --station-no ST-01 --target-url https://example/api");
        Console.WriteLine("  dotnet run --project ... -- --config src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/serial-to-http.json");
        Console.WriteLine();
        Console.WriteLine("常用参数:");
        Console.WriteLine("  --list-scenarios                列出内置体验场景");
        Console.WriteLine("  --scenario <id>                 指定场景 ID");
        Console.WriteLine("  --config <file>                 从 JSON 配置文件加载");
        Console.WriteLine("  --payload <value>               直接输入载荷文本/JSON/HEX");
        Console.WriteLine("  --payload-file <file>           从文件读取输入载荷");
        Console.WriteLine("  --content-type <text|json|hex>  指定输入内容类型");
        Console.WriteLine("  --topic <value>                 指定消息主题/路径");
        Console.WriteLine("  --output <console|file>         指定输出方式");
        Console.WriteLine("  --output-file <file>            文件输出路径");
        Console.WriteLine("  --dry-run                       只打印最终配置与模拟摘要，不启动网关");
        Console.WriteLine("  --validate                      仅做配置标准化与校验，不执行预检和启动");
        Console.WriteLine("  --precheck                      执行运行前预检，输出风险与阻断项");
        Console.WriteLine("  --doctor                        聚合 validate + precheck 结果并输出诊断摘要");
        Console.WriteLine("  --save-config <file>            导出标准化后的 JSON 配置");
        Console.WriteLine("  --station-no <value>            Serial->HTTP 场景参数");
        Console.WriteLine("  --target-url <value>            Serial->HTTP 场景参数");
        Console.WriteLine("  --mapping <a=b;c=d>             JSON 字段映射参数");
    }

    private static void PrintScenarioList()
    {
        Console.WriteLine("可用内置场景:");
        foreach (var scenario in GatewayScenarioCatalog.GetBuiltInScenarios())
        {
            Console.WriteLine($"  [{scenario.Order}] {scenario.Id} - {scenario.Name}");
            Console.WriteLine($"      {scenario.Description}");
            Console.WriteLine($"      常见参数: {string.Join("; ", scenario.ParameterHints)}");
        }
    }

    private static void PrintRunSummary(GatewayCliRequest request, GatewayCliOptions options)
    {
        Console.WriteLine("=== ProtocolGateway CLI 模拟摘要 ===");
        Console.WriteLine($"SchemaVersion: {request.SchemaVersion}");
        Console.WriteLine($"场景: {request.Scenario}");
        Console.WriteLine($"输入模式: {request.Source.Mode}");
        Console.WriteLine($"内容类型: {request.Source.ContentType}");
        Console.WriteLine($"源协议: {request.Source.Protocol}");
        Console.WriteLine($"输出模式: {request.Output.Mode}");
        Console.WriteLine($"DryRun: {(options.DryRun ? "Yes" : "No")}");

        if (!string.IsNullOrWhiteSpace(request.Source.PayloadFile))
        {
            Console.WriteLine($"输入文件: {request.Source.PayloadFile}");
        }

        if (!string.IsNullOrWhiteSpace(request.Output.FilePath))
        {
            Console.WriteLine($"输出文件: {request.Output.FilePath}");
        }

        Console.WriteLine("===================================");
    }

    private static void PrintDryRunConfig(GatewayCliRequest request)
    {
        Console.WriteLine();
        Console.WriteLine("--- 标准化 JSON 配置预览 ---");
        Console.WriteLine(GatewayCliRequestLoader.Serialize(request));
        Console.WriteLine("----------------------------");
        Console.WriteLine();
    }

    private static void PrintPrecheckReport(GatewayCliPrecheckReport report)
    {
        Console.WriteLine();
        Console.WriteLine("--- Gateway 运行前预检 ---");
        foreach (var item in report.Items)
        {
            Console.WriteLine($"[{item.Level}] {item.Title}: {item.Message}");
        }

        Console.WriteLine(report.HasErrors
            ? "[Precheck] 存在阻断项，请修复后再启动。"
            : "[Precheck] 未发现阻断项，可以继续 dry-run 或实际启动。");
        Console.WriteLine("-------------------------");
        Console.WriteLine();
    }

    private static void PrintDoctorReport(GatewayCliDoctorReport report)
    {
        Console.WriteLine();
        Console.WriteLine("=== Gateway Doctor Report ===");
        Console.WriteLine($"SchemaVersion: {report.SchemaVersion}");
        Console.WriteLine($"Scenario: {report.Scenario}");
        Console.WriteLine($"ConfigSource: {report.ConfigSource}");
        Console.WriteLine($"WorkingDirectory: {report.WorkingDirectory}");
        Console.WriteLine($"Validation: {(report.ValidationPassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Summary: Errors={report.ErrorCount}, Warnings={report.WarningCount}, Info={report.InfoCount}");
        if (report.TitleSummaries.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("--- Summary By Title ---");
            foreach (var summary in report.TitleSummaries)
            {
                Console.WriteLine($"- {summary}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("--- Precheck Items ---");
        foreach (var item in report.Items)
        {
            Console.WriteLine($"[{item.Level}] {item.Title}: {item.Message}");
        }

        if (report.Recommendations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("--- Recommendations ---");
            foreach (var recommendation in report.Recommendations)
            {
                Console.WriteLine($"- {recommendation}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(report.HasErrors
            ? "[Doctor] 检测到阻断项，暂不建议启动。"
            : "[Doctor] 未检测到阻断项，可继续 dry-run、precheck 或实际启动。");
        Console.WriteLine("==============================");
        Console.WriteLine();
    }
}

public sealed class GatewayCliOptions
{
    public string? ScenarioId { get; private set; }
    public string? ConfigPath { get; private set; }
    public bool ListScenarios { get; private set; }
    public bool ShowHelp { get; private set; }
    public string? Payload { get; private set; }
    public string? PayloadFile { get; private set; }
    public string? ContentType { get; private set; }
    public string? Topic { get; private set; }
    public string? SourceProtocol { get; private set; }
    public string? OutputMode { get; private set; }
    public string? OutputFile { get; private set; }
    public string? StationNo { get; private set; }
    public string? TargetUrl { get; private set; }
    public string? Mapping { get; private set; }
    public bool DryRun { get; private set; }
    public bool ValidateOnly { get; private set; }
    public bool PrecheckOnly { get; private set; }
    public bool DoctorOnly { get; private set; }
    public string? SaveConfigPath { get; private set; }

    public static GatewayCliOptions Parse(string[] args)
    {
        var options = new GatewayCliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            string NextValue()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"参数 {current} 缺少值。");
                }

                i++;
                return args[i];
            }

            switch (current)
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--list-scenarios":
                    options.ListScenarios = true;
                    break;
                case "--scenario":
                    options.ScenarioId = NextValue();
                    break;
                case "--config":
                    options.ConfigPath = NextValue();
                    break;
                case "--payload":
                    options.Payload = NextValue();
                    break;
                case "--payload-file":
                    options.PayloadFile = NextValue();
                    break;
                case "--content-type":
                    options.ContentType = NextValue();
                    break;
                case "--topic":
                    options.Topic = NextValue();
                    break;
                case "--source-protocol":
                    options.SourceProtocol = NextValue();
                    break;
                case "--output":
                    options.OutputMode = NextValue();
                    break;
                case "--output-file":
                    options.OutputFile = NextValue();
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--validate":
                    options.ValidateOnly = true;
                    break;
                case "--precheck":
                    options.PrecheckOnly = true;
                    break;
                case "--doctor":
                    options.DoctorOnly = true;
                    break;
                case "--save-config":
                    options.SaveConfigPath = NextValue();
                    break;
                case "--station-no":
                    options.StationNo = NextValue();
                    break;
                case "--target-url":
                    options.TargetUrl = NextValue();
                    break;
                case "--mapping":
                    options.Mapping = NextValue();
                    break;
                default:
                    throw new ArgumentException($"未知参数: {current}");
            }
        }

        return options;
    }
}

public sealed class GatewayCliRequest
{
    public string SchemaVersion { get; set; } = GatewayCliSchema.Version1;
    public string Scenario { get; set; } = GatewayScenarioCatalog.SerialToHttpId;
    public GatewaySourceOptions Source { get; set; } = new();
    public GatewayOutputOptions Output { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public SerialToHttpOptions SerialToHttp { get; set; } = new();
    public JsonTransformOptions JsonTransform { get; set; } = new();
}

public sealed class GatewaySourceOptions
{
    public string Mode { get; set; } = GatewayCliModes.Inline;
    public string Protocol { get; set; } = "CLI";
    public string ContentType { get; set; } = "text";
    public string Topic { get; set; } = "cli/input";
    public string? Payload { get; set; }
    public string? PayloadFile { get; set; }
}

public sealed class GatewayOutputOptions
{
    public string Mode { get; set; } = GatewayCliModes.Console;
    public string Name { get; set; } = "ConsolePreview";
    public string? FilePath { get; set; }
}

public sealed class SerialToHttpOptions
{
    public string StationNo { get; set; } = "STATION-01";
    public string TargetUrl { get; set; } = "https://example.com/api/measurements";
}

public sealed class JsonTransformOptions
{
    public Dictionary<string, string> FieldMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["device_id"] = "deviceId",
        ["measured_value"] = "value",
        ["unit"] = "unit"
    };
}

public static class GatewayCliModes
{
    public const string Inline = "inline";
    public const string File = "file";
    public const string Console = "console";

    public static bool IsSupportedSourceMode(string? mode) =>
        string.Equals(mode, Inline, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, File, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedOutputMode(string? mode) =>
        string.Equals(mode, Console, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, File, StringComparison.OrdinalIgnoreCase);
}

public static class GatewayCliContentTypes
{
    public const string Text = "text";
    public const string Json = "json";
    public const string Hex = "hex";

    public static bool IsSupported(string? contentType) =>
        string.Equals(contentType, Text, StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, Json, StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, Hex, StringComparison.OrdinalIgnoreCase);
}

public static class GatewayCliSchema
{
    public const string Version1 = "protocol-gateway-cli/v1";
}

public static class GatewayCliRequestLoader
{
    public static GatewayCliRequest LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("配置文件路径不能为空。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("未找到配置文件。", fullPath);
        }

        var json = File.ReadAllText(fullPath);
        var request = JsonSerializer.Deserialize<GatewayCliRequest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        return request ?? throw new InvalidOperationException("配置文件为空或格式无效。");
    }

    public static string SaveToFile(GatewayCliRequest request, string path)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("导出配置路径不能为空。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, Serialize(request));
        return fullPath;
    }

    public static string Serialize(GatewayCliRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}

public static class GatewayCliRequestFactory
{
    public static GatewayCliRequest FromOptions(GatewayCliOptions options)
    {
        var request = CreateDefault(options.ScenarioId!);

        if (!string.IsNullOrWhiteSpace(options.Payload))
        {
            request.Source.Payload = options.Payload;
        }

        if (!string.IsNullOrWhiteSpace(options.PayloadFile))
        {
            request.Source.Mode = GatewayCliModes.File;
            request.Source.PayloadFile = options.PayloadFile;
        }

        if (!string.IsNullOrWhiteSpace(options.ContentType))
        {
            request.Source.ContentType = options.ContentType!;
        }

        if (!string.IsNullOrWhiteSpace(options.Topic))
        {
            request.Source.Topic = options.Topic!;
        }

        if (!string.IsNullOrWhiteSpace(options.SourceProtocol))
        {
            request.Source.Protocol = options.SourceProtocol!;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputMode))
        {
            request.Output.Mode = options.OutputMode!;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            request.Output.Mode = GatewayCliModes.File;
            request.Output.FilePath = options.OutputFile;
        }

        if (!string.IsNullOrWhiteSpace(options.StationNo))
        {
            request.SerialToHttp.StationNo = options.StationNo!;
        }

        if (!string.IsNullOrWhiteSpace(options.TargetUrl))
        {
            request.SerialToHttp.TargetUrl = options.TargetUrl!;
        }

        if (!string.IsNullOrWhiteSpace(options.Mapping))
        {
            request.JsonTransform.FieldMappings = ParseMappings(options.Mapping!);
        }

        return request;
    }

    public static void ApplyOverrides(GatewayCliRequest request, GatewayCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.Payload))
        {
            request.Source.Mode = GatewayCliModes.Inline;
            request.Source.Payload = options.Payload;
            request.Source.PayloadFile = null;
        }

        if (!string.IsNullOrWhiteSpace(options.PayloadFile))
        {
            request.Source.Mode = GatewayCliModes.File;
            request.Source.PayloadFile = options.PayloadFile;
        }

        if (!string.IsNullOrWhiteSpace(options.ContentType))
        {
            request.Source.ContentType = options.ContentType!;
        }

        if (!string.IsNullOrWhiteSpace(options.Topic))
        {
            request.Source.Topic = options.Topic!;
        }

        if (!string.IsNullOrWhiteSpace(options.SourceProtocol))
        {
            request.Source.Protocol = options.SourceProtocol!;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputMode))
        {
            request.Output.Mode = options.OutputMode!;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            request.Output.Mode = GatewayCliModes.File;
            request.Output.FilePath = options.OutputFile;
        }

        if (!string.IsNullOrWhiteSpace(options.StationNo))
        {
            request.SerialToHttp.StationNo = options.StationNo!;
        }

        if (!string.IsNullOrWhiteSpace(options.TargetUrl))
        {
            request.SerialToHttp.TargetUrl = options.TargetUrl!;
        }

        if (!string.IsNullOrWhiteSpace(options.Mapping))
        {
            request.JsonTransform.FieldMappings = ParseMappings(options.Mapping!);
        }
    }

    public static void Normalize(GatewayCliRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.SchemaVersion = string.IsNullOrWhiteSpace(request.SchemaVersion)
            ? GatewayCliSchema.Version1
            : request.SchemaVersion;

        request.Output.Name = string.IsNullOrWhiteSpace(request.Output.Name)
            ? request.Output.Mode == GatewayCliModes.File ? "FileOutput" : "ConsolePreview"
            : request.Output.Name;

        request.Source.Topic = string.IsNullOrWhiteSpace(request.Source.Topic) ? "cli/input" : request.Source.Topic;
        request.Source.Protocol = string.IsNullOrWhiteSpace(request.Source.Protocol) ? "CLI" : request.Source.Protocol;
        request.Source.ContentType = string.IsNullOrWhiteSpace(request.Source.ContentType) ? "text" : request.Source.ContentType.ToLowerInvariant();
        request.Source.Mode = string.IsNullOrWhiteSpace(request.Source.Mode) ? GatewayCliModes.Inline : request.Source.Mode.ToLowerInvariant();
        request.Output.Mode = string.IsNullOrWhiteSpace(request.Output.Mode) ? GatewayCliModes.Console : request.Output.Mode.ToLowerInvariant();
    }

    public static GatewayCliRequest CreateDefault(string scenarioId)
    {
        return scenarioId switch
        {
            GatewayScenarioCatalog.SerialToHttpId => new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.SerialToHttpId,
                Source = new GatewaySourceOptions
                {
                    Protocol = "Serial",
                    ContentType = "text",
                    Topic = "serial/input",
                    Payload = "SN123,10.5,20.3,15.7"
                }
            },
            GatewayScenarioCatalog.JsonTransformId => new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.JsonTransformId,
                Source = new GatewaySourceOptions
                {
                    Protocol = "JSON",
                    ContentType = "json",
                    Topic = "json/input",
                    Payload = "{\"device_id\":\"A1\",\"measured_value\":88,\"unit\":\"C\"}"
                }
            },
            GatewayScenarioCatalog.TextForwardId => new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.TextForwardId,
                Source = new GatewaySourceOptions
                {
                    Protocol = "TCP",
                    ContentType = "text",
                    Topic = "tcp/input",
                    Payload = "D100=42"
                }
            },
            GatewayScenarioCatalog.SerialToHttpFileId => new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.SerialToHttpFileId,
                Source = new GatewaySourceOptions
                {
                    Protocol = "Serial",
                    ContentType = "text",
                    Topic = "serial/input",
                    Payload = "SN123,10.5,20.3,15.7"
                },
                Output = new GatewayOutputOptions
                {
                    Mode = GatewayCliModes.File,
                    Name = "FileOutput",
                    FilePath = "./protocol-gateway-http-output.json"
                }
            },
            GatewayScenarioCatalog.JsonTransformFileId => new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.JsonTransformFileId,
                Source = new GatewaySourceOptions
                {
                    Protocol = "JSON",
                    ContentType = "json",
                    Topic = "json/input",
                    Payload = "{\"device_id\":\"A1\",\"measured_value\":88,\"unit\":\"C\"}"
                },
                Output = new GatewayOutputOptions
                {
                    Mode = GatewayCliModes.File,
                    Name = "FileOutput",
                    FilePath = "./protocol-gateway-json-output.json"
                }
            },
            GatewayScenarioCatalog.TextForwardFileId => new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.TextForwardFileId,
                Source = new GatewaySourceOptions
                {
                    Protocol = "TCP",
                    ContentType = "text",
                    Topic = "tcp/input",
                    Payload = "D100=42"
                },
                Output = new GatewayOutputOptions
                {
                    Mode = GatewayCliModes.File,
                    Name = "FileOutput",
                    FilePath = "./protocol-gateway-forward-output.log"
                }
            },
            _ => throw new ArgumentException($"不支持的场景: {scenarioId}")
        };
    }

    public static Dictionary<string, string> ParseMappings(string mappingText)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pairs = mappingText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var segments = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (segments.Length != 2 || string.IsNullOrWhiteSpace(segments[0]) || string.IsNullOrWhiteSpace(segments[1]))
            {
                throw new ArgumentException($"非法 mapping 片段: {pair}");
            }

            mappings[segments[0]] = segments[1];
        }

        return mappings;
    }
}

public static class GatewayCliRequestValidator
{
    public static void Validate(GatewayCliRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SchemaVersion))
        {
            throw new InvalidOperationException("SchemaVersion 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Scenario))
        {
            throw new InvalidOperationException("Scenario 不能为空。");
        }

        _ = GatewayScenarioCatalog.Find(request.Scenario)
            ?? throw new InvalidOperationException($"未知场景: {request.Scenario}");

        if (!GatewayCliModes.IsSupportedSourceMode(request.Source.Mode))
        {
            throw new InvalidOperationException($"不支持的 source.mode: {request.Source.Mode}");
        }

        if (!GatewayCliModes.IsSupportedOutputMode(request.Output.Mode))
        {
            throw new InvalidOperationException($"不支持的 output.mode: {request.Output.Mode}");
        }

        if (!GatewayCliContentTypes.IsSupported(request.Source.ContentType))
        {
            throw new InvalidOperationException($"不支持的 contentType: {request.Source.ContentType}");
        }

        if (request.Source.Mode == GatewayCliModes.File && string.IsNullOrWhiteSpace(request.Source.PayloadFile))
        {
            throw new InvalidOperationException("文件输入模式必须提供 payloadFile。");
        }

        if (request.Source.Mode == GatewayCliModes.Inline && string.IsNullOrWhiteSpace(request.Source.Payload))
        {
            throw new InvalidOperationException("inline 输入模式必须提供 payload。");
        }

        if (request.Output.Mode == GatewayCliModes.File && string.IsNullOrWhiteSpace(request.Output.FilePath))
        {
            throw new InvalidOperationException("文件输出模式必须提供 output.filePath。");
        }

        if (GatewayScenarioCatalog.IsSerialToHttpScenario(request.Scenario))
        {
            if (string.IsNullOrWhiteSpace(request.SerialToHttp.StationNo) || string.IsNullOrWhiteSpace(request.SerialToHttp.TargetUrl))
            {
                throw new InvalidOperationException("SerialToHttp 场景需要 stationNo 与 targetUrl。");
            }
        }

        if (GatewayScenarioCatalog.IsJsonTransformScenario(request.Scenario) && request.JsonTransform.FieldMappings.Count == 0)
        {
            throw new InvalidOperationException("JsonTransform 场景至少需要一个字段映射。");
        }
    }
}

public sealed class GatewayCliPrecheckItem
{
    public required string Level { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
}

public sealed class GatewayCliPrecheckReport
{
    public List<GatewayCliPrecheckItem> Items { get; } = [];
    public bool HasErrors => Items.Any(static item => string.Equals(item.Level, "ERROR", StringComparison.OrdinalIgnoreCase));
}

public sealed class GatewayCliDoctorReport
{
    public required string SchemaVersion { get; init; }
    public required string Scenario { get; init; }
    public required string ConfigSource { get; init; }
    public required string WorkingDirectory { get; init; }
    public required bool ValidationPassed { get; set; }
    public List<GatewayCliPrecheckItem> Items { get; } = [];
    public List<string> Recommendations { get; } = [];
    public int ErrorCount => CountByLevel(Items, "ERROR");
    public int WarningCount => CountByLevel(Items, "WARN");
    public int InfoCount => CountByLevel(Items, "INFO");
    public IReadOnlyList<string> TitleSummaries => Items
        .GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(static group =>
        {
            var errorCount = CountByLevel(group, "ERROR");
            var warningCount = CountByLevel(group, "WARN");
            var infoCount = CountByLevel(group, "INFO");
            return $"{group.Key}: Errors={errorCount}, Warnings={warningCount}, Info={infoCount}";
        })
        .ToArray();
    public bool HasErrors => ErrorCount > 0;

    private static int CountByLevel(IEnumerable<GatewayCliPrecheckItem> items, string level) =>
        items.Count(item => string.Equals(item.Level, level, StringComparison.OrdinalIgnoreCase));
}

public static class GatewayCliPrecheckRunner
{
    public static GatewayCliPrecheckReport Run(GatewayCliRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var report = new GatewayCliPrecheckReport();
        report.Items.Add(new GatewayCliPrecheckItem
        {
            Level = "INFO",
            Title = "Scenario",
            Message = $"场景 '{request.Scenario}' 已通过基础校验。"
        });

        AddInputChecks(request, report);
        AddOutputChecks(request, report);
        AddScenarioChecks(request, report);
        AddContentChecks(request, report);

        return report;
    }

    private static void AddInputChecks(GatewayCliRequest request, GatewayCliPrecheckReport report)
    {
        if (request.Source.Mode == GatewayCliModes.File)
        {
            var fullPayloadPath = Path.GetFullPath(request.Source.PayloadFile!);
            if (File.Exists(fullPayloadPath))
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "INFO",
                    Title = "InputFile",
                    Message = $"输入文件存在: {fullPayloadPath}"
                });

                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = CanReadFile(fullPayloadPath) ? "INFO" : "ERROR",
                    Title = "InputFileAccess",
                    Message = CanReadFile(fullPayloadPath)
                        ? $"输入文件可读: {fullPayloadPath}"
                        : $"输入文件存在但不可读: {fullPayloadPath}"
                });
            }
            else
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "ERROR",
                    Title = "InputFile",
                    Message = $"输入文件不存在: {fullPayloadPath}"
                });
            }

            if (!string.IsNullOrWhiteSpace(request.Source.Payload))
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "WARN",
                    Title = "InputConflict",
                    Message = "当前为 file 输入模式，但 payload 仍有值；运行时将优先使用 payloadFile。"
                });
            }
        }
        else
        {
            report.Items.Add(new GatewayCliPrecheckItem
            {
                Level = "INFO",
                Title = "InlinePayload",
                Message = $"Inline 载荷长度: {request.Source.Payload?.Length ?? 0}"
            });

            if (!string.IsNullOrWhiteSpace(request.Source.PayloadFile))
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "WARN",
                    Title = "InputConflict",
                    Message = "当前为 inline 输入模式，但 payloadFile 仍有值；运行时将忽略 payloadFile。"
                });
            }
        }
    }

    private static void AddOutputChecks(GatewayCliRequest request, GatewayCliPrecheckReport report)
    {
        if (request.Output.Mode == GatewayCliModes.File)
        {
            var fullOutputPath = Path.GetFullPath(request.Output.FilePath!);
            var directory = Path.GetDirectoryName(fullOutputPath);
            var directoryExists = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
            report.Items.Add(new GatewayCliPrecheckItem
            {
                Level = directoryExists ? "INFO" : "WARN",
                Title = "OutputFile",
                Message = directoryExists
                    ? $"输出目录存在: {directory}"
                    : $"输出目录当前不存在，运行时将尝试创建: {directory}"
            });

            if (File.Exists(fullOutputPath))
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "WARN",
                    Title = "OutputOverwrite",
                    Message = $"输出文件已存在，运行时可能覆盖现有内容: {fullOutputPath}"
                });
            }

            if (Directory.Exists(fullOutputPath))
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "ERROR",
                    Title = "OutputPathConflict",
                    Message = $"输出路径指向已存在目录，无法作为文件写入: {fullOutputPath}"
                });
            }

            if (!string.IsNullOrWhiteSpace(directory) && directoryExists)
            {
                var canWriteDirectory = CanWriteDirectory(directory);
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = canWriteDirectory ? "INFO" : "ERROR",
                    Title = "OutputDirectoryWriteAccess",
                    Message = canWriteDirectory
                        ? $"输出目录可写: {directory}"
                        : $"输出目录存在但当前不可写，请检查目录写权限: {directory}"
                });
            }
            else if (!string.IsNullOrWhiteSpace(directory))
            {
                var canCreateDirectory = CanCreateDirectory(directory);
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = canCreateDirectory ? "INFO" : "ERROR",
                    Title = "OutputDirectoryAccess",
                    Message = canCreateDirectory
                        ? $"输出目录可创建: {directory}"
                        : $"输出目录不可创建，请检查父目录是否存在及写权限: {directory}"
                });
            }

            if (request.Source.Mode == GatewayCliModes.File
                && !string.IsNullOrWhiteSpace(request.Source.PayloadFile)
                && string.Equals(Path.GetFullPath(request.Source.PayloadFile), fullOutputPath, StringComparison.OrdinalIgnoreCase))
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "WARN",
                    Title = "InputOutputConflict",
                    Message = "输入文件与输出文件指向同一路径，运行时可能覆盖原始输入。"
                });
            }
        }
        else
        {
            report.Items.Add(new GatewayCliPrecheckItem
            {
                Level = "INFO",
                Title = "OutputMode",
                Message = "当前输出模式为控制台预览。"
            });
        }
    }

    private static void AddScenarioChecks(GatewayCliRequest request, GatewayCliPrecheckReport report)
    {
        if (GatewayScenarioCatalog.IsSerialToHttpScenario(request.Scenario))
        {
            if (Uri.TryCreate(request.SerialToHttp.TargetUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "INFO",
                    Title = "TargetUrl",
                    Message = $"目标地址有效: {request.SerialToHttp.TargetUrl}"
                });

                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = uri.IsDefaultPort ? "WARN" : "INFO",
                    Title = "TargetUrlPort",
                    Message = uri.IsDefaultPort
                        ? $"targetUrl 未显式指定端口，当前将使用 {uri.Scheme} 默认端口。"
                        : $"targetUrl 已显式指定端口: {uri.Port}"
                });

                if (!uri.IsDefaultPort)
                {
                    if (uri.Scheme == Uri.UriSchemeHttp && uri.Port == 443)
                    {
                        report.Items.Add(new GatewayCliPrecheckItem
                        {
                            Level = "WARN",
                            Title = "TargetUrlSchemePort",
                            Message = "targetUrl 当前使用 http 但端口为 443，这通常对应 https，请确认现场网关或反向代理是否做了明文转 TLS 转发。"
                        });
                    }
                    else if (uri.Scheme == Uri.UriSchemeHttps && uri.Port == 80)
                    {
                        report.Items.Add(new GatewayCliPrecheckItem
                        {
                            Level = "WARN",
                            Title = "TargetUrlSchemePort",
                            Message = "targetUrl 当前使用 https 但端口为 80，这通常对应 http，请确认目标服务、代理或负载均衡配置是否与证书链路一致。"
                        });
                    }
                    else if ((uri.Scheme == Uri.UriSchemeHttp && uri.Port == 80)
                        || (uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443))
                    {
                        report.Items.Add(new GatewayCliPrecheckItem
                        {
                            Level = "INFO",
                            Title = "TargetUrlSchemePort",
                            Message = $"targetUrl 已显式指定与 {uri.Scheme} 协议一致的常见端口: {uri.Port}"
                        });
                    }
                }

                if (string.IsNullOrWhiteSpace(uri.AbsolutePath) || string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
                {
                    report.Items.Add(new GatewayCliPrecheckItem
                    {
                        Level = "WARN",
                        Title = "TargetUrlPath",
                        Message = "targetUrl 未包含明确的业务路径，现场联调时可能命中根路径或默认路由。"
                    });
                }

                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
                {
                    report.Items.Add(new GatewayCliPrecheckItem
                    {
                        Level = "WARN",
                        Title = "TargetUrlHost",
                        Message = $"targetUrl 当前指向本机地址 {uri.Host}，请确认部署环境下该地址对目标服务真实可达。"
                    });
                }
            }
            else
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "ERROR",
                    Title = "TargetUrl",
                    Message = $"目标地址不是合法的 HTTP/HTTPS URL: {request.SerialToHttp.TargetUrl}"
                });
            }
        }

        if (GatewayScenarioCatalog.IsJsonTransformScenario(request.Scenario))
        {
            var duplicateTargets = request.JsonTransform.FieldMappings
                .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
                .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(static item => item)
                .ToArray();

            if (duplicateTargets.Length > 0)
            {
                report.Items.Add(new GatewayCliPrecheckItem
                {
                    Level = "WARN",
                    Title = "MappingConflict",
                    Message = $"检测到多个源字段映射到同一目标字段: {string.Join(", ", duplicateTargets)}"
                });
            }
        }
    }

    private static void AddContentChecks(GatewayCliRequest request, GatewayCliPrecheckReport report)
    {
        report.Items.Add(new GatewayCliPrecheckItem
        {
            Level = "INFO",
            Title = "ContentType",
            Message = $"内容类型: {request.Source.ContentType}"
        });

        report.Items.Add(new GatewayCliPrecheckItem
        {
            Level = "INFO",
            Title = "Output",
            Message = $"输出模式: {request.Output.Mode}"
        });

        if (GatewayScenarioCatalog.IsJsonTransformScenario(request.Scenario)
            && !string.Equals(request.Source.ContentType, GatewayCliContentTypes.Json, StringComparison.OrdinalIgnoreCase))
        {
            report.Items.Add(new GatewayCliPrecheckItem
            {
                Level = "WARN",
                Title = "ScenarioContentType",
                Message = "json-transform 场景通常应使用 json contentType，当前配置可能导致转换语义不一致。"
            });
        }
    }

    private static bool CanReadFile(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.CanRead;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanCreateDirectory(string directory)
    {
        try
        {
            var parentDirectory = Path.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            var probePath = Path.Combine(directory, $".gateway-write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public static class GatewayCliDoctorRunner
{
    public static GatewayCliDoctorReport Run(GatewayCliRequest request, GatewayCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var report = new GatewayCliDoctorReport
        {
            SchemaVersion = request.SchemaVersion,
            Scenario = request.Scenario,
            ConfigSource = string.IsNullOrWhiteSpace(options.ConfigPath) ? "cli/generated" : Path.GetFullPath(options.ConfigPath),
            WorkingDirectory = Environment.CurrentDirectory,
            ValidationPassed = true
        };

        try
        {
            GatewayCliRequestValidator.Validate(request);
            report.Items.Add(new GatewayCliPrecheckItem
            {
                Level = "INFO",
                Title = "Validation",
                Message = "配置字段、模式与场景约束校验通过。"
            });
        }
        catch (Exception ex)
        {
            report.ValidationPassed = false;
            report.Items.Add(new GatewayCliPrecheckItem
            {
                Level = "ERROR",
                Title = "Validation",
                Message = ex.Message
            });
        }

        if (report.ValidationPassed)
        {
            var precheckReport = GatewayCliPrecheckRunner.Run(request);
            report.Items.AddRange(precheckReport.Items);
        }
        else
        {
            report.Recommendations.Add("请先修复 validate 阶段的配置错误，再执行 precheck、dry-run 或实际启动。");
        }

        AddRecommendations(report);
        return report;
    }

    private static void AddRecommendations(GatewayCliDoctorReport report)
    {
        if (report.ErrorCount > 0)
        {
            report.Recommendations.Add($"当前共有 {report.ErrorCount} 个 ERROR 项，请先修复后再尝试 dry-run 或实际启动。");
        }
        else
        {
            report.Recommendations.Add("当前无 ERROR 项，可继续执行 --dry-run 复核最终配置，或直接执行实际场景。");
        }

        if (report.WarningCount > 0)
        {
            report.Recommendations.Add($"当前共有 {report.WarningCount} 个 WARN 项，请人工复核是否属于可接受风险或配置漂移。");
        }

        if (report.InfoCount > 0)
        {
            report.Recommendations.Add($"当前共有 {report.InfoCount} 个 INFO 项，可作为现场排障与交接记录的辅助信息。");
        }

        report.Recommendations.Add("建议保存标准化配置，便于问题复现与现场交接。");
    }
}

public sealed class GatewayScenarioDefinition
{
    public required int Order { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string[] ParameterHints { get; init; }
}

public static class GatewayScenarioCatalog
{
    public const string SerialToHttpId = "serial-to-http";
    public const string JsonTransformId = "json-transform";
    public const string TextForwardId = "text-forward";
    public const string SerialToHttpFileId = "serial-to-http-file";
    public const string JsonTransformFileId = "json-transform-file";
    public const string TextForwardFileId = "text-forward-file";

    private static readonly GatewayScenarioDefinition[] Scenarios =
    [
        new GatewayScenarioDefinition
        {
            Order = 1,
            Id = SerialToHttpId,
            Name = "串口文本 -> HTTP JSON",
            Description = "把串口风格的逗号分隔文本转换成 HTTP JSON 请求体，快速体验协议转换。",
            ParameterHints = ["stationNo", "targetUrl", "payload=SN123,10.5,20.3"]
        },
        new GatewayScenarioDefinition
        {
            Order = 2,
            Id = JsonTransformId,
            Name = "JSON 字段映射 -> JSON",
            Description = "把上游 JSON 字段名映射为下游模型字段名，用于 API/平台适配。",
            ParameterHints = ["mapping=device_id=deviceId;measured_value=value", "payload={...}"]
        },
        new GatewayScenarioDefinition
        {
            Order = 3,
            Id = TextForwardId,
            Name = "文本透传 -> 目标输出",
            Description = "不做转换，直接体验 GatewayService + MessagePipeline 的路由与转发能力。",
            ParameterHints = ["payload=D100=42", "output=console|file"]
        },
        new GatewayScenarioDefinition
        {
            Order = 4,
            Id = SerialToHttpFileId,
            Name = "串口文本 -> HTTP JSON -> 文件归档",
            Description = "把串口风格文本转换成 HTTP JSON，并落盘成文件，适合体验无头导出流程。",
            ParameterHints = ["stationNo", "targetUrl", "outputFile=./protocol-gateway-http-output.json"]
        },
        new GatewayScenarioDefinition
        {
            Order = 5,
            Id = JsonTransformFileId,
            Name = "JSON 字段映射 -> 文件归档",
            Description = "把上游 JSON 重塑成目标 JSON，并写入文件，适合快速验证字段映射结果。",
            ParameterHints = ["mapping=device_id=deviceId;measured_value=value", "outputFile=./protocol-gateway-json-output.json"]
        },
        new GatewayScenarioDefinition
        {
            Order = 6,
            Id = TextForwardFileId,
            Name = "文本透传 -> 文件归档",
            Description = "不做协议转换，直接把文本落盘，适合排查输入内容与链路连通性。",
            ParameterHints = ["payload=D100=42", "outputFile=./protocol-gateway-forward-output.log"]
        }
    ];

    public static IReadOnlyList<GatewayScenarioDefinition> GetBuiltInScenarios() => Scenarios;

    public static GatewayScenarioDefinition? Find(string id) =>
        Scenarios.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool IsSerialToHttpScenario(string id) =>
        string.Equals(id, SerialToHttpId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, SerialToHttpFileId, StringComparison.OrdinalIgnoreCase);

    public static bool IsJsonTransformScenario(string id) =>
        string.Equals(id, JsonTransformId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, JsonTransformFileId, StringComparison.OrdinalIgnoreCase);
}

public static class GatewayInteractivePrompt
{
    public static GatewayCliRequest BuildInteractiveRequest()
    {
        Console.WriteLine("=== ProtocolGateway CLI 交互式体验 ===");
        foreach (var scenario in GatewayScenarioCatalog.GetBuiltInScenarios())
        {
            Console.WriteLine($"[{scenario.Order}] {scenario.Name}");
            Console.WriteLine($"    {scenario.Description}");
        }

        var selectedScenario = AskScenario();
        var request = GatewayCliRequestFactory.CreateDefault(selectedScenario.Id);

        Console.WriteLine();
        Console.WriteLine("输入模式: 1=直接输入内容  2=从文件读取内容");
        var inputMode = ReadWithDefault("请选择输入模式", "1");
        if (inputMode == "2")
        {
            request.Source.Mode = GatewayCliModes.File;
            request.Source.PayloadFile = ReadRequired("请输入输入文件路径");
        }
        else
        {
            request.Source.Payload = ReadRequired($"请输入载荷内容（默认示例可直接回车）", request.Source.Payload);
        }

        request.Source.Topic = ReadRequired("请输入 Topic/Path", request.Source.Topic);
        request.Source.ContentType = ReadRequired("请输入内容类型(text/json/hex)", request.Source.ContentType);
        request.Source.Protocol = ReadRequired("请输入源协议名", request.Source.Protocol);

        switch (request.Scenario)
        {
            case GatewayScenarioCatalog.SerialToHttpId:
            case GatewayScenarioCatalog.SerialToHttpFileId:
                request.SerialToHttp.StationNo = ReadRequired("请输入 stationNo", request.SerialToHttp.StationNo);
                request.SerialToHttp.TargetUrl = ReadRequired("请输入 targetUrl", request.SerialToHttp.TargetUrl);
                break;
            case GatewayScenarioCatalog.JsonTransformId:
            case GatewayScenarioCatalog.JsonTransformFileId:
                var mappingInput = ReadRequired(
                    "请输入字段映射(a=b;c=d)",
                    string.Join(';', request.JsonTransform.FieldMappings.Select(item => $"{item.Key}={item.Value}")));
                request.JsonTransform.FieldMappings = GatewayCliRequestFactory.ParseMappings(mappingInput);
                break;
        }

        Console.WriteLine("输出模式: 1=控制台预览  2=写入文件");
        var outputMode = ReadWithDefault("请选择输出模式", "1");
        if (outputMode == "2")
        {
            request.Output.Mode = GatewayCliModes.File;
            request.Output.FilePath = ReadRequired("请输入输出文件路径", "./protocol-gateway-output.log");
        }
        else
        {
            request.Output.Mode = GatewayCliModes.Console;
        }

        return request;
    }

    private static GatewayScenarioDefinition AskScenario()
    {
        var selected = ReadWithDefault("请选择场景序号", "1");
        if (!int.TryParse(selected, out var number))
        {
            throw new InvalidOperationException("场景序号必须是数字。");
        }

        return GatewayScenarioCatalog.GetBuiltInScenarios().FirstOrDefault(item => item.Order == number)
               ?? throw new InvalidOperationException($"不存在的场景序号: {number}");
    }

    private static string ReadWithDefault(string prompt, string defaultValue)
    {
        Console.Write($"{prompt} [{defaultValue}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
    }

    private static string ReadRequired(string prompt, string? defaultValue = null)
    {
        var value = defaultValue is null ? ReadWithDefault(prompt, string.Empty) : ReadWithDefault(prompt, defaultValue);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{prompt} 不能为空。");
        }

        return value;
    }
}

public static class GatewaySimulationRunner
{
    public static async Task RunAsync(GatewayCliRequest request, CancellationToken cancellationToken = default)
    {
        var message = BuildMessage(request);
        var pipeline = new ResilientMessagePipeline();
        var output = CreateOutput(request.Output);
        pipeline.RegisterOutput(output);
        pipeline.AddRouter(new RouteRule
        {
            Name = "CliRouteAll",
            Condition = _ => true,
            OutputNames = { output.Name },
            ContinueMatching = false,
            Priority = 1
        });

        foreach (var transformer in BuildTransformers(request))
        {
            pipeline.AddTransformer(transformer);
        }

        var gateway = new GatewayService(pipeline);
        gateway.AddInput(new SyntheticInputPlugin(message));

        try
        {
            await gateway.StartAsync(cancellationToken);
        }
        finally
        {
            await gateway.StopAsync();
            output.Dispose();
        }
    }

    private static Message BuildMessage(GatewayCliRequest request)
    {
        var payload = request.Source.Mode == GatewayCliModes.File
            ? File.ReadAllText(Path.GetFullPath(request.Source.PayloadFile!))
            : request.Source.Payload!;

        var message = new Message
        {
            Topic = request.Source.Topic,
            Timestamp = DateTime.Now,
            Metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["Protocol"] = request.Source.Protocol
            }
        };

        switch (request.Source.ContentType.ToLowerInvariant())
        {
            case "json":
                message.SetJsonContent(payload);
                break;
            case "hex":
                message.SetHexContent(payload);
                break;
            default:
                message.SetTextContent(payload);
                break;
        }

        return message;
    }

    private static IEnumerable<Func<Message, Task<Message>>> BuildTransformers(GatewayCliRequest request)
    {
        switch (request.Scenario)
        {
            case GatewayScenarioCatalog.SerialToHttpId:
            case GatewayScenarioCatalog.SerialToHttpFileId:
            {
                // 内联 CSV→JSON 转换逻辑（替代已移除的 SerialToHttpConverter）
                var stationNo = request.SerialToHttp.StationNo;
                var targetUrl = request.SerialToHttp.TargetUrl;
                yield return message =>
                {
                    var rawData = message.GetTextContent();
                    var parts = rawData?.Split(',');
                    if (parts == null || parts.Length < 2)
                    {
                        return Task.FromResult<Message>(null!);
                    }

                    var payload = new Dictionary<string, object>
                    {
                        ["sn"] = parts[0].Trim(),
                        ["stationNo"] = stationNo,
                        ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["values"] = new List<Dictionary<string, object>>()
                    };

                    for (int i = 1; i < parts.Length; i++)
                    {
                        if (double.TryParse(parts[i].Trim(), out var value))
                        {
                            ((List<Dictionary<string, object>>)payload["values"]).Add(new Dictionary<string, object>
                            {
                                ["index"] = i,
                                ["value"] = value
                            });
                        }
                    }

                    var jsonContent = System.Text.Json.JsonSerializer.Serialize(payload);
                    var result = new Message
                    {
                        Topic = targetUrl,
                        Payload = System.Text.Encoding.UTF8.GetBytes(jsonContent),
                        ContentType = "json",
                        Timestamp = DateTime.Now,
                        Metadata =
                        {
                            ["Protocol"] = "HTTP",
                            ["Method"] = "POST",
                            ["Content-Type"] = "application/json",
                            ["Source-Protocol"] = "Serial",
                            ["Source-Port"] = message.Metadata.TryGetValue("PortName", out var port) ? port : "unknown"
                        }
                    };
                    return Task.FromResult<Message>(result);
                };
                break;
            }
            case GatewayScenarioCatalog.JsonTransformId:
            case GatewayScenarioCatalog.JsonTransformFileId:
            {
                // 内联 JSON 字段映射逻辑（替代已移除的 JsonTransformerConverter）
                var fieldMappings = request.JsonTransform.FieldMappings;
                yield return message =>
                {
                    if (message.ContentType != "json")
                        return Task.FromResult<Message>(message);

                    try
                    {
                        using var doc = JsonDocument.Parse(message.GetJsonContent());
                        var root = doc.RootElement;
                        var output = new JsonObject();

                        foreach (var mapping in fieldMappings)
                        {
                            if (root.TryGetProperty(mapping.Key, out var value))
                            {
                                output[mapping.Value] = JsonNode.Parse(value.GetRawText());
                            }
                        }

                        output["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var jsonContent = output.ToJsonString();

                        var result = new Message
                        {
                            Topic = message.Topic,
                            Payload = System.Text.Encoding.UTF8.GetBytes(jsonContent),
                            ContentType = "json",
                            Timestamp = DateTime.Now,
                            Metadata = new Dictionary<string, string>(message.Metadata)
                        };
                        return Task.FromResult<Message>(result);
                    }
                    catch
                    {
                        return Task.FromResult<Message>(null!);
                    }
                };
                break;
            }
        }
    }

    private static IOutputPlugin CreateOutput(GatewayOutputOptions options)
    {
        return options.Mode switch
        {
            GatewayCliModes.Console => new ConsolePreviewOutputPlugin(options.Name),
            GatewayCliModes.File => new FileOutputPlugin(new FileOutputConfig
            {
                Name = string.IsNullOrWhiteSpace(options.Name) ? "FileOutput" : options.Name,
                FilePath = Path.GetFullPath(options.FilePath!),
                Append = true
            }),
            _ => throw new InvalidOperationException($"不支持的输出模式: {options.Mode}")
        };
    }
}
