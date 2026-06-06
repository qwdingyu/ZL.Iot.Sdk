// ============================================================
//  ZL.Iot.Runner.Generator - ProjectGenerator
//  -------------------------------------------------------------
//  核心编排器：模板 → 渲染 → 编译 → 打包
// ============================================================

using System.Text;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Core;

/// <summary>
/// 生成器主实现：模板渲染 → 文件系统 → dotnet publish → zip 打包。
/// </summary>
public class ProjectGenerator : IProjectGenerator
{
    /// <summary>
    /// 源文件排除模式（source 模式打包时跳过）
    /// </summary>
    private static readonly string[] SourceExcludePatterns =
    [
        "bin/", "obj/", ".vs/", "packages/", ".cache/"
    ];

    /// <summary>
    /// 生成部署包
    /// </summary>
    /// <param name="request">生成请求</param>
    /// <param name="onProgress">进度回调：(phase, percent) — phase 如 "rendering", "building", "packing"</param>
    /// <param name="ct">取消令牌</param>
    public async Task<GenerateResult> GenerateAsync(GenerateRequest request, CancellationToken ct = default, Func<string, int, Task>? onProgress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Step 1: 校验输入
            request.Validate();
            await ReportProgress("validating", 5);

            // Step 2: 创建临时工作目录
            var workDir = Path.Combine(
                Path.GetTempPath(),
                $"zl-gen-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(workDir);

                // Step 3: 渲染模板 → 写入文件
                await ReportProgress("rendering", 15);
                await RenderTemplatesAsync(request, workDir, ct);
                await ReportProgress("rendering", 30);

                // Step 4: 注入 runner.config.json
                await WriteConfigAsync(request, workDir, ct);

                // Step 5: Binary vs Source
                byte[] zipBytes;
                string zipFileName;
                PackageManifest? manifest = null;

                if (request.Sku == SkuMode.Binary)
                {
                    await ReportProgress("building", 40);
                    var (bytes, m) = await GenerateBinaryAsync(request, workDir, ct);
                    zipBytes = bytes;
                    manifest = m;
                    await ReportProgress("building", 90);
                    var hostType = request.Platform == TargetPlatform.WinForm ? HostType.WinForms : HostType.Console;
                    zipFileName = $"{request.ProjectName}-{request.Version}-{request.RuntimeIdentifier}-{hostType.ToString().ToLowerInvariant()}.zip";
                }
                else
                {
                    await ReportProgress("packing", 40);
                    zipBytes = await GenerateSourceAsync(request, workDir, ct);
                    await ReportProgress("packing", 90);
                    zipFileName = $"{request.ProjectName}-source.zip";
                }

                await ReportProgress("complete", 100);
                sw.Stop();
                return GenerateResult.Ok(zipBytes, zipFileName, sw.Elapsed, manifest);
            }
            finally
            {
                // 清理临时目录
                try
                {
                    if (Directory.Exists(workDir))
                        Directory.Delete(workDir, recursive: true);
                }
                catch
                {
                    // 清理失败不影响返回结果
                }
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return GenerateResult.Fail($"生成失败: {ex.Message}", sw.Elapsed);
        }

        async Task ReportProgress(string phase, int percent)
        {
            if (onProgress != null)
                await onProgress(phase, percent);
        }
    }

    /// <summary>
    /// 渲染所有模板文件并写入工作目录
    /// </summary>
    private static async Task RenderTemplatesAsync(GenerateRequest request, string workDir, CancellationToken ct)
    {
        var platformDir = TemplateRenderer.GetPlatformDir(request.Platform);
        var templateFiles = GetTemplateFiles(platformDir);

        foreach (var fileName in templateFiles)
        {
            ct.ThrowIfCancellationRequested();

            var templateText = TemplateRenderer.ReadTemplate(request.Platform, fileName);
            if (templateText == null)
                throw new FileNotFoundException($"模板文件未找到: {platformDir}/{fileName}");

            var rendered = TemplateRenderer.Render(templateText, request);
            var outputName = ReplaceExtension(fileName);

            // MyApp.csproj → {ProjectName}.csproj
            if (outputName == "MyApp.csproj")
                outputName = $"{request.ProjectName}.csproj";

            var outputPath = Path.Combine(workDir, outputName);
            await File.WriteAllTextAsync(outputPath, rendered, ct);
        }
    }

