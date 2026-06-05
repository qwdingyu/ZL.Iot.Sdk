using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Core.Services;

/// <summary>
/// 调用 dotnet publish 执行构建与发布
/// </summary>
public class BuildEngine
{
    private readonly ILogger<BuildEngine>? _logger;

    public BuildEngine(ILogger<BuildEngine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 对指定 .csproj 执行 dotnet publish
    /// </summary>
    public BuildResult Publish(string projectPath, BuildPublishOptions options)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            _logger?.LogInformation(
                "[BuildEngine] Starting publish: {Project} | {Configuration} | {RID} | SelfContained={SelfContained} | SingleFile={SingleFile}",
                Path.GetFileName(projectPath), options.Configuration,
                options.RuntimeIdentifier, options.SelfContained, options.PublishSingleFile);

            // 构建 publish 命令参数
            var args = new StringBuilder();
            args.Append($"publish \"{projectPath}\"");
            args.Append($" -c {options.Configuration}");
            args.Append($" -r {options.RuntimeIdentifier}");
            args.Append($" --self-contained {(options.SelfContained ? "true" : "false")}");
            args.Append($" -p:PublishSingleFile={(options.PublishSingleFile ? "true" : "false")}");
            args.Append($" -p:IncludeNativeLibrariesForSelfExtract=true");
            args.Append($" -p:EnableCompressionInSingleFile={(options.CompressionEnabled ? "true" : "false")}");
            args.Append($" -p:UseAppHost=true");
            args.Append(" -nologo");
            args.Append($" -o \"{options.OutputDirectory}\"");

            _logger?.LogDebug("[BuildEngine] Command: dotnet {Args}", args);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start dotnet process.");

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            var outputWait = new TaskCompletionSource();
            var errorWait = new TaskCompletionSource();

            void OnOutput(object sender, DataReceivedEventArgs e)
            {
                if (e.Data is null) { outputWait.TrySetResult(); return; }
                outputBuilder.AppendLine(e.Data);
                if (e.Data.Contains("warning CS", StringComparison.OrdinalIgnoreCase) ||
                    e.Data.Contains("warning NU", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(e.Data.Trim());
                }
            }

            void OnError(object sender, DataReceivedEventArgs e)
            {
                if (e.Data is null) { errorWait.TrySetResult(); return; }
                errorBuilder.AppendLine(e.Data);
                if (e.Data.Contains("error CS", StringComparison.OrdinalIgnoreCase) ||
                    e.Data.Contains("error NU", StringComparison.OrdinalIgnoreCase) ||
                    e.Data.Contains("error MSB", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(e.Data.Trim());
                }
            }

            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnError;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var exited = process.WaitForExit(600_000); // 10 minutes timeout
            if (!exited)
            {
                process.Kill(true);
                errors.Add("Build timeout after 10 minutes.");
            }

            var success = process.ExitCode == 0;

            _logger?.LogInformation(
                "[BuildEngine] Publish {Status}: ExitCode={ExitCode}, Duration={Duration}s, Warnings={WarningCount}, Errors={ErrorCount}",
                success ? "SUCCESS" : "FAILED", process.ExitCode,
                sw.Elapsed.TotalSeconds, warnings.Count, errors.Count);

            // 计算输出文件哈希
            var fileHashes = new Dictionary<string, string>();
            if (success && Directory.Exists(options.OutputDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(options.OutputDirectory, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(options.OutputDirectory, file);
                    fileHashes[relativePath] = ComputeSha256(file);
                }
            }

            return new BuildResult
            {
                Success = success,
                OutputPath = options.OutputDirectory,
                PackagePath = string.Empty,
                PackageSizeBytes = 0,
                FileHashes = fileHashes,
                Warnings = [.. warnings],
                Errors = [.. errors],
                BuildDuration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BuildEngine] Publish failed with exception");
            errors.Add(ex.Message);
            return new BuildResult
            {
                Success = false,
                OutputPath = options.OutputDirectory,
                Errors = [.. errors],
                BuildDuration = sw.Elapsed
            };
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
