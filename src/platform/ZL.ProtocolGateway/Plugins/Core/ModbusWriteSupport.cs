using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.Json;
using ZL.ProtocolGateway.Framing;

namespace ZL.ProtocolGateway.Plugins
{
    public readonly struct ModbusWriteOperation
    {
        public ModbusWriteOperation(ushort address, ushort value, bool isCoil, byte unitId)
        {
            Address = address;
            Value = value;
            IsCoil = isCoil;
            UnitId = unitId;
        }

        public ushort Address { get; }
        public ushort Value { get; }
        public bool IsCoil { get; }
        public byte UnitId { get; }
    }

    public static class ModbusWriteSupport
    {
        public static List<ModbusWriteOperation> ParseWrites(Message message, byte defaultUnitId = 1)
        {
            var writes = new List<ModbusWriteOperation>();
            // 优先尝试 JSON 内容，失败时回退到纯文本（GetJsonContent 在 ContentType != "json" 时抛异常，?? 无法捕获）
            string jsonPayload = "{}";
            try
            {
                jsonPayload = message.GetJsonContent() ?? "{}";
            }
            catch
            {
                jsonPayload = message.GetTextContent() ?? "{}";
            }

            // JsonDocument.Parse 可能对非 JSON 文本抛 JsonReaderException，需捕获后返回空列表
            try
            {
                using var document = JsonDocument.Parse(jsonPayload);
                if (!document.RootElement.TryGetProperty("registers", out var registers) || registers.ValueKind != JsonValueKind.Array)
                {
                    return writes;
                }

                foreach (var item in registers.EnumerateArray())
                {
                    if (!item.TryGetProperty("address", out var addressElement) ||
                        !item.TryGetProperty("value", out var valueElement))
                    {
                        continue;
                    }

                    byte unitId = defaultUnitId;
                    if (item.TryGetProperty("unitId", out var unitIdElement) && unitIdElement.TryGetByte(out var parsedUnitId))
                    {
                        unitId = parsedUnitId;
                    }

                    string addressText = addressElement.GetString() ?? string.Empty;
                    string valueText = valueElement.GetString() ?? valueElement.ToString();
                    if (TryParseAddress(addressText, valueText, unitId, out var write))
                    {
                        writes.Add(write);
                    }
                }
            }
            catch
            {
                return writes;
            }

            return writes;
        }

