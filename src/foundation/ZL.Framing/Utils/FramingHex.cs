using System;
using System.Globalization;

namespace ZL.Framing
{
    public static class FramingHex
    {
        public static byte[] ParseHexBytes(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();
            var cleaned = text.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Replace(";", string.Empty, StringComparison.Ordinal);
            if (cleaned.Length % 2 == 1)
            {
                cleaned = "0" + cleaned;
            }
            var bytes = new byte[cleaned.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(cleaned.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return bytes;
        }
    }
}