    /// <summary>
    /// 写入 runner.config.json 配置文件
    /// </summary>
    private static async Task WriteConfigAsync(GenerateRequest request, string workDir, CancellationToken ct)
    {
        string configContent;
        string configFileName;

        if (request.ConfigFormat == ConfigFormat.Xml)
        {
            configContent = ConfigLoader.ToXml(request.Config);
            configFileName = "runner.config.xml";
        }
        else
        {
            configContent = ConfigLoader.ToJson(request.Config, writeIndented: true);
            configFileName = "runner.config.json";
        }

        var configPath = Path.Combine(workDir, configFileName);
        await File.WriteAllTextAsync(configPath, configContent, ct);
    }

    /// <summary>
    /// Binary 模式：dotnet publish → 打包 publish 目录（含 manifest 清单）
    /// </summary>
    private static async Task<(byte[] ZipBytes, PackageManifest Manifest)> GenerateBinaryAsync(GenerateRequest request, string workDir, CancellationToken ct)
    {
        var publishDir = Path.Combine(workDir, "publish");

        // 根据宿主类型构建发布选项
        var hostType = request.Platform == TargetPlatform.WinForm ? HostType.WinForms : HostType.Console;
        var isSelfContained = hostType == HostType.Console;

        var buildOptions = new BuildPublishOptions
        {
            Configuration = "Release",
            RuntimeIdentifier = request.RuntimeIdentifier!,
            SelfContained = isSelfContained,
            PublishSingleFile = isSelfContained,
            IncludeNativeLibrariesForSelfExtract = isSelfContained,
            EnableCompressionInSingleFile = isSelfContained,
            OutputDirectory = publishDir,
            HostType = hostType
        };

        var buildResult = await BuildEngine.PublishAsync(workDir, buildOptions, ct);
        if (!buildResult.Success)
            throw new InvalidOperationException(
                $"dotnet publish 失败: {string.Join("; ", buildResult.Errors)}");

        // 写入 README
        var readmePath = Path.Combine(publishDir, "README.txt");
        var readme = BuildReadme(request);
        await File.WriteAllTextAsync(readmePath, readme, ct);

        // 构建设备摘要
        var deviceSummaries = request.Config.Devices?.Select(d => new DeviceSummary
        {
            Code = d.Code,
            Protocol = d.Protocol,
            Ip = d.Ip,
            TagCount = d.Tags?.Count ?? 0
        }).ToList() ?? new List<DeviceSummary>();

        // 打包 + 生成 manifest
        var (zipBytes, manifest) = PackageBuilder.BuildPackage(
            publishDir,
            request.ProjectName,
            request.Version,
            hostType.ToString(),
            request.RuntimeIdentifier!,
            isSelfContained,
            buildResult.FileHashes,
            deviceSummaries);

        return (zipBytes, manifest);
    }

    /// <summary>
    /// Source 模式：直接打包整个项目目录
    /// </summary>
    private static async Task<byte[]> GenerateSourceAsync(GenerateRequest request, string workDir, CancellationToken ct)
    {
        // 写入 build 脚本
        var buildBat = $"""
@echo off
echo Building {request.ProjectName}...
dotnet build -c Release
echo Done. Output: bin\Release\
pause
""";
        await File.WriteAllTextAsync(Path.Combine(workDir, "build.bat"), buildBat, ct);

        var buildSh = $"""
#!/bin/bash
echo "Building {request.ProjectName}..."
dotnet build -c Release
echo "Done. Output: bin/Release/"
""";
        await File.WriteAllTextAsync(Path.Combine(workDir, "build.sh"), buildSh, ct);

        // 写入 README
        var readme = BuildReadme(request);
        await File.WriteAllTextAsync(Path.Combine(workDir, "README.md"), readme, ct);

        return PackageBuilder.PackDirectory(workDir, $"{request.ProjectName}-source.zip", SourceExcludePatterns);
    }

