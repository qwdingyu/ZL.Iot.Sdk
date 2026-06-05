namespace ZL.Iot.Runner.Generator.Core.Models;

/// <summary>
/// 代码脚手架生成选项
/// </summary>
public class ScaffoldOptions
{
    /// <summary>应用名称</summary>
    public string ApplicationName { get; set; } = "RunnerApp";

    /// <summary>应用版本</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>输出项目目录</summary>
    public required string OutputDirectory { get; set; }

    /// <summary>目标运行时标识符</summary>
    public string RuntimeIdentifier { get; set; } = "win-x64";

    /// <summary>宿主类型</summary>
    public HostType HostType { get; set; } = HostType.Console;

    /// <summary>数据库连接字符串</summary>
    public string DatabaseConnection { get; set; } = "Server=localhost;Port=3306;Database=iot_runner;Uid=root;Pwd=";

    /// <summary>数据库类型 (0=MySQL, 1=SQLServer, 2=SQLite)</summary>
    public int DatabaseType { get; set; } = 0;

    /// <summary>设备列表</summary>
    public List<DeviceSummary> Devices { get; set; } = new();

    /// <summary>是否生成 NuGet.config（指向本地 feed）</summary>
    public bool GenerateNugetConfig { get; set; } = true;
}
