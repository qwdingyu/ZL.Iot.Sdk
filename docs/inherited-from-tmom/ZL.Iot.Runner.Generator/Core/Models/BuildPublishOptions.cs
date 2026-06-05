namespace ZL.Iot.Runner.Generator.Core.Models;

/// <summary>
/// 宿主类型：控制台（生产部署）或 WinForms（调试场景）
/// </summary>
public enum HostType
{
    Console,
    WinForms
}

/// <summary>
/// 构建与发布选项
/// </summary>
public class BuildPublishOptions
{
    /// <summary>编译配置，默认 Release</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>目标运行时标识符，默认 win-x64</summary>
    public string RuntimeIdentifier { get; set; } = "win-x64";

    /// <summary>是否自包含发布（自带 .NET Runtime）</summary>
    public bool SelfContained { get; set; } = true;

    /// <summary>是否发布为单文件</summary>
    public bool PublishSingleFile { get; set; } = true;

    /// <summary>是否启用单文件内压缩</summary>
    public bool CompressionEnabled { get; set; } = true;

    /// <summary>发布输出目录</summary>
    public required string OutputDirectory { get; set; }

    /// <summary>宿主类型</summary>
    public HostType HostType { get; set; } = HostType.Console;

    /// <summary>应用名称（生成的项目名）</summary>
    public string ApplicationName { get; set; } = "RunnerApp";

    /// <summary>应用版本</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 根据宿主类型自动调整发布策略：
    /// - Console: 自包含 + 单文件（方便无 .NET 环境部署）
    /// - WinForms: 框架依赖 + 多文件（减小体积，保留资源文件）
    /// </summary>
    public void ApplyHostTypeDefaults()
    {
        switch (HostType)
        {
            case HostType.Console:
                SelfContained = true;
                PublishSingleFile = true;
                CompressionEnabled = true;
                break;
            case HostType.WinForms:
                SelfContained = false;
                PublishSingleFile = false;
                CompressionEnabled = false;
                break;
        }
    }
}

/// <summary>
/// 构建结果
/// </summary>
public class BuildResult
{
    /// <summary>构建是否成功</summary>
    public bool Success { get; set; }

    /// <summary>发布输出路径</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>打包产物路径（zip）</summary>
    public string PackagePath { get; set; } = string.Empty;

    /// <summary>包大小（字节）</summary>
    public long PackageSizeBytes { get; set; }

    /// <summary>各文件 SHA256 哈希</summary>
    public Dictionary<string, string> FileHashes { get; set; } = new();

    /// <summary>构建警告列表</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();

    /// <summary>构建错误列表</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();

    /// <summary>构建耗时</summary>
    public TimeSpan BuildDuration { get; set; }
}
