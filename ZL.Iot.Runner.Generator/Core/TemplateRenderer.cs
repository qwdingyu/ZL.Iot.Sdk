// ============================================================
//  ZL.Iot.Runner.Generator - TemplateRenderer
//  -------------------------------------------------------------
//  Scriban 模板渲染器：读取 EmbeddedResource 模板 → 替换占位符 → 输出文本
// ============================================================

using Scriban;
using Scriban.Runtime;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Core;

/// <summary>
/// 模板渲染器：从程序集 EmbeddedResource 读取模板文件，用 Scriban 渲染占位符。
/// </summary>
public static class TemplateRenderer
{
    /// <summary>
    /// 从 EmbeddedResource 读取模板内容
    /// 模板存储在 ZL.Iot.Runner.Templates 程序集中
    /// </summary>
    /// <param name="platform">目标平台（对应 Templates 目录下的子目录名）</param>
    /// <param name="fileName">文件名（如 MyApp.csproj.scriban）</param>
    /// <returns>模板文本，未找到返回 null</returns>
    public static string? ReadTemplate(TargetPlatform platform, string fileName)
    {
        var templatesAssembly = LoadTemplatesAssembly();
        if (templatesAssembly == null)
            throw new InvalidOperationException("ZL.Iot.Runner.Templates 程序集未找到，请确认项目引用正确");

        // .NET 将嵌入资源的目录分隔符 '-' 规范化为 '_'
        var dir = GetPlatformDir(platform).Replace('-', '_');
        var templateName = $"ZL.Iot.Runner.Templates.{dir}.{fileName}";

        using var stream = templatesAssembly.GetManifestResourceStream(templateName);
        if (stream == null) return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static System.Reflection.Assembly? _templatesAssembly;
    private static readonly object _templatesLock = new();

    private static System.Reflection.Assembly LoadTemplatesAssembly()
    {
        if (_templatesAssembly != null) return _templatesAssembly;

        lock (_templatesLock)
        {
            if (_templatesAssembly != null) return _templatesAssembly;

            // 1) 检查已加载的程序集
            _templatesAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ZL.Iot.Runner.Templates");
            if (_templatesAssembly != null) return _templatesAssembly;

            // 2) 从当前程序集所在目录加载
            var baseDir = Path.GetDirectoryName(typeof(TemplateRenderer).Assembly.Location) ?? ".";
            var dllPath = Path.Combine(baseDir, "ZL.Iot.Runner.Templates.dll");
            if (File.Exists(dllPath))
            {
                _templatesAssembly = System.Reflection.Assembly.LoadFrom(dllPath);
                return _templatesAssembly;
            }

            throw new InvalidOperationException("ZL.Iot.Runner.Templates.dll 未找到");
        }
    }

    /// <summary>
    /// 获取平台对应的目录名
    /// </summary>
    public static string GetPlatformDir(TargetPlatform platform)
    {
        return platform switch
        {
            TargetPlatform.Console => "console",
            TargetPlatform.WindowsService => "windows-service",
            TargetPlatform.LinuxSystemd => "linux-systemd",
            TargetPlatform.WinForm => "winform",
            TargetPlatform.Web => "web",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }

    /// <summary>
    /// 渲染模板：将 Scriban 占位符替换为真实值
    /// </summary>
    public static string Render(string templateText, GenerateRequest request)
    {
        var template = Template.Parse(templateText, "template");
        if (template.HasErrors)
        {
            throw new InvalidOperationException(
                $"模板语法错误: {string.Join("; ", template.Messages)}");
        }

        var context = new TemplateContext();
        var globals = new ScriptObject();

        // 注入全局变量
        globals.Add("project_name", request.ProjectName);
        globals.Add("namespace", ToPascalCase(request.ProjectName));
        globals.Add("version", request.Version);
        globals.Add("runner_version", GetRunnerVersion());
        globals.Add("generated_at", DateTime.UtcNow.ToString("O"));
        globals.Add("platform", GetPlatformDir(request.Platform));
        globals.Add("runtime_identifier", request.RuntimeIdentifier ?? "win-x64");
        globals.Add("host_type", GetHostType(request.Platform));

        context.PushGlobal(globals);

        return template.Render(context);
    }

    /// <summary>
    /// 获取 Runner 当前版本号（硬编码，CI 发布时同步更新）
    /// </summary>
    private static string GetRunnerVersion()
    {
        // Phase 1 固定版本。后续改为从 ZL.Iot.Runner 程序集读取或 NuGet 包版本。
        return "1.0.0";
    }

    /// <summary>
    /// 获取宿主类型名称
    /// </summary>
    public static string GetHostType(TargetPlatform platform)
    {
        return platform switch
        {
            TargetPlatform.Console => "Console",
            TargetPlatform.WindowsService => "WindowsService",
            TargetPlatform.LinuxSystemd => "LinuxSystemd",
            TargetPlatform.WinForm => "WinForms",
            TargetPlatform.Web => "Web",
            _ => "Console"
        };
    }

    /// <summary>
    /// 转为 PascalCase（去掉空格、特殊字符，首字母大写）
    /// </summary>
    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return "MyApp";

        var result = new System.Text.StringBuilder();
        bool nextUpper = true;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (nextUpper)
                {
                    result.Append(char.ToUpper(c));
                }
                else
                {
                    result.Append(c);
                }
                nextUpper = false;
            }
            else
            {
                nextUpper = true;
            }
        }

        return result.Length > 0 ? result.ToString() : "MyApp";
    }
}
