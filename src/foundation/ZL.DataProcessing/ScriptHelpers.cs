using System;
using System.Globalization;

namespace ZL.DataProcessing
{
    public static class HexHelper
    {
        public static string Normalize(string input)
        {
            return HexCodec.Normalize(input);
        }

        public static byte[] ToBytes(string hex)
        {
            return HexCodec.ToBytes(hex);
        }

        public static string ToString(byte[] data, string separator = "")
        {
            return HexCodec.ToString(data, separator);
        }

        public static string FromInt(int value, int byteCount, bool littleEndian = false)
        {
            if (byteCount <= 0) return string.Empty;
            byte[] bytes = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                int shift = littleEndian ? i * 8 : (byteCount - 1 - i) * 8;
                bytes[i] = (byte)((value >> shift) & 0xFF);
            }
            return HexCodec.ToString(bytes);
        }
    }

    public static class ChecksumHelper
    {
        public static int GetLength(string method)
        {
            if (string.IsNullOrWhiteSpace(method)) method = "SUM8";
            switch (method.Trim().ToUpperInvariant())
            {
                case "SUM":
                case "SUM8":
                case "XOR":
                case "CRC8":
                case "CRC8_ITU":
                case "CRC8_MAXIM":
                case "CRC8_SAE_J1850":
                case "CRC8_ROHC":
                    return 1;
                case "SUM16":
                case "SUM16_BE":
                case "SUM16_LE":
                case "XOR16":
                case "XOR16_BE":
                case "XOR16_LE":
                case "CRC16":
                case "CRC16_MODBUS":
                case "CRC16_CCITT":
                case "CRC16_IBM":
                case "CRC16_X25":
                case "CRC16_XMODEM":
                case "CRC16_USB":
                case "CRC16_DNP":
                case "CRC16_KERMIT":
                case "CRC16_AUG_CCITT":
                    return 2;
                case "SUM32":
                case "SUM32_BE":
                case "SUM32_LE":
                case "XOR32":
                case "XOR32_BE":
                case "XOR32_LE":
                case "CRC32":
                case "CRC32C":
                case "CRC32_MPEG2":
                    return 4;
                default:
                    throw new InvalidOperationException($"Unknown checksum method: {method}");
            }
        }

        public static byte[] Calculate(byte[] data, string method = "SUM8")
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(method)) method = "SUM8";

            switch (method.Trim().ToUpperInvariant())
            {
                case "SUM":
                case "SUM8":
                    return new[] { Sum8(data) };
                case "SUM16":
                case "SUM16_BE":
                    return ToBigEndian(Sum16(data));
                case "SUM16_LE":
                    return ToLittleEndian(Sum16(data));
                case "SUM32":
                case "SUM32_BE":
                    return ToBigEndian(Sum32(data));
                case "SUM32_LE":
                    return ToLittleEndian(Sum32(data));
                case "XOR":
                    return new[] { Xor8(data) };
                case "XOR16":
                case "XOR16_BE":
                    return ToBigEndian(Xor16(data));
                case "XOR16_LE":
                    return ToLittleEndian(Xor16(data));
                case "XOR32":
                case "XOR32_BE":
                    return ToBigEndian(Xor32(data));
                case "XOR32_LE":
                    return ToLittleEndian(Xor32(data));
                case "CRC8":
                    return new[] { Crc8(data) };
                case "CRC8_ITU":
                    return new[] { Crc8Itu(data) };
                case "CRC8_MAXIM":
                    return new[] { Crc8Maxim(data) };
                case "CRC8_SAE_J1850":
                    return new[] { Crc8SaeJ1850(data) };
                case "CRC8_ROHC":
                    return new[] { Crc8Rohc(data) };
                case "CRC16":
                case "CRC16_MODBUS":
                    return ToLittleEndian(Crc16Modbus(data));
                case "CRC16_CCITT":
                    return ToBigEndian(Crc16Ccitt(data));
                case "CRC16_IBM":
                    return ToLittleEndian(Crc16Ibm(data));
                case "CRC16_X25":
                    return ToLittleEndian(Crc16X25(data));
                case "CRC16_XMODEM":
                    return ToBigEndian(Crc16Xmodem(data));
                case "CRC16_USB":
                    return ToLittleEndian(Crc16Usb(data));
                case "CRC16_DNP":
                    return ToLittleEndian(Crc16Dnp(data));
                case "CRC16_KERMIT":
                    return ToLittleEndian(Crc16Kermit(data));
                case "CRC16_AUG_CCITT":
                    return ToBigEndian(Crc16AugCcitt(data));
                case "CRC32":
                    return ToBigEndian(Crc32(data));
                case "CRC32C":
                    return ToBigEndian(Crc32C(data));
                case "CRC32_MPEG2":
                    return ToBigEndian(Crc32Mpeg2(data));
                default:
                    throw new InvalidOperationException($"Unknown checksum method: {method}");
            }
        }

        public static string ToHex(byte[] data, string method = "SUM8", string separator = "")
        {
            return HexCodec.ToString(Calculate(data, method), separator);
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

        private static ushort Sum16(byte[] data)
        {
            uint sum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i];
            }
            return (ushort)(sum & 0xFFFF);
        }

        private static uint Sum32(byte[] data)
        {
            uint sum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i];
            }
            return sum;
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

        private static ushort Xor16(byte[] data)
        {
            ushort value = 0;
            for (int i = 0; i < data.Length; i++)
            {
                value ^= data[i];
            }
            return value;
        }

        private static uint Xor32(byte[] data)
        {
            uint value = 0;
            for (int i = 0; i < data.Length; i++)
            {
                value ^= data[i];
            }
            return value;
        }

        private static byte Crc8(byte[] data)
        {
            return Crc8General(data, 0x07, 0x00, 0x00, false, false);
        }

        private static byte Crc8Itu(byte[] data)
        {
            return Crc8General(data, 0x07, 0x00, 0x55, false, false);
        }

        private static byte Crc8Maxim(byte[] data)
        {
            return Crc8General(data, 0x8C, 0x00, 0x00, true, true);
        }

        private static byte Crc8SaeJ1850(byte[] data)
        {
            return Crc8General(data, 0x1D, 0xFF, 0xFF, false, false);
        }

        private static byte Crc8Rohc(byte[] data)
        {
            return Crc8General(data, 0xE0, 0xFF, 0x00, true, true);
        }

        private static ushort Crc16Modbus(byte[] data)
        {
            return Crc16General(data, 0xA001, 0xFFFF, 0x0000, true, true);
        }

        private static ushort Crc16Ccitt(byte[] data)
        {
            return Crc16General(data, 0x1021, 0xFFFF, 0x0000, false, false);
        }

        private static ushort Crc16Ibm(byte[] data)
        {
            return Crc16General(data, 0xA001, 0x0000, 0x0000, true, true);
        }

        private static ushort Crc16X25(byte[] data)
        {
            return Crc16General(data, 0x8408, 0xFFFF, 0xFFFF, true, true);
        }

        private static ushort Crc16Xmodem(byte[] data)
        {
            return Crc16General(data, 0x1021, 0x0000, 0x0000, false, false);
        }

        private static ushort Crc16Usb(byte[] data)
        {
            return Crc16General(data, 0xA001, 0xFFFF, 0xFFFF, true, true);
        }

        private static ushort Crc16Dnp(byte[] data)
        {
            return Crc16General(data, 0xA6BC, 0x0000, 0xFFFF, true, true);
        }

        private static ushort Crc16Kermit(byte[] data)
        {
            return Crc16General(data, 0x8408, 0x0000, 0x0000, true, true);
        }

        private static ushort Crc16AugCcitt(byte[] data)
        {
            return Crc16General(data, 0x1021, 0x1D0F, 0x0000, false, false);
        }

        private static uint Crc32(byte[] data)
        {
            return Crc32General(data, 0xEDB88320, 0xFFFFFFFF, 0xFFFFFFFF, true, true);
        }

        private static uint Crc32C(byte[] data)
        {
            return Crc32General(data, 0x82F63B78, 0xFFFFFFFF, 0xFFFFFFFF, true, true);
        }

        private static uint Crc32Mpeg2(byte[] data)
        {
            return Crc32General(data, 0x04C11DB7, 0xFFFFFFFF, 0x00000000, false, false);
        }

        private static byte Crc8General(byte[] data, byte poly, byte init, byte xorOut, bool reflectIn, bool reflectOut)
        {
            byte crc = init;
            for (int i = 0; i < data.Length; i++)
            {
                byte cur = data[i];
                if (reflectIn)
                {
                    crc ^= cur;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool lsb = (crc & 0x01) != 0;
                        crc >>= 1;
                        if (lsb)
                        {
                            crc ^= poly;
                        }
                    }
                }
                else
                {
                    crc ^= cur;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool msb = (crc & 0x80) != 0;
                        crc <<= 1;
                        if (msb)
                        {
                            crc ^= poly;
                        }
                    }
                }
            }

            if (reflectIn != reflectOut)
            {
                crc = Reflect8(crc);
            }

            return (byte)(crc ^ xorOut);
        }

        private static ushort Crc16General(
            byte[] data,
            ushort poly,
            ushort init,
            ushort xorOut,
            bool reflectIn,
            bool reflectOut)
        {
            ushort crc = init;
            for (int i = 0; i < data.Length; i++)
            {
                ushort cur = data[i];
                if (reflectIn)
                {
                    crc ^= cur;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool lsb = (crc & 0x0001) != 0;
                        crc >>= 1;
                        if (lsb)
                        {
                            crc ^= poly;
                        }
                    }
                }
                else
                {
                    crc ^= (ushort)(cur << 8);
                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool msb = (crc & 0x8000) != 0;
                        crc <<= 1;
                        if (msb)
                        {
                            crc ^= poly;
                        }
                    }
                }
            }

            if (reflectIn != reflectOut)
            {
                crc = Reflect16(crc);
            }

            return (ushort)(crc ^ xorOut);
        }

        private static uint Crc32General(
            byte[] data,
            uint poly,
            uint init,
            uint xorOut,
            bool reflectIn,
            bool reflectOut)
        {
            uint crc = init;
            for (int i = 0; i < data.Length; i++)
            {
                uint cur = data[i];
                if (reflectIn)
                {
                    crc ^= cur;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool lsb = (crc & 1) != 0;
                        crc >>= 1;
                        if (lsb)
                        {
                            crc ^= poly;
                        }
                    }
                }
                else
                {
                    crc ^= cur << 24;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool msb = (crc & 0x80000000) != 0;
                        crc <<= 1;
                        if (msb)
                        {
                            crc ^= poly;
                        }
                    }
                }
            }

            if (reflectIn != reflectOut)
            {
                crc = Reflect32(crc);
            }

            return crc ^ xorOut;
        }

        private static byte Reflect8(byte value)
        {
            byte result = 0;
            for (int i = 0; i < 8; i++)
            {
                result <<= 1;
                result |= (byte)(value & 1);
                value >>= 1;
            }
            return result;
        }

        private static ushort Reflect16(ushort value)
        {
            ushort result = 0;
            for (int i = 0; i < 16; i++)
            {
                result <<= 1;
                result |= (ushort)(value & 1);
                value >>= 1;
            }
            return result;
        }

        private static uint Reflect32(uint value)
        {
            uint result = 0;
            for (int i = 0; i < 32; i++)
            {
                result <<= 1;
                result |= (value & 1);
                value >>= 1;
            }
            return result;
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

        private static byte[] ToLittleEndian(uint value)
        {
            return new[]
            {
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF)
            };
        }
    }

    public static class CrcHelper
    {
        public static byte Xor(byte[] data)
        {
            return ChecksumHelper.Calculate(data, "XOR")[0];
        }

        public static byte Sum(byte[] data)
        {
            return ChecksumHelper.Calculate(data, "SUM8")[0];
        }

        public static byte[] Modbus(byte[] data)
        {
            return ChecksumHelper.Calculate(data, "CRC16_MODBUS");
        }

        public static byte[] Ccitt(byte[] data)
        {
            return ChecksumHelper.Calculate(data, "CRC16_CCITT");
        }

        public static byte[] Crc32(byte[] data)
        {
            return ChecksumHelper.Calculate(data, "CRC32");
        }
    }

    public static class BitHelper
    {
        public static bool Get(int value, int bitIndex)
        {
            return BinaryLogic.GetBit(value, bitIndex);
        }

        public static bool Get(byte value, int bitIndex)
        {
            return BinaryLogic.GetBit(value, bitIndex);
        }

        public static byte Set(byte value, int bitIndex, bool on)
        {
            return BinaryLogic.SetBit(value, bitIndex, on);
        }
    }

    public static class FormatHelper
    {
        public static string Binary(int value, int width = 0)
        {
            string text = Convert.ToString(value, 2);
            if (width > 0)
            {
                return text.PadLeft(width, '0');
            }
            return text;
        }

        public static string Bcd(int value)
        {
            byte bcd = BinaryLogic.IntToBcd(value);
            return bcd.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
