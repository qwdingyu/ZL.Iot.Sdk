using System;

namespace ZL.DataProcessing
{
    public static class BinaryLogic
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
                case "ABCD":
                default:
                    break;
            }

            return result;
        }

        public static int BcdToInt(byte bcd)
        {
            return (bcd >> 4) * 10 + (bcd & 0x0F);
        }

        public static byte IntToBcd(int value)
        {
            if (value > 99 || value < 0) return 0;
            return (byte)(((value / 10) << 4) | (value % 10));
        }

        public static bool GetBit(byte b, int bitIndex)
        {
            return (b & (1 << bitIndex)) != 0;
        }

        public static bool GetBit(int value, int bitIndex)
        {
            return (value & (1 << bitIndex)) != 0;
        }

        public static byte SetBit(byte b, int bitIndex, bool on)
        {
            if (on) return (byte)(b | (1 << bitIndex));
            return (byte)(b & ~(1 << bitIndex));
        }
    }
}
