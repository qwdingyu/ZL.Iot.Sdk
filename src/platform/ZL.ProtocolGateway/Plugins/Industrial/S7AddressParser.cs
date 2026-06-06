using System;
using System.Text;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// S7 地址解析器 — 纯函数，无副作用，可独立测试。
    /// 从 S7OutputPlugin 提取，消除 1013 行单体类。
    /// </summary>
    public static class S7AddressParser
    {
        /// <summary>
        /// S7 地址解析结果
        /// </summary>
        public struct ParsedAddress
        {
            /// <summary>DB 号（DB 区域有效，其他区域为 0）</summary>
            public int DbNumber { get; }
            /// <summary>S7 区域码: 0x81=Input, 0x82=Output, 0x83=Merk, 0x84=DB</summary>
            public byte Area { get; }
            /// <summary>字节偏移量</summary>
            public int ByteOffset { get; }
            /// <summary>位偏移量（0-7，仅 isBit=true 时有效）</summary>
            public int BitOffset { get; }
            /// <summary>是否为位地址</summary>
            public bool IsBit { get; }

            public ParsedAddress(int dbNumber, byte area, int byteOffset, int bitOffset, bool isBit)
            {
                DbNumber = dbNumber;
                Area = area;
                ByteOffset = byteOffset;
                BitOffset = bitOffset;
                IsBit = isBit;
            }
        }

        /// <summary>
        /// S7 区域码常量
        /// </summary>
        public static class AreaCodes
        {
            /// <summary>输入区域 (I)</summary>
            public const byte Input = 0x81;
            /// <summary>输出区域 (Q)</summary>
            public const byte Output = 0x82;
            /// <summary>标志位区域 (M)</summary>
            public const byte Merk = 0x83;
            /// <summary>数据块区域 (DB)</summary>
            public const byte DataBlock = 0x84;
        }

        /// <summary>
        /// 默认地址: DB1.DBW0
        /// </summary>
        public static readonly ParsedAddress Default = new(1, AreaCodes.DataBlock, 0, 0, false);

        /// <summary>
        /// 解析 S7 地址字符串，提取 DB 号、区域码、字节偏移和位偏移
        /// 支持格式: DB1.DBW10, DB1.DBB10, DB1.DBX0.0, M0, M0.0, I0, I0.0, Q0, Q0.0
        /// </summary>
        public static ParsedAddress Parse(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return Default;

            address = address.ToUpperInvariant().Trim();

            // DB 区域: DB1.DBW10, DB1.DBB10, DB1.DBX0.0
            if (address.StartsWith("DB"))
            {
                return ParseDataBlock(address);
            }

            // M 区域: M0, M0.0
            if (address.StartsWith("M"))
            {
                return ParseSimpleArea(address, "M", 1, AreaCodes.Merk);
            }

            // I 区域: I0, I0.0
            if (address.StartsWith("I"))
            {
                return ParseSimpleArea(address, "I", 1, AreaCodes.Input);
            }

            // Q 区域: Q0, Q0.0
            if (address.StartsWith("Q"))
            {
                return ParseSimpleArea(address, "Q", 1, AreaCodes.Output);
            }

            // Default: DB1.DBW0
            return Default;
        }

        private static ParsedAddress ParseDataBlock(string address)
        {
            var dotIdx = address.IndexOf('.');
            if (dotIdx <= 2)
                return Default;

            int dbNum;
            if (!int.TryParse(address.Substring(2, dotIdx - 2), out dbNum))
            {
                GatewayLog.Warn("S7AddressParser", $"Invalid DB number in address '{address}', using default");
                return Default;
            }

            var rest = address.Substring(dotIdx + 1);

            if (rest.StartsWith("DBX"))
            {
                return ParseDbx(rest, dbNum);
            }

            if (rest.StartsWith("DBW") || rest.StartsWith("DBB") || rest.StartsWith("DBD"))
            {
                int byteOff;
                if (!int.TryParse(rest.Substring(3), out byteOff))
                {
                    GatewayLog.Warn("S7AddressParser", $"Invalid byte offset in address '{address}', using default");
                    return new ParsedAddress(dbNum, AreaCodes.DataBlock, 0, 0, false);
                }
                return new ParsedAddress(dbNum, AreaCodes.DataBlock, byteOff, 0, false);
            }

            // Fallback: DB1 (treat as DB1.DBW0)
            return Default;
        }

        private static ParsedAddress ParseDbx(string rest, int dbNum)
        {
            var parts = rest.Substring(3).Split('.');
            int byteOff;
            if (!int.TryParse(parts[0], out byteOff))
            {
                GatewayLog.Warn("S7AddressParser", $"Invalid byte offset in address DB{dbNum}.{rest}, using default");
                return new ParsedAddress(dbNum, AreaCodes.DataBlock, 0, 0, false);
            }

            int bitOff = 0;
            if (parts.Length > 1)
            {
                if (!int.TryParse(parts[1], out bitOff))
                {
                    GatewayLog.Warn("S7AddressParser", $"Invalid bit offset in address DB{dbNum}.{rest}, using default");
                }
            }

            return new ParsedAddress(dbNum, AreaCodes.DataBlock, byteOff, bitOff, true);
        }

        private static ParsedAddress ParseSimpleArea(string address, string prefix, int prefixLen, byte areaCode)
        {
            var parts = address.Substring(prefixLen).Split('.');
            int byteOff;
            if (!int.TryParse(parts[0], out byteOff))
            {
                GatewayLog.Warn("S7AddressParser", $"Invalid byte offset in address '{address}', using default");
                return new ParsedAddress(0, areaCode, 0, 0, false);
            }

            int bitOff = 0;
            bool isBit = parts.Length > 1;

            if (isBit)
            {
                if (!int.TryParse(parts[1], out bitOff))
                {
                    GatewayLog.Warn("S7AddressParser", $"Invalid bit offset in address '{address}', using default");
                }
            }

            return new ParsedAddress(0, areaCode, byteOff, bitOff, isBit);
        }

        /// <summary>
        /// 将值编码为 S7 字节数组
        /// </summary>
        public static byte[] EncodeValue(object value, string dataType)
        {
            if (value == null) return Array.Empty<byte>();

            return dataType.ToUpperInvariant() switch
            {
                "BOOL" => new[] { ((bool)(value is bool b ? b : Convert.ToBoolean(value)) ? (byte)1 : (byte)0) },
                "BYTE" or "CHAR" => new[] { (byte)(value is byte bv ? bv : Convert.ToByte(value)) },
                "WORD" or "INT16" or "INT" => BitConverter.GetBytes((short)(value is short sv ? sv : Convert.ToInt16(value))),
                "UINT16" or "UINT" => BitConverter.GetBytes((ushort)(value is ushort uv ? uv : Convert.ToUInt16(value))),
                "DWORD" or "INT32" or "LONG" => BitConverter.GetBytes((int)(value is int iv ? iv : Convert.ToInt32(value))),
                "UINT32" => BitConverter.GetBytes((uint)(value is uint uiv ? uiv : Convert.ToUInt32(value))),
                "REAL" or "FLOAT" => BitConverter.GetBytes((float)(value is float fv ? fv : Convert.ToSingle(value))),
                "LREAL" or "DOUBLE" => BitConverter.GetBytes((double)(value is double dv ? dv : Convert.ToDouble(value))),
                "STRING" or _ => Encoding.UTF8.GetBytes(value.ToString() ?? "")
            };
        }
    }
}