    /// <summary>
    /// 构建 README 使用说明
    /// </summary>
    private static string BuildReadme(GenerateRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {request.ProjectName}");
        sb.AppendLine();
        sb.AppendLine($"## 版本: {request.Version}");
        sb.AppendLine();

        var exeName = request.Platform == TargetPlatform.WinForm
            ? $"{request.ProjectName}.exe"
            : request.ProjectName;

        switch (request.Platform)
        {
            case TargetPlatform.Console:
                sb.AppendLine("## 快速开始");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine($"{exeName}");
                sb.AppendLine();
                sb.AppendLine($"# 指定配置文件");
                sb.AppendLine($"{exeName} runner.config.json");
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("## 停止");
                sb.AppendLine("按 Ctrl+C 优雅退出。");
                break;

            case TargetPlatform.WindowsService:
                sb.AppendLine("## 快速开始");
                sb.AppendLine();
                sb.AppendLine("```bat");
                sb.AppendLine(":: 安装服务（管理员）");
                sb.AppendLine("install.bat");
                sb.AppendLine();
                sb.AppendLine(":: 启动/停止");
                sb.AppendLine($"sc start {request.ProjectName}");
                sb.AppendLine($"sc stop {request.ProjectName}");
                sb.AppendLine();
                sb.AppendLine(":: 卸载服务");
                sb.AppendLine("uninstall.bat");
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("## 日志");
                sb.AppendLine("NLog 日志输出到程序同目录下的 logs/ 文件夹。");
                break;

            case TargetPlatform.LinuxSystemd:
                sb.AppendLine("## 快速开始");
                sb.AppendLine();
                sb.AppendLine("```bash");
                sb.AppendLine("# 安装服务（root）");
                sb.AppendLine("sudo bash install.sh");
                sb.AppendLine();
                sb.AppendLine("# 查看状态/日志");
                sb.AppendLine($"systemctl status {request.ProjectName}");
                sb.AppendLine($"journalctl -u {request.ProjectName} -f");
                sb.AppendLine();
                sb.AppendLine("# 卸载");
                sb.AppendLine("sudo bash uninstall.sh");
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("## 日志");
                sb.AppendLine($"NLog 日志输出到 /opt/{request.ProjectName}/logs/ 目录。");
                break;

            case TargetPlatform.WinForm:
                sb.AppendLine($"双击 {exeName} 启动。");
                sb.AppendLine();
                sb.AppendLine("## 界面说明");
                sb.AppendLine("- 上部左侧: 设备状态表格（实时刷新）");
                sb.AppendLine("- 上部右侧: 执行器配置面板");
                sb.AppendLine("- 下部: 日志查看器");
                sb.AppendLine("- 底部: 启动/停止按钮");
                break;

            case TargetPlatform.Web:
                sb.AppendLine("Web 宿主（预留）。");
                break;
        }

        sb.AppendLine();
        sb.AppendLine("## 配置说明");
        sb.AppendLine("编辑 runner.config.json 修改设备连接参数、标签、执行器。");
        sb.AppendLine();
        sb.AppendLine("## 日志");
        sb.AppendLine("NLog.config 控制日志级别和输出目标。默认输出到 logs/ 目录。");
        sb.AppendLine();
        sb.AppendLine($"## 生成信息");
        sb.AppendLine($"- 平台: {request.Platform}");
        sb.AppendLine($"- 运行时: {request.RuntimeIdentifier}");
        sb.AppendLine($"- 生成时间: {DateTime.UtcNow:O}");
        return sb.ToString();
    }

    /// <summary>
    /// 获取平台目录下的所有模板文件名
    /// 从 ZL.Iot.Runner.Templates 程序集读取 EmbeddedResource
    /// </summary>
    private static string[] GetTemplateFiles(string platformDir)
    {
        var templatesAssembly = TemplateRenderer.LoadTemplatesAssembly();
        if (templatesAssembly == null)
        {
            Console.Error.WriteLine("[Generator] WARNING: ZL.Iot.Runner.Templates assembly not found");
            return Array.Empty<string>();
        }

        // .NET 将嵌入资源的目录分隔符 '-' 规范化为 '_'
        var resourceDir = platformDir.Replace('-', '_');
        var prefix = $"ZL.Iot.Runner.Templates.{resourceDir}.";
        return templatesAssembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .Select(n => n[prefix.Length..])
            .ToArray();
    }

    /// <summary>
    /// 替换文件扩展名（.scriban → 真实扩展名）
    /// 例如: MyApp.csproj.scriban → MyApp.csproj
    ///       ProgramCs.scriban → Program.cs
    /// </summary>
    private static string ReplaceExtension(string fileName)
    {
        if (!fileName.EndsWith(".scriban", StringComparison.Ordinal))
            return fileName;

        var baseName = fileName[..^".scriban".Length];

        // ProgramCs.scriban → Program.cs（因 MSBuild 会将 .cs 误认为区域性代码）
        if (baseName.EndsWith("Cs", StringComparison.Ordinal))
            return baseName[..^2] + ".cs";

        // installSh.scriban → install.sh（因 MSBuild 会将 .sh 误认为区域性代码）
        if (baseName.EndsWith("Sh", StringComparison.Ordinal))
            return baseName[..^2] + ".sh";

        return baseName;
    }
}

/// <summary>
/// 生成器主接口
/// </summary>
public interface IProjectGenerator
{
    Task<GenerateResult> GenerateAsync(GenerateRequest request, CancellationToken ct = default, Func<string, int, Task>? onProgress = null);
}
