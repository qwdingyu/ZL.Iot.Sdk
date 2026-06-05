// ============================================================
//  ProjectGenerator 端到端测试
//  覆盖：Source 模式完整生成、Binary 模式（需 dotnet SDK）、
//        各平台模板渲染、zip 内容验证、README 平台特定说明
// ============================================================

using System.IO.Compression;
using System.Text;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Generator.Core;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Tests;

public class ProjectGeneratorTests
{
    private ProjectGenerator _generator = new();

    private GenerateRequest CreateSourceRequest(TargetPlatform platform = TargetPlatform.Console)
    {
        return new GenerateRequest
        {
            ProjectName = "TestRunner",
            Version = "1.0.0",
            Platform = platform,
            Sku = SkuMode.Source,
            Config = new RunnerConfig
            {
                Runner = new RunnerOptions
                {
                    Name = "TestRunner",
                    LogLevel = "Information",
                    DataStorage = new DataStorageOptions
                    {
                        Type = "Sqlite",
                        ConnectionString = "Data Source=./test.db"
                    }
                },
                Devices = new List<DeviceProfile>
                {
                    new()
                    {
                        Code = "plc1",
                        Protocol = "SiemensS7",
                        Ip = "192.168.1.100",
                        Port = 102,
                        Rack = 0,
                        Slot = 1,
                        Tags = new List<TagProfile>
                        {
                            new() { Id = "T1", Address = "DB1.DBD0", DataType = "float", Enable = true, TagType = "D" },
                            new() { Id = "T2", Address = "DB1.DBX4.0", DataType = "bool", Enable = true, TagType = "M" }
                        },
                        Executors = new List<ExecutorProfile>
                        {
                            new()
                            {
                                BizCode = "E1",
                                TagId = "T1",
                                JudgeType = "1",
                                JudgeExp = "1",
                                ExeType = "M",
                                Script = "SELECT 1",
                                ExeOrder = 1,
                                Enable = true
                            }
                        }
                    }
                }
            }
        };
    }

    #region Source 模式 - 完整端到端

