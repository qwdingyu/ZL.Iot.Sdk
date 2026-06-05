using System;

namespace ZL.Framing
{
    public sealed class ByteFramingOptions
    {
        public string Strategy { get; set; } = "Timeout";
        public int FixedLength { get; set; }
        public int LengthFieldOffset { get; set; }
        public int LengthFieldSize { get; set; } = 2;
        public string LengthFieldEndian { get; set; } = "Big";
        public bool LengthFieldIncludesHeader { get; set; }
        public bool LengthFieldIncludesChecksum { get; set; }
        public int LengthFieldAdjustment { get; set; }
        public int MinFrameLength { get; set; } = 1;
        public int MaxFrameLength { get; set; } = 4096;
        public string Checksum { get; set; } = string.Empty;
        public int ChecksumOffset { get; set; } = -1;
        public int ChecksumRangeStart { get; set; } = -1;
        public int ChecksumRangeLength { get; set; } = -1;
        public string SyncBytes { get; set; } = string.Empty;
        public string ResyncPolicy { get; set; } = "ScanForSync";
        public string TimeoutAction { get; set; } = string.Empty;
        public int BufferInitialCapacity { get; set; } = 4096;
        public int BufferMaxCapacity { get; set; } = 1024 * 1024;
        public int MaxResyncSkip { get; set; } = 2048;

        public ByteFramingStrategy GetStrategy()
        {
            if (string.IsNullOrWhiteSpace(Strategy)) return ByteFramingStrategy.Timeout;
            if (Strategy.Equals("FixedLength", StringComparison.OrdinalIgnoreCase)) return ByteFramingStrategy.FixedLength;
            if (Strategy.Equals("LengthField", StringComparison.OrdinalIgnoreCase)) return ByteFramingStrategy.LengthField;
            if (Strategy.Equals("LengthFieldWithChecksum", StringComparison.OrdinalIgnoreCase)
                || Strategy.Equals("LengthFieldChecksum", StringComparison.OrdinalIgnoreCase))
            {
                return ByteFramingStrategy.LengthFieldWithChecksum;
            }
            if (Strategy.Equals("Timeout", StringComparison.OrdinalIgnoreCase)) return ByteFramingStrategy.Timeout;
            return ByteFramingStrategy.Timeout;
        }

        public ResyncMode GetResyncMode()
        {
            if (string.IsNullOrWhiteSpace(ResyncPolicy)) return ResyncMode.ScanForSync;
            if (ResyncPolicy.Equals("DropOneByte", StringComparison.OrdinalIgnoreCase)) return ResyncMode.DropOneByte;
            return ResyncMode.ScanForSync;
        }

        public TimeoutMode GetTimeoutMode()
        {
            if (string.IsNullOrWhiteSpace(TimeoutAction)) return TimeoutMode.Hold;
            if (TimeoutAction.Equals("Emit", StringComparison.OrdinalIgnoreCase)) return TimeoutMode.Emit;
            if (TimeoutAction.Equals("Clear", StringComparison.OrdinalIgnoreCase)) return TimeoutMode.Clear;
            return TimeoutMode.Hold;
        }

        public byte[] GetSyncBytes()
        {
            if (string.IsNullOrWhiteSpace(SyncBytes)) return Array.Empty<byte>();
            return FramingHex.ParseHexBytes(SyncBytes);
        }

        public void Normalize()
        {
            if (FixedLength < 0) FixedLength = 0;
            if (LengthFieldOffset < 0) LengthFieldOffset = 0;
            if (LengthFieldSize != 1 && LengthFieldSize != 2 && LengthFieldSize != 3 && LengthFieldSize != 4)
            {
                LengthFieldSize = 2;
            }
            if (MinFrameLength <= 0) MinFrameLength = 1;
            if (MaxFrameLength <= 0) MaxFrameLength = 4096;
            if (MaxFrameLength < MinFrameLength) MaxFrameLength = MinFrameLength;
            if (string.IsNullOrWhiteSpace(LengthFieldEndian)) LengthFieldEndian = "Big";
            if (BufferInitialCapacity <= 0) BufferInitialCapacity = 4096;
            if (BufferMaxCapacity <= 0) BufferMaxCapacity = 1024 * 1024;
            if (BufferInitialCapacity > BufferMaxCapacity) BufferInitialCapacity = BufferMaxCapacity;
            if (MaxResyncSkip <= 0) MaxResyncSkip = 2048;
        }
    }

    public enum ByteFramingStrategy
    {
        Timeout,
        FixedLength,
        LengthField,
        LengthFieldWithChecksum
    }

    public enum ResyncMode
    {
        DropOneByte,
        ScanForSync
    }

    public enum TimeoutMode
    {
        Hold,
        Emit,
        Clear
    }
}
