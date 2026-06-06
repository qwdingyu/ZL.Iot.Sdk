// ============================================================
// 文件：ModbusTcpProtocolEngineTests.cs
// 描述：ModbusTcpProtocolEngine 单元测试 — 帧构建、响应解析、工具方法
// ============================================================

using System;
using System.Buffers.Binary;
using System.Threading;
using ZL.ProtocolGateway.Plugins.Shared;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins
{
    public class ModbusTcpProtocolEngineTests
    {
        #region BuildReadHoldingRegistersRequest

        [Fact]
        public void BuildReadHoldingRegistersRequest_Returns12Bytes()
        {
            var frame = ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(
                transactionId: 0x1234, unitId: 1, startAddress: 0, quantity: 10);

            Assert.Equal(12, frame.Length);
        }

        [Fact]
        public void BuildReadHoldingRegistersRequest_WritesCorrectTransactionId()
        {
            var frame = ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(
                transactionId: 0xABCD, unitId: 1, startAddress: 0, quantity: 1);

            Assert.Equal(0xAB, frame[0]);
            Assert.Equal(0xCD, frame[1]);
        }

        [Fact]
        public void BuildReadHoldingRegistersRequest_ProtocolIdIsZero()
        {
            var frame = ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(
                transactionId: 1, unitId: 1, startAddress: 0, quantity: 1);

            Assert.Equal(0x00, frame[2]);
            Assert.Equal(0x00, frame[3]);
        }

        [Fact]
        public void BuildReadHoldingRegistersRequest_LengthIs6()
        {
            var frame = ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(
                transactionId: 1, unitId: 1, startAddress: 0, quantity: 1);

            Assert.Equal(ushort.Parse("0006", System.Globalization.NumberStyles.HexNumber),
                BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(4, 2)));
        }

        [Fact]
        public void BuildReadHoldingRegistersRequest_WritesUnitIdAndFunctionCode()
        {
            var frame = ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(
                transactionId: 1, unitId: 5, startAddress: 100, quantity: 10);

            Assert.Equal(5, frame[6]);   // Unit ID
            Assert.Equal(0x03, frame[7]); // Function code
        }

        [Fact]
        public void BuildReadHoldingRegistersRequest_WritesStartAddressAndQuantity()
        {
            var frame = ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(
                transactionId: 1, unitId: 1, startAddress: 0x01F4, quantity: 0x000A);

            Assert.Equal(0x01F4, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(8, 2)));
            Assert.Equal(0x000A, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(10, 2)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(126)]
        public void BuildReadHoldingRegistersRequest_InvalidQuantity_ThrowsArgumentOutOfRangeException(ushort quantity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(1, 1, 0, quantity));
        }

        #endregion

        #region BuildReadCoilsRequest

        [Fact]
        public void BuildReadCoilsRequest_Returns12Bytes()
        {
            var frame = ModbusTcpProtocolEngine.BuildReadCoilsRequest(
                transactionId: 1, unitId: 1, startAddress: 0, quantity: 100);

            Assert.Equal(12, frame.Length);
        }

        [Fact]
        public void BuildReadCoilsRequest_FunctionCodeIs0x01()
        {
            var frame = ModbusTcpProtocolEngine.BuildReadCoilsRequest(
                transactionId: 1, unitId: 1, startAddress: 0, quantity: 1);

            Assert.Equal(0x01, frame[7]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2001)]
        public void BuildReadCoilsRequest_InvalidQuantity_Throws(ushort quantity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ModbusTcpProtocolEngine.BuildReadCoilsRequest(1, 1, 0, quantity));
        }

        #endregion

        #region ParseReadHoldingRegistersResponse

        [Fact]
        public void ParseReadHoldingRegistersResponse_ValidFrame_ReturnsValues()
        {
            // MBAP(6) + UnitID(1) + Function(0x03) + ByteCount(4) + 2 registers (4 bytes) = 14 bytes
            // Length field = bytes after Length field = UnitID + FC + ByteCount + Data = 1+1+1+4 = 7
            var frame = new byte[]
            {
                0x00, 0x01, // Transaction ID
                0x00, 0x00, // Protocol ID
                0x00, 0x07, // Length (7 bytes follow: UnitID + PDU)
                0x01,       // Unit ID
                0x03,       // Function code
                0x04,       // Byte count
                0x00, 0x01, // Register 1 = 1
                0x00, 0x02  // Register 2 = 2
            };

            var (success, values) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(frame);

            Assert.True(success);
            Assert.NotNull(values);
            Assert.Equal(2, values.Length);
            Assert.Equal((ushort)1, values[0]);
            Assert.Equal((ushort)2, values[1]);
        }

        [Fact]
        public void ParseReadHoldingRegistersResponse_Null_ReturnsFailure()
        {
            var (success, values) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(null);

            Assert.False(success);
            Assert.Null(values);
        }

        [Fact]
        public void ParseReadHoldingRegistersResponse_TooShort_ReturnsFailure()
        {
            var frame = new byte[] { 0x00, 0x01, 0x00 };

            var (success, values) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(frame);

            Assert.False(success);
            Assert.Null(values);
        }

        [Fact]
        public void ParseReadHoldingRegistersResponse_ErrorFunctionCode_ReturnsFailure()
        {
            // Error response: function code 0x83 (0x03 | 0x80)
            var frame = new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x03, 0x01,
                0x83, // Error function code
                0x02  // Exception code
            };

            var (success, values) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(frame);

            Assert.False(success);
            Assert.Null(values);
        }

        [Fact]
        public void ParseReadHoldingRegistersResponse_WrongFunctionCode_ReturnsFailure()
        {
            var frame = new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01,
                0x01, // Function code 0x01 (Read Coils, not 0x03)
                0x02
            };

            var (success, values) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(frame);

            Assert.False(success);
            Assert.Null(values);
        }

        [Fact]
        public void ParseReadHoldingRegistersResponse_IncompleteData_ReturnsFailure()
        {
            // Byte count says 4 but only 2 bytes available
            var frame = new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01,
                0x03, // Function code
                0x04, // Byte count = 4
                0x00, 0x01 // Only 2 bytes
            };

            var (success, values) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(frame);

            Assert.False(success);
            Assert.Null(values);
        }

        #endregion

        #region ParseReadCoilsResponse

        [Fact]
        public void ParseReadCoilsResponse_ValidFrame_ReturnsValues()
        {
            // 8 coils, all true in first byte (0xFF)
            // MBAP(6) + UnitID(1) + FC(1) + ByteCount(1) + Data(1) = 10 bytes
            // Length field = 5 (UnitID + FC + ByteCount + Data)
            var frame = new byte[]
            {
                0x00, 0x01, // Transaction ID
                0x00, 0x00, // Protocol ID
                0x00, 0x05, // Length (5 bytes follow: UnitID + PDU)
                0x01,       // Unit ID
                0x01,       // Function code 0x01
                0x01,       // Byte count
                0xFF        // All 8 coils = true
            };

            var (success, values) = ModbusTcpProtocolEngine.ParseReadCoilsResponse(frame, 8);

            Assert.True(success);
            Assert.NotNull(values);
            Assert.Equal(8, values.Length);
            for (int i = 0; i < 8; i++)
                Assert.True(values[i]);
        }

        [Fact]
        public void ParseReadCoilsResponse_ErrorFunctionCode_ReturnsFailure()
        {
            var frame = new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x03, 0x01,
                0x81, // Error function code
                0x02
            };

            var (success, values) = ModbusTcpProtocolEngine.ParseReadCoilsResponse(frame, 8);

            Assert.False(success);
            Assert.Null(values);
        }

        [Fact]
        public void ParseReadCoilsResponse_Null_ReturnsFailure()
        {
            var (success, values) = ModbusTcpProtocolEngine.ParseReadCoilsResponse(null, 8);

            Assert.False(success);
            Assert.Null(values);
        }

        #endregion

        #region ConvertToModbusAddress

        [Theory]
        [InlineData("0", 0)]
        [InlineData("100", 100)]
        [InlineData("65535", 65535)]
        public void ConvertToModbusAddress_ValidString_ReturnsParsed(string input, ushort expected)
        {
            var result = ModbusTcpProtocolEngine.ConvertToModbusAddress(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("-1")]
        public void ConvertToModbusAddress_InvalidString_ThrowsFormatException(string input)
        {
            Assert.Throws<FormatException>(() => ModbusTcpProtocolEngine.ConvertToModbusAddress(input));
        }

        #endregion

        #region Constants

        [Fact]
        public void FunctionCodes_HaveCorrectValues()
        {
            Assert.Equal(0x03, ModbusTcpProtocolEngine.FunctionReadHoldingRegisters);
            Assert.Equal(0x01, ModbusTcpProtocolEngine.FunctionReadCoils);
            Assert.Equal(0x06, ModbusTcpProtocolEngine.FunctionWriteSingleRegister);
            Assert.Equal(0x05, ModbusTcpProtocolEngine.FunctionWriteSingleCoil);
        }

        #endregion
    }
}
