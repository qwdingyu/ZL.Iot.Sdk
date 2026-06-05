using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Generator.Core.Models;
using ZL.Iot.Runner.Generator.Core.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<ScaffoldEngine>();
        services.AddSingleton<BuildEngine>();
        services.AddSingleton<PackageBuilder>();
        services.AddSingleton<PipelineOrchestrator>();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var orchestrator = host.Services.GetRequiredService<PipelineOrchestrator>();

var parser = new OptionParser(args);

// 确定模板目录
var templatesBase = Path.GetFullPath(Path.Combine(
    Directory.GetCurrentDirectory(), "..", "ZL.Iot.Runner.Templates"));

if (!Directory.Exists(templatesBase))
{
    // 尝试从程序集位置查找
    var asmDir = Path.GetDirectoryName(typeof(PipelineOrchestrator).Assembly.Location)!;
    var altPath = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "ZL.Iot.Runner.Templates"));
    if (Directory.Exists(altPath))
        templatesBase = altPath;
}

var templateDir = Path.Combine(templatesBase, parser.HostType == HostType.WinForms ? "winform" : "console");

if (!Directory.Exists(templateDir))
{
    logger.LogError("Template directory not found: {Path}", templateDir);
    logger.LogInformation("Available templates base: {Base}", templatesBase);
    return 1;
}

logger.LogInformation("Using templates from: {Path}", templateDir);

// 构建选项
var options = new BuildPublishOptions
{
    ApplicationName = parser.AppName,
    Version = parser.Version,
    Configuration = "Release",
    RuntimeIdentifier = parser.Rid,
    HostType = parser.HostType,
    OutputDirectory = parser.OutputDir
};

// 执行流水线
var workDir = Path.Combine(parser.OutputDir, "work");
var result = orchestrator.RunPipeline(options, templateDir, workDir);

// 输出结果
var sep = new string('═', 60);
Console.WriteLine();
Console.WriteLine(sep);
if (result.Success)
{
    Console.WriteLine("✓ Build succeeded!");
    Console.WriteLine($"  Package: {result.PackagePath}");
    Console.WriteLine($"  Size: {result.PackageSizeBytes / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"  Duration: {result.BuildDuration.TotalSeconds:F1}s");
    Console.WriteLine($"  Files: {result.FileHashes.Count}");
    if (result.Warnings.Length > 0)
    {
        Console.WriteLine($"  Warnings: {result.Warnings.Length}");
        foreach (var w in result.Warnings.Take(5))
            Console.WriteLine($"    {w}");
    }
}
else
{
    Console.WriteLine("✗ Build failed!");
    foreach (var e in result.Errors)
        Console.WriteLine($"  ERROR: {e}");
    if (result.Warnings.Length > 0)
    {
        Console.WriteLine("  Warnings:");
        foreach (var w in result.Warnings.Take(5))
            Console.WriteLine($"    {w}");
    }
}
Console.WriteLine(sep);

return result.Success ? 0 : 1;

/// <summary>
/// 简单的命令行参数解析器
/// </summary>
public class OptionParser
{
    private readonly string[] _args;

    public OptionParser(string[] args)
    {
        _args = args;
    }

    public string AppName => GetArg("--name") ?? "RunnerApp";
    public string Version => GetArg("--version") ?? "1.0.0";
    public string Rid => GetArg("--rid") ?? "win-x64";
    public HostType HostType => ParseHostType(GetArg("--host") ?? "console");
    public string OutputDir => GetArg("--output") ?? Path.Combine(Directory.GetCurrentDirectory(), "output");

    private string? GetArg(string key)
    {
        for (var i = 0; i < _args.Length - 1; i++)
        {
            if (_args[i] == key) return _args[i + 1];
        }
        return null;
    }

    private static HostType ParseHostType(string value) => value.ToLowerInvariant() switch
    {
        "winforms" or "winform" or "wf" => HostType.WinForms,
        _ => HostType.Console
    };
}
