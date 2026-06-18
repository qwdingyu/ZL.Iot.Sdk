// ============================================================
//  ZL.Iot.Runner.Generator - TemplateRenderer
//  -------------------------------------------------------------
//  Scriban 模板渲染器：读取 EmbeddedResource 模板 → 替换占位符 → 输出文本
// ============================================================

using System.IO;
using System.Reflection;
using System.Text.Json;
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
    /// 模板存储在 ZL.Iot.Runner.Generator 程序集的 Templates 目录资源中
    /// </summary>
    /// <param name="platform">目标平台（对应 Templates 目录下的子目录名）</param>
    /// <param name="fileName">文件名（如 MyApp.csproj.scriban）</param>
    /// <returns>模板文本，未找到返回 null</returns>
    public static string? ReadTemplate(TargetPlatform platform, string fileName)
    {
        var dir = GetPlatformDir(platform).Replace('-', '_');
        return ReadTemplateFromDir(dir, fileName);
    }

    /// <summary>
    /// 读取模板文件（按指定目录名）
    /// 用于 CS 继承模式等不按 TargetPlatform 分目录的场景
    /// </summary>
    public static string? ReadTemplateFromDir(string dirName, string fileName)
    {
        var templatesAssembly = LoadTemplatesAssembly();

        var dir = dirName.Replace('-', '_');
        var templateName = $"ZL.Iot.Runner.Generator.Templates.{dir}.{fileName}";

        using var stream = templatesAssembly.GetManifestResourceStream(templateName);
        if (stream == null)
        {
            var all = templatesAssembly.GetManifestResourceNames()
                .Where(r => r.Contains(dir) && r.EndsWith(fileName)).ToList();
            if (all.Count == 1)
            {
                using var fallback = templatesAssembly.GetManifestResourceStream(all[0]);
                if (fallback != null)
                {
                    using var fallbackReader = new StreamReader(fallback);
                    return fallbackReader.ReadToEnd();
                }
            }
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static System.Reflection.Assembly LoadTemplatesAssembly()
    {
        return typeof(TemplateRenderer).Assembly;
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
        globals.Add("namespace", GetNamespace(request));
        globals.Add("version", request.Version);
        globals.Add("runner_version", GetPackageVersion());
        globals.Add("generated_at", DateTime.UtcNow.ToString("O"));
        globals.Add("platform", GetPlatformDir(request.Platform));
        globals.Add("runtime_identifier", request.RuntimeIdentifier ?? "win-x64");
        globals.Add("host_type", GetHostType(request.Platform));
        globals.Add("format", request.ConfigFormat == ConfigFormat.Xml ? "xml" : "json");

        // 注入设备列表（供 cs-inheritance 模板生成设备类）
        var devices = new ScriptArray();
        foreach (var device in request.Config.Devices ?? new())
        {
            var d = new ScriptObject();
            d["Code"] = device.Code;
            d["Protocol"] = device.Protocol;
            d["Ip"] = device.Ip;
            d["Port"] = device.Port;
            devices.Add(d);
        }
        globals.Add("devices", devices);

        context.PushGlobal(globals);

        return template.Render(context);
    }

    /// <summary>
    /// 获取命名空间：优先使用独立配置的 Namespace，否则从 ProjectName 派生
    /// </summary>
    private static string GetNamespace(GenerateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Namespace))
            return request.Namespace.Trim();
        return ToPascalCase(request.ProjectName);
    }

    /// <summary>
    /// 获取生成项目需要引用的 NuGet 包版本。
    /// </summary>
    internal static string GetPackageVersion()
    {
        var overrideVersion = Environment.GetEnvironmentVariable("ZL_IOT_RUNNER_PACKAGE_VERSION");
        if (IsStablePackageVersion(overrideVersion))
        {
            return overrideVersion!;
        }

        var depsVersion = TryGetPackageVersionFromDeps("ZL.Iot.Runner.Generator")
            ?? TryGetPackageVersionFromDeps("ZL.Iot.Runner");
        if (IsStablePackageVersion(depsVersion))
        {
            return depsVersion!;
        }

        try
        {
            // 开发期 fallback：本地源码引用时没有 NuGet 包上下文，只能读取程序集信息。
            var generatorAssembly = typeof(TemplateRenderer).Assembly;
            var attr = generatorAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var informationalVersion = attr?.InformationalVersion?.Split('+')[0];
            if (IsStablePackageVersion(informationalVersion))
            {
                return informationalVersion!;
            }

            var version = generatorAssembly.GetName().Version?.ToString();
            if (IsStablePackageVersion(version))
            {
                return version!;
            }
        }
        catch
        {
            // 所有读取失败时返回默认值。
        }

        return "1.0.0";
    }

    private static string? TryGetPackageVersionFromDeps(string packageName)
    {
        foreach (var path in GetDepsFileCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("targets", out var targets))
                {
                    continue;
                }

                foreach (var target in targets.EnumerateObject())
                {
                    foreach (var library in target.Value.EnumerateObject())
                    {
                        var separatorIndex = library.Name.LastIndexOf('/');
                        if (separatorIndex <= 0)
                        {
                            continue;
                        }

                        var name = library.Name[..separatorIndex];
                        if (!string.Equals(name, packageName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var version = library.Name[(separatorIndex + 1)..];
                        if (IsStablePackageVersion(version))
                        {
                            return version;
                        }
                    }
                }
            }
            catch
            {
                // deps.json 只用于增强 NuGet 版本推断，读取失败时继续走 fallback。
            }
        }

        return null;
    }

    private static IEnumerable<string> GetDepsFileCandidates()
    {
        var depsFiles = AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string;
        if (!string.IsNullOrWhiteSpace(depsFiles))
        {
            foreach (var path in depsFiles.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return path;
            }
        }

        var entryLocation = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entryLocation))
        {
            yield return Path.ChangeExtension(entryLocation, ".deps.json");
        }

        var entryName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(entryName))
        {
            yield return Path.Combine(AppContext.BaseDirectory, $"{entryName}.deps.json");
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            yield return Path.ChangeExtension(Environment.ProcessPath, ".deps.json");
        }
    }

    private static bool IsStablePackageVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return Version.TryParse(version, out _);
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
