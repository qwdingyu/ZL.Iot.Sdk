// ============================================================
//  ZL.Iot.Runner.Generator - BuildEngine
//  -------------------------------------------------------------
//  调用 dotnet publish 编译生成 self-contained 可执行文件
// ============================================================

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ZL.Iot.Runner.Generator.Core;

/// <summary>
/// 主机类型
/// </summary>
public enum HostType
{
    /// <summary>控制台应用</summary>
    Console = 0,
    /// <summary>WinForms 应用</summary>
    WinForms = 1
}

/// <summary>
/// 发布选项
/// </summary>
public class BuildPublishOptions
{
    /// <summary>编译配置，默认 Release</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>目标运行时标识符，默认 win-x64</summary>
    public string RuntimeIdentifier { get; set; } = "win-x64";

    /// <summary>是否自包含发布，默认 true</summary>
    public bool SelfContained { get; set; } = true;

    /// <summary>是否发布为单文件，默认 true</summary>
    public bool PublishSingleFile { get; set; } = true;

    /// <summary>是否包含本机库以便自解压，默认 true</summary>
    public bool IncludeNativeLibrariesForSelfExtract { get; set; } = true;

    /// <summary>是否在单文件中启用压缩，默认 true</summary>
    public bool EnableCompressionInSingleFile { get; set; } = true;

    /// <summary>输出目录（null 则使用默认 publish 目录）</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>主机类型</summary>
    public HostType HostType { get; set; } = HostType.Console;
}

/// <summary>
/// 构建结果
/// </summary>
public class BuildResult
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>输出目录路径</summary>
    public string? OutputPath { get; set; }

    /// <summary>包文件路径</summary>
    public string? PackagePath { get; set; }

    /// <summary>包大小（字节）</summary>
    public long PackageSizeBytes { get; set; }

    /// <summary>输出文件 SHA256 哈希映射</summary>
    public Dictionary<string, string> FileHashes { get; set; } = new();

    /// <summary>警告信息列表</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();

    /// <summary>错误信息列表</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();

    /// <summary>构建耗时</summary>
    public TimeSpan BuildDuration { get; set; }
}

/// <summary>
/// 构建引擎：调用 dotnet publish 编译项目。
/// 仅在 Binary SKU 模式下使用。
/// </summary>
public static class BuildEngine
{
    /// <summary>
    /// 自包含发布超时时间（秒）
    /// </summary>
    private const int SelfContainedTimeoutSeconds = 180;

    /// <summary>
    /// 框架依赖发布超时时间（秒）
    /// </summary>
    private const int FrameworkDependentTimeoutSeconds = 120;

    /// <summary>
    /// 调用 dotnet publish 编译项目（兼容旧签名）
    /// </summary>
    /// <param name="projectDir">项目根目录（包含 .csproj 的目录）</param>
    /// <param name="runtimeIdentifier">目标 RID，如 win-x64</param>
    /// <param name="outputDir">输出目录（publish 产物存放位置）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>publish 输出目录的绝对路径</returns>
    public static string Publish(string projectDir, string runtimeIdentifier, string outputDir, CancellationToken ct = default)
    {
        var options = new BuildPublishOptions
        {
            RuntimeIdentifier = runtimeIdentifier,
            OutputDirectory = outputDir
        };
        var result = Publish(projectDir, options, ct);
        if (!result.Success)
            throw new InvalidOperationException(
                $"dotnet publish 失败。错误: {string.Join("; ", result.Errors)}");
        return result.OutputPath!;
    }