        public static byte[] BuildTcpWriteRequest(ModbusWriteOperation write, ushort transactionId)
        {
            byte[] pdu = BuildWritePdu(write);
            byte[] frame = new byte[7 + pdu.Length];
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), transactionId);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), (ushort)(pdu.Length + 1));
            frame[6] = write.UnitId;
            Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);
            return frame;
        }

        public static byte[] BuildRtuWriteRequest(ModbusWriteOperation write)
        {
            byte[] frame = new byte[8];
            frame[0] = write.UnitId;
            frame[1] = write.IsCoil ? (byte)0x05 : (byte)0x06;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), write.Address);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), write.Value);
            ushort crc = ModbusRtuSplitter.Crc16Modbus(frame, 0, 6);
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);
            return frame;
        }

        public static void ValidateTcpWriteResponse(byte[] response, ModbusWriteOperation write)
        {
            if (response.Length < 12)
            {
                throw new InvalidOperationException("Invalid Modbus TCP response length");
            }

            byte functionCode = response[7];
            if ((functionCode & 0x80) != 0)
            {
                throw new InvalidOperationException($"Modbus exception code: {response[8]}");
            }

            ushort address = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(8, 2));
            ushort value = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(10, 2));
            if (address != write.Address || value != write.Value)
            {
                throw new InvalidOperationException("Modbus write response mismatch");
            }
        }

        public static void ValidateRtuWriteResponse(byte[] response, ModbusWriteOperation write)
        {
            if (response.Length != 8)
            {
                throw new InvalidOperationException("Invalid Modbus RTU response length");
            }

            ushort expectedCrc = ModbusRtuSplitter.Crc16Modbus(response, 0, 6);
            ushort actualCrc = (ushort)(response[6] | (response[7] << 8));
            if (expectedCrc != actualCrc)
            {
                throw new InvalidOperationException("Invalid Modbus RTU CRC");
            }

            byte functionCode = response[1];
            if ((functionCode & 0x80) != 0)
            {
                throw new InvalidOperationException($"Modbus exception code: {response[2]}");
            }

            if (response[0] != write.UnitId)
            {
                throw new InvalidOperationException("Modbus RTU unit id mismatch");
            }

            ushort address = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
            ushort value = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(4, 2));
            if (address != write.Address || value != write.Value)
            {
                throw new InvalidOperationException("Modbus RTU write response mismatch");
            }
        }

        public static bool TryParseAddress(string addressText, string valueText, byte unitId, out ModbusWriteOperation write)
        {
            write = default;
            if (string.IsNullOrWhiteSpace(addressText))
            {
                return false;
            }

            string normalized = addressText.Trim().ToUpperInvariant();
            string digits = ExtractDigits(normalized);
            if (string.IsNullOrWhiteSpace(digits) || !int.TryParse(digits, out var numericAddress))
            {
                return false;
            }

            bool boolLike = bool.TryParse(valueText, out _) ||
                            string.Equals(valueText, "0", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(valueText, "1", StringComparison.OrdinalIgnoreCase);

            bool isCoil =
                normalized.StartsWith("COIL", StringComparison.Ordinal) ||
                normalized.StartsWith("C", StringComparison.Ordinal) ||
                normalized.StartsWith("M", StringComparison.Ordinal) ||
                normalized.StartsWith("X", StringComparison.Ordinal) ||
                normalized.StartsWith("Y", StringComparison.Ordinal) ||
                (numericAddress is >= 1 and <= 9999 && boolLike && !normalized.StartsWith("4", StringComparison.Ordinal));

            if (isCoil)
            {
                ushort coilAddress = (ushort)Math.Max(0, numericAddress - 1);
                ushort coilValue = ParseBoolLikeValue(valueText) ? (ushort)0xFF00 : (ushort)0x0000;
                write = new ModbusWriteOperation(coilAddress, coilValue, true, unitId);
                return true;
            }

            ushort registerAddress = numericAddress >= 40001
                ? (ushort)(numericAddress - 40001)
                : (ushort)numericAddress;
            ushort registerValue = ParseRegisterValue(valueText);
            write = new ModbusWriteOperation(registerAddress, registerValue, false, unitId);
            return true;
        }

        private static byte[] BuildWritePdu(ModbusWriteOperation write)
        {
            byte[] pdu = new byte[5];
            pdu[0] = write.IsCoil ? (byte)0x05 : (byte)0x06;
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), write.Address);
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), write.Value);
            return pdu;
        }

        private static ushort ParseRegisterValue(string valueText)
        {
            if (int.TryParse(valueText, out var intValue))
            {
                return unchecked((ushort)intValue);
            }

            if (double.TryParse(valueText, out var doubleValue))
            {
                return unchecked((ushort)Math.Round(doubleValue));
            }

            if (bool.TryParse(valueText, out var boolValue))
            {
                return boolValue ? (ushort)1 : (ushort)0;
            }

            throw new InvalidOperationException($"Unsupported Modbus register value: {valueText}");
        }

        private static bool ParseBoolLikeValue(string valueText)
        {
            if (bool.TryParse(valueText, out var boolValue))
            {
                return boolValue;
            }

            return valueText.Trim() switch
            {
                "1" => true,
                "0" => false,
                _ => throw new InvalidOperationException($"Unsupported Modbus coil value: {valueText}")
            };
        }

        private static string ExtractDigits(string text)
        {
            Span<char> buffer = stackalloc char[text.Length];
            int count = 0;
            foreach (var ch in text)
            {
                if (char.IsDigit(ch))
                {
                    buffer[count++] = ch;
                }
            }

            return new string(buffer.Slice(0, count).ToArray());
        }
    }
}
