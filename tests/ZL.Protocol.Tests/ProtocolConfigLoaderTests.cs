using ZL.Protocol.Models;
using ZL.Protocol;

namespace ZL.Protocol.Tests;

public class ProtocolConfigLoaderTests
{
    [Fact]
    public void ParseJson_EmptyString_ReturnsNull()
    {
        var result = ProtocolConfigLoader.ParseJson("");
        Assert.Null(result);
    }

    [Fact]
    public void ParseJson_WhiteSpaceString_ReturnsNull()
    {
        var result = ProtocolConfigLoader.ParseJson("   \n  ");
        Assert.Null(result);
    }

    [Fact]
    public void ParseJson_NullString_ReturnsNull()
    {
        var result = ProtocolConfigLoader.ParseJson(null);
        Assert.Null(result);
    }

    [Fact]
    public void ParseJson_InvalidJson_ReturnsNull()
    {
        var result = ProtocolConfigLoader.ParseJson("{ invalid json }");
        Assert.Null(result);
    }

    [Fact]
    public void ParseJson_ValidJson_ReturnsConfig()
    {
        var json = """
        {
            "ProtocolName": "TestDevice",
            "FrameMode": "Text",
            "Terminator": "\n",
            "DefaultTimeoutMs": 3000,
            "InterCommandWaitMs": 100
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.Equal("TestDevice", result.ProtocolName);
        Assert.Equal("Text", result.FrameMode);
        Assert.Equal("\n", result.Terminator);
        Assert.Equal(3000, result.DefaultTimeoutMs);
        Assert.Equal(100, result.InterCommandWaitMs);
    }

    [Fact]
    public void ParseJson_ApplyDefaults_SetsTerminator()
    {
        var json = """
        {
            "ProtocolName": "TestDevice"
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json, applyDefaults: true);

        Assert.NotNull(result);
        Assert.Equal("\n", result.Terminator);
    }

