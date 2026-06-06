#nullable enable
using System;
using System.Collections.Generic;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins;

/// <summary>
/// SparkplugBOutputPlugin 单元测试
/// 测试配置验证、主题构建、数据类型映射、序列号和生命周期状态。
/// Start/Stop 需要真实 MQTT Broker，不在单元测试中覆盖。
/// </summary>
public class SparkplugBOutputPluginTests
{
    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SparkplugBOutputPlugin(null!));
    }

    [Fact]
    public void Constructor_ValidConfig_SetsProperties()
    {
        var config = new SparkplugBOutputConfig
        {
            Name = "test-sparkplug",
            BrokerHost = "localhost",
            BrokerPort = 1883,
            GroupId = "TestGroup",
            EdgeNodeId = "TestEdge"
        };

        using var plugin = new SparkplugBOutputPlugin(config);
        Assert.Equal("test-sparkplug", plugin.Name);
        Assert.Equal("sparkplug-b", plugin.ProtocolType);
        Assert.Equal(SparkplugBState.Unknown, plugin.LifecycleState);
    }

    [Fact]
    public void Constructor_DefaultValues()
    {
        var config = new SparkplugBOutputConfig();
        Assert.Equal("SparkplugB", config.Name);
        Assert.Equal("localhost", config.BrokerHost);
        Assert.Equal(1883, config.BrokerPort);
        Assert.Equal("DefaultGroup", config.GroupId);
        Assert.Equal("EdgeNode1", config.EdgeNodeId);
        Assert.False(config.UseProtobuf);
        Assert.Equal(1, config.Qos);
        Assert.False(config.Retain);
        Assert.Equal(100, config.AggregateIntervalMs);
        Assert.Equal(30000, config.KeepAliveIntervalMs);
    }

    [Fact]
    public void Constructor_CustomConfig_SetsLifecycleStateToUnknown()
    {
        var config = new SparkplugBOutputConfig
        {
            BrokerHost = "broker.example.com",
            BrokerPort = 8883,
            GroupId = "Factory1",
            EdgeNodeId = "Line1",
            UseProtobuf = true,
            Qos = 2,
            Retain = true,
            AggregateIntervalMs = 0,
            KeepAliveIntervalMs = 60000
        };

        using var plugin = new SparkplugBOutputPlugin(config);
        Assert.Equal(SparkplugBState.Unknown, plugin.LifecycleState);
        Assert.Equal(PluginStatus.Stopped, plugin.Status);
    }

    [Fact]
    public void Config_WithUsernameAndPassword()
    {
        var config = new SparkplugBOutputConfig
        {
            Username = "admin",
            Password = "secret"
        };

        using var plugin = new SparkplugBOutputPlugin(config);
        Assert.NotNull(plugin);
    }

    [Fact]
    public void Config_WithTagMappings()
    {
        var config = new SparkplugBOutputConfig
        {
            TagMappings = new Dictionary<string, string>
            {
                { "DB1.DBW0", "Temperature" },
                { "DB1.DBW2", "Pressure" },
                { "DB1.DBD4", "FlowRate" }
            }
        };

        using var plugin = new SparkplugBOutputPlugin(config);
        Assert.NotNull(plugin);
    }

    // ---- SparkplugBTopics 静态工具类测试 ----

    [Fact]
    public void Topics_NBirth_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.NBirth("Group1", "Edge1");
        Assert.Equal("spBv1.0/Group1/NBIRTH/Edge1", topic);
    }

    [Fact]
    public void Topics_NDeath_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.NDeath("Group1", "Edge1");
        Assert.Equal("spBv1.0/Group1/NDEATH/Edge1", topic);
    }

    [Fact]
    public void Topics_NAlive_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.NAlive("Group1", "Edge1");
        Assert.Equal("spBv1.0/Group1/NDALIVE/Edge1", topic);
    }

    [Fact]
    public void Topics_NData_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.NData("Group1", "Edge1");
        Assert.Equal("spBv1.0/Group1/NDATA/Edge1", topic);
    }

    [Fact]
    public void Topics_DBirth_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.DBirth("Group1", "Edge1", "Device1");
        Assert.Equal("spBv1.0/Group1/DBIRTH/Edge1/Device1", topic);
    }

    [Fact]
    public void Topics_DDeath_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.DDeath("Group1", "Edge1", "Device1");
        Assert.Equal("spBv1.0/Group1/DDEATH/Edge1/Device1", topic);
    }

    [Fact]
    public void Topics_DData_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.DData("Group1", "Edge1", "Device1");
        Assert.Equal("spBv1.0/Group1/DDATA/Edge1/Device1", topic);
    }

    [Fact]
    public void Topics_NCmd_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.NCmd("Group1", "Edge1");
        Assert.Equal("spBv1.0/Group1/NCMD/Edge1", topic);
    }

    [Fact]
    public void Topics_DCmd_FormatsCorrectly()
    {
        var topic = SparkplugBTopics.DCmd("Group1", "Edge1", "Device1");
        Assert.Equal("spBv1.0/Group1/DCMD/Edge1/Device1", topic);
    }

    // ---- SparkplugMetricJson 测试 ----

    [Fact]
    public void MetricJson_DefaultValues()
    {
        var metric = new SparkplugMetricJson();
        Assert.Equal("", metric.Name);
        Assert.Equal(0, metric.DataType);
        Assert.Null(metric.Value);
        Assert.Equal(0L, metric.Timestamp);
        Assert.False(metric.IsHistorical);
        Assert.False(metric.IsDeadband);
        Assert.False(metric.IsTemplate);
        Assert.False(metric.IsAlias);
        Assert.False(metric.IsNull);
        Assert.False(metric.IsUUID);
    }

    [Fact]
    public void MetricJson_SetValues()
    {
        var metric = new SparkplugMetricJson
        {
            Name = "Temperature",
            DataType = 3, // Float
            Value = 25.5f,
            Timestamp = 1234567890L,
            IsHistorical = true
        };

        Assert.Equal("Temperature", metric.Name);
        Assert.Equal(3, metric.DataType);
        Assert.Equal(25.5f, metric.Value);
        Assert.Equal(1234567890L, metric.Timestamp);
        Assert.True(metric.IsHistorical);
    }

    // ---- SparkplugBState 枚举值测试 ----

    [Fact]
    public void SparkplugBState_EnumValues()
    {
        Assert.Equal(0, (int)SparkplugBState.Unknown);
        Assert.Equal(1, (int)SparkplugBState.Born);
        Assert.Equal(2, (int)SparkplugBState.Alive);
        Assert.Equal(3, (int)SparkplugBState.Dead);
    }

    // ---- 数据类型映射测试 ----
    // MapDataType 是 private static，通过反射测试

    [Theory]
    [InlineData("BOOL", 1)]
    [InlineData("BIT", 1)]
    [InlineData("BYTE", 2)]
    [InlineData("INT16", 3)]
    [InlineData("WORD", 3)]
    [InlineData("UINT16", 4)]
    [InlineData("INT32", 5)]
    [InlineData("DWORD", 5)]
    [InlineData("UINT32", 6)]
    [InlineData("INT64", 11)]
    [InlineData("UINT64", 12)]
    [InlineData("FLOAT", 7)]
    [InlineData("FLOAT32", 7)]
    [InlineData("DOUBLE", 8)]
    [InlineData("FLOAT64", 8)]
    [InlineData("STRING", 20)]
    public void MapDataType_KnownTypes_MapCorrectly(string dataType, int expectedCode)
    {
        // MapDataType is private static; verify via reflection
        var method = typeof(SparkplugBOutputPlugin).GetMethod("MapDataType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object[] { dataType });
        Assert.Equal(expectedCode, result);
    }

    [Fact]
    public void MapDataType_UnknownType_ReturnsStringCode()
    {
        var method = typeof(SparkplugBOutputPlugin).GetMethod("MapDataType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method!.Invoke(null, new object[] { "UNKNOWN_TYPE" });
        Assert.Equal(20, result); // String = 20
    }

    [Fact]
    public void MapDataType_CaseInsensitive()
    {
        var method = typeof(SparkplugBOutputPlugin).GetMethod("MapDataType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var resultUpper = method!.Invoke(null, new object[] { "BOOL" });
        var resultLower = method!.Invoke(null, new object[] { "bool" });
        Assert.Equal(resultUpper, resultLower);
    }
}
