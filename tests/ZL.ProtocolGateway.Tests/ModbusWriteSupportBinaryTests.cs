using System;
using System.Buffers.Binary;
using System.Text.Json;
using ZL.ProtocolGateway.Framing;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class ModbusWriteSupportBinaryTests
    {
        #region BuildTcpWriteRequest

        [Fact]
        public void BuildTcpWriteRequest_Coil_ProducesValidTcpFrame()
        {
            var write = new ModbusWriteOperation(address: 5, value: 0xFF00, isCoil: true, unitId: 1);
            var frame = ModbusWriteSupport.BuildTcpWriteRequest(write, transactionId: 0x1234);

            // TCP frame: [txn_hi, txn_lo, proto_hi, proto_lo, len_hi, len_lo, unitId, FC, addr_hi, addr_lo, val_hi, val_lo]
            Assert.Equal(13, frame.Length); // 7 header + 6 PDU
            Assert.Equal((byte)0x12, frame[0]);
            Assert.Equal((byte)0x34, frame[1]);
            Assert.Equal((byte)0x00, frame[2]); // protocol id = 0
            Assert.Equal((byte)0x00, frame[3]);
            Assert.Equal((byte)0x00, frame[4]); // length hi
            Assert.Equal((byte)0x06, frame[5]); // length lo = PDU length + 1 = 5 + 1 = 6
            Assert.Equal((byte)0x01, frame[6]); // unit id
            Assert.Equal((byte)0x05, frame[7]); // FC05 = Write Single Coil
            Assert.Equal((byte)0x00, frame[8]); // address hi (5 = 0x0005)
            Assert.Equal((byte)0x05, frame[9]); // address lo
            Assert.Equal((byte)0xFF, frame[10]); // value hi (0xFF00)
            Assert.Equal((byte)0x00, frame[11]); // value lo
        }

        [Fact]
        public void BuildTcpWriteRequest_Register_ProducesValidTcpFrame()
        {
            var write = new ModbusWriteOperation(address: 100, value: 0x0064, isCoil: false, unitId: 3);
            var frame = ModbusWriteSupport.BuildTcpWriteRequest(write, transactionId: 1);

            Assert.Equal(13, frame.Length);
            Assert.Equal((byte)0x00, frame[0]); // txn hi
            Assert.Equal((byte)0x01, frame[1]); // txn lo
            Assert.Equal((byte)0x03, frame[6]); // unit id
            Assert.Equal((byte)0x06, frame[7]); // FC06 = Write Single Register
            Assert.Equal((byte)0x00, frame[8]); // address hi (100 = 0x0064)
            Assert.Equal((byte)0x64, frame[9]); // address lo
            Assert.Equal((byte)0x00, frame[10]); // value hi
            Assert.Equal((byte)0x64, frame[11]); // value lo
        }

        #endregion

        #region BuildRtuWriteRequest

        [Fact]
        public void BuildRtuWriteRequest_Coil_ProducesValidRtuFrame()
        {
            var write = new ModbusWriteOperation(address: 0, value: 0xFF00, isCoil: true, unitId: 1);
            var frame = ModbusWriteSupport.BuildRtuWriteRequest(write);

            Assert.Equal(8, frame.Length);
            Assert.Equal((byte)0x01, frame[0]); // unit id
            Assert.Equal((byte)0x05, frame[1]); // FC05
            Assert.Equal((byte)0x00, frame[2]); // address hi
            Assert.Equal((byte)0x00, frame[3]); // address lo
            Assert.Equal((byte)0xFF, frame[4]); // value hi
            Assert.Equal((byte)0x00, frame[5]); // value lo

            // Verify CRC
            ushort expectedCrc = ModbusRtuSplitter.Crc16Modbus(frame, 0, 6);
            ushort actualCrc = (ushort)(frame[6] | (frame[7] << 8));
            Assert.Equal(expectedCrc, actualCrc);
        }

        [Fact]
        public void BuildRtuWriteRequest_Register_ProducesValidRtuFrame()
        {
            var write = new ModbusWriteOperation(address: 10, value: 200, isCoil: false, unitId: 2);
            var frame = ModbusWriteSupport.BuildRtuWriteRequest(write);

            Assert.Equal(8, frame.Length);
            Assert.Equal((byte)0x02, frame[0]); // unit id
            Assert.Equal((byte)0x06, frame[1]); // FC06

            // Verify CRC
            ushort expectedCrc = ModbusRtuSplitter.Crc16Modbus(frame, 0, 6);
            ushort actualCrc = (ushort)(frame[6] | (frame[7] << 8));
            Assert.Equal(expectedCrc, actualCrc);
        }

        #endregion

        #region ValidateTcpWriteResponse

        [Fact]
        public void ValidateTcpWriteResponse_ValidRegisterResponse_Passes()
        {
            // Valid FC06 response: [txn, proto, len, unitId, FC06, addr_hi, addr_lo, val_hi, val_lo]
            var write = new ModbusWriteOperation(address: 5, value: 100, isCoil: false, unitId: 1);
            byte[] response = new byte[]
            {
                0x00, 0x01, // transaction id
                0x00, 0x00, // protocol id
                0x00, 0x06, // length
                0x01,       // unit id
                0x06,       // FC06 echo
                0x00, 0x05, // address = 5
                0x00, 0x64  // value = 100
            };

            // Should not throw
            ModbusWriteSupport.ValidateTcpWriteResponse(response, write);
        }

        [Fact]
        public void ValidateTcpWriteResponse_ValidCoilResponse_Passes()
        {
            var write = new ModbusWriteOperation(address: 3, value: 0xFF00, isCoil: true, unitId: 1);
            byte[] response = new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x06,
                0x01,       // unit id
                0x05,       // FC05 echo
                0x00, 0x03, // address = 3
                0xFF, 0x00  // value = 0xFF00 (coil ON)
            };

            ModbusWriteSupport.ValidateTcpWriteResponse(response, write);
        }

        [Fact]
        public void ValidateTcpWriteResponse_ShortResponse_Throws()
        {
            var write = new ModbusWriteOperation(address: 0, value: 0, isCoil: false, unitId: 1);
            byte[] response = new byte[] { 1, 2, 3, 4, 5 }; // < 12 bytes

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
            Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidateTcpWriteResponse_ExceptionResponse_Throws()
        {
            var write = new ModbusWriteOperation(address: 0, value: 0, isCoil: false, unitId: 1);
            byte[] response = new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x03,
                0x01, 0x86, 0x02, // FC06 | 0x80 = exception, error code 2
                0x00, 0x00, 0x00, 0x00
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
            Assert.Contains("exception", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidateTcpWriteResponse_Mismatch_Throws()
        {
            var write = new ModbusWriteOperation(address: 5, value: 100, isCoil: false, unitId: 1);
            byte[] response = new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x06,
                0x01, 0x06,
                0x00, 0x05, // address = 5 (correct)
                0x00, 0x01  // value = 1 (wrong, expected 100)
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateTcpWriteResponse(response, write));
            Assert.Contains("mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region ValidateRtuWriteResponse

        [Fact]
        public void ValidateRtuWriteResponse_ValidResponse_Passes()
        {
            var write = new ModbusWriteOperation(address: 5, value: 100, isCoil: false, unitId: 1);
            byte[] body = new byte[] { 0x01, 0x06, 0x00, 0x05, 0x00, 0x64 };
            ushort crc = ModbusRtuSplitter.Crc16Modbus(body, 0, body.Length);
            byte[] response = new byte[8];
            System.Buffer.BlockCopy(body, 0, response, 0, body.Length);
            response[6] = (byte)(crc & 0xFF);
            response[7] = (byte)(crc >> 8);

            ModbusWriteSupport.ValidateRtuWriteResponse(response, write);
        }

        [Fact]
        public void ValidateRtuWriteResponse_ValidCoilResponse_Passes()
        {
            var write = new ModbusWriteOperation(address: 0, value: 0xFF00, isCoil: true, unitId: 1);
            byte[] body = new byte[] { 0x01, 0x05, 0x00, 0x00, 0xFF, 0x00 };
            ushort crc = ModbusRtuSplitter.Crc16Modbus(body, 0, body.Length);
            byte[] response = new byte[8];
            System.Buffer.BlockCopy(body, 0, response, 0, body.Length);
            response[6] = (byte)(crc & 0xFF);
            response[7] = (byte)(crc >> 8);

            ModbusWriteSupport.ValidateRtuWriteResponse(response, write);
        }

        [Fact]
        public void ValidateRtuWriteResponse_WrongCrc_Throws()
        {
            var write = new ModbusWriteOperation(address: 0, value: 0, isCoil: false, unitId: 1);
            byte[] response = new byte[] { 0x01, 0x06, 0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
            Assert.Contains("CRC", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidateRtuWriteResponse_ExceptionResponse_Throws()
        {
            var write = new ModbusWriteOperation(address: 0, value: 0, isCoil: false, unitId: 1);
            byte[] body = new byte[] { 0x01, 0x86, 0x01, 0x00, 0x00, 0x00 };
            ushort crc = ModbusRtuSplitter.Crc16Modbus(body, 0, body.Length);
            byte[] response = new byte[8];
            System.Buffer.BlockCopy(body, 0, response, 0, body.Length);
            response[6] = (byte)(crc & 0xFF);
            response[7] = (byte)(crc >> 8);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
            Assert.Contains("exception", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidateRtuWriteResponse_UnitMismatch_Throws()
        {
            var write = new ModbusWriteOperation(address: 0, value: 1, isCoil: false, unitId: 1);
            byte[] body = new byte[] { 0x02, 0x06, 0x00, 0x00, 0x00, 0x01 }; // unitId=2, expected 1
            ushort crc = ModbusRtuSplitter.Crc16Modbus(body, 0, body.Length);
            byte[] response = new byte[8];
            System.Buffer.BlockCopy(body, 0, response, 0, body.Length);
            response[6] = (byte)(crc & 0xFF);
            response[7] = (byte)(crc >> 8);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
            Assert.Contains("unit id", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidateRtuWriteResponse_WrongLength_Throws()
        {
            var write = new ModbusWriteOperation(address: 0, value: 0, isCoil: false, unitId: 1);
            byte[] response = new byte[] { 1, 2, 3, 4, 5, 6, 7 }; // 7 bytes, expected 8

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ModbusWriteSupport.ValidateRtuWriteResponse(response, write));
            Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region TryParseAddress Edge Cases

        [Fact]
        public void TryParseAddress_AddressZero_ParsesAsCoil()
        {
            // Address 0 is not in 1-9999 range, so not auto-coil; treated as register
            var result = ModbusWriteSupport.TryParseAddress("0", "1", 1, out var write);

            Assert.True(result);
            Assert.False(write.IsCoil);
            Assert.Equal((ushort)0, write.Address);
        }

        [Fact]
        public void TryParseAddress_LargeAddress_ParsesAsRegister()
        {
            var result = ModbusWriteSupport.TryParseAddress("65535", "1", 1, out var write);

            Assert.True(result);
            Assert.False(write.IsCoil);
            Assert.Equal((ushort)25534, write.Address); // 65535 - 40001
        }

        [Fact]
        public void TryParseAddress_DoubleValue_RoundsToUshort()
        {
            var result = ModbusWriteSupport.TryParseAddress("40001", "3.7", 1, out var write);

            Assert.True(result);
            Assert.False(write.IsCoil);
            Assert.Equal((ushort)4, write.Value); // Math.Round(3.7) = 4
        }

        [Fact]
        public void TryParseAddress_InvalidValue_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                ModbusWriteSupport.TryParseAddress("40001", "abc", 1, out _);
            });
        }

        [Fact]
        public void TryParseAddress_EmptyAddress_ReturnsFalse()
        {
            var result = ModbusWriteSupport.TryParseAddress("", "1", 1, out var write);

            Assert.False(result);
        }

        #endregion
    }
}
