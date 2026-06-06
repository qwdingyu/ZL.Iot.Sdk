using System;
using System.Buffers.Binary;
using System.Text;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class ModbusWriteSupportTests
    {
        [Fact]
        public void ParseWrites_RegisterAddress_ParsesHoldingRegister()
        {
            var message = new Message();
            message.SetJsonContent(@"{""registers"":[{""address"":""40001"",""value"":""123""}]}");

            var writes = ModbusWriteSupport.ParseWrites(message, 1);

            var write = Assert.Single(writes);
            Assert.False(write.IsCoil);
            Assert.Equal((ushort)0, write.Address);
            Assert.Equal((ushort)123, write.Value);
            Assert.Equal((byte)1, write.UnitId);
        }

        [Fact]
        public void ParseWrites_CoilAddress_ParsesCoilWrite()
        {
            var message = new Message();
            message.SetJsonContent(@"{""registers"":[{""address"":""M10"",""value"":""true""}]}");

            var writes = ModbusWriteSupport.ParseWrites(message, 3);

            var write = Assert.Single(writes);
            Assert.True(write.IsCoil);
            Assert.Equal((ushort)9, write.Address);
            Assert.Equal((ushort)0xFF00, write.Value);
            Assert.Equal((byte)3, write.UnitId);
        }

        [Fact]
        public void BuildTcpWriteRequest_BuildsValidMbapFrame()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            var frame = ModbusWriteSupport.BuildTcpWriteRequest(write, 7);

            Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(0, 2)));
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2, 2)));
            Assert.Equal((ushort)6, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(4, 2)));
            Assert.Equal((byte)1, frame[6]);
            Assert.Equal((byte)0x06, frame[7]);
            Assert.Equal((ushort)55, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(10, 2)));
        }

        [Fact]
        public void BuildRtuWriteRequest_AppendsCrc()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            var frame = ModbusWriteSupport.BuildRtuWriteRequest(write);

            Assert.Equal(8, frame.Length);
            Assert.Equal((byte)1, frame[0]);
            Assert.Equal((byte)0x06, frame[1]);
            ushort expectedCrc = ProtocolGateway.Framing.ModbusRtuSplitter.Crc16Modbus(frame, 0, 6);
            ushort actualCrc = (ushort)(frame[6] | (frame[7] << 8));
            Assert.Equal(expectedCrc, actualCrc);
        }

        #region ParseWrites 边界测试

        [Fact]
        public void ParseWrites_EmptyJson_ReturnsEmptyList()
        {
            var message = new Message();
            message.SetJsonContent("{}");

            var writes = ModbusWriteSupport.ParseWrites(message, 1);

            Assert.Empty(writes);
        }

        [Fact]
        public void ParseWrites_MissingRegisters_ReturnsEmptyList()
        {
            var message = new Message();
            message.SetJsonContent(@"{""data"":[{""address"":""1"",""value"":""2""}]}");

            var writes = ModbusWriteSupport.ParseWrites(message, 1);

            Assert.Empty(writes);
        }

        [Fact]
        public void ParseWrites_MissingAddress_SkipsItem()
        {
            var message = new Message();
            message.SetJsonContent(@"{""registers"":[{""value"":""123""},{""address"":""40001"",""value"":""456""}]}");

            var writes = ModbusWriteSupport.ParseWrites(message, 1);

            var write = Assert.Single(writes);
            Assert.Equal((ushort)456, write.Value);
        }

        [Fact]
        public void ParseWrites_CustomUnitId_OverridesDefault()
        {
            var message = new Message();
            message.SetJsonContent(@"{""registers"":[{""address"":""40001"",""value"":""1"",""unitId"":5}]}");

            var writes = ModbusWriteSupport.ParseWrites(message, 1);

            var write = Assert.Single(writes);
            Assert.Equal((byte)5, write.UnitId);
        }

        [Fact]
        public void ParseWrites_MultipleRegisters_ReturnsAll()
        {
            var message = new Message();
            message.SetJsonContent(@"{""registers"":[
                {""address"":""40001"",""value"":""1""},
                {""address"":""40002"",""value"":""2""},
                {""address"":""M1"",""value"":""true""}
            ]}");

            var writes = ModbusWriteSupport.ParseWrites(message, 1);

            Assert.Equal(3, writes.Count);
            Assert.False(writes[0].IsCoil);
            Assert.False(writes[1].IsCoil);
            Assert.True(writes[2].IsCoil);
        }

        #endregion

        #region TryParseAddress 地址解析测试

        [Theory]
        [InlineData("40001", "123", false, 0, 123)]    // 标准保持寄存器，>=40001 偏移 -40001
        [InlineData("40002", "0", false, 1, 0)]        // 地址偏移
        [InlineData("30001", "50", false, 30001, 50)]  // 输入寄存器，代码不对 30001 做偏移
        [InlineData("1", "1", true, 0, 0xFF00)]        // 线圈地址范围 1-9999 + bool值
        [InlineData("C1", "true", true, 0, 0xFF00)]    // C 前缀 → 线圈
        [InlineData("X10", "false", true, 9, 0)]       // X 前缀 → 线圈
        [InlineData("Y5", "1", true, 4, 0xFF00)]       // Y 前缀 → 线圈
        public void TryParseAddress_KnownFormats_ParsesCorrectly(string address, string value, bool expectedIsCoil, ushort expectedAddr, ushort expectedValue)
        {
            var result = ModbusWriteSupport.TryParseAddress(address, value, 1, out var write);

            Assert.True(result);
            Assert.Equal(expectedIsCoil, write.IsCoil);
            Assert.Equal(expectedAddr, write.Address);
            Assert.Equal(expectedValue, write.Value);
        }

        [Fact]
        public void TryParseAddress_EmptyAddress_ReturnsFalse()
        {
            var result = ModbusWriteSupport.TryParseAddress("", "123", 1, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseAddress_WhitespaceAddress_ReturnsFalse()
        {
            var result = ModbusWriteSupport.TryParseAddress("   ", "123", 1, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseAddress_NonNumericAddress_ReturnsFalse()
        {
            var result = ModbusWriteSupport.TryParseAddress("ABC", "123", 1, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseAddress_DoubleValue_RoundsToUshort()
        {
            var result = ModbusWriteSupport.TryParseAddress("40001", "3.7", 1, out var write);

            Assert.True(result);
            Assert.Equal((ushort)4, write.Value); // Math.Round(3.7) = 4
        }

        [Fact]
        public void TryParseAddress_BoolValue_ForRegister_Returns0Or1()
        {
            var result = ModbusWriteSupport.TryParseAddress("4", "true", 1, out var write);

            Assert.True(result);
            Assert.False(write.IsCoil); // 地址4 不是线圈范围
            Assert.Equal((ushort)1, write.Value);
        }

        [Fact]
        public void TryParseAddress_UnsupportedValue_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.TryParseAddress("COIL1", "notabool", 1, out _));
        }

        #endregion

        #region 响应验证测试

        [Fact]
        public void ValidateTcpWriteResponse_ValidResponse_DoesNotThrow()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            // 构建有效响应: MBAP(7) + Function(1) + Address(2) + Value(2) = 12 bytes
            var response = new byte[12];
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), 1); // transactionId
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), 6); // length
            response[6] = 1; // unitId
            response[7] = 0x06; // function code (no error)
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8, 2), 0); // address
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10, 2), 55); // value

            ModbusWriteSupport.ValidateTcpWriteResponse(response, write); // 不应抛异常
        }

        [Fact]
        public void ValidateTcpWriteResponse_TooShort_Throws()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            var response = new byte[5];

            Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
        }

        [Fact]
        public void ValidateTcpWriteResponse_ExceptionCode_Throws()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            var response = new byte[12];
            response[7] = 0x86; // 错误功能码 (0x06 | 0x80)
            response[8] = 0x02; // 异常码

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
            Assert.Contains("Modbus exception code", ex.Message);
        }

        [Fact]
        public void ValidateTcpWriteResponse_ValueMismatch_Throws()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            var response = new byte[12];
            response[7] = 0x06;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10, 2), 99); // 不匹配

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
            Assert.Contains("mismatch", ex.Message);
        }

        [Fact]
        public void ValidateRtuWriteResponse_ValidResponse_DoesNotThrow()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            var response = new byte[8];
            response[0] = 1; // unitId
            response[1] = 0x06; // function code
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0); // address
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), 55); // value
            // 计算 CRC
            ushort crc = ProtocolGateway.Framing.ModbusRtuSplitter.Crc16Modbus(response, 0, 6);
            response[6] = (byte)(crc & 0xFF);
            response[7] = (byte)(crc >> 8);

            ModbusWriteSupport.ValidateRtuWriteResponse(response, write); // 不应抛异常
        }

        [Fact]
        public void ValidateRtuWriteResponse_BadCrc_Throws()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            var response = new byte[] { 1, 0x06, 0, 0, 0, 55, 0xFF, 0xFF }; // 错误 CRC

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
            Assert.Contains("CRC", ex.Message);
        }

        [Fact]
        public void ValidateRtuWriteResponse_UnitIdMismatch_Throws()
        {
            var write = new ModbusWriteOperation(0, 55, false, 1);
            var response = new byte[8];
            response[0] = 2; // 不匹配的 unitId
            response[1] = 0x06;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), 55);
            ushort crc = ProtocolGateway.Framing.ModbusRtuSplitter.Crc16Modbus(response, 0, 6);
            response[6] = (byte)(crc & 0xFF);
            response[7] = (byte)(crc >> 8);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
            Assert.Contains("unit id mismatch", ex.Message);
        }

        #endregion

        #region RTU 线圈帧测试

        [Fact]
        public void BuildRtuWriteRequest_Coil_UsesFunctionCode05()
        {
            var write = new ModbusWriteOperation(10, 0xFF00, true, 3);
            var frame = ModbusWriteSupport.BuildRtuWriteRequest(write);

            Assert.Equal(8, frame.Length);
            Assert.Equal((byte)3, frame[0]);   // unitId
            Assert.Equal((byte)0x05, frame[1]); // function code for coil
            Assert.Equal((ushort)10, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2, 2)));
            Assert.Equal((ushort)0xFF00, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(4, 2)));
        }

        #endregion
    }
}
