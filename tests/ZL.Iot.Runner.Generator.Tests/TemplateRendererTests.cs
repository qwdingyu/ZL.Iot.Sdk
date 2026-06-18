// ============================================================
//  TemplateRenderer 单元测试
//  覆盖：ReadTemplate、Render、GetPlatformDir、GetHostType、
//        线程安全、模板变量注入
// ============================================================

using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Generator.Core;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Tests;

public class TemplateRendererTests
{
    #region GetPlatformDir

    [Theory]
    [InlineData(TargetPlatform.Console, "console")]
    [InlineData(TargetPlatform.WindowsService, "windows-service")]
    [InlineData(TargetPlatform.LinuxSystemd, "linux-systemd")]
    [InlineData(TargetPlatform.WinForm, "winform")]
    [InlineData(TargetPlatform.Web, "web")]
    public void GetPlatformDir_MapsCorrectly(TargetPlatform platform, string expected)
    {
        Assert.Equal(expected, TemplateRenderer.GetPlatformDir(platform));
    }

    #endregion

    #region GetHostType

    [Theory]
    [InlineData(TargetPlatform.Console, "Console")]
    [InlineData(TargetPlatform.WindowsService, "WindowsService")]
    [InlineData(TargetPlatform.LinuxSystemd, "LinuxSystemd")]
    [InlineData(TargetPlatform.WinForm, "WinForms")]
    [InlineData(TargetPlatform.Web, "Web")]
    public void GetHostType_MapsCorrectly(TargetPlatform platform, string expected)
    {
        Assert.Equal(expected, TemplateRenderer.GetHostType(platform));
    }

    #endregion

    #region ReadTemplate - EmbeddedResource 加载

    [Theory]
    [InlineData(TargetPlatform.Console, "MyApp.csproj.scriban")]
    [InlineData(TargetPlatform.Console, "ProgramCs.scriban")]
    [InlineData(TargetPlatform.Console, "NLog.config.scriban")]
    [InlineData(TargetPlatform.WindowsService, "MyApp.csproj.scriban")]
    [InlineData(TargetPlatform.WindowsService, "ProgramCs.scriban")]
    [InlineData(TargetPlatform.WindowsService, "install.bat.scriban")]
    [InlineData(TargetPlatform.WindowsService, "uninstall.bat.scriban")]
    [InlineData(TargetPlatform.LinuxSystemd, "MyApp.csproj.scriban")]
    [InlineData(TargetPlatform.LinuxSystemd, "ProgramCs.scriban")]
    [InlineData(TargetPlatform.LinuxSystemd, "installSh.scriban")]
    [InlineData(TargetPlatform.LinuxSystemd, "uninstallSh.scriban")]
    [InlineData(TargetPlatform.LinuxSystemd, "runner.service.scriban")]
    [InlineData(TargetPlatform.WinForm, "MyApp.csproj.scriban")]
    [InlineData(TargetPlatform.WinForm, "ProgramCs.scriban")]
    [InlineData(TargetPlatform.WinForm, "MainFormCs.scriban")]
    [InlineData(TargetPlatform.WinForm, "TagGridViewCs.scriban")]
    [InlineData(TargetPlatform.WinForm, "ExecutorPanelCs.scriban")]
    public void ReadTemplate_KnownTemplates_ReturnsNonEmpty(TargetPlatform platform, string fileName)
    {
        var content = TemplateRenderer.ReadTemplate(platform, fileName);
        Assert.NotNull(content);
        Assert.NotEmpty(content);
        // 所有模板都应包含至少一个 Scriban 占位符
        Assert.Contains("{{", content);
    }

    [Fact]
    public void ReadTemplate_NonExistent_ReturnsNull()
    {
        var content = TemplateRenderer.ReadTemplate(TargetPlatform.Console, "does_not_exist.scriban");
        Assert.Null(content);
    }

    #endregion

    #region Render - 模板变量替换

