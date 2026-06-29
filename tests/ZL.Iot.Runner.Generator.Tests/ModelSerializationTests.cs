// ============================================================
//  Generator Models 序列化往返测试
//  覆盖：GenerateRequest / GenerateResult / JobStatusEvent
//  验证 System.Text.Json 序列化后反序列化可恢复关键属性
// ============================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Xunit.Abstractions;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Generator.Core;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Tests;

/// <summary>
/// 模型序列化往返测试，确保核心模型可被 System.Text.Json 正确往返
/// </summary>
public class ModelSerializationTests
{
    private readonly ITestOutputHelper _output;

    public ModelSerializationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    #region GenerateRequest

    [Fact]
    public void GenerateRequest_RoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new GenerateRequest
        {
            Platform = TargetPlatform.WindowsService,
            Sku = SkuMode.Binary,
            RuntimeIdentifier = "linux-x64",
            ProjectName = "Line1Plc",
            Namespace = "FactoryA.Line1",
            ConfigFormat = ConfigFormat.Json,
            Version = "2.1.0",
            Config = new RunnerConfig
            {
                Runner = new RunnerOptions { Name = "RunnerA", LogLevel = "Debug" },
                Devices = new System.Collections.Generic.List<DeviceProfile>
                {
                    new DeviceProfile
                    {
                        Code = "plc1",
                        Protocol = "modbus-tcp",
                        Ip = "10.0.0.11",
                        Port = 502,
                        Rack = 0,
                        Slot = 1,
                        Tags = new System.Collections.Generic.List<TagProfile>
                        {
                            new TagProfile { Id = "T1", Address = "40001", DataType = "short", Enable = true, TagType = "D" }
                        },
                        Executors = new System.Collections.Generic.List<ExecutorProfile>
                        {
                            new ExecutorProfile { BizCode = "E1", TagId = "T1", JudgeType = 1, JudgeExp = "1", ExeType = "M", Script = "SELECT 1", ExeOrder = 1, Enable = true }
                        }
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GenerateRequest>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Platform, deserialized.Platform);
        Assert.Equal(original.Sku, deserialized.Sku);
        Assert.Equal(original.RuntimeIdentifier, deserialized.RuntimeIdentifier);
        Assert.Equal(original.ProjectName, deserialized.ProjectName);
        Assert.Equal(original.Namespace, deserialized.Namespace);
        Assert.Equal(original.ConfigFormat, deserialized.ConfigFormat);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.NotNull(deserialized.Config);
        Assert.Equal(original.Config.Runner.Name, deserialized.Config.Runner.Name);
        Assert.Single(deserialized.Config.Devices);
        Assert.Equal("plc1", deserialized.Config.Devices[0].Code);
        Assert.Equal("modbus-tcp", deserialized.Config.Devices[0].Protocol);
        Assert.Single(deserialized.Config.Devices[0].Tags);
        Assert.Equal("T1", deserialized.Config.Devices[0].Tags[0].Id);
    }

    #endregion

    #region GenerateResult

    [Fact]
    public void GenerateResult_RoundTrip_PreservesAllProperties()
    {
        // Arrange
        var manifest = new PackageManifest
        {
            ApplicationName = "MyApp",
            Version = "1.0.0",
            HostType = "console",
            RuntimeIdentifier = "win-x64",
            SelfContained = true,
            BuildTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Files = new Dictionary<string, string>
            {
                ["MyApp.exe"] = "abc123"
            },
            Devices = new List<DeviceSummary>
            {
                new DeviceSummary { Code = "plc1", Protocol = "S7", Ip = "1.2.3.4", TagCount = 2 }
            },
            Sha256 = "sha256hex"
        };

        var original = GenerateResult.Ok(
            zipBytes: new byte[] { 0x01, 0x02, 0x03 },
            zipFileName: "MyApp-win-x64.zip",
            elapsed: TimeSpan.FromSeconds(12.3),
            manifest: manifest);

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GenerateResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal(original.ErrorMessage, deserialized.ErrorMessage);
        Assert.Equal(original.ZipFileName, deserialized.ZipFileName);
        Assert.Equal(original.Elapsed, deserialized.Elapsed);
        Assert.NotNull(deserialized.Manifest);
        Assert.Equal(manifest.ApplicationName, deserialized.Manifest.ApplicationName);
        Assert.Equal(manifest.Version, deserialized.Manifest.Version);
        Assert.Equal(manifest.HostType, deserialized.Manifest.HostType);
        Assert.Equal(manifest.RuntimeIdentifier, deserialized.Manifest.RuntimeIdentifier);
        Assert.Equal(manifest.SelfContained, deserialized.Manifest.SelfContained);
        Assert.Equal(manifest.BuildTime, deserialized.Manifest.BuildTime);
        Assert.Equal(manifest.Sha256, deserialized.Manifest.Sha256);
        Assert.Single(deserialized.Manifest.Files);
        Assert.Single(deserialized.Manifest.Devices);
        Assert.Equal("plc1", deserialized.Manifest.Devices[0].Code);
    }

    [Fact]
    public void GenerateResult_Fail_RoundTrip_PreservesErrorMessage()
    {
        // Arrange
        var original = GenerateResult.Fail("构建失败：缺少依赖", TimeSpan.FromSeconds(3));

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GenerateResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.False(deserialized.Success);
        Assert.Equal("构建失败：缺少依赖", deserialized.ErrorMessage);
        Assert.Null(deserialized.ZipBytes);
        Assert.Null(deserialized.ZipFileName);
        Assert.Null(deserialized.Manifest);
    }

    #endregion

    #region JobStatusEvent

    [Fact]
    public void JobStatusEvent_RoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new JobStatusEvent
        {
            Timestamp = new DateTime(2024, 6, 29, 12, 0, 0, DateTimeKind.Utc),
            Status = JobStatus.Running,
            Message = "正在编译",
            ProgressPercent = 45,
            Phase = "building",
            ErrorMessage = null,
            ResultFileName = "out.zip",
            ElapsedSeconds = 10.5
        };

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<JobStatusEvent>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.Message, deserialized.Message);
        Assert.Equal(original.ProgressPercent, deserialized.ProgressPercent);
        Assert.Equal(original.Phase, deserialized.Phase);
        Assert.Equal(original.ErrorMessage, deserialized.ErrorMessage);
        Assert.Equal(original.ResultFileName, deserialized.ResultFileName);
        Assert.Equal(original.ElapsedSeconds, deserialized.ElapsedSeconds);
    }