    /// <summary>
    /// 调用 dotnet publish 编译项目（新签名）
    /// </summary>
    /// <param name="projectDir">项目根目录（包含 .csproj 的目录）</param>
    /// <param name="options">发布选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>构建结果</returns>
    public static BuildResult Publish(string projectDir, BuildPublishOptions options, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new BuildResult();

        try
        {
            var csprojPath = FindCsproj(projectDir);
            if (csprojPath == null)
            {
                result.Success = false;
                result.Errors = new[] { $"在 {projectDir} 下未找到 .csproj 文件" };
                result.BuildDuration = sw.Elapsed;
                return result;
            }

            var outputDir = options.OutputDirectory
                ?? Path.Combine(projectDir, "bin", "publish", options.RuntimeIdentifier);

            var args = new List<string>
            {
                "publish",
                csprojPath,
                "-c", options.Configuration,
                "-r", options.RuntimeIdentifier,
                "--nologo"
            };

            if (options.HostType == HostType.Console)
            {
                args.Add("--self-contained");
                args.Add(options.SelfContained ? "true" : "false");
                args.Add($"-p:PublishSingleFile={(options.PublishSingleFile ? "true" : "false")}");
                if (options.IncludeNativeLibrariesForSelfExtract)
                    args.Add("-p:IncludeNativeLibrariesForSelfExtract=true");
                if (options.EnableCompressionInSingleFile)
                    args.Add("-p:EnableCompressionInSingleFile=true");
            }
            else // WinForms
            {
                args.Add("--self-contained");
                args.Add("false");
                args.Add("-p:PublishSingleFile=false");
                args.Add("-p:UseAppHost=true");
            }

            args.Add("-o");
            args.Add(outputDir);

            var timeoutSeconds = options.HostType == HostType.Console && options.SelfContained
                ? SelfContainedTimeoutSeconds
                : FrameworkDependentTimeoutSeconds;

            // 使用 ArgumentList 安全传递参数（.NET 7+，正确处理含空格/引号/反斜杠的路径）
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 dotnet 进程，请确认 .NET SDK 已安装且 dotnet 在 PATH 中");

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line != null) outputBuilder.AppendLine(line);
                }
            }, ct);

            var errorTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await process.StandardError.ReadLineAsync();
                    if (line != null) errorBuilder.AppendLine(line);
                }
            }, ct);

            var waitForExitTask = Task.Run(() => process.WaitForExit(), ct);

            var completed = Task.WhenAny(waitForExitTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct));
            completed.Wait(ct);

            var wasKilled = false;
            if (!process.HasExited)
            {
                wasKilled = true;
                process.Kill(true);
                // Kill(true) 后等待进程真正退出，避免后续访问 ExitCode 时异常
                process.WaitForExit(2000);
            }

            // 等待所有流读取任务完成后再访问 outputBuilder/errorBuilder，避免数据竞争
            // Kill(true) 后管道可能不会立即关闭，加 5 秒超时防止无限 hang
            if (!Task.WaitAll(new[] { outputTask, errorTask }, 5000))
            {
                // 流读取超时 — 用已有的输出继续，不阻塞
            }

            if (wasKilled)
            {
                result.Success = false;
                result.Errors = new[]
                {
                    $"dotnet publish 在 {timeoutSeconds} 秒内未完成，进程已被终止。" +
                    $"标准错误: {errorBuilder.ToString().Truncate(500)}"
                };
                result.BuildDuration = sw.Elapsed;
                return result;
            }

            if (process.ExitCode != 0)
            {
                result.Success = false;
                result.Errors = new[]
                {
                    $"dotnet publish 失败 (退出码 {process.ExitCode})。" +
                    $"标准输出: {outputBuilder.ToString().Truncate(300)}" +
                    $"标准错误: {errorBuilder.ToString().Truncate(500)}"
                };
                result.BuildDuration = sw.Elapsed;
                return result;
            }

            // 计算输出文件的 SHA256 哈希
            var hashes = new Dictionary<string, string>();
            if (Directory.Exists(outputDir))
            {
                foreach (var file in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(outputDir, file);
                    using var stream = File.OpenRead(file);
                    var hash = SHA256.HashData(stream);
                    var hashString = Convert.ToHexString(hash).ToLowerInvariant();
                    hashes[relativePath] = hashString;
                }
            }

            result.Success = true;
            result.OutputPath = outputDir;
            result.FileHashes = hashes;
            result.BuildDuration = sw.Elapsed;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Errors = new[] { "构建操作已被取消" };
            result.BuildDuration = sw.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors = new[] { ex.Message };
            result.BuildDuration = sw.Elapsed;
            return result;
        }
    }

    /// <summary>
    /// 在目录下查找 .csproj 文件
    /// </summary>
    private static string? FindCsproj(string dir)
    {
        var files = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);
        return files.Length > 0 ? files[0] : null;
    }

    /// <summary>
    /// 截断字符串（保留前 N 个字符）
    /// </summary>
    private static string Truncate(this string s, int maxLen)
    {
        return s.Length <= maxLen ? s : s[..maxLen] + "...";
    }
}
