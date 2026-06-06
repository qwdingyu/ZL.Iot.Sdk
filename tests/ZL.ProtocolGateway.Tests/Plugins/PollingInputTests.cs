using System;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using ZL.ProtocolGateway.Plugins.Shared;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins;

/// <summary>
/// 工业协议轮询输入层测试 — 验证协议引擎、轮询基类、S7/Modbus 输入插件
/// </summary>
public class PollingInputTests
{
    #region S7ProtocolEngine — 地址解析

    [Fact]
    public void ParseAddress_DBAddress_ParsesCorrectly()
    {
        var result = S7ProtocolEngine.ParseAddress("DB1.DBD0");

        Assert.Equal(1, result.dbNumber);
        Assert.Equal(S7ProtocolEngine.AreaMarkerDB, result.area);
        Assert.Equal(0, result.byteOffset);
        Assert.False(result.isBit);
    }

    [Fact]
    public void ParseAddress_DBWordAddress_ParsesCorrectly()
    {
        var result = S7ProtocolEngine.ParseAddress("DB3.DBW10");

        Assert.Equal(3, result.dbNumber);
        Assert.Equal(S7ProtocolEngine.AreaMarkerDB, result.area);
        Assert.Equal(10, result.byteOffset);
    }

    [Fact]
    public void ParseAddress_DBBitAddress_ParsesCorrectly()
    {
        var result = S7ProtocolEngine.ParseAddress("DB1.DBX5.3");

        Assert.Equal(1, result.dbNumber);
        Assert.Equal(S7ProtocolEngine.AreaMarkerDB, result.area);
        Assert.Equal(5, result.byteOffset);
        Assert.Equal(3, result.bitOffset);
        Assert.True(result.isBit);
    }

    [Fact]
    public void ParseAddress_MerkerAddress_ParsesCorrectly()
    {
        var result = S7ProtocolEngine.ParseAddress("M10");

        Assert.Equal(0, result.dbNumber);
        Assert.Equal(S7ProtocolEngine.AreaMarkerMerker, result.area);
        Assert.Equal(10, result.byteOffset);
    }

    [Fact]
    public void ParseAddress_InputBitAddress_ParsesCorrectly()
    {
        var result = S7ProtocolEngine.ParseAddress("I0.5");

        Assert.Equal(S7ProtocolEngine.AreaMarkerInput, result.area);
        Assert.Equal(0, result.byteOffset);
        Assert.Equal(5, result.bitOffset);
        Assert.True(result.isBit);
    }

    [Fact]
    public void ParseAddress_OutputAddress_ParsesCorrectly()
    {
        var result = S7ProtocolEngine.ParseAddress("Q0");

        Assert.Equal(S7ProtocolEngine.AreaMarkerOutput, result.area);
        Assert.Equal(0, result.byteOffset);
    }

    [Fact]
    public void ParseAddress_NullOrDefault_ReturnsDefault()
    {
        var result = S7ProtocolEngine.ParseAddress("");

        Assert.Equal(1, result.dbNumber);
        Assert.Equal(S7ProtocolEngine.AreaMarkerDB, result.area);
    }

    #endregion

    #region S7ProtocolEngine — 帧构建

    [Fact]
    public void BuildReadRequest_ProducesValidTpktFrame()
    {
        var frame = S7ProtocolEngine.BuildReadRequest("DB1.DBD0", 0x04, 4, 0x1001);

        Assert.True(frame.Length > 20);
        Assert.Equal(0x03, frame[0]); // TPKT version
        Assert.Equal(0x00, frame[1]); // TPKT reserved
        // Length field
        int totalLen = (frame[2] << 8) | frame[3];
        Assert.Equal(frame.Length, totalLen);
        // COTP DT
        Assert.Equal(0x02, frame[4]);
        Assert.Equal(0xF0, frame[5]);
        Assert.Equal(0x80, frame[6]);
        // S7 Header
        Assert.Equal(0x32, frame[7]); // Protocol ID
        Assert.Equal(0x01, frame[8]); // ROSCTR: Job
        Assert.Equal(0x04, frame[17]); // Function: Read Var
    }

    [Fact]
    public void BuildWriteRequest_ProducesValidTpktFrame()
    {
        byte[] value = new byte[] { 0x00, 0x00, 0x00, 0x01 };
        var frame = S7ProtocolEngine.BuildWriteRequest("DB1.DBD0", value, 0x2001);

        Assert.True(frame.Length > 25);
        Assert.Equal(0x03, frame[0]);
        Assert.Equal(0x32, frame[7]);
        Assert.Equal(0x05, frame[17]); // Function: Write Var
    }

