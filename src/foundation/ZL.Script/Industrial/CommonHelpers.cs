using System;
using System.Text;
using System.Globalization;

namespace ZL.Script.Industrial
{
    public static class HexHelper
    {
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
            }
            return sb.ToString();
        }

        public static byte[] ToBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Array.Empty<byte>();
            string cleaned = Normalize(hex);
            if (cleaned.Length % 2 != 0) cleaned = "0" + cleaned;
            
            byte[] bytes = new byte[cleaned.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(cleaned.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return bytes;
        }

        public static string ToString(byte[] data, string separator = "")
        {
            if (data == null || data.Length == 0) return string.Empty;
            return BitConverter.ToString(data).Replace("-", separator);
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
            return ToString(bytes);
        }

        public static bool TryStripChecksum(string hexInput, int checksumByteLen, out string strippedHex, out byte[] checksumBytes)
        {
            strippedHex = hexInput ?? string.Empty;
            checksumBytes = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(hexInput) || checksumByteLen <= 0) return false;

            byte[] totalBytes;
            try { totalBytes = ToBytes(hexInput); } catch { return false; }

            if (totalBytes.Length <= checksumByteLen) return false;

            int dataLen = totalBytes.Length - checksumByteLen;
            byte[] data = new byte[dataLen];
            checksumBytes = new byte[checksumByteLen];

            Buffer.BlockCopy(totalBytes, 0, data, 0, dataLen);
            Buffer.BlockCopy(totalBytes, dataLen, checksumBytes, 0, checksumByteLen);

            strippedHex = ToString(data);
            return true;
        }
    }

    public static class BinaryHelper
    {
        public static byte[] SwapBytes(byte[] source, string format = "ABCD")
        {
            if (source == null || source.Length < 2) return source ?? Array.Empty<byte>();

            byte[] result = (byte[])source.Clone();
            string fmt = format?.ToUpperInvariant() ?? "ABCD";

            switch (fmt)
            {
                case "DCBA":
                    Array.Reverse(result);
                    break;
                case "CDAB":
                    if (result.Length >= 4)
                    {
                        for (int i = 0; i < result.Length - 1; i += 2)
                        {
                            byte temp = result[i];
                            result[i] = result[i + 1];
                            result[i + 1] = temp;
                        }
                    }
                    break;
                case "BADC":
                    for (int i = 0; i < result.Length - 1; i += 2)
                    {
                        byte temp = result[i];
                        result[i] = result[i + 1];
                        result[i + 1] = temp;
                    }
                    break;
                default: break;
            }
            return result;
        }

        public static int BcdToInt(byte bcd) => ((bcd >> 4) * 10) + (bcd & 0x0F);

        public static byte IntToBcd(int value)
        {
            if (value > 99 || value < 0) return 0;
            return (byte)(((value / 10) << 4) | (value % 10));
        }

        public static bool GetBit(int value, int bitIndex) => (value & (1 << bitIndex)) != 0;
        public static bool GetBit(byte b, int bitIndex) => (b & (1 << bitIndex)) != 0;

        public static byte SetBit(byte b, int bitIndex, bool on)
        {
            if (on) return (byte)(b | (1 << bitIndex));
            return (byte)(b & ~(1 << bitIndex));
        }
    }

    public static class SimHelper
    {
        private static readonly Random _random = new Random();

        /// <summary>
        /// 为数值添加随机噪声
        /// </summary>
        public static double AddNoise(double value, double ratio = 0.001)
        {
            return value + (value * ratio * (_random.NextDouble() * 2 - 1));
        }

        /// <summary>
        /// 在指定百分比范围内生成随机值
        /// </summary>
        public static double Range(double value, double percent = 5.0)
        {
            double range = value * (percent / 100.0);
            return value + (_random.NextDouble() * 2 - 1) * range;
        }

        /// <summary>
        /// 格式化为固定小数位的字符串
        /// </summary>
        public static string Float(double value, int digits = 4)
        {
            return value.ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 格式化为科学计数法
        /// </summary>
        public static string Sci(double value, int digits = 6)
        {
            string format = "E" + digits;
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
