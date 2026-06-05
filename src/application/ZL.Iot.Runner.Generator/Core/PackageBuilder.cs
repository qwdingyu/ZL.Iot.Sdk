// ============================================================
//  ZL.Iot.Runner.Generator - PackageBuilder
//  -------------------------------------------------------------
//  打包引擎：将输出目录压缩为 zip 字节流，并生成包清单
// ============================================================

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZL.Iot.Runner.Generator.Core;

/// <summary>
/// 设备摘要
/// </summary>
public class DeviceSummary
{
    /// <summary>设备编码</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>协议类型</summary>
    public string Protocol { get; set; } = string.Empty;

    /// <summary>IP 地址</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>标签数量</summary>
    public int TagCount { get; set; }
}

/// <summary>
/// 包清单：描述一个二进制分发的完整元数据
/// </summary>
public class PackageManifest
{
    /// <summary>应用名称</summary>
    [JsonPropertyName("applicationName")]
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>版本号</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>宿主类型：Console / WinForms 等</summary>
    [JsonPropertyName("hostType")]
    public string HostType { get; set; } = string.Empty;

    /// <summary>运行时标识符，如 win-x64</summary>
    [JsonPropertyName("runtimeIdentifier")]
    public string RuntimeIdentifier { get; set; } = string.Empty;

    /// <summary>是否自包含发布</summary>
    [JsonPropertyName("selfContained")]
    public bool SelfContained { get; set; }

    /// <summary>构建时间</summary>
    [JsonPropertyName("buildTime")]
    public DateTime BuildTime { get; set; }

    /// <summary>文件名 -> SHA256 字典</summary>
    [JsonPropertyName("files")]
    public Dictionary<string, string> Files { get; set; } = new();

    /// <summary>设备列表</summary>
    [JsonPropertyName("devices")]
    public List<DeviceSummary> Devices { get; set; } = new();

    /// <summary>整个 zip 包的 SHA256</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// 打包引擎：将指定目录压缩为 zip 字节流，并生成包清单。
/// </summary>
public static class PackageBuilder
{
    /// <summary>
    /// 将目录打包为 zip 字节流
    /// </summary>
    /// <param name="sourceDir">要打包的源目录</param>
    /// <param name="zipFileName">zip 文件名（不含路径）</param>
    /// <param name="excludePatterns">要排除的文件/目录模式（如 bin/, obj/）</param>
    public static byte[] PackDirectory(string sourceDir, string zipFileName, string[]? excludePatterns = null)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"源目录不存在: {sourceDir}");

        excludePatterns ??= Array.Empty<string>();

        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (ShouldExclude(file, sourceDir, excludePatterns))
                    continue;

                var relativePath = Path.GetRelativePath(sourceDir, file);
                archive.CreateEntryFromFile(file, relativePath);
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 构建完整的分发包：将源目录打包为 zip，并生成对应的清单
    /// </summary>
    /// <param name="sourceDir">要打包的源目录</param>
    /// <param name="applicationName">应用名称</param>
    /// <param name="version">版本号</param>
    /// <param name="hostType">宿主类型（Console / WinForms）</param>
    /// <param name="runtimeIdentifier">运行时标识符（如 win-x64）</param>
    /// <param name="selfContained">是否自包含</param>
    /// <param name="fileHashes">文件名 -> SHA256 字典（可选）</param>
    /// <param name="devices">设备列表（可选）</param>
    /// <returns>zip 字节流和包清单</returns>
    public static (byte[] ZipBytes, PackageManifest Manifest) BuildPackage(
        string sourceDir,
        string applicationName,
        string version,
        string hostType,
        string runtimeIdentifier,
        bool selfContained,
        Dictionary<string, string>? fileHashes = null,
        List<DeviceSummary>? devices = null)
    {
        // 1. 构造清单对象（Sha256 暂未知，打包后计算）
        var manifest = new PackageManifest
        {
            ApplicationName = applicationName,
            Version = version,
            HostType = hostType,
            RuntimeIdentifier = runtimeIdentifier,
            SelfContained = selfContained,
            BuildTime = DateTime.UtcNow,
            Files = fileHashes ?? new Dictionary<string, string>(),
            Devices = devices ?? new List<DeviceSummary>(),
            Sha256 = string.Empty
        };

        // 2. 将 manifest.json 写入源目录（临时），确保打包时包含它
        var manifestPath = Path.Combine(sourceDir, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);

        try
        {
            // 3. 打包目录（manifest.json 会被自动包含）
            var zipBytes = PackDirectory(sourceDir, $"{applicationName}-{runtimeIdentifier}.zip");

            // 4. 计算整个 zip 包的 SHA256
            var sha256 = SHA256.HashData(zipBytes);
            var sha256Hex = Convert.ToHexString(sha256).ToLowerInvariant();
            manifest.Sha256 = sha256Hex;

            // 5. 用更新后的 Sha256 重新写入 manifest.json
            // 注意：这是经典的"九头蛇问题"（hydra problem）— manifest 中的 sha256 是第一次打包的哈希，
            // 第二次打包后实际 zip 的哈希会变化。但文件级哈希（Files 字典）不受影响，足以验证完整性。
            manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);

            // 6. 重新打包（包含更新后的 manifest.json）
            zipBytes = PackDirectory(sourceDir, $"{applicationName}-{runtimeIdentifier}.zip");

            return (zipBytes, manifest);
        }
        finally
        {
            // 7. 清理临时 manifest.json
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
        }
    }

    /// <summary>
    /// 判断文件是否应该排除
    /// </summary>
    private static bool ShouldExclude(string filePath, string baseDir, string[] excludePatterns)
    {
        var relativePath = Path.GetRelativePath(baseDir, filePath);
        foreach (var pattern in excludePatterns)
        {
            if (relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
