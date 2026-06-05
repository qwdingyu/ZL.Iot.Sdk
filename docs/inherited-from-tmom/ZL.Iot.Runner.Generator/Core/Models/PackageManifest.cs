using System.Text.Json.Serialization;

namespace ZL.Iot.Runner.Generator.Core.Models;

/// <summary>
/// 设备摘要信息
/// </summary>
public class DeviceSummary
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
}

/// <summary>
/// 打包清单 — 描述产物的版本、哈希、依赖信息
/// </summary>
public class PackageManifest
{
    [JsonPropertyName("applicationName")]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("hostType")]
    public string HostType { get; set; } = "Console";

    [JsonPropertyName("runtimeIdentifier")]
    public string RuntimeIdentifier { get; set; } = "win-x64";

    [JsonPropertyName("selfContained")]
    public bool SelfContained { get; set; }

    [JsonPropertyName("buildTime")]
    public DateTime BuildTime { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("files")]
    public Dictionary<string, string> Files { get; set; } = new();

    [JsonPropertyName("devices")]
    public List<DeviceSummary> Devices { get; set; } = new();

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
