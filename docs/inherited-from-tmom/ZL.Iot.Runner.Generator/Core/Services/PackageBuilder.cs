using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Core.Services;

/// <summary>
/// 将发布输出打包为 zip 并生成 manifest.json
/// </summary>
public class PackageBuilder
{
    private readonly ILogger<PackageBuilder>? _logger;

    public PackageBuilder(ILogger<PackageBuilder>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 对 publish 输出目录打包为 zip，并生成 manifest.json
    /// </summary>
    /// <param name="publishDirectory">dotnet publish 输出目录</param>
    /// <param name="options">打包选项</param>
    /// <returns>zip 文件路径和 manifest 信息</returns>
    public (string ZipPath, PackageManifest Manifest) CreatePackage(string publishDirectory, BuildPublishOptions options)
    {
        _logger?.LogInformation(
            "[PackageBuilder] Creating package from: {Dir} | {AppName} v{Version}",
            publishDirectory, options.ApplicationName, options.Version);

        if (!Directory.Exists(publishDirectory))
            throw new DirectoryNotFoundException($"Publish directory not found: {publishDirectory}");

        // 生成 manifest
        var manifest = new PackageManifest
        {
            ApplicationName = options.ApplicationName,
            Version = options.Version,
            HostType = options.HostType.ToString(),
            RuntimeIdentifier = options.RuntimeIdentifier,
            SelfContained = options.SelfContained,
            BuildTime = DateTime.UtcNow,
            Devices = new List<DeviceSummary>()
        };

        // 计算各文件 SHA256
        var files = Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(publishDirectory, file);
            // 跳过 manifest 自身
            if (relativePath == "manifest.json") continue;
            manifest.Files[relativePath] = ComputeSha256(file);
        }

        // 构建 zip 文件名: {AppName}_v{Version}_{RID}_{hostType}.zip
        var hostSuffix = options.HostType == HostType.Console ? "console" : "winforms";
        var zipFileName = $"{options.ApplicationName}_v{options.Version}_{options.RuntimeIdentifier}_{hostSuffix}.zip";
        var zipPath = Path.Combine(Path.GetDirectoryName(publishDirectory)!, zipFileName);

        // 如果已存在则删除
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        // 创建 zip
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(publishDirectory, file);
                archive.CreateEntryFromFile(file, relativePath);
            }

            // 写入 manifest.json
            var manifestEntry = archive.CreateEntry("manifest.json");
            using var manifestStream = manifestEntry.Open();
            using var writer = new StreamWriter(manifestStream);
            var manifestJsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var manifestJson = JsonSerializer.Serialize(manifest, manifestJsonOptions);
            writer.Write(manifestJson);
        }

        // 计算整体包哈希
        manifest.Sha256 = ComputeSha256(zipPath);

        // 将 manifest 也写入 publish 目录
        var manifestPath = Path.Combine(publishDirectory, "manifest.json");
        var manifestWriteOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, manifestWriteOptions));

        var zipSize = new FileInfo(zipPath).Length;
        _logger?.LogInformation(
            "[PackageBuilder] Package created: {ZipPath} ({Size:F2} MB) | {FileCount} files",
            zipPath, zipSize / 1024.0 / 1024.0, manifest.Files.Count);

        return (zipPath, manifest);
    }

    /// <summary>
    /// 验证 zip 包完整性（对比 manifest 中的 SHA256）
    /// </summary>
    public bool VerifyPackage(string zipPath)
    {
        var actualHash = ComputeSha256(zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("manifest.json not found in package.");

        PackageManifest? manifest = null;
        using var entryStream = manifestEntry.Open();
        using var reader = new StreamReader(entryStream);
        manifest = JsonSerializer.Deserialize<PackageManifest>(reader.ReadToEnd());

        var isValid = string.Equals(actualHash, manifest?.Sha256, StringComparison.OrdinalIgnoreCase);
        _logger?.LogInformation(
            "[PackageBuilder] Verification {Status}: {ZipPath}",
            isValid ? "PASSED" : "FAILED", zipPath);

        return isValid;
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
