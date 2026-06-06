#nullable enable
using System;
using System.Buffers.Binary;
using System.Text.Json;
using ZL.ProtocolGateway.Framing;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins
{
    /// <summary>
    /// ModbusWriteSupport 深度测试 — 帧字节级验证、边界值、异常路径
    /// </summary>
    public class ModbusWriteSupportDeepTests
    {
        #region BuildTcpWriteRequest — 字节级验证

        [Fact]
        public void BuildTcpWriteRequest_HoldingRegister_FrameStructure_IsCorrect()
        {
            var write = new ModbusWriteOperation(address: 100, value: 0x1234, isCoil: false, unitId: 5);
            var frame = ModbusWriteSupport.BuildTcpWriteRequest(write, transactionId: 0xCAFE);

            // MBAP 头部: 7 bytes
            Assert.InRange(frame.Length, 12, 12); // 7 header + 5 PDU

            // Transaction ID (2 bytes, big-endian)
            Assert.Equal(0xCA, frame[0]);
            Assert.Equal(0xFE, frame[1]);

            // Protocol ID (2 bytes) = 0
            Assert.Equal(0x00, frame[2]);
            Assert.Equal(0x00, frame[3]);

            // Length (2 bytes) = PDU length + 1 = 6
            Assert.Equal(0x00, frame[4]);
            Assert.Equal(0x06, frame[5]);

            // Unit ID
            Assert.Equal(5, frame[6]);

            // PDU: Function code 0x06 (Write Single Register)
            Assert.Equal(0x06, frame[7]);

            // Register address (big-endian) = 100 = 0x0064
            Assert.Equal(0x00, frame[8]);
            Assert.Equal(0x64, frame[9]);

            // Register value (big-endian) = 0x1234
            Assert.Equal(0x12, frame[10]);
            Assert.Equal(0x34, frame[11]);
        }

        [Fact]
        public void BuildTcpWriteRequest_Coil_FrameStructure_IsCorrect()
        {
            var write = new ModbusWriteOperation(address: 0, value: 0xFF00, isCoil: true, unitId: 1);
            var frame = ModbusWriteSupport.BuildTcpWriteRequest(write, transactionId: 1);

            // Function code 0x05 (Write Single Coil)
            Assert.Equal(0x05, frame[7]);

            // Coil address = 0
            Assert.Equal(0x00, frame[8]);
            Assert.Equal(0x00, frame[9]);

            // Coil value = 0xFF00 (ON)
            Assert.Equal(0xFF, frame[10]);
            Assert.Equal(0x00, frame[11]);
        }

        #endregion

        #region BuildRtuWriteRequest — CRC 验证

        [Fact]
        public void BuildRtuWriteRequest_HoldingRegister_FrameAndCrc_AreCorrect()
        {
            var write = new ModbusWriteOperation(address: 0, value: 1, isCoil: false, unitId: 1);
            var frame = ModbusWriteSupport.BuildRtuWriteRequest(write);

            Assert.Equal(8, frame.Length);
            Assert.Equal(1, frame[0]);   // Unit ID
            Assert.Equal(0x06, frame[1]); // Function code: Write Single Register
            Assert.Equal(0x00, frame[2]); // Address high
            Assert.Equal(0x00, frame[3]); // Address low
            Assert.Equal(0x00, frame[4]); // Value high
            Assert.Equal(0x01, frame[5]); // Value low

            // 验证 CRC
            ushort crc = ModbusRtuSplitter.Crc16Modbus(frame, 0, 6);
            Assert.Equal((byte)(crc & 0xFF), frame[6]);
            Assert.Equal((byte)(crc >> 8), frame[7]);
        }

        [Fact]
        public void BuildRtuWriteRequest_Coil_FrameAndCrc_AreCorrect()
        {
            var write = new ModbusWriteOperation(address: 5, value: 0xFF00, isCoil: true, unitId: 3);
            var frame = ModbusWriteSupport.BuildRtuWriteRequest(write);

            Assert.Equal(8, frame.Length);
            Assert.Equal(3, frame[0]);    // Unit ID
            Assert.Equal(0x05, frame[1]); // Function code: Write Single Coil
            Assert.Equal(0x00, frame[2]); // Address high
            Assert.Equal(0x05, frame[3]); // Address low
            Assert.Equal(0xFF, frame[4]); // Value high (ON)
            Assert.Equal(0x00, frame[5]); // Value low

            // 验证 CRC
            ushort crc = ModbusRtuSplitter.Crc16Modbus(frame, 0, 6);
            Assert.Equal((byte)(crc & 0xFF), frame[6]);
            Assert.Equal((byte)(crc >> 8), frame[7]);
        }

        #endregion

        #region ValidateTcpWriteResponse — 边界测试

        [Fact]
        public void ValidateTcpWriteResponse_TooShort_Throws()
        {
            var write = new ModbusWriteOperation(0, 0, false, 1);
            var shortResponse = new byte[] { 0, 0, 0, 0, 0, 0, 1 }; // 只有 7 bytes

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(shortResponse, write));
        }

        [Fact]
        public void ValidateTcpWriteResponse_ExceptionFunctionCode_Throws()
        {
            var write = new ModbusWriteOperation(100, 200, false, 1);
            // 构建一个异常响应: function code = 0x86 (0x06 | 0x80), exception code = 2
            var response = new byte[12];
            response[6] = 1;       // Unit ID
            response[7] = 0x86;    // Exception function code
            response[8] = 0x02;    // Exception code: Illegal Data Value

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
        }

        [Fact]
        public void ValidateTcpWriteResponse_ValueMismatch_Throws()
        {
            var write = new ModbusWriteOperation(100, 200, false, 1);
            var response = new byte[12];
            response[6] = 1;       // Unit ID
            response[7] = 0x06;    // Function code
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8, 2), 100); // Address OK
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10, 2), 999); // Wrong value

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
        }

        [Fact]
        public void ValidateTcpWriteResponse_AddressMismatch_Throws()
        {
            var write = new ModbusWriteOperation(100, 200, false, 1);
            var response = new byte[12];
            response[6] = 1;
            response[7] = 0x06;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8, 2), 200); // Wrong address
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10, 2), 200); // Value OK for wrong addr

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
        }

        #endregion

        #region ValidateRtuWriteResponse — 边界测试

        [Fact]
        public void ValidateRtuWriteResponse_WrongLength_Throws()
        {
            var write = new ModbusWriteOperation(0, 0, false, 1);
            var shortResponse = new byte[] { 1, 0x06, 0, 0, 0, 0 }; // 6 bytes, not 8

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(shortResponse, write));
        }

        [Fact]
        public void ValidateRtuWriteResponse_BadCrc_Throws()
        {
            var write = new ModbusWriteOperation(0, 1, false, 1);
            var response = new byte[] { 1, 0x06, 0, 0, 0, 1, 0xFF, 0xFF }; // 故意错误 CRC

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
        }

        [Fact]
        public void ValidateRtuWriteResponse_ExceptionFunctionCode_Throws()
        {
            var write = new ModbusWriteOperation(0, 1, false, 1);
            var response = new byte[8];
            response[0] = 1;
            response[1] = 0x86; // Exception function code
            response[2] = 0x01; // Exception code: Illegal Function
            // CRC doesn't matter since we check function code first... actually CRC is checked first
            // Let's compute correct CRC for the first 6 bytes
            ushort crc = ModbusRtuSplitter.Crc16Modbus(response, 0, 6);
            response[6] = (byte)(crc & 0xFF);
            response[7] = (byte)(crc >> 8);

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
        }

        [Fact]
        public void ValidateRtuWriteResponse_UnitIdMismatch_Throws()
        {
            var write = new ModbusWriteOperation(0, 1, false, unitId: 5);
            var response = new byte[8];
            response[0] = 1; // Wrong unit ID (expected 5)
            response[1] = 0x06;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), 1);
            ushort crc = ModbusRtuSplitter.Crc16Modbus(response, 0, 6);
            response[6] = (byte)(crc & 0xFF);
            response[7] = (byte)(crc >> 8);

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
        }

        #endregion

        #region ParseWrites — 异常 JSON 处理

        [Fact]
        public void ParseWrites_InvalidJson_ReturnsEmptyList()
        {
            var msg = new Message();
            msg.SetJsonContent("{not valid json}");

            // JsonDocument.Parse 对非法 JSON 应静默返回空列表，而非抛出异常
            var result = ModbusWriteSupport.ParseWrites(msg, 1);
            Assert.Empty(result);
        }



        [Fact]
        public void ParseWrites_JsonContentType_ValidData_ParsesCorrectly()
        {
            var msg = new Message();
            msg.SetJsonContent("{\"registers\":[{\"address\":\"40001\",\"value\":\"42\"}]}");

            var writes = ModbusWriteSupport.ParseWrites(msg, 1);

            Assert.Single(writes);
            Assert.Equal((ushort)0, writes[0].Address); // 40001 - 40001 = 0
            Assert.Equal((ushort)42, writes[0].Value);
        }

        [Fact]
        public void ParseWrites_NoRegistersField_ReturnsEmpty()
        {
            var msg = new Message();
            msg.SetJsonContent("{\"other\":\"data\"}");

            var writes = ModbusWriteSupport.ParseWrites(msg, 1);
            Assert.Empty(writes);
        }

        [Fact]
        public void ParseWrites_RegistersNotArray_ReturnsEmpty()
        {
            var msg = new Message();
            msg.SetJsonContent("{\"registers\":\"not-an-array\"}");

            var writes = ModbusWriteSupport.ParseWrites(msg, 1);
            Assert.Empty(writes);
        }

        [Fact]
        public void ParseWrites_MissingValueField_Skipped()
        {
            var msg = new Message();
            msg.SetJsonContent("{\"registers\":[{\"address\":\"40001\"}]}");

            var writes = ModbusWriteSupport.ParseWrites(msg, 1);
            Assert.Empty(writes); // 缺少 value 字段 → 跳过
        }

        [Fact]
        public void ParseWrites_CustomUnitId_PerItem_OverridesDefault()
        {
            var msg = new Message();
            msg.SetJsonContent(@"{""registers"":[
                {""address"":""40001"",""value"":""1"",""unitId"":10},
                {""address"":""40002"",""value"":""2""}
            ]}");

            var writes = ModbusWriteSupport.ParseWrites(msg, defaultUnitId: 1);

            Assert.Equal(2, writes.Count);
            Assert.Equal(10, writes[0].UnitId); // 自定义 unitId
            Assert.Equal(1, writes[1].UnitId);  // 使用默认值
        }

        [Fact]
        public void ParseWrites_TextContent_FallsBackToTextContent()
        {
            // ParseWrites 已修复 try-catch 回退逻辑，Text 内容类型现在可以正常解析
            var msg = new Message();
            msg.SetTextContent("{\"registers\":[{\"address\":\"40001\",\"value\":\"42\"}]}");

            var writes = ModbusWriteSupport.ParseWrites(msg, 1);

            Assert.Single(writes);
            Assert.Equal(0, writes[0].Address); // 40001 - 40001 = 0
            Assert.Equal((ushort)42, writes[0].Value);
        }

        #endregion

        #region TryParseAddress — 额外边界

        [Fact]
        public void TryParseAddress_CoilWithZeroValue_ParsesAsOff()
        {
            Assert.True(ModbusWriteSupport.TryParseAddress("1", "0", 1, out var write));
            Assert.True(write.IsCoil);
            Assert.Equal((ushort)0, write.Value); // OFF
        }

        [Fact]
        public void TryParseAddress_CoilWithOneValue_ParsesAsOn()
        {
            Assert.True(ModbusWriteSupport.TryParseAddress("5", "1", 1, out var write));
            Assert.True(write.IsCoil);
            Assert.Equal((ushort)0xFF00, write.Value); // ON
        }

        [Fact]
        public void TryParseAddress_Address9999_BoolValue_IsCoil()
        {
            Assert.True(ModbusWriteSupport.TryParseAddress("9999", "true", 1, out var write));
            Assert.True(write.IsCoil);
            Assert.Equal((ushort)9998, write.Address); // 9999 - 1
        }

        [Fact]
        public void TryParseAddress_Address10000_BoolValue_IsRegister()
        {
            // 10000 > 9999, 不在线圈地址范围，即使值是 bool-like
            Assert.True(ModbusWriteSupport.TryParseAddress("10000", "true", 1, out var write));
            Assert.False(write.IsCoil);
        }

        [Fact]
        public void TryParseAddress_Address4_PrefixedWith4_IsRegister()
        {
            // "4" 开头 → 即使值看起来像 bool，也当作寄存器
            Assert.True(ModbusWriteSupport.TryParseAddress("4", "1", 1, out var write));
            Assert.False(write.IsCoil); // 以 "4" 开头 → 寄存器
        }

        #endregion
    }
}
#nullable restore