    [Fact]
    public void BuildMultiReadRequest_ProducesValidFrame()
    {
        var tags = new S7ReadTag[]
        {
            new S7ReadTag("DB1.DBD0", 0x04, 4),
            new S7ReadTag("DB1.DBD4", 0x05, 4),
        };

        var frame = S7ProtocolEngine.BuildMultiReadRequest(tags, 0x3001);

        Assert.True(frame.Length > 30);
        Assert.Equal(0x03, frame[0]);
        Assert.Equal(0x32, frame[7]);
        Assert.Equal(0x04, frame[17]); // Read Var (S7 header 10 bytes + func code at offset 10 → frame[17])
        // Verify param/data lengths: paramLen at S7 offset 6-7 → frame[13-14], dataLen at S7 offset 8-9 → frame[15-16]
        int paramLen = (frame[13] << 8) | frame[14];
        int dataLen = (frame[15] << 8) | frame[16];
        Assert.Equal(28, paramLen); // 4 (func+reserved+count) + 12*2 items
        Assert.Equal(8, dataLen);   // 4 * 2 items
        // Total length sanity check
        int totalLen = (frame[2] << 8) | frame[3];
        Assert.Equal(frame.Length, totalLen);
    }

    #endregion

    #region S7ProtocolEngine — 响应解析

    [Fact]
    public void ParseReadResponse_SuccessResponse_ReturnsData()
    {
        // Simulate a valid S7 Read response: TPKT(4) + COTP(3) + S7Header(10) + Param(4) + Data(return_code+transport+length+data)
        byte[] response = new byte[29];
        response[0] = 0x03; // TPKT version
        response[2] = (byte)(29 >> 8);
        response[3] = (byte)(29 & 0xFF);
        response[4] = 0x02; response[5] = 0xF0; response[6] = 0x80; // COTP DT
        response[7] = 0x32; // Protocol ID
        response[8] = 0x03; // ROSCTR: Ack_Data
        response[9] = 0x00; // error_class
        response[10] = 0x00; // error_code
        response[17] = 0x04; // Function: Read Var
        response[18] = 0x00; response[19] = 0x01; // item count = 1
        response[21] = 0xFF; // return_code: success
        response[22] = 0x04; // transport size: DWORD
        response[23] = 0x00; // data length high
        response[24] = 0x04; // data length low = 4 bytes
        response[25] = 0x40; response[26] = 0x49; response[27] = 0x0F; response[28] = 0xDB; // 3.14f in big-endian

        var (success, data) = S7ProtocolEngine.ParseReadResponse(response);

        Assert.True(success);
        Assert.NotNull(data);
        Assert.Equal(4, data.Length);
        Assert.Equal(0x40, data[0]);
    }

    [Fact]
    public void ParseReadResponse_FailureResponse_ReturnsFalse()
    {
        byte[] response = new byte[29];
        response[7] = 0x32;
        response[8] = 0x03;
        response[21] = 0x00; // return_code: error (not 0xFF)

        var (success, _) = S7ProtocolEngine.ParseReadResponse(response);

        Assert.False(success);
    }

    [Fact]
    public void ParseReadResponse_TooShort_ReturnsFalse()
    {
        var (success, _) = S7ProtocolEngine.ParseReadResponse(new byte[10]);
        Assert.False(success);
    }

    [Fact]
    public void ParseReadResponse_Null_ReturnsFalse()
    {
        var (success, _) = S7ProtocolEngine.ParseReadResponse(null!);
        Assert.False(success);
    }

    #endregion

    #region S7ProtocolEngine — COTP/S7 握手帧

    [Fact]
    public void BuildCotpConnectionRequest_ProducesValidFrame()
    {
        var frame = S7ProtocolEngine.BuildCotpConnectionRequest();

        Assert.Equal(0x03, frame[0]); // TPKT version
        Assert.Equal(19, frame.Length); // 4 + 15
        Assert.Equal(0xE0, frame[5]); // COTP CR PDU type
    }

    [Fact]
    public void BuildS7SetupCommunication_ProducesValidFrame()
    {
        var frame = S7ProtocolEngine.BuildS7SetupCommunication(rack: 0, slot: 1);

        Assert.True(frame.Length > 30);
        Assert.Equal(0x03, frame[0]);
        Assert.Equal(0x32, frame[7]); // Protocol ID
        // Rack and slot are in data section
        Assert.Contains((byte)0x00, frame); // rack=0
    }

    #endregion

    #region ModbusTcpProtocolEngine — 帧构建

