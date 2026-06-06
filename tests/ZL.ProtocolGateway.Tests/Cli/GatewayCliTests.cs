using System;
using System.IO;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Cli;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Cli;

[Collection(GatewayCliTestCollection.Name)]
public class GatewayCliTests
{
    [Fact]
    public void GatewayScenarioCatalog_ShouldExposeEnhancedScenarios()
    {
        Assert.NotNull(GatewayScenarioCatalog.Find(GatewayScenarioCatalog.SerialToHttpFileId));
        Assert.NotNull(GatewayScenarioCatalog.Find(GatewayScenarioCatalog.JsonTransformFileId));
        Assert.NotNull(GatewayScenarioCatalog.Find(GatewayScenarioCatalog.TextForwardFileId));
    }

    [Fact]
    public void GatewayCliRequestLoader_LoadFromFile_BindsScenario()
    {
        var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "examples", "serial-to-http.test.json"));

        var request = GatewayCliRequestLoader.LoadFromFile(configPath);

        Assert.Equal(GatewayScenarioCatalog.SerialToHttpId, request.Scenario);
        Assert.Equal("Serial", request.Source.Protocol);
        Assert.Equal("ST-TEST", request.SerialToHttp.StationNo);
    }

    [Fact]
    public async Task GatewayCliApp_RunAsync_DryRun_PrintsNormalizedConfig()
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var exitCode = await GatewayCliApp.RunAsync([
                "--scenario", GatewayScenarioCatalog.SerialToHttpId,
                "--payload", "SN123,10.5,20.3",
                "--station-no", "ST-01",
                "--target-url", "https://example.com/api/upload",
                "--dry-run"
            ]);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("SchemaVersion: protocol-gateway-cli/v1", output);
        Assert.Contains("[DryRun]", output);
        Assert.DoesNotContain("Protocol Gateway Starting", output);
    }

    [Fact]
    public async Task GatewayCliApp_RunAsync_SaveConfig_WritesNormalizedJson()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"protocol-gateway-cli-{Guid.NewGuid():N}.json");

        try
        {
            var exitCode = await GatewayCliApp.RunAsync([
                "--scenario", GatewayScenarioCatalog.JsonTransformId,
                "--mapping", "device_id=deviceId;measured_value=value;unit=unit",
                "--save-config", tempFile,
                "--dry-run"
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(tempFile));

            var request = GatewayCliRequestLoader.LoadFromFile(tempFile);
            Assert.Equal(GatewayCliSchema.Version1, request.SchemaVersion);
            Assert.Equal(GatewayScenarioCatalog.JsonTransformId, request.Scenario);
            Assert.Equal("deviceId", request.JsonTransform.FieldMappings["device_id"]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task GatewayCliApp_RunAsync_ConfigPlusOverrides_ExportsFinalRequest()
    {
        var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "examples", "serial-to-http.test.json"));
        var exportPath = Path.Combine(Path.GetTempPath(), $"protocol-gateway-cli-export-{Guid.NewGuid():N}.json");

        try
        {
            var exitCode = await GatewayCliApp.RunAsync([
                "--config", configPath,
                "--target-url", "https://override.example/api",
                "--save-config", exportPath,
                "--dry-run"
            ]);

            Assert.Equal(0, exitCode);
            var request = GatewayCliRequestLoader.LoadFromFile(exportPath);
            Assert.Equal("https://override.example/api", request.SerialToHttp.TargetUrl);
        }
        finally
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
        }
    }

    [Fact]
    public async Task GatewayCliApp_RunAsync_ValidateOnly_ReturnsSuccessWithoutDryRunBanner()
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var exitCode = await GatewayCliApp.RunAsync([
                "--scenario", GatewayScenarioCatalog.TextForwardId,
                "--payload", "D100=42",
                "--validate"
            ]);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("[Validate] 配置校验通过", output);
        Assert.DoesNotContain("[DryRun]", output);
        Assert.DoesNotContain("--- Gateway 运行前预检 ---", output);
    }

    [Fact]
    public async Task GatewayCliApp_RunAsync_PrecheckWithInvalidHttpUrl_ReturnsBlockingExitCode()
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var exitCode = await GatewayCliApp.RunAsync([
                "--scenario", GatewayScenarioCatalog.SerialToHttpId,
                "--payload", "SN123,10.5,20.3",
                "--station-no", "ST-01",
                "--target-url", "not-a-url",
                "--precheck"
            ]);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("--- Gateway 运行前预检 ---", output);
        Assert.Contains("[ERROR] TargetUrl", output);
        Assert.Contains("[Precheck] 存在阻断项", output);
    }

    [Fact]
    public void GatewayCliPrecheckRunner_FileOutputWithoutDirectory_ShouldWarnButNotError()
    {
        var request = new GatewayCliRequest
        {
            Scenario = GatewayScenarioCatalog.TextForwardFileId,
            Source = new GatewaySourceOptions
            {
                Mode = GatewayCliModes.Inline,
                Protocol = "TCP",
                ContentType = "text",
                Topic = "tcp/input",
                Payload = "D100=42"
            },
            Output = new GatewayOutputOptions
            {
                Mode = GatewayCliModes.File,
                Name = "FileOutput",
                FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "out.log")
            }
        };

        var report = GatewayCliPrecheckRunner.Run(request);

        Assert.False(report.HasErrors);
        Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "OutputFile");
    }

    [Fact]
    public void GatewayCliPrecheckRunner_InlineModeWithPayloadFile_ShouldWarnAboutInputConflict()
    {
        var request = new GatewayCliRequest
        {
            Scenario = GatewayScenarioCatalog.TextForwardId,
            Source = new GatewaySourceOptions
            {
                Mode = GatewayCliModes.Inline,
                Protocol = "TCP",
                ContentType = "text",
                Topic = "tcp/input",
                Payload = "D100=42",
                PayloadFile = "./ignored.txt"
            },
            Output = new GatewayOutputOptions
            {
                Mode = GatewayCliModes.Console,
                Name = "ConsolePreview"
            }
        };

        var report = GatewayCliPrecheckRunner.Run(request);

        Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "InputConflict");
    }

    [Fact]
    public void GatewayCliPrecheckRunner_JsonTransformWithDuplicateTargets_ShouldWarnAboutMappingConflict()
    {
        var request = new GatewayCliRequest
        {
            Scenario = GatewayScenarioCatalog.JsonTransformId,
            Source = new GatewaySourceOptions
            {
                Mode = GatewayCliModes.Inline,
                Protocol = "JSON",
                ContentType = "json",
                Topic = "json/input",
                Payload = "{\"device_id\":\"A1\",\"measured_value\":88}"
            },
            Output = new GatewayOutputOptions
            {
                Mode = GatewayCliModes.Console,
                Name = "ConsolePreview"
            },
            JsonTransform = new JsonTransformOptions
            {
                FieldMappings = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["device_id"] = "value",
                    ["measured_value"] = "value"
                }
            }
        };

        var report = GatewayCliPrecheckRunner.Run(request);

        Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "MappingConflict");
    }

    [Fact]
    public async Task GatewayCliApp_RunAsync_Doctor_PrintsDoctorReport()
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var exitCode = await GatewayCliApp.RunAsync([
                "--scenario", GatewayScenarioCatalog.TextForwardId,
                "--payload", "D100=42",
                "--doctor"
            ]);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("=== Gateway Doctor Report ===", output);
        Assert.Contains("Validation: PASS", output);
        Assert.Contains("Summary: Errors=0, Warnings=0", output);
        Assert.Contains("--- Summary By Title ---", output);
        Assert.Contains("ContentType: Errors=0, Warnings=0, Info=1", output);
        Assert.Contains("--- Recommendations ---", output);
        Assert.Contains("当前共有", output);
    }

    [Fact]
    public void GatewayCliPrecheckRunner_FileModeWithExistingInput_ShouldReportReadableInputFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"protocol-gateway-input-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "D100=42");

        try
        {
            var request = new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.TextForwardFileId,
                Source = new GatewaySourceOptions
                {
                    Mode = GatewayCliModes.File,
                    Protocol = "TCP",
                    ContentType = "text",
                    Topic = "tcp/input",
                    PayloadFile = tempFile
                },
                Output = new GatewayOutputOptions
                {
                    Mode = GatewayCliModes.Console,
                    Name = "ConsolePreview"
                }
            };

            var report = GatewayCliPrecheckRunner.Run(request);

            Assert.Contains(report.Items, item => item.Level == "INFO" && item.Title == "InputFileAccess");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void GatewayCliPrecheckRunner_FileOutputPathPointingToDirectory_ShouldReportError()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"protocol-gateway-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var request = new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.TextForwardFileId,
                Source = new GatewaySourceOptions
                {
                    Mode = GatewayCliModes.Inline,
                    Protocol = "TCP",
                    ContentType = "text",
                    Topic = "tcp/input",
                    Payload = "D100=42"
                },
                Output = new GatewayOutputOptions
                {
                    Mode = GatewayCliModes.File,
                    Name = "FileOutput",
                    FilePath = tempDirectory
                }
            };

            var report = GatewayCliPrecheckRunner.Run(request);

            Assert.Contains(report.Items, item => item.Level == "ERROR" && item.Title == "OutputPathConflict");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory);
            }
        }
    }

    [Fact]
    public void GatewayCliPrecheckRunner_InputAndOutputUseSamePath_ShouldWarnAboutConflict()
    {
        var samePath = Path.Combine(Path.GetTempPath(), $"protocol-gateway-same-{Guid.NewGuid():N}.txt");
        File.WriteAllText(samePath, "D100=42");

        try
        {
            var request = new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.TextForwardFileId,
                Source = new GatewaySourceOptions
                {
                    Mode = GatewayCliModes.File,
                    Protocol = "TCP",
                    ContentType = "text",
                    Topic = "tcp/input",
                    PayloadFile = samePath
                },
                Output = new GatewayOutputOptions
                {
                    Mode = GatewayCliModes.File,
                    Name = "FileOutput",
                    FilePath = samePath
                }
            };

            var report = GatewayCliPrecheckRunner.Run(request);

            Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "InputOutputConflict");
            Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "OutputOverwrite");
        }
        finally
        {
            if (File.Exists(samePath))
            {
                File.Delete(samePath);
            }
        }
    }

    [Fact]
    public void GatewayCliPrecheckRunner_FileOutputWithExistingTarget_ShouldWarnAboutOverwrite()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"protocol-gateway-output-{Guid.NewGuid():N}.log");
        File.WriteAllText(outputPath, "existing");

        try
        {
            var request = new GatewayCliRequest
            {
                Scenario = GatewayScenarioCatalog.TextForwardFileId,
                Source = new GatewaySourceOptions
                {
                    Mode = GatewayCliModes.Inline,
                    Protocol = "TCP",
                    ContentType = "text",
                    Topic = "tcp/input",
                    Payload = "D100=42"
                },
                Output = new GatewayOutputOptions
                {
                    Mode = GatewayCliModes.File,
                    Name = "FileOutput",
                    FilePath = outputPath
                }
            };

            var report = GatewayCliPrecheckRunner.Run(request);

            Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "OutputOverwrite");
            Assert.Contains(report.Items, item => item.Level == "INFO" && item.Title == "OutputDirectoryWriteAccess");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void GatewayCliPrecheckRunner_SerialToHttpWithRootPathAndLoopbackHost_ShouldWarnAboutTargetUrlSemantics()
    {
        var request = new GatewayCliRequest
        {
            Scenario = GatewayScenarioCatalog.SerialToHttpId,
            Source = new GatewaySourceOptions
            {
                Mode = GatewayCliModes.Inline,
                Protocol = "Serial",
                ContentType = "text",
                Topic = "serial/input",
                Payload = "SN123,10.5,20.3"
            },
            Output = new GatewayOutputOptions
            {
                Mode = GatewayCliModes.Console,
                Name = "ConsolePreview"
            },
            SerialToHttp = new SerialToHttpOptions
            {
                StationNo = "ST-01",
                TargetUrl = "http://localhost"
            }
        };

        var report = GatewayCliPrecheckRunner.Run(request);

        Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "TargetUrlPort");
        Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "TargetUrlPath");
        Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "TargetUrlHost");
    }

    [Fact]
    public void GatewayCliPrecheckRunner_SerialToHttpWithExplicitPort_ShouldReportPortInfo()
    {
        var request = new GatewayCliRequest
        {
            Scenario = GatewayScenarioCatalog.SerialToHttpId,
            Source = new GatewaySourceOptions
            {
                Mode = GatewayCliModes.Inline,
                Protocol = "Serial",
                ContentType = "text",
                Topic = "serial/input",
                Payload = "SN123,10.5,20.3"
            },
            Output = new GatewayOutputOptions
            {
                Mode = GatewayCliModes.Console,
                Name = "ConsolePreview"
            },
            SerialToHttp = new SerialToHttpOptions
            {
                StationNo = "ST-01",
                TargetUrl = "https://example.com:8443/api/upload"
            }
        };

        var report = GatewayCliPrecheckRunner.Run(request);

        Assert.Contains(report.Items, item => item.Level == "INFO" && item.Title == "TargetUrlPort" && item.Message.Contains("8443", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Items, item => item.Title == "TargetUrlSchemePort");
    }

    [Fact]
    public void GatewayCliPrecheckRunner_SerialToHttpWithMismatchedSchemeAndPort_ShouldWarnAboutSchemePortCombination()
    {
        var request = new GatewayCliRequest
        {
            Scenario = GatewayScenarioCatalog.SerialToHttpId,
            Source = new GatewaySourceOptions
            {
                Mode = GatewayCliModes.Inline,
                Protocol = "Serial",
                ContentType = "text",
                Topic = "serial/input",
                Payload = "SN123,10.5,20.3"
            },
            Output = new GatewayOutputOptions
            {
                Mode = GatewayCliModes.Console,
                Name = "ConsolePreview"
            },
            SerialToHttp = new SerialToHttpOptions
            {
                StationNo = "ST-01",
                TargetUrl = "https://example.com:80/api/upload"
            }
        };

        var report = GatewayCliPrecheckRunner.Run(request);

        Assert.Contains(report.Items, item => item.Level == "WARN" && item.Title == "TargetUrlSchemePort" && item.Message.Contains("https", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GatewayCliApp_RunAsync_Doctor_WithInvalidScenarioConfig_PrintsValidationFailure()
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var exitCode = await GatewayCliApp.RunAsync([
                "--scenario", GatewayScenarioCatalog.TextForwardId,
                "--payload", "D100=42",
                "--content-type", "xml",
                "--doctor"
            ]);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("Validation: FAIL", output);
        Assert.Contains("Summary: Errors=1", output);
        Assert.Contains("--- Summary By Title ---", output);
        Assert.Contains("Validation: Errors=1, Warnings=0, Info=0", output);
        Assert.Contains("[ERROR] Validation", output);
        Assert.Contains("不支持的 contentType: xml", output);
        Assert.Contains("当前共有 1 个 ERROR 项", output);
    }

    [Fact]
    public async Task GatewaySimulationRunner_SerialToHttp_PrintsConvertedPayload()
    {
        var request = new GatewayCliRequest
        {
            Scenario = GatewayScenarioCatalog.SerialToHttpId,
            Source = new GatewaySourceOptions
            {
                Protocol = "Serial",
                ContentType = "text",
                Topic = "serial/input",
                Payload = "SN123,10.5,20.3"
            },
            Output = new GatewayOutputOptions
            {
                Mode = GatewayCliModes.Console,
                Name = "ConsolePreview"
            },
            SerialToHttp = new SerialToHttpOptions
            {
                StationNo = "ST-01",
                TargetUrl = "https://example.com/api/upload"
            }
        };

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            await GatewaySimulationRunner.RunAsync(request);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("ContentType : json", output);
        Assert.Contains("stationNo", output);
        Assert.Contains("ST-01", output);
        Assert.Contains("https://example.com/api/upload", output);
    }
}