    #endregion

    #region GenerateJob（可序列化属性视图）

    [Fact]
    public void GenerateJob_SerializableProperties_RoundTrip_PreservesState()
    {
        // Arrange
        var request = new GenerateRequest { ProjectName = "TestApp", Platform = TargetPlatform.Console, Sku = SkuMode.Source };
        var job = GenerateJob.Create(request, "user-1");

        // 通过公共 API 推进状态
        job.SetRunning();
        job.EmitProgressEvent("rendering", 30);
        job.SetSucceeded(new byte[] { 0x01 }, "out.zip", GenerateResult.Ok(new byte[] { 0x01 }, "out.zip", TimeSpan.FromSeconds(1)));

        // 构造仅包含可序列化属性的匿名 DTO 进行 round-trip
        var dto = new
        {
            job.Id,
            job.UserId,
            job.Status,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.ErrorMessage,
            job.ResultFileName,
            job.DownloadToken,
            job.QueuePosition,
            job.Progress,
            job.Phase
        };

        // Act
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Debug: 输出实际 JSON 结构，便于定位属性名不匹配问题
        _output.WriteLine("Serialized JSON: " + json);

        // Assert
        Assert.Equal(job.Id, root.GetProperty("id").GetGuid());
        Assert.Equal(job.UserId, root.GetProperty("userId").GetString());
        Assert.Equal((int)job.Status, root.GetProperty("status").GetInt32());
        Assert.Equal(job.CreatedAt, root.GetProperty("createdAt").GetDateTime());
        Assert.Equal(job.StartedAt, root.GetProperty("startedAt").GetDateTime());
        Assert.Equal(job.CompletedAt, root.GetProperty("completedAt").GetDateTime());

        // ErrorMessage 为 null 时会被 WhenWritingNull 忽略，因此需兼容两种序列化结果
        if (root.TryGetProperty("errorMessage", out var errorProp))
        {
            Assert.Equal(job.ErrorMessage, errorProp.GetString());
        }
        else
        {
            Assert.Null(job.ErrorMessage);
        }

        Assert.Equal(job.ResultFileName, root.GetProperty("resultFileName").GetString());
        Assert.Equal(job.DownloadToken, root.GetProperty("downloadToken").GetString());
        Assert.Equal(job.QueuePosition, root.GetProperty("queuePosition").GetInt32());
        Assert.Equal(job.Progress, root.GetProperty("progress").GetInt32());
        Assert.Equal(job.Phase, root.GetProperty("phase").GetString());
    }

    #endregion
}
