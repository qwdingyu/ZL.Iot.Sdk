// ============================================================
//  ZL.Iot.Runner.Generator - GenerateRequest
//  -------------------------------------------------------------
//  生成请求模型：用户在前端选的配置 + 目标平台 + SKU 模式
// ============================================================

using ZL.Iot.Runner.Configuration;

namespace ZL.Iot.Runner.Generator.Core.Models;

/// <summary>
/// 生成请求：用户在前端选的配置 + 目标平台 + SKU 模式
/// </summary>
public class GenerateRequest
{
    /// <summary>
    /// Runner 配置（从 Web 端传来的 JSON，与 ZL.Iot.Runner 的 RunnerConfig 同构）
    /// </summary>
    public RunnerConfig Config { get; set; } = new();

    /// <summary>
    /// 目标平台形态
    /// </summary>
    public TargetPlatform Platform { get; set; } = TargetPlatform.Console;

    /// <summary>
    /// 产物模式：Binary=免费 exe，Source=付费源码
    /// </summary>
    public SkuMode Sku { get; set; } = SkuMode.Binary;

    /// <summary>
    /// 目标运行时标识（仅 Binary 模式必填）
    /// 例如：win-x64 / linux-x64 / osx-x64
    /// </summary>
    public string? RuntimeIdentifier { get; set; }

    /// <summary>
    /// 项目名（用于文件名、zip 名）
    /// </summary>
    public string ProjectName { get; set; } = "MyPlc";

    /// <summary>
    /// 命名空间（可选，为空则从 ProjectName 派生）
    /// 示例: "FactoryA.Line1.Plc01" — C# 命名空间支持点号分组
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// 配置文件格式：Json（默认）/ Xml
    /// </summary>
    public ConfigFormat ConfigFormat { get; set; } = ConfigFormat.Json;

    /// <summary>
    /// 版本号（注入到 .csproj 的 Version 标签）
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 验证请求参数是否合法
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
            throw new ArgumentException("ProjectName 不能为空", nameof(ProjectName));

        if (Sku == SkuMode.Binary && string.IsNullOrWhiteSpace(RuntimeIdentifier))
            throw new ArgumentException("Binary 模式下 RuntimeIdentifier 不能为空", nameof(RuntimeIdentifier));

        if (Sku == SkuMode.Binary && !AllowedRids.Contains(RuntimeIdentifier!))
            throw new ArgumentException(
                $"RuntimeIdentifier '{RuntimeIdentifier}' 不支持，仅支持: {string.Join(", ", AllowedRids)}",
                nameof(RuntimeIdentifier));

        if (Sku == SkuMode.Binary)
        {
            var incompatible = GetIncompatiblePlatformRidError();
            if (incompatible != null)
                throw new ArgumentException(incompatible);
        }

        if (Config.Devices == null || Config.Devices.Count == 0)
            throw new ArgumentException("至少需要配置一个设备", nameof(Config));

        if (!string.IsNullOrWhiteSpace(Namespace))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(Namespace, @"^[a-zA-Z_][a-zA-Z0-9_.]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$"))
                throw new ArgumentException(
                    $"命名空间格式不合法: '{Namespace}'，只允许字母、数字、下划线和点号，且每段必须以字母或下划线开头",
                    nameof(Namespace));
        }
    }

    /// <summary>
    /// 平台与 RID 兼容性检查，不兼容返回错误信息
    /// </summary>
    private string? GetIncompatiblePlatformRidError()
    {
        if (string.IsNullOrEmpty(RuntimeIdentifier))
            return null;

        var rid = RuntimeIdentifier;
        return Platform switch
        {
            TargetPlatform.WinForm =>
                !rid.StartsWith("win-", StringComparison.Ordinal)
                    ? $"WinForm 仅支持 Windows 运行时 (win-x64 / win-x86)，当前值: {rid}"
                    : null,
            TargetPlatform.WindowsService =>
                !rid.StartsWith("win-", StringComparison.Ordinal)
                    ? $"Windows 服务仅支持 Windows 运行时 (win-x64 / win-x86)，当前值: {rid}"
                    : null,
            TargetPlatform.LinuxSystemd =>
                !rid.StartsWith("linux-", StringComparison.Ordinal)
                    ? $"Linux systemd 仅支持 Linux 运行时 (linux-x64 / linux-arm64 / linux-musl-x64)，当前值: {rid}"
                    : null,
            _ => null // Console 和 Web 支持所有 RID
        };
    }

    /// <summary>
    /// 允许的目标运行时标识
    /// </summary>
    public static readonly string[] AllowedRids =
    [
        "win-x64",
        "win-x86",
        "linux-x64",
        "linux-arm64",
        "linux-musl-x64",
        "osx-x64",
        "osx-arm64"
    ];
}

/// <summary>
/// 目标平台形态
/// </summary>
public enum TargetPlatform
{
    Console = 0,
    WindowsService = 1,
    LinuxSystemd = 2,
    WinForm = 3,
    Web = 4
}

/// <summary>
/// 商业化 SKU 模式
/// </summary>
public enum SkuMode
{
    /// <summary>
    /// Binary：dotnet publish 输出（免费用户）
    /// </summary>
    Binary = 0,

    /// <summary>
    /// Source：完整 .sln + .csproj + .cs（付费用户）
    /// </summary>
    Source = 1
}

/// <summary>
/// 配置文件格式
/// </summary>
public enum ConfigFormat
{
    Json = 0,
    Xml = 1
}
