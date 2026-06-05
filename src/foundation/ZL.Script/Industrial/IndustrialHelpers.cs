using System;
using System.Globalization;
using System.Collections.Generic;

namespace ZL.Script.Industrial
{
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
                    return 2; // Default to 2 for safety, but usually throws in old code. 
            }
        }

        public static byte[] Calculate(byte[] data, string method = "SUM8")
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(method)) method = "SUM8";

            switch (method.Trim().ToUpperInvariant())
            {
                case "SUM": case "SUM8": return new[] { Sum8(data) };
                case "SUM16": case "SUM16_BE": return ToBigEndian(Sum16(data));
                case "SUM16_LE": return ToLittleEndian(Sum16(data));
                case "SUM32": case "SUM32_BE": return ToBigEndian(Sum32(data));
                case "SUM32_LE": return ToLittleEndian(Sum32(data));
                case "XOR": case "XOR8": return new[] { Xor8(data) };
                case "XOR16": case "XOR16_BE": return ToBigEndian(Xor16(data));
                case "XOR16_LE": return ToLittleEndian(Xor16(data));
                case "XOR32": case "XOR32_BE": return ToBigEndian(Xor32(data));
                case "XOR32_LE": return ToLittleEndian(Xor32(data));
                case "CRC8": return new[] { Crc8(data) };
                case "CRC8_ITU": return new[] { Crc8General(data, 0x07, 0x00, 0x55, false, false) };
                case "CRC8_MAXIM": return new[] { Crc8General(data, 0x8C, 0x00, 0x00, true, true) };
                case "CRC8_SAE_J1850": return new[] { Crc8General(data, 0x1D, 0xFF, 0xFF, false, false) };
                case "CRC8_ROHC": return new[] { Crc8General(data, 0xE0, 0xFF, 0x00, true, true) };
                case "CRC16": case "CRC16_MODBUS": case "MODBUS": return ToLittleEndian(Crc16Modbus(data));
                case "CRC16_CCITT": case "CCITT": return ToBigEndian(Crc16General(data, 0x1021, 0xFFFF, 0x0000, false, false));
                case "CRC16_IBM": return ToLittleEndian(Crc16General(data, 0xA001, 0x0000, 0x0000, true, true));
                case "CRC16_X25": return ToLittleEndian(Crc16General(data, 0x8408, 0xFFFF, 0xFFFF, true, true));
                case "CRC16_XMODEM": return ToBigEndian(Crc16General(data, 0x1021, 0x0000, 0x0000, false, false));
                case "CRC16_USB": return ToLittleEndian(Crc16General(data, 0xA001, 0xFFFF, 0xFFFF, true, true));
                case "CRC16_DNP": return ToLittleEndian(Crc16General(data, 0xA6BC, 0x0000, 0xFFFF, true, true));
                case "CRC16_KERMIT": return ToLittleEndian(Crc16General(data, 0x8408, 0x0000, 0x0000, true, true));
                case "CRC16_AUG_CCITT": return ToBigEndian(Crc16General(data, 0x1021, 0x1D0F, 0x0000, false, false));
                case "CRC32": return ToBigEndian(Crc32General(data, 0xEDB88320, 0xFFFFFFFF, 0xFFFFFFFF, true, true));
                case "CRC32C": return ToBigEndian(Crc32General(data, 0x82F63B78, 0xFFFFFFFF, 0xFFFFFFFF, true, true));
                case "CRC32_MPEG2": return ToBigEndian(Crc32General(data, 0x04C11DB7, 0xFFFFFFFF, 0x00000000, false, false));
                default:
                    throw new NotSupportedException($"Method {method} not supported.");
            }
        }

        #region Base Algorithms (Ported from ZL.DataProcessing for 100% compatibility)

        private static byte Sum8(byte[] data)
        {
            int sum = 0;
            foreach (var b in data) sum += b;
            return (byte)(sum & 0xFF);
        }

        private static ushort Sum16(byte[] data)
        {
            uint sum = 0;
            foreach (var b in data) sum += b;
            return (ushort)(sum & 0xFFFF);
        }

        private static uint Sum32(byte[] data)
        {
            uint sum = 0;
            foreach (var b in data) sum += b;
            return sum;
        }

        private static byte Xor8(byte[] data)
        {
            byte v = 0;
            foreach (var b in data) v ^= b;
            return v;
        }

        private static ushort Xor16(byte[] data)
        {
            ushort v = 0;
            foreach (var b in data) v ^= b;
            return v;
        }

        private static uint Xor32(byte[] data)
        {
            uint v = 0;
            foreach (var b in data) v ^= b;
            return v;
        }

        private static byte Crc8(byte[] data) => Crc8General(data, 0x07, 0x00, 0x00, false, false);
        private static ushort Crc16Modbus(byte[] data) => Crc16General(data, 0xA001, 0xFFFF, 0x0000, true, true);

        private static byte Crc8General(byte[] data, byte poly, byte init, byte xorOut, bool reflectIn, bool reflectOut)
        {
            byte crc = init;
            foreach (byte b in data)
            {
                if (reflectIn)
                {
                    crc ^= b;
                    for (int i = 0; i < 8; i++)
                        crc = (crc & 0x01) != 0 ? (byte)((crc >> 1) ^ poly) : (byte)(crc >> 1);
                }
                else
                {
                    crc ^= b;
                    for (int i = 0; i < 8; i++)
                        crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ poly) : (byte)(crc << 1);
                }
            }
            if (reflectIn != reflectOut)
            {
                byte res = 0;
                for (int i = 0; i < 8; i++) { res <<= 1; res |= (byte)(crc & 1); crc >>= 1; }
                crc = res;
            }
            return (byte)(crc ^ xorOut);
        }

        private static ushort Crc16General(byte[] data, ushort poly, ushort init, ushort xorOut, bool reflectIn, bool reflectOut)
        {
            ushort crc = init;
            foreach (byte b in data)
            {
                if (reflectIn)
                {
                    crc ^= b;
                    for (int i = 0; i < 8; i++)
                        crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ poly) : (ushort)(crc >> 1);
                }
                else
                {
                    crc ^= (ushort)(b << 8);
                    for (int i = 0; i < 8; i++)
                        crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ poly) : (ushort)(crc << 1);
                }
            }
            if (reflectIn != reflectOut)
            {
                ushort res = 0;
                for (int i = 0; i < 16; i++) { res <<= 1; res |= (ushort)(crc & 1); crc >>= 1; }
                crc = res;
            }
            return (ushort)(crc ^ xorOut);
        }

        private static uint Crc32General(byte[] data, uint poly, uint init, uint xorOut, bool reflectIn, bool reflectOut)
        {
            uint crc = init;
            foreach (byte b in data)
            {
                if (reflectIn)
                {
                    crc ^= b;
                    for (int i = 0; i < 8; i++)
                        crc = (crc & 1) != 0 ? (uint)((crc >> 1) ^ poly) : (uint)(crc >> 1);
                }
                else
                {
                    crc ^= (uint)b << 24;
                    for (int i = 0; i < 8; i++)
                        crc = (crc & 0x80000000) != 0 ? (uint)((crc << 1) ^ poly) : (uint)(crc << 1);
                }
            }
            if (reflectIn != reflectOut)
            {
                uint res = 0;
                for (int i = 0; i < 32; i++) { res <<= 1; res |= (uint)(crc & 1); crc >>= 1; }
                crc = res;
            }
            return crc ^ xorOut;
        }

        #endregion

        private static byte[] ToLittleEndian(ushort v) => new[] { (byte)(v & 0xFF), (byte)(v >> 8) };
        private static byte[] ToBigEndian(ushort v) => new[] { (byte)(v >> 8), (byte)(v & 0xFF) };
        private static byte[] ToLittleEndian(uint v) => new[] { (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF), (byte)((v >> 24) & 0xFF) };
        private static byte[] ToBigEndian(uint v) => new[] { (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF) };

        public static string ToHex(byte[] data, string method = "SUM8", string separator = "")
        {
            byte[] res = Calculate(data, method);
            return BitConverter.ToString(res).Replace("-", separator);
        }
    }

    public static class Crc
    {
        public static byte Xor(byte[] data) => ChecksumHelper.Calculate(data, "XOR")[0];
        public static byte Sum(byte[] data) => ChecksumHelper.Calculate(data, "SUM8")[0];
        public static byte[] Modbus(byte[] data) => ChecksumHelper.Calculate(data, "CRC16_MODBUS");
        public static byte[] Ccitt(byte[] data) => ChecksumHelper.Calculate(data, "CRC16_CCITT");
        public static byte[] Crc32(byte[] data) => ChecksumHelper.Calculate(data, "CRC32");
    }

    public static class FormatHelper
    {
        public static string Binary(int value, int width = 0)
        {
            string text = Convert.ToString(value, 2);
            return width > 0 ? text.PadLeft(width, '0') : text;
        }

        public static string Hex(int value, string format = "X2")
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string Bcd(int value)
        {
            return BinaryHelper.IntToBcd(value).ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