    [Fact]
    public void BuildReadHoldingRegistersRequest_ProducesValidMbapFrame()
    {
        var frame = ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(0x0001, 1, 0, 10);

        Assert.Equal(12, frame.Length); // MBAP(6) + UnitID(1) + PDU(5)
        Assert.Equal(0x00, frame[0]); // Transaction ID high
        Assert.Equal(0x01, frame[1]); // Transaction ID low
        Assert.Equal(0x00, frame[2]); // Protocol ID high
        Assert.Equal(0x00, frame[3]); // Protocol ID low
        Assert.Equal(0x00, frame[4]); // Length high
        Assert.Equal(0x06, frame[5]); // Length low = 6
        Assert.Equal(0x01, frame[6]); // Unit ID
        Assert.Equal(ModbusTcpProtocolEngine.FunctionReadHoldingRegisters, frame[7]); // Function code
        Assert.Equal(0x00, frame[8]); // Start address high
        Assert.Equal(0x00, frame[9]); // Start address low
        Assert.Equal(0x00, frame[10]); // Quantity high
        Assert.Equal(0x0A, frame[11]); // Quantity low = 10
    }

    [Fact]
    public void BuildReadCoilsRequest_ProducesValidFrame()
    {
        var frame = ModbusTcpProtocolEngine.BuildReadCoilsRequest(5, 3, 100, 8);

        Assert.Equal(12, frame.Length);
        Assert.Equal(0x00, frame[0]);
        Assert.Equal(0x05, frame[1]);
        Assert.Equal(0x03, frame[6]); // Unit ID
        Assert.Equal(ModbusTcpProtocolEngine.FunctionReadCoils, frame[7]);
    }

