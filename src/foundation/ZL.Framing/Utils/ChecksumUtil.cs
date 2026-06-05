using System;

namespace ZL.Framing
{
    public static class ChecksumUtil
    {
        public static int GetLength(string method)
        {
            if (string.IsNullOrWhiteSpace(method)) method = "SUM8";
            switch (method.Trim().ToUpperInvariant())
            {
                case "SUM":
                case "SUM8":
                case "XOR":
                    return 1;
                case "CRC16":
                case "CRC16_MODBUS":
                case "CRC16_CCITT":
                    return 2;
                case "CRC32":
                    return 4;
                default:
                    throw new InvalidOperationException($"Unknown checksum method: {method}");
            }
        }

        public static byte[] Calculate(byte[] data, string method)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(method)) method = "SUM8";

            switch (method.Trim().ToUpperInvariant())
            {
                case "SUM":
                case "SUM8":
                    return new[] { Sum8(data) };
                case "XOR":
                    return new[] { Xor8(data) };
                case "CRC16":
                case "CRC16_MODBUS":
                    return ToLittleEndian(Crc16Modbus(data));
                case "CRC16_CCITT":
                    return ToBigEndian(Crc16Ccitt(data));
                case "CRC32":
                    return ToBigEndian(Crc32(data));
                default:
                    throw new InvalidOperationException($"Unknown checksum method: {method}");
            }
        }

        public static byte[] Append(byte[] data, string method)
        {
            var checksum = Calculate(data, method);
            var output = new byte[data.Length + checksum.Length];
            Buffer.BlockCopy(data, 0, output, 0, data.Length);
            Buffer.BlockCopy(checksum, 0, output, data.Length, checksum.Length);
            return output;
        }

        private static byte Sum8(byte[] data)
        {
            int sum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i];
            }
            return (byte)(sum & 0xFF);
        }

        private static byte Xor8(byte[] data)
        {
            byte value = 0;
            for (int i = 0; i < data.Length; i++)
            {
                value ^= data[i];
            }
            return value;
        }

        private static ushort Crc16Modbus(byte[] data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    bool lsb = (crc & 0x0001) != 0;
                    crc >>= 1;
                    if (lsb)
                    {
                        crc ^= 0xA001;
                    }
                }
            }
            return crc;
        }

        private static ushort Crc16Ccitt(byte[] data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                    {
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    }
                    else
                    {
                        crc <<= 1;
                    }
                }
            }
            return crc;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc = (crc >> 1) ^ 0xEDB88320;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return ~crc;
        }

        private static byte[] ToLittleEndian(ushort value)
        {
            return new[] { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) };
        }

        private static byte[] ToBigEndian(ushort value)
        {
            return new[] { (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF) };
        }

        private static byte[] ToBigEndian(uint value)
        {
            return new[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }
    }
}