    private GenerateRequest CreateRenderRequest(TargetPlatform platform = TargetPlatform.Console, string? rid = "win-x64")
    {
        return new GenerateRequest
        {
            ProjectName = "MyFactoryPlc",
            Version = "2.3.4",
            Platform = platform,
            Sku = SkuMode.Binary,
            RuntimeIdentifier = rid,
            Config = new RunnerConfig
            {
                Runner = new RunnerOptions { Name = "FactoryRunner" },
                Devices = new List<DeviceProfile>
                {
                    new()
                    {
                        Code = "plc1",
                        Protocol = "SiemensS7",
                        Ip = "192.168.1.100",
                        Port = 102,
                        Tags = new List<TagProfile>
                        {
                            new() { Id = "T1", Address = "DB1.DBD0", DataType = "float", Enable = true, TagType = "D" }
                        },
                        Executors = new List<ExecutorProfile>()
                    }
                }
            }
        };
    }

    [Fact]
    public void Render_Csproj_ReplacesAllVariables()
    {
        var template = TemplateRenderer.ReadTemplate(TargetPlatform.Console, "MyApp.csproj.scriban")!;
        var request = CreateRenderRequest();
        var rendered = TemplateRenderer.Render(template, request);

        // 不应再包含未替换的占位符
        Assert.DoesNotContain("{{ project_name }}", rendered);
        Assert.DoesNotContain("{{ namespace }}", rendered);
        Assert.DoesNotContain("{{ version }}", rendered);
        Assert.DoesNotContain("{{ runner_version }}", rendered);
        Assert.DoesNotContain("{{ runtime_identifier }}", rendered);

        // 应包含替换后的值
        Assert.Contains("MyFactoryPlc", rendered);
        Assert.Contains("2.3.4", rendered);
        Assert.Contains("win-x64", rendered);
        Assert.Contains("PublishTrimmed", rendered);
        Assert.Contains("<PublishTrimmed>false</PublishTrimmed>", rendered);
    }

    [Fact]
    public void Render_Csproj_UsesResolvedPackageVersionForRunnerAndIotHub()
    {
        var previous = Environment.GetEnvironmentVariable("ZL_IOT_RUNNER_PACKAGE_VERSION");
        Environment.SetEnvironmentVariable("ZL_IOT_RUNNER_PACKAGE_VERSION", "9.8.7");

        try
        {
            var template = TemplateRenderer.ReadTemplate(TargetPlatform.Console, "MyApp.csproj.scriban")!;
            var request = CreateRenderRequest();
            var rendered = TemplateRenderer.Render(template, request);

            Assert.Contains("<PackageReference Include=\"ZL.Iot.Runner\" Version=\"9.8.7\" />", rendered);
            Assert.Contains("<PackageReference Include=\"ZL.IotHub\" Version=\"9.8.7\" />", rendered);
            Assert.DoesNotContain("Version=\"1.0.0\"", rendered);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZL_IOT_RUNNER_PACKAGE_VERSION", previous);
        }
    }

    [Fact]
    public void Render_RespectsCustomRuntimeIdentifier()
    {
        var template = TemplateRenderer.ReadTemplate(TargetPlatform.Console, "MyApp.csproj.scriban")!;
        var request = CreateRenderRequest(rid: "linux-x64");
        var rendered = TemplateRenderer.Render(template, request);

        Assert.Contains("linux-x64", rendered);
        Assert.DoesNotContain("win-x64", rendered);
    }

    [Fact]
    public void Render_ProgramCs_ReplacesProjectName()
    {
        var template = TemplateRenderer.ReadTemplate(TargetPlatform.Console, "ProgramCs.scriban")!;
        var request = CreateRenderRequest();
        var rendered = TemplateRenderer.Render(template, request);

        Assert.DoesNotContain("{{ project_name }}", rendered);
        Assert.Contains("MyFactoryPlc", rendered);
    }

