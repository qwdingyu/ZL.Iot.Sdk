using Scriban;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Core.Services;

/// <summary>
/// 基于 Scriban 模板生成 .NET 项目脚手架
/// </summary>
public class ScaffoldEngine
{
    private readonly ILogger<ScaffoldEngine>? _logger;

    public ScaffoldEngine(ILogger<ScaffoldEngine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 从模板目录生成完整 .NET 项目
    /// </summary>
    public void Generate(ScaffoldOptions options, string templateDirectory)
    {
        _logger?.LogInformation(
            "[ScaffoldEngine] Generating project: {AppName} v{Version} | Host={HostType} | RID={RID} | Output={OutputDir}",
            options.ApplicationName, options.Version,
            options.HostType, options.RuntimeIdentifier, options.OutputDirectory);

        if (!Directory.Exists(templateDirectory))
            throw new DirectoryNotFoundException($"Template directory not found: {templateDirectory}");

        Directory.CreateDirectory(options.OutputDirectory);

        // 构建模板上下文
        var context = new Dictionary<string, object>
        {
            ["AppName"] = options.ApplicationName,
            ["Version"] = options.Version,
            ["RuntimeIdentifier"] = options.RuntimeIdentifier,
            ["HostType"] = options.HostType.ToString(),
            ["DatabaseConnection"] = options.DatabaseConnection,
            ["DatabaseType"] = options.DatabaseType,
            ["Devices"] = options.Devices,
            ["GenerateNugetConfig"] = options.GenerateNugetConfig,
            ["IsConsole"] = options.HostType == HostType.Console,
            ["IsWinForms"] = options.HostType == HostType.WinForms,
            ["Year"] = DateTime.Now.Year
        };

        // 渲染所有 .scriban 模板文件
        var templateFiles = Directory.EnumerateFiles(templateDirectory, "*.scriban", SearchOption.AllDirectories);
        int rendered = 0;

        foreach (var templateFile in templateFiles)
        {
            var relativePath = Path.GetRelativePath(templateDirectory, templateFile);
            // 移除 .scriban 扩展名得到目标文件名
            var targetRelativePath = relativePath.Replace(".scriban", "", StringComparison.OrdinalIgnoreCase);
            var targetPath = Path.Combine(options.OutputDirectory, targetRelativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            var templateContent = File.ReadAllText(templateFile);
            var template = Template.Parse(templateContent, Path.GetFileName(templateFile));
            var output = template.Render(context);

            File.WriteAllText(targetPath, output);
            rendered++;
            _logger?.LogDebug("[ScaffoldEngine] Rendered: {Template} -> {Target}", templateFile, targetPath);
        }

        // 复制非模板文件（如 .ico、.proto 等）
        var nonTemplateFiles = Directory.EnumerateFiles(templateDirectory, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase));

        foreach (var srcFile in nonTemplateFiles)
        {
            var relativePath = Path.GetRelativePath(templateDirectory, srcFile);
            var targetPath = Path.Combine(options.OutputDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(srcFile, targetPath, overwrite: true);
            _logger?.LogDebug("[ScaffoldEngine] Copied: {File}", srcFile);
        }

        _logger?.LogInformation("[ScaffoldEngine] Generated {Count} files from templates", rendered);
    }
}
