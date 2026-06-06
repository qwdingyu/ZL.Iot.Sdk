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
    /// 调用 dotnet publish 编译项目（兼容旧签名，同步）
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
    /// 异步调用 dotnet restore + dotnet publish（推荐在 JobScheduler 中使用）
    /// </summary>
    public static async Task<BuildResult> PublishAsync(string projectDir, BuildPublishOptions options, CancellationToken ct = default)
    {
        var csprojPath = FindCsproj(projectDir);
        if (csprojPath == null)
        {
            return new BuildResult
            {
                Success = false,
                Errors = new[] { $"在 {projectDir} 下未找到 .csproj 文件" }
            };
        }

        // 1) 先执行 dotnet restore，确保 NuGet 包可用
        var restoreResult = await RunDotnetAsync(projectDir, csprojPath, "restore", "--nologo", ct: ct);
        if (!restoreResult)
        {
            return new BuildResult
            {
                Success = false,
                Errors = new[] { "dotnet restore 失败，请检查 NuGet.config 和网络连接" }
            };
        }

        // 2) 执行 dotnet publish
        return Publish(projectDir, options, ct);
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

        string? outputDir = null;
        try
        {
            var csprojPath = FindCsproj(projectDir);
            if (csprojPath == null)
            {
                result.Success = false;
                result.Errors = new[] { $"在 {projectDir} 下未找到 .csproj 文件" };
                result.BuildDuration = sw.Elapsed;
                if (Directory.Exists(outputDir))
                {
                    try { Directory.Delete(outputDir, recursive: true); }
                    catch { /* cleanup best-effort */ }
                }
                return result;
            }

            outputDir = options.OutputDirectory
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

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(ct);

            var waitForExitTask = Task.Run(() => process.WaitForExit(), ct);

            var completed = Task.WhenAny(waitForExitTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct));
            completed.Wait(ct);

            var wasKilled = false;
            if (!process.HasExited)
            {
                wasKilled = true;
                process.Kill(true);
                process.WaitForExit(2000);
            }

            // 等待流读取完成
            try { Task.WaitAll(new[] { outputTask, errorTask }, 5000); }
            catch { /* 忽略 */ }

            // 将结果写入 StringBuilder
            try { outputBuilder.Append(outputTask.Result); } catch { }
            try { errorBuilder.Append(errorTask.Result); } catch { }

            if (wasKilled)
            {
                result.Success = false;
                result.Errors = new[]
                {
                    $"dotnet publish 在 {timeoutSeconds} 秒内未完成，进程已被终止。" +
                    $"标准错误: {errorBuilder.ToString().Truncate(500)}"
                };
                result.BuildDuration = sw.Elapsed;
                if (Directory.Exists(outputDir))
                {
                    try { Directory.Delete(outputDir, recursive: true); }
                    catch { /* cleanup best-effort */ }
                }
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
                if (Directory.Exists(outputDir))
                {
                    try { Directory.Delete(outputDir, recursive: true); }
                    catch { /* cleanup best-effort */ }
                }
                return result;
            }

            // 从 stdout/stderr 中提取警告
            var warningLines = new List<string>();
            foreach (var line in outputBuilder.ToString().Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("warn:", StringComparison.Ordinal))
                    warningLines.Add(trimmed);
            }
            foreach (var line in errorBuilder.ToString().Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("warn:", StringComparison.Ordinal))
                    warningLines.Add(trimmed);
            }
            result.Warnings = warningLines.ToArray();

            // 计算输出文件的 SHA256 哈希
            var hashes = new Dictionary<string, string>();
            long totalSize = 0;
            if (Directory.Exists(outputDir))
            {
                foreach (var file in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(outputDir, file);
                    using var stream = File.OpenRead(file);
                    var hash = SHA256.HashData(stream);
                    var hashString = Convert.ToHexString(hash).ToLowerInvariant();
                    hashes[relativePath] = hashString;
                    totalSize += stream.Length;
                }
            }

            result.Success = true;
            result.OutputPath = outputDir;
            result.FileHashes = hashes;
            result.PackageSizeBytes = totalSize;
            result.BuildDuration = sw.Elapsed;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Errors = new[] { "构建操作已被取消" };
            result.BuildDuration = sw.Elapsed;
            if (Directory.Exists(outputDir))
            {
                try { Directory.Delete(outputDir, recursive: true); }
                catch { /* cleanup best-effort */ }
            }
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors = new[] { ex.Message };
            result.BuildDuration = sw.Elapsed;
            if (Directory.Exists(outputDir))
            {
                try { Directory.Delete(outputDir, recursive: true); }
                catch { /* cleanup best-effort */ }
            }
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

    /// <summary>
    /// 运行 dotnet 命令（用于 restore 等辅助操作）
    /// </summary>
    private static async Task<bool> RunDotnetAsync(string workingDir, string projectPath, string command, string? extraArgs = null, CancellationToken ct = default)
    {
        var args = new List<string> { command, projectPath, "--nologo" };
        if (!string.IsNullOrEmpty(extraArgs))
            args.AddRange(extraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 dotnet 进程");

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        var completed = await Task.WhenAny(Task.Run(() => process.WaitForExit(), ct), Task.Delay(TimeSpan.FromSeconds(60), ct));
        if (!process.HasExited)
        {
            process.Kill(true);
            process.WaitForExit(2000);
        }

        // 等待流读取完成，避免管道未关闭前就返回
        try { await Task.WhenAll(outputTask, errorTask); } catch { /* 管道已关闭，忽略 */ }

        ct.ThrowIfCancellationRequested();
        return process.ExitCode == 0;
    }
}