    [Fact]
    public void Render_NLogConfig_ReplacesProjectName()
    {
        var template = TemplateRenderer.ReadTemplate(TargetPlatform.Console, "NLog.config.scriban")!;
        var request = CreateRenderRequest();
        var rendered = TemplateRenderer.Render(template, request);

        Assert.DoesNotContain("{{ project_name }}", rendered);
        Assert.Contains("MyFactoryPlc", rendered);
    }

    [Fact]
    public void Render_WinFormTemplates_ReplacesVariables()
    {
        var request = CreateRenderRequest(platform: TargetPlatform.WinForm);

        var csproj = TemplateRenderer.ReadTemplate(TargetPlatform.WinForm, "MyApp.csproj.scriban")!;
        var rendered = TemplateRenderer.Render(csproj, request);
        Assert.DoesNotContain("{{ project_name }}", rendered);
        Assert.Contains("MyFactoryPlc", rendered);

        var mainForm = TemplateRenderer.ReadTemplate(TargetPlatform.WinForm, "MainFormCs.scriban")!;
        var renderedForm = TemplateRenderer.Render(mainForm, request);
        Assert.DoesNotContain("{{ project_name }}", renderedForm);
    }

    [Fact]
    public void Render_LinuxSystemd_ReplacesVariables()
    {
        var request = CreateRenderRequest(platform: TargetPlatform.LinuxSystemd);

        var service = TemplateRenderer.ReadTemplate(TargetPlatform.LinuxSystemd, "runner.service.scriban")!;
        var rendered = TemplateRenderer.Render(service, request);
        Assert.DoesNotContain("{{ project_name }}", rendered);
        Assert.Contains("MyFactoryPlc", rendered);

        var install = TemplateRenderer.ReadTemplate(TargetPlatform.LinuxSystemd, "installSh.scriban")!;
        var renderedInstall = TemplateRenderer.Render(install, request);
        Assert.DoesNotContain("{{ project_name }}", renderedInstall);
    }

    [Fact]
    public void Render_InjectsPlatformAndHostType()
    {
        var request = CreateRenderRequest(platform: TargetPlatform.WinForm);
        // 使用一个包含 platform 和 host_type 的简单模板
        var templateText = "platform={{ platform }}, host={{ host_type }}";
        var rendered = TemplateRenderer.Render(templateText, request);
        Assert.Contains("platform=winform", rendered);
        Assert.Contains("host=WinForms", rendered);
    }

    [Fact]
    public void Render_InjectsRuntimeIdentifier()
    {
        var request = CreateRenderRequest(rid: "osx-x64");
        var templateText = "rid={{ runtime_identifier }}";
        var rendered = TemplateRenderer.Render(templateText, request);
        Assert.Contains("rid=osx-x64", rendered);
    }

    [Fact]
    public void Render_InvalidScribanSyntax_Throws()
    {
        var request = CreateRenderRequest();
        var invalidTemplate = "{{ invalid syntax [[[ ";
        Assert.Throws<InvalidOperationException>(() => TemplateRenderer.Render(invalidTemplate, request));
    }

    #endregion

    #region 线程安全

    [Fact]
    public async Task ReadTemplate_ConcurrentAccess_IsThreadSafe()
    {
        var platforms = new[]
        {
            TargetPlatform.Console,
            TargetPlatform.WindowsService,
            TargetPlatform.LinuxSystemd,
            TargetPlatform.WinForm
        };

        var tasks = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            foreach (var platform in platforms)
            {
                tasks.Add(Task.Run(() =>
                {
                    var content = TemplateRenderer.ReadTemplate(platform, "MyApp.csproj.scriban");
                    Assert.NotNull(content);
                }));
            }
        }

        // 所有并发读取不应抛出异常
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Render_ConcurrentAccess_IsThreadSafe()
    {
        var template = TemplateRenderer.ReadTemplate(TargetPlatform.Console, "MyApp.csproj.scriban")!;
        var request = CreateRenderRequest();

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            var rendered = TemplateRenderer.Render(template, request);
            Assert.DoesNotContain("{{", rendered);
        })).ToList();

        await Task.WhenAll(tasks);
    }

    #endregion
}
