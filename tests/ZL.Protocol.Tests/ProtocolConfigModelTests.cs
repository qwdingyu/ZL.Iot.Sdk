using ZL.Protocol.Models;

namespace ZL.Protocol.Tests;

public class ProtocolConfigModelTests
{
    [Fact]
    public void DefaultProtocolConfig_HasCorrectDefaults()
    {
        var config = new ProtocolConfig();

        Assert.Equal(string.Empty, config.ProtocolName);
        Assert.Equal("Text", config.FrameMode);
        Assert.Null(config.CheckSum);
        Assert.False(config.AutoAppendCheckSum);
        Assert.Equal("\n", config.Terminator);
        Assert.Equal(2000, config.DefaultTimeoutMs);
        Assert.Equal(50, config.InterCommandWaitMs);
        Assert.Equal("Broadcast", config.AsyncEventDispatchMode);
        Assert.Equal(0, config.JitterMs);
        Assert.Equal(0, config.SimDelayMs);
        Assert.Equal(0.0, config.SimPacketLossRate);
        Assert.Equal(0.0, config.ValueJitter);
        Assert.Equal(0.0, config.ValueNoise);
        Assert.False(config.IsProtected);
        Assert.Empty(config.Commands);
        Assert.Null(config.ReadStrategy);
        Assert.Null(config.UnknownResponse);
        Assert.Null(config.Events);
        Assert.Null(config.SimulationTags);
    }

    [Fact]
    public void Commands_CanAddAndRetrieve()
    {
        var config = new ProtocolConfig { ProtocolName = "Test" };
        var cmd = new CommandDefinition { CommandTemplate = "VOLT {v}" };

        config.Commands["SetVoltage"] = cmd;

        Assert.Single(config.Commands);
        Assert.True(config.Commands.ContainsKey("SetVoltage"));
        Assert.Equal("VOLT {v}", config.Commands["SetVoltage"].CommandTemplate);
    }

    [Fact]
    public void Commands_DuplicateKey_OverwritesValue()
    {
        var config = new ProtocolConfig();
        config.Commands["Test"] = new CommandDefinition { CommandTemplate = "OLD" };

        config.Commands["Test"] = new CommandDefinition { CommandTemplate = "NEW" };

        Assert.Single(config.Commands);
        Assert.Equal("NEW", config.Commands["Test"].CommandTemplate);
    }

    [Fact]
    public void Events_CanAddAndRetrieve()
    {
        var config = new ProtocolConfig();
        var evt = new EventDefinition
        {
            EventId = "OverTemp",
            Trigger = "temperature > 100",
            Template = "ALERT: {temperature}",
            Enabled = true
        };

        config.Events = new List<EventDefinition>();
        config.Events.Add(evt);

        Assert.Single(config.Events);
        Assert.Equal("OverTemp", config.Events[0].EventId);
        Assert.True(config.Events[0].Enabled);
    }

    [Fact]
    public void SimulationTags_CanAddAndRetrieve()
    {
        var config = new ProtocolConfig();
        var tag = new SimulationTagConfig
        {
            Mode = "Sine",
            InitialValue = 20.0,
            Min = 0.0,
            Max = 100.0,
            Amplitude = 10.0,
            Offset = 50.0,
            Frequency = 0.5,
            Enabled = true
        };

        config.SimulationTags = new Dictionary<string, SimulationTagConfig>();
        config.SimulationTags["temperature"] = tag;

        Assert.Single(config.SimulationTags);
        Assert.True(config.SimulationTags.ContainsKey("temperature"));
        Assert.Equal("Sine", config.SimulationTags["temperature"].Mode);
    }
}

public class CommandDefinitionTests
{
    [Fact]
    public void DefaultCommandDefinition_HasCorrectDefaults()
    {
        var cmd = new CommandDefinition();

        Assert.Equal(string.Empty, cmd.CommandTemplate);
        Assert.Equal(0, cmd.WaitAfterMs);
        Assert.Null(cmd.Parser);
        Assert.Null(cmd.ReadStrategy);
        Assert.Null(cmd.MatchPattern);
        Assert.Null(cmd.ResponseTemplate);
        Assert.Null(cmd.AutoAppendCheckSum);
        Assert.Null(cmd.CheckSum);
        Assert.Null(cmd.SetStateKey);
        Assert.Null(cmd.SetStateGroupIndex);
        Assert.Null(cmd.StateReset);
        Assert.False(cmd.IsFavorite);
        Assert.Null(cmd.DependsOn);
        Assert.Null(cmd.Validation);
        Assert.Null(cmd.ValidationErrorResponse);
        Assert.Null(cmd.ConditionalResponse);
        Assert.Null(cmd.Triggers);
        Assert.Null(cmd.ResponseExpression);
        Assert.Null(cmd.UiCanRead);
        Assert.Null(cmd.UiCanWrite);
    }

