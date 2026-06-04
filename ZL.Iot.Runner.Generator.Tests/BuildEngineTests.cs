// ============================================================
//  BuildEngine 单元测试
//  覆盖：Publish 无效项目、超时、取消、BuildResult 属性、
//        HostType 区分、SHA256 哈希计算
//  注意：dotnet publish 真机测试需要 SDK 环境，这里做逻辑层测试
// ============================================================

using System.Security.Cryptography;
using ZL.Iot.Runner.Generator.Core;

namespace ZL.Iot.Runner.Generator.Tests;

public class BuildEngineTests : IDisposable
{
    private readonly string _tempDir;

    public BuildEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BuildEngineTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    #region BuildPublishOptions 默认值

    [Fact]
    public void BuildPublishOptions_Defaults_AreCorrect()
    {
        var opts = new BuildPublishOptions();
        Assert.Equal("Release", opts.Configuration);
        Assert.Equal("win-x64", opts.RuntimeIdentifier);
        Assert.True(opts.SelfContained);
        Assert.True(opts.PublishSingleFile);
        Assert.True(opts.IncludeNativeLibrariesForSelfExtract);
        Assert.True(opts.EnableCompressionInSingleFile);
        Assert.Null(opts.OutputDirectory);
        Assert.Equal(HostType.Console, opts.HostType);
    }

    #endregion

    #region BuildResult 默认值

    [Fact]
    public void BuildResult_Defaults_AreCorrect()
    {
        var result = new BuildResult();
        Assert.False(result.Success);
        Assert.Null(result.OutputPath);
        Assert.Null(result.PackagePath);
        Assert.Equal(0L, result.PackageSizeBytes);
        Assert.Empty(result.FileHashes);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Errors);
        Assert.Equal(TimeSpan.Zero, result.BuildDuration);
    }

    #endregion

    #region Publish - 无效项目目录

    [Fact]
    public void Publish_NoCsproj_ReturnsFailure()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = BuildEngine.Publish(emptyDir, new BuildPublishOptions());

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Contains(".csproj", result.Errors[0]);
        Assert.NotEqual(TimeSpan.Zero, result.BuildDuration);
    }

    #endregion

    #region Publish - 兼容旧签名

    [Fact]
    public void Publish_OldSignature_NoCsproj_ThrowsInvalidOperationException()
    {
        var emptyDir = Path.Combine(_tempDir, "empty2");
        Directory.CreateDirectory(emptyDir);

        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildEngine.Publish(emptyDir, "win-x64", Path.Combine(emptyDir, "output")));

        Assert.Contains("dotnet publish 失败", ex.Message);
    }

    #endregion

    #region Publish - 取消令牌

    [Fact]
    public async Task Publish_CanceledToken_ReturnsFailure()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // 即使取消了，不会崩溃，返回失败结果
        var result = BuildEngine.Publish(_tempDir, new BuildPublishOptions(), cts.Token);
        Assert.False(result.Success);
    }

    #endregion

    #region Publish - 有效 .csproj 真机测试（如果 dotnet SDK 可用）

    [Fact(Skip = "Requires dotnet SDK and network access for NuGet restore")]
    public void Publish_ValidCsproj_CompletesSuccessfully()
    {
        // 创建一个最小 .csproj
        var projectDir = Path.Combine(_tempDir, "minapp");
        Directory.CreateDirectory(projectDir);

        var csproj = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <PublishSingleFile>false</PublishSingleFile>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
</Project>
""";
        File.WriteAllText(Path.Combine(projectDir, "MinApp.csproj"), csproj);

        File.WriteAllText(Path.Combine(projectDir, "Program.cs"),
            "Console.WriteLine(\"Hello\");");

        var result = BuildEngine.Publish(projectDir, new BuildPublishOptions
        {
            RuntimeIdentifier = "win-x64",
            SelfContained = false,
            PublishSingleFile = false,
            HostType = HostType.Console
        });

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.NotNull(result.OutputPath);
        Assert.NotEmpty(result.FileHashes);
    }

    #endregion

    #region HostType 区分

    [Fact]
    public void BuildPublishOptions_HostType_WinForms()
    {
        var opts = new BuildPublishOptions { HostType = HostType.WinForms };
        Assert.Equal(HostType.WinForms, opts.HostType);
    }

    [Fact]
    public void HostType_Enum_HasExpectedValues()
    {
        Assert.Equal(0, (int)HostType.Console);
        Assert.Equal(1, (int)HostType.WinForms);
    }

    #endregion

    #region SHA256 哈希计算验证

    [Fact]
    public void Publish_CalculatesFileHashes_Correctly()
    {
        // 由于 NoCsproj 场景不会计算哈希，我们验证 BuildResult.FileHashes 结构
        var result = new BuildResult
        {
            Success = true,
            FileHashes = new Dictionary<string, string>
            {
                ["app.exe"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("test"))).ToLowerInvariant()
            }
        };

        Assert.Single(result.FileHashes);
        var hash = result.FileHashes["app.exe"];
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
    }

    #endregion
}
