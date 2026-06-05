using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Core.Services;

/// <summary>
/// 一键流水线编排：Scaffold → Build → Package
/// </summary>
public class PipelineOrchestrator
{
    private readonly ScaffoldEngine _scaffoldEngine;
    private readonly BuildEngine _buildEngine;
    private readonly PackageBuilder _packageBuilder;
    private readonly ILogger<PipelineOrchestrator>? _logger;

    public PipelineOrchestrator(
        ScaffoldEngine scaffoldEngine,
        BuildEngine buildEngine,
        PackageBuilder packageBuilder,
        ILogger<PipelineOrchestrator>? logger = null)
    {
        _scaffoldEngine = scaffoldEngine;
        _buildEngine = buildEngine;
        _packageBuilder = packageBuilder;
        _logger = logger;
    }

    /// <summary>
    /// 执行完整流水线：生成项目 → 编译发布 → 打包 zip
    /// </summary>
    /// <param name="options">构建发布选项</param>
    /// <param name="templateDirectory">模板目录路径</param>
    /// <param name="workDirectory">工作目录（生成中间项目文件）</param>
    /// <returns>构建结果，含 zip 路径</returns>
    public BuildResult RunPipeline(BuildPublishOptions options, string templateDirectory, string? workDirectory = null)
    {
        var sw = Stopwatch.StartNew();

        _logger?.LogInformation(
            "[Pipeline] Starting full pipeline: {AppName} v{Version} | {HostType} | {RID}",
            options.ApplicationName, options.Version, options.HostType, options.RuntimeIdentifier);

        // 应用宿主类型默认策略
        options.ApplyHostTypeDefaults();

        // 工作目录结构:
        //   workDirectory/
        //     project/      ← 生成的 .csproj + 源码
        //     publish/      ← dotnet publish 输出
        //     packages/     ← 最终 zip 包
        workDirectory ??= Path.Combine(Path.GetTempPath(), $"runner-gen-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(workDirectory, "project");
        var publishDir = Path.Combine(workDirectory, "publish");
        var packagesDir = Path.Combine(workDirectory, "packages");

        Directory.CreateDirectory(workDirectory);
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(publishDir);
        Directory.CreateDirectory(packagesDir);

        try
        {
            // Phase 1: Scaffold — 生成项目
            _logger?.LogInformation("[Pipeline] Phase 1/3: Scaffolding project...");
            var scaffoldOpts = new ScaffoldOptions
            {
                ApplicationName = options.ApplicationName,
                Version = options.Version,
                OutputDirectory = projectDir,
                RuntimeIdentifier = options.RuntimeIdentifier,
                HostType = options.HostType
            };
            _scaffoldEngine.Generate(scaffoldOpts, templateDirectory);

            // Phase 2: Build & Publish
            _logger?.LogInformation("[Pipeline] Phase 2/3: Building & publishing...");
            options.OutputDirectory = publishDir;
            var projectFile = Path.Combine(projectDir, $"{options.ApplicationName}.csproj");

            if (!File.Exists(projectFile))
                throw new FileNotFoundException($"Generated project file not found: {projectFile}");

            var buildResult = _buildEngine.Publish(projectFile, options);

            if (!buildResult.Success)
            {
                _logger?.LogError("[Pipeline] Build failed. Errors: {Errors}", string.Join("; ", buildResult.Errors));
                buildResult.BuildDuration = sw.Elapsed;
                return buildResult;
            }

            // Phase 3: Package
            _logger?.LogInformation("[Pipeline] Phase 3/3: Packaging...");
            options.OutputDirectory = publishDir; // 确保 PackageBuilder 使用正确路径
            var (zipPath, manifest) = _packageBuilder.CreatePackage(publishDir, options);

            // 移动 zip 到 packages 目录
            var finalZipPath = Path.Combine(packagesDir, Path.GetFileName(zipPath));
            if (File.Exists(finalZipPath))
                File.Delete(finalZipPath);
            File.Move(zipPath, finalZipPath);

            buildResult.PackagePath = finalZipPath;
            buildResult.PackageSizeBytes = new FileInfo(finalZipPath).Length;

            _logger?.LogInformation(
                "[Pipeline] Pipeline complete! Package: {Package} ({Size:F2} MB) | Total: {Duration:F1}s",
                finalZipPath, buildResult.PackageSizeBytes / 1024.0 / 1024.0, sw.Elapsed.TotalSeconds);

            buildResult.BuildDuration = sw.Elapsed;
            return buildResult;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Pipeline] Pipeline failed");
            return new BuildResult
            {
                Success = false,
                Errors = [ex.Message],
                BuildDuration = sw.Elapsed
            };
        }
    }
}