    [Fact]
    public async Task GenerateAsync_SourceConsole_Succeeds()
    {
        var request = CreateSourceRequest(TargetPlatform.Console);
        var result = await _generator.GenerateAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.ZipBytes);
        Assert.NotEmpty(result.ZipBytes);
        Assert.NotNull(result.ZipFileName);
        Assert.EndsWith("source.zip", result.ZipFileName!);
        Assert.NotEqual(TimeSpan.Zero, result.Elapsed);
    }

    [Theory]
    [InlineData(TargetPlatform.Console)]
    [InlineData(TargetPlatform.WindowsService)]
    [InlineData(TargetPlatform.LinuxSystemd)]
    [InlineData(TargetPlatform.WinForm)]
    public async Task GenerateAsync_Source_AllPlatforms_Succeed(TargetPlatform platform)
    {
        var request = CreateSourceRequest(platform);
        var result = await _generator.GenerateAsync(request);

        Assert.True(result.Success, $"{platform} 平台生成失败: {result.ErrorMessage}");
        Assert.NotNull(result.ZipBytes);
        Assert.NotEmpty(result.ZipBytes);
    }

    [Fact]
    public async Task GenerateAsync_SourceConsole_ZipContainsExpectedFiles()
    {
        var request = CreateSourceRequest(TargetPlatform.Console);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var allNames = archive.Entries.Select(e => e.Name).ToList();
        var allPaths = archive.Entries.Select(e => e.FullName).ToList();

        // .csproj 文件（已重命名为 ProjectName.csproj）
        Assert.Contains(allNames, n => n == "TestRunner.csproj");

        // Program.cs
        Assert.Contains(allNames, n => n == "Program.cs");

        // 配置文件
        Assert.Contains(allNames, n => n == "runner.config.json");
        Assert.Contains(allNames, n => n == "NLog.config");

        // build 脚本
        Assert.Contains(allNames, n => n == "build.bat");
        Assert.Contains(allNames, n => n == "build.sh");

        // README
        Assert.Contains(allNames, n => n == "README.md");
    }

    [Fact]
    public async Task GenerateAsync_SourceConsole_CsprojHasCorrectContent()
    {
        var request = CreateSourceRequest(TargetPlatform.Console);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var csprojEntry = archive.GetEntry("TestRunner.csproj")!;
        using var reader = new StreamReader(csprojEntry.Open());
        var csprojContent = await reader.ReadToEndAsync();

        Assert.Contains("<AssemblyName>TestRunner</AssemblyName>", csprojContent);
        Assert.Contains("<Version>1.0.0</Version>", csprojContent);
        Assert.Contains("ZL.Iot.Runner.Lib", csprojContent);
        Assert.Contains("NLog.Extensions.Logging", csprojContent);
        Assert.Contains("PublishTrimmed", csprojContent);
        Assert.DoesNotContain("{{ project_name }}", csprojContent);
        Assert.DoesNotContain("{{ version }}", csprojContent);
    }

    [Fact]
    public async Task GenerateAsync_SourceConsole_ConfigHasDevices()
    {
        var request = CreateSourceRequest(TargetPlatform.Console);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var configEntry = archive.GetEntry("runner.config.json")!;
        using var reader = new StreamReader(configEntry.Open());
        var configContent = await reader.ReadToEndAsync();

        Assert.Contains("\"plc1\"", configContent);
        Assert.Contains("\"SiemensS7\"", configContent);
        Assert.Contains("\"192.168.1.100\"", configContent);
        Assert.Contains("\"T1\"", configContent);
        Assert.Contains("\"DB1.DBD0\"", configContent);
        Assert.Contains("\"E1\"", configContent);
    }

    [Fact]
    public async Task GenerateAsync_SourceConsole_ReadmeHasPlatformSpecificContent()
    {
        var request = CreateSourceRequest(TargetPlatform.Console);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var readmeEntry = archive.GetEntry("README.md")!;
        using var reader = new StreamReader(readmeEntry.Open());
        var readme = await reader.ReadToEndAsync();

        Assert.Contains("# TestRunner", readme);
        Assert.Contains("## 快速开始", readme);
        Assert.Contains("## 停止", readme);
        Assert.Contains("Ctrl+C", readme);
    }

    #endregion

    #region Source 模式 - 各平台 README 特定内容

    [Fact]
    public async Task GenerateAsync_WindowsService_ReadmeHasServiceInstructions()
    {
        var request = CreateSourceRequest(TargetPlatform.WindowsService);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        var readme = await ExtractFile(result, "README.md");
        Assert.Contains("install.bat", readme);
        Assert.Contains("sc start TestRunner", readme);
        Assert.Contains("sc stop TestRunner", readme);
        Assert.Contains("健康检查", readme);
    }

    [Fact]
    public async Task GenerateAsync_LinuxSystemd_ReadmeHasSystemdInstructions()
    {
        var request = CreateSourceRequest(TargetPlatform.LinuxSystemd);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        var readme = await ExtractFile(result, "README.md");
        Assert.Contains("sudo bash install.sh", readme);
        Assert.Contains("systemctl status TestRunner", readme);
        Assert.Contains("journalctl", readme);
        Assert.Contains("curl http://localhost:5000/health", readme);
    }

    [Fact]
    public async Task GenerateAsync_WinForm_ReadmeHasUiInstructions()
    {
        var request = CreateSourceRequest(TargetPlatform.WinForm);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        var readme = await ExtractFile(result, "README.md");
        Assert.Contains(".exe", readme);
        Assert.Contains("界面说明", readme);
        Assert.Contains("设备状态表格", readme);
        Assert.Contains("执行器配置面板", readme);
    }

    #endregion

    #region Source 模式 - WinForms 特定文件

    [Fact]
    public async Task GenerateAsync_WinForm_ZipContainsWinFormsFiles()
    {
        var request = CreateSourceRequest(TargetPlatform.WinForm);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var allNames = archive.Entries.Select(e => e.Name).ToList();

        Assert.Contains(allNames, n => n == "Program.cs");
        Assert.Contains(allNames, n => n == "MainForm.cs");
        Assert.Contains(allNames, n => n == "TagGridView.cs");
        Assert.Contains(allNames, n => n == "ExecutorPanel.cs");
        Assert.Contains(allNames, n => n == "TestRunner.csproj");
    }

    #endregion

    #region Source 模式 - Linux systemd 特定文件

    [Fact]
    public async Task GenerateAsync_LinuxSystemd_ZipContainsServiceFiles()
    {
        var request = CreateSourceRequest(TargetPlatform.LinuxSystemd);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var allNames = archive.Entries.Select(e => e.Name).ToList();

        Assert.Contains(allNames, n => n == "install.sh");
        Assert.Contains(allNames, n => n == "uninstall.sh");
        Assert.Contains(allNames, n => n == "runner.service");
    }

    #endregion

    #region Source 模式 - Windows Service 特定文件

    [Fact]
    public async Task GenerateAsync_WindowsService_ZipContainsBatFiles()
    {
        var request = CreateSourceRequest(TargetPlatform.WindowsService);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var allNames = archive.Entries.Select(e => e.Name).ToList();

        Assert.Contains(allNames, n => n == "install.bat");
        Assert.Contains(allNames, n => n == "uninstall.bat");
    }

    #endregion

    #region Build 脚本验证

    [Fact]
    public async Task GenerateAsync_BuildScriptsContainProjectName()
    {
        var request = CreateSourceRequest(TargetPlatform.Console);
        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success);

        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        // build.bat
        var batEntry = archive.GetEntry("build.bat")!;
        using var batReader = new StreamReader(batEntry.Open());
        var batContent = await batReader.ReadToEndAsync();
        Assert.Contains("TestRunner", batContent);
        Assert.DoesNotContain("{{ project_name }}", batContent);

        // build.sh
        var shEntry = archive.GetEntry("build.sh")!;
        using var shReader = new StreamReader(shEntry.Open());
        var shContent = await shReader.ReadToEndAsync();
        Assert.Contains("TestRunner", shContent);
        Assert.DoesNotContain("{{ project_name }}", shContent);
    }

    #endregion

    #region 错误场景

    [Fact]
    public async Task GenerateAsync_InvalidRequest_ReturnsFailure()
    {
        var request = new GenerateRequest
        {
            ProjectName = "", // 无效
            Sku = SkuMode.Binary,
            RuntimeIdentifier = "win-x64",
            Config = new RunnerConfig
            {
                Runner = new RunnerOptions(),
                Devices = new List<DeviceProfile>
                {
                    new() { Code = "p1", Protocol = "S7", Ip = "1.2.3.4", Port = 102 }
                }
            }
        };

        var result = await _generator.GenerateAsync(request);
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("生成失败", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateAsync_CancellationToken_ReturnsFailure()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = CreateSourceRequest();
        var result = await _generator.GenerateAsync(request, cts.Token);
        Assert.False(result.Success);
    }

    #endregion

    #region 进度回调

    [Fact]
    public async Task GenerateAsync_ProgressCallback_IsCalled()
    {
        var phases = new List<(string phase, int percent)>();
        Func<string, int, Task> onProgress = (phase, percent) =>
        {
            phases.Add((phase, percent));
            return Task.CompletedTask;
        };

        var request = CreateSourceRequest();
        await _generator.GenerateAsync(request, CancellationToken.None, onProgress);

        Assert.NotEmpty(phases);
        // 应该有 validating, rendering, packing, complete 阶段
        var phaseNames = phases.Select(p => p.phase).Distinct().ToList();
        Assert.Contains("validating", phaseNames);
        Assert.Contains("rendering", phaseNames);
        Assert.Contains("complete", phaseNames);

        // 最后应该是 100%
        Assert.Contains(phases, p => p.percent == 100);
    }

    #endregion

    #region Binary 模式（需 dotnet SDK）

    [Fact(Skip = "Requires dotnet SDK and NuGet restore")]
    public async Task GenerateAsync_BinaryConsole_CompletesSuccessfully()
    {
        var request = new GenerateRequest
        {
            ProjectName = "BinTest",
            Version = "1.0.0",
            Platform = TargetPlatform.Console,
            Sku = SkuMode.Binary,
            RuntimeIdentifier = "win-x64",
            Config = new RunnerConfig
            {
                Runner = new RunnerOptions { Name = "BinTest" },
                Devices = new List<DeviceProfile>
                {
                    new()
                    {
                        Code = "plc1",
                        Protocol = "SiemensS7",
                        Ip = "192.168.1.1",
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

        var result = await _generator.GenerateAsync(request);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Manifest);
        Assert.NotNull(result.ZipBytes);

        // 验证 zip 文件名格式
        Assert.NotNull(result.ZipFileName);
        Assert.Contains("BinTest", result.ZipFileName);
        Assert.Contains("1.0.0", result.ZipFileName);
        Assert.Contains("win-x64", result.ZipFileName);
        Assert.Contains("console", result.ZipFileName);
    }

    #endregion

    #region 辅助方法

    private static async Task<string> ExtractFile(GenerateResult result, string fileName)
    {
        using var ms = new MemoryStream(result.ZipBytes!);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry(fileName)!;
        using var reader = new StreamReader(entry.Open());
        return await reader.ReadToEndAsync();
    }

    #endregion
}
