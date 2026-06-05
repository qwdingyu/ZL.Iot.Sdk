// ============================================================
//  GenerateRequest 单元测试
//  覆盖：Validate 参数校验、AllowedRids、默认值
// ============================================================

using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Tests;

public class GenerateRequestTests
{
    private GenerateRequest CreateValidRequest(SkuMode sku = SkuMode.Binary, string? rid = "win-x64")
    {
        return new GenerateRequest
        {
            ProjectName = "TestRunner",
            Version = "1.0.0",
            Sku = sku,
            RuntimeIdentifier = rid,
            Config = new RunnerConfig
            {
                Runner = new RunnerOptions { Name = "Test" },
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
    public void Validate_DefaultRequest_Succeeds()
    {
        var req = CreateValidRequest();
        req.Validate(); // 不应抛出异常
    }

    [Fact]
    public void Validate_SourceMode_NoRid_Succeeds()
    {
        var req = CreateValidRequest(sku: SkuMode.Source, rid: null);
        req.Validate(); // Source 模式不需要 RID
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    [InlineData("osx-x64")]
    public void Validate_AllowedRids_Succeeds(string rid)
    {
        var req = CreateValidRequest(rid: rid);
        req.Validate();
    }

    [Fact]
    public void Validate_EmptyProjectName_Throws()
    {
        var req = CreateValidRequest();
        req.ProjectName = "";
        var ex = Assert.Throws<ArgumentException>(() => req.Validate());
        Assert.Equal(nameof(GenerateRequest.ProjectName), ex.ParamName);
    }

    [Fact]
    public void Validate_WhitespaceProjectName_Throws()
    {
        var req = CreateValidRequest();
        req.ProjectName = "   ";
        Assert.Throws<ArgumentException>(() => req.Validate());
    }

    [Fact]
    public void Validate_BinaryWithNullRid_Throws()
    {
        var req = CreateValidRequest();
        req.RuntimeIdentifier = null;
        var ex = Assert.Throws<ArgumentException>(() => req.Validate());
        Assert.Equal(nameof(GenerateRequest.RuntimeIdentifier), ex.ParamName);
    }

    [Fact]
    public void Validate_BinaryWithUnsupportedRid_Throws()
    {
        var req = CreateValidRequest();
        req.RuntimeIdentifier = "win-x86"; // 不在 AllowedRids 中
        var ex = Assert.Throws<ArgumentException>(() => req.Validate());
        Assert.Equal(nameof(GenerateRequest.RuntimeIdentifier), ex.ParamName);
        Assert.Contains("不支持", ex.Message);
    }

    [Fact]
    public void Validate_NoDevices_Throws()
    {
        var req = CreateValidRequest();
        req.Config.Devices = new List<DeviceProfile>();
        var ex = Assert.Throws<ArgumentException>(() => req.Validate());
        Assert.Equal(nameof(GenerateRequest.Config), ex.ParamName);
    }

    [Fact]
    public void Validate_NullDevices_Throws()
    {
        var req = CreateValidRequest();
        req.Config.Devices = null!;
        var ex = Assert.Throws<ArgumentException>(() => req.Validate());
        Assert.Equal(nameof(GenerateRequest.Config), ex.ParamName);
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var req = new GenerateRequest();
        Assert.Equal("MyPlc", req.ProjectName);
        Assert.Equal("1.0.0", req.Version);
        Assert.Equal(TargetPlatform.Console, req.Platform);
        Assert.Equal(SkuMode.Binary, req.Sku);
    }

    [Fact]
    public void AllowedRids_ContainsExpectedValues()
    {
        Assert.Contains("win-x64", GenerateRequest.AllowedRids);
        Assert.Contains("linux-x64", GenerateRequest.AllowedRids);
        Assert.Contains("osx-x64", GenerateRequest.AllowedRids);
    }
}
