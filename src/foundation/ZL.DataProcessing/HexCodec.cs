using System;
using System.Globalization;
using System.Text;

namespace ZL.DataProcessing
{
    public static class HexCodec
    {
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (IsHexChar(c)) sb.Append(char.ToUpperInvariant(c));
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
            return BitConverter.ToString(data).Replace("-", separator, StringComparison.Ordinal);
        }

        public static bool TryStripChecksum(string hexInput, int checksumByteLen, out string strippedHex, out byte[] checksumBytes)
        {
            strippedHex = hexInput ?? string.Empty;
            checksumBytes = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(hexInput) || checksumByteLen <= 0) return false;

            byte[] totalBytes;
            try
            {
                totalBytes = ToBytes(hexInput);
            }
            catch
            {
                return false;
            }

            if (totalBytes.Length <= checksumByteLen) return false;

            int dataLen = totalBytes.Length - checksumByteLen;
            byte[] data = new byte[dataLen];
            checksumBytes = new byte[checksumByteLen];

            Buffer.BlockCopy(totalBytes, 0, data, 0, dataLen);
            Buffer.BlockCopy(totalBytes, dataLen, checksumBytes, 0, checksumByteLen);

            strippedHex = ToString(data);
            return true;
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