    [Fact]
    public void ReadStrategy_DefaultIsTerminator()
    {
        var strategy = new ReadStrategyDefinition();
        Assert.Equal("Terminator", strategy.Type);
        Assert.Null(strategy.Terminator);
        Assert.Equal(0, strategy.Length);
    }

    [Fact]
    public void ResponseParser_DefaultIsNone()
    {
        var parser = new ResponseParserDefinition();
        Assert.Equal("None", parser.Type);
        Assert.Null(parser.Pattern);
        Assert.Equal(0, parser.Index);
        Assert.Equal("String", parser.TargetType);
    }

    [Fact]
    public void ValidationRule_WithEnumValues()
    {
        var rule = new ValidationRule
        {
            Min = 0.0,
            Max = 100.0,
            EnumValues = new List<string> { "ON", "OFF" },
            Type = "enum"
        };

        Assert.Equal(0.0, rule.Min);
        Assert.Equal(100.0, rule.Max);
        Assert.Equal(2, rule.EnumValues.Count);
        Assert.Equal("enum", rule.Type);
    }

    [Fact]
    public void ConditionalResponse_AllFields()
    {
        var cr = new ConditionalResponse
        {
            Condition = "temperature > 50",
            IfTrue = "HIGH",
            IfFalse = "LOW",
            Default = "UNKNOWN"
        };

        Assert.Equal("HIGH", cr.IfTrue);
        Assert.Equal("LOW", cr.IfFalse);
        Assert.Equal("UNKNOWN", cr.Default);
    }
}

public class EventDefinitionTests
{
    [Fact]
    public void DefaultEventDefinition_HasCorrectDefaults()
    {
        var evt = new EventDefinition();

        Assert.Equal(string.Empty, evt.EventId);
        Assert.Equal(string.Empty, evt.Trigger);
        Assert.Equal(string.Empty, evt.Template);
        Assert.Equal(0, evt.IntervalMs);
        Assert.True(evt.Enabled);
    }

    [Fact]
    public void EventDefinition_SetAllProperties()
    {
        var evt = new EventDefinition
        {
            EventId = "evt-001",
            Trigger = "temperature > 100",
            Template = "ALERT: {temperature}",
            IntervalMs = 5000,
            Enabled = true
        };

        Assert.Equal("evt-001", evt.EventId);
        Assert.Equal("temperature > 100", evt.Trigger);
        Assert.Equal("ALERT: {temperature}", evt.Template);
        Assert.Equal(5000, evt.IntervalMs);
        Assert.True(evt.Enabled);
    }
}

public class SimulationTagConfigTests
{
    [Fact]
    public void DefaultSimulationTagConfig_HasCorrectDefaults()
    {
        var tag = new SimulationTagConfig();

        Assert.Equal("Fixed", tag.Mode);
        Assert.Equal(0.0, tag.InitialValue);
        Assert.Null(tag.FixedValue);
        Assert.Equal(0.0, tag.Min);
        Assert.Equal(0.0, tag.Max);
        Assert.Equal(0.0, tag.RampRate);
        Assert.Equal(0.1, tag.Frequency);
        Assert.Equal(10.0, tag.Amplitude);
        Assert.Equal(50.0, tag.Offset);
        Assert.Equal(5000, tag.StepDurationMs);
        Assert.Null(tag.StepValues);
        Assert.Equal("0.##", tag.Format);
        Assert.Null(tag.Unit);
        Assert.Equal(0, tag.UpdateIntervalMs);
        Assert.True(tag.Enabled);
    }

    [Fact]
    public void SineTagConfig_Fields()
    {
        var tag = new SimulationTagConfig
        {
            Mode = "Sine",
            InitialValue = 20.0,
            Min = 0.0,
            Max = 100.0,
            Amplitude = 25.0,
            Offset = 50.0,
            Frequency = 0.5,
            UpdateIntervalMs = 1000,
            Format = "F2",
            Unit = "°C"
        };

        Assert.Equal("Sine", tag.Mode);
        Assert.Equal(25.0, tag.Amplitude);
        Assert.Equal(50.0, tag.Offset);
        Assert.Equal(0.5, tag.Frequency);
        Assert.Equal("F2", tag.Format);
        Assert.Equal("°C", tag.Unit);
    }

    [Fact]
    public void StepTagConfig_StepValues()
    {
        var tag = new SimulationTagConfig
        {
            Mode = "Step",
            StepDurationMs = 3000,
            StepValues = new List<double> { 10.0, 20.0, 30.0, 40.0 }
        };

        Assert.Equal("Step", tag.Mode);
        Assert.Equal(3000, tag.StepDurationMs);
        Assert.Equal(4, tag.StepValues.Count);
        Assert.Equal(10.0, tag.StepValues[0]);
    }
}