    [Fact]
    public void ParseJson_NoApplyDefaults_LeavesTerminatorEmpty()
    {
        var json = """
        {
            "ProtocolName": "TestDevice"
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json, applyDefaults: false);

        Assert.NotNull(result);
        Assert.Equal("\n", result.Terminator); // 默认值已是 "\n"
    }

    [Fact]
    public void ParseJson_WithCommands_DeserializesCorrectly()
    {
        var json = """
        {
            "ProtocolName": "MultiCmd",
            "Commands": {
                "SetVoltage": {
                    "CommandTemplate": "VOLT {v}",
                    "WaitAfterMs": 100,
                    "ResponseTemplate": "{v}"
                },
                "GetId": {
                    "CommandTemplate": "*IDN?",
                    "WaitAfterMs": 50,
                    "ResponseTemplate": "MyDevice,Model,Serial"
                }
            }
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.Equal("MultiCmd", result.ProtocolName);
        Assert.Equal(2, result.Commands.Count);
        Assert.True(result.Commands.ContainsKey("SetVoltage"));
        Assert.True(result.Commands.ContainsKey("GetId"));
        Assert.Equal("VOLT {v}", result.Commands["SetVoltage"].CommandTemplate);
        Assert.Equal(100, result.Commands["SetVoltage"].WaitAfterMs);
        Assert.Equal("*IDN?", result.Commands["GetId"].CommandTemplate);
        Assert.Equal(50, result.Commands["GetId"].WaitAfterMs);
    }

    [Fact]
    public void ParseJson_WithReadStrategy_DeserializesCorrectly()
    {
        var json = """
        {
            "ProtocolName": "StrategyTest",
            "ReadStrategy": {
                "Type": "Length",
                "Length": 100
            }
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.NotNull(result.ReadStrategy);
        Assert.Equal("Length", result.ReadStrategy.Type);
        Assert.Equal(100, result.ReadStrategy.Length);
    }

    [Fact]
    public void ParseJson_WithEvents_DeserializesCorrectly()
    {
        var json = """
        {
            "ProtocolName": "EventTest",
            "Events": [
                {
                    "EventId": "OverTemp",
                    "Trigger": "temperature > 100",
                    "Template": "ALERT: {temperature}",
                    "IntervalMs": 5000,
                    "Enabled": true
                },
                {
                    "EventId": "LowPower",
                    "Trigger": "voltage < 10",
                    "Template": "WARN: voltage low",
                    "IntervalMs": 1000,
                    "Enabled": false
                }
            ]
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.NotNull(result.Events);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("OverTemp", result.Events[0].EventId);
        Assert.Equal("LowPower", result.Events[1].EventId);
        Assert.True(result.Events[0].Enabled);
        Assert.False(result.Events[1].Enabled);
    }

    [Fact]
    public void ParseJson_WithSimulationTags_DeserializesCorrectly()
    {
        var json = """
        {
            "ProtocolName": "SimTagTest",
            "SimulationTags": {
                "temperature": {
                    "Mode": "Sine",
                    "InitialValue": 20.0,
                    "Min": 0.0,
                    "Max": 100.0,
                    "Amplitude": 10.0,
                    "Offset": 50.0,
                    "Frequency": 0.5,
                    "Format": "F2",
                    "Unit": "°C",
                    "Enabled": true
                }
            }
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.NotNull(result.SimulationTags);
        Assert.Single(result.SimulationTags);
        Assert.True(result.SimulationTags.ContainsKey("temperature"));
        Assert.Equal("Sine", result.SimulationTags["temperature"].Mode);
        Assert.Equal(0.5, result.SimulationTags["temperature"].Frequency);
        Assert.Equal("°C", result.SimulationTags["temperature"].Unit);
    }

    [Fact]
    public void ParseJson_WithChecksum_DeserializesCorrectly()
    {
        var json = """
        {
            "ProtocolName": "ChecksumTest",
            "CheckSum": "CRC16",
            "AutoAppendCheckSum": true
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.Equal("CRC16", result.CheckSum);
        Assert.True(result.AutoAppendCheckSum);
    }

    [Fact]
    public void ParseJson_WithBehaviorControls_DeserializesCorrectly()
    {
        var json = """
        {
            "ProtocolName": "BehaviorTest",
            "JitterMs": 50,
            "SimDelayMs": 100,
            "SimPacketLossRate": 0.05,
            "ValueJitter": 0.02,
            "ValueNoise": 0.01,
            "IsProtected": true
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.Equal(50, result.JitterMs);
        Assert.Equal(100, result.SimDelayMs);
        Assert.Equal(0.05, result.SimPacketLossRate);
        Assert.Equal(0.02, result.ValueJitter);
        Assert.Equal(0.01, result.ValueNoise);
        Assert.True(result.IsProtected);
    }

    [Fact]
    public void ParseJson_WithConditionalResponse_DeserializesCorrectly()
    {
        var json = """
        {
            "ProtocolName": "CRTest",
            "Commands": {
                "Test": {
                    "CommandTemplate": "STAT?",
                    "ConditionalResponse": {
                        "Condition": "value > 50",
                        "IfTrue": "HIGH",
                        "IfFalse": "LOW",
                        "Default": "UNKNOWN"
                    }
                }
            }
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        var cmd = result.Commands["Test"];
        Assert.NotNull(cmd.ConditionalResponse);
        Assert.Equal("value > 50", cmd.ConditionalResponse.Condition);
        Assert.Equal("HIGH", cmd.ConditionalResponse.IfTrue);
        Assert.Equal("LOW", cmd.ConditionalResponse.IfFalse);
        Assert.Equal("UNKNOWN", cmd.ConditionalResponse.Default);
    }

    [Fact]
    public void ParseJson_WithValidationRule_DeserializesCorrectly()
    {
        var json = """
        {
            "ProtocolName": "ValTest",
            "Commands": {
                "SetLevel": {
                    "CommandTemplate": "LEVEL {l}",
                    "Validation": {
                        "Min": 0.0,
                        "Max": 100.0,
                        "EnumValues": ["LOW", "MED", "HIGH"],
                        "Type": "enum"
                    }
                }
            }
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        var cmd = result.Commands["SetLevel"];
        Assert.NotNull(cmd.Validation);
        Assert.Equal(0.0, cmd.Validation.Min);
        Assert.Equal(100.0, cmd.Validation.Max);
        Assert.Equal("enum", cmd.Validation.Type);
        Assert.Equal(3, cmd.Validation.EnumValues.Count);
    }

    [Fact]
    public void ParseJson_PreserveUnknownResponse()
    {
        var json = """
        {
            "ProtocolName": "UnknownTest",
            "UnknownResponse": "ERR: UNKNOWN CMD"
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.Equal("ERR: UNKNOWN CMD", result.UnknownResponse);
    }

    [Fact]
    public void ParseJson_WithResponseExpression()
    {
        var json = """
        {
            "ProtocolName": "ExprTest",
            "Commands": {
                "Calc": {
                    "CommandTemplate": "CALC {a} {b}",
                    "ResponseExpression": "{a} + {b}"
                }
            }
        }
        """;

        var result = ProtocolConfigLoader.ParseJson(json);

        Assert.NotNull(result);
        Assert.Equal("{a} + {b}", result.Commands["Calc"].ResponseExpression);
    }

    [Fact]
    public void LoadFromFile_NonExistentFile_ReturnsNull()
    {
        var result = ProtocolConfigLoader.LoadFromFile("/non/existent/path.json");
        Assert.Null(result);
    }

    [Fact]
    public void LoadFromFile_EmptyPath_ReturnsNull()
    {
        var result = ProtocolConfigLoader.LoadFromFile("");
        Assert.Null(result);
    }

    [Fact]
    public void LoadFromEmbedded_DefaultImplementation_ReturnsNull()
    {
        var result = ProtocolConfigLoader.LoadFromEmbedded("some.resource");
        Assert.Null(result);
    }
}