    [Fact]
    public void BuildReadHoldingRegistersRequest_InvalidQuantity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(1, 1, 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(1, 1, 0, 126));
    }

    #endregion

    #region ModbusTcpProtocolEngine — 响应解析

    [Fact]
    public void ParseReadHoldingRegistersResponse_ValidResponse_ReturnsValues()
    {
        // MBAP(7) + Function(1) + ByteCount(1) + 3 registers (6 bytes) = 15 bytes
        byte[] frame = new byte[16];
        frame[8] = ModbusTcpProtocolEngine.FunctionReadHoldingRegisters;
        frame[9] = 0x06;
        frame[10] = 0x00; frame[11] = 0x01;
        frame[12] = 0x00; frame[13] = 0x64;
        frame[14] = 0x03; frame[15] = 0xE8;

        var (success, values) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(frame);

        Assert.True(success);
        Assert.NotNull(values);
        Assert.Equal(3, values.Length);
        Assert.Equal((ushort)1, values[0]);
        Assert.Equal((ushort)100, values[1]);
        Assert.Equal((ushort)1000, values[2]);
    }

    [Fact]
    public void ParseReadHoldingRegistersResponse_ErrorResponse_ReturnsFalse()
    {
        byte[] frame = new byte[10];
        frame[8] = (byte)(ModbusTcpProtocolEngine.FunctionReadHoldingRegisters + 0x80); // Error response

        var (success, _) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(frame);

        Assert.False(success);
    }

    [Fact]
    public void ParseReadHoldingRegistersResponse_TooShort_ReturnsFalse()
    {
        var (success, _) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(new byte[5]);
        Assert.False(success);
    }

    #endregion

    #region S7PollingInputPlugin — 配置验证

    [Fact]
    public void S7PollingInputConfig_InvalidServerIp_ReturnsError()
    {
        var config = new S7PollingInputConfig { ServerIp = "" };
        var errors = config.Validate();

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.PropertyName == "ServerIp");
    }

    [Fact]
    public void S7PollingInputConfig_NoTags_ReturnsError()
    {
        var config = new S7PollingInputConfig { ServerIp = "192.168.1.1" };
        var errors = config.Validate();

        Assert.Contains(errors, e => e.PropertyName == "Tags");
    }

    [Fact]
    public void S7PollingInputConfig_ValidConfig_ReturnsNoErrors()
    {
        var config = new S7PollingInputConfig
        {
            ServerIp = "192.168.1.10",
            Port = 102,
            Tags = new[] { new S7InputTag("DB1.DBD0", "FLOAT", 1000) }
        };
        var errors = config.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void S7PollingInputPlugin_Constructor_RegistersTags()
    {
        var config = new S7PollingInputConfig
        {
            ServerIp = "192.168.1.10",
            Tags = new[]
            {
                new S7InputTag("DB1.DBD0", "FLOAT", 500),
                new S7InputTag("DB1.DBW4", "UINT16", 2000),
            }
        };

        var plugin = new S7PollingInputPlugin(config);

        Assert.Equal("S7", plugin.ProtocolType);
        Assert.NotNull(plugin.Name);
    }

    #endregion

    #region ModbusTcpPollingInputPlugin — 配置验证

    [Fact]
    public void ModbusTcpPollingInputConfig_InvalidServerIp_ReturnsError()
    {
        var config = new ModbusTcpPollingInputConfig { ServerIp = "" };
        var errors = config.Validate();

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ModbusTcpPollingInputConfig_ValidConfig_ReturnsNoErrors()
    {
        var config = new ModbusTcpPollingInputConfig
        {
            ServerIp = "192.168.1.20",
            Port = 502,
            Registers = new[] { new ModbusInputRegister(0, 10, 1000) }
        };
        var errors = config.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void ModbusTcpPollingInputPlugin_Constructor_RegistersTags()
    {
        var config = new ModbusTcpPollingInputConfig
        {
            ServerIp = "192.168.1.20",
            Registers = new[]
            {
                new ModbusInputRegister(0, 5, 500),
                new ModbusInputRegister(100, 1, 2000, isCoil: true),
            }
        };

        var plugin = new ModbusTcpPollingInputPlugin(config);

        Assert.Equal("ModbusTcp", plugin.ProtocolType);
        Assert.NotNull(plugin.Name);
    }

    #endregion

    #region IndustrialPollingInputBase — 变化检测

    /// <summary>
    /// 测试用最小子类
    /// </summary>
    private class TestPollingInput : IndustrialPollingInputBase
    {
        public override string Name => "TestPoll";
        public override string ProtocolType => "Test";

        protected override Task TryConnectAsync(CancellationToken ct) => Task.CompletedTask;
        protected override Task<PollResult[]> OnPollAsync(CancellationToken ct) => Task.FromResult(Array.Empty<PollResult>());
    }

    [Fact]
    public void HasValueChanged_FirstValue_ReturnsTrue()
    {
        var input = new TestPollingInput();
        // Use reflection to access protected method
        var method = typeof(IndustrialPollingInputBase).GetMethod("HasValueChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = method!.Invoke(input, new object[] { "tag1", 42.0 });
        Assert.True((bool)result!);
    }

    [Fact]
    public void HasValueChanged_SameValue_ReturnsFalse()
    {
        var input = new TestPollingInput();
        var method = typeof(IndustrialPollingInputBase).GetMethod("HasValueChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // First call: value changes
        method!.Invoke(input, new object[] { "tag1", 42.0 });
        // Second call: same value
        var result = method.Invoke(input, new object[] { "tag1", 42.0 });
        Assert.False((bool)result!);
    }

    [Fact]
    public void HasValueChanged_DifferentValue_ReturnsTrue()
    {
        var input = new TestPollingInput();
        var method = typeof(IndustrialPollingInputBase).GetMethod("HasValueChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        method!.Invoke(input, new object[] { "tag1", 42.0 });
        var result = method.Invoke(input, new object[] { "tag1", 99.0 });
        Assert.True((bool)result!);
    }

    [Fact]
    public void ResetValueCache_ClearsCache()
    {
        var input = new TestPollingInput();
        var method = typeof(IndustrialPollingInputBase).GetMethod("HasValueChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        method!.Invoke(input, new object[] { "tag1", 42.0 });

        input.ResetValueCache();

        // After reset, same value should be reported as changed
        var result = method.Invoke(input, new object[] { "tag1", 42.0 });
        Assert.True((bool)result!);
    }

    #endregion

    #region AllenBradleyPollingInputPlugin — 配置验证

    [Fact]
    public void AllenBradleyPollingInputConfig_InvalidServerIp_ReturnsError()
    {
        var config = new AllenBradleyPollingInputConfig { ServerIp = "" };
        var errors = config.Validate();

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.PropertyName == "ServerIp");
    }

    [Fact]
    public void AllenBradleyPollingInputConfig_NoTags_ReturnsError()
    {
        var config = new AllenBradleyPollingInputConfig { ServerIp = "192.168.1.1" };
        var errors = config.Validate();

        Assert.Contains(errors, e => e.PropertyName == "Tags");
    }

    [Fact]
    public void AllenBradleyPollingInputConfig_ValidConfig_ReturnsNoErrors()
    {
        var config = new AllenBradleyPollingInputConfig
        {
            ServerIp = "192.168.1.10",
            Port = 44818,
            Tags = new[] { new AllenBradleyInputTag { TagName = "MyTag", DataType = "REAL" } }
        };
        var errors = config.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void AllenBradleyPollingInputPlugin_Constructor_RegistersTags()
    {
        var config = new AllenBradleyPollingInputConfig
        {
            ServerIp = "192.168.1.10",
            Tags = new[]
            {
                new AllenBradleyInputTag { TagName = "MyTag", DataType = "REAL" },
                new AllenBradleyInputTag { TagName = "Counter", DataType = "DINT" },
            }
        };

        var plugin = new AllenBradleyPollingInputPlugin(config);

        Assert.Equal("AllenBradley", plugin.ProtocolType);
        Assert.NotNull(plugin.Name);
    }

    #endregion
}
