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
    /// 项目名（用于命名空间、文件名、zip 名）
    /// </summary>
    public string ProjectName { get; set; } = "MyPlc";

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

        if (Config.Devices == null || Config.Devices.Count == 0)
            throw new ArgumentException("至少需要配置一个设备", nameof(Config));
    }

    /// <summary>
    /// 允许的目标运行时标识
    /// </summary>
    public static readonly string[] AllowedRids =
    [
        "win-x64",
        "linux-x64",
        "osx-x64"
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
