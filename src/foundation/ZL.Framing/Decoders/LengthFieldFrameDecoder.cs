using System;

namespace ZL.Framing
{
    public sealed class LengthFieldFrameDecoder : IFrameDecoder
    {
        private readonly int _maxFrameLength;
        private readonly int _minFrameLength;
        private readonly int _lengthFieldOffset;
        private readonly int _lengthFieldLength;
        private readonly int _lengthAdjustment;
        private readonly bool _littleEndian;
        private readonly bool _includesHeader;
        private readonly bool _includesChecksum;
        private readonly string _checksumMethod;
        private readonly int _checksumOffset;
        private readonly int _checksumRangeStart;
        private readonly int _checksumRangeLength;
        private readonly byte[] _syncBytes;
        private readonly ResyncMode _resync;
        private readonly int _maxSkip;

        public LengthFieldFrameDecoder(ByteFramingOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Normalize();

            _maxFrameLength = options.MaxFrameLength;
            _minFrameLength = options.MinFrameLength;
            _lengthFieldOffset = options.LengthFieldOffset;
            _lengthFieldLength = options.LengthFieldSize;
            _lengthAdjustment = options.LengthFieldAdjustment;
            _littleEndian = options.LengthFieldEndian.Equals("Little", StringComparison.OrdinalIgnoreCase);
            _includesHeader = options.LengthFieldIncludesHeader;
            _includesChecksum = options.LengthFieldIncludesChecksum;
            _checksumMethod = options.Checksum ?? string.Empty;
            _checksumOffset = options.ChecksumOffset;
            _checksumRangeStart = options.ChecksumRangeStart;
            _checksumRangeLength = options.ChecksumRangeLength;
            _syncBytes = options.GetSyncBytes();
            _resync = options.GetResyncMode();
            _maxSkip = Math.Max(1, options.MaxResyncSkip);
        }

        public DecodeResult TryDecode(IByteBuffer buffer, out byte[] frame)
        {
            frame = null;

            if (_syncBytes.Length > 0)
            {
                int syncIndex = buffer.IndexOf(_syncBytes, 0, Math.Min(buffer.ReadableBytes, _maxSkip));
                if (syncIndex < 0)
                {
                    if (buffer.ReadableBytes > _maxFrameLength)
                    {
                        int bytesToSkip = buffer.ReadableBytes - _syncBytes.Length + 1;
                        if (bytesToSkip > 0)
                        {
                            buffer.Skip(bytesToSkip);
                            return DecodeResult.Recovery;
                        }
                    }
                    return DecodeResult.NeedMoreData;
                }

                if (syncIndex > 0)
                {
                    buffer.Skip(syncIndex);
                    return DecodeResult.Recovery;
                }
            }

            int minHeader = _lengthFieldOffset + _lengthFieldLength;
            if (buffer.ReadableBytes < minHeader) return DecodeResult.NeedMoreData;

            long lengthValue = ReadLength(buffer);
            if (lengthValue <= 0)
            {
                return Resync(buffer);
            }

            long totalLength = lengthValue;
            if (!_includesHeader)
            {
                totalLength += _lengthFieldOffset + _lengthFieldLength;
            }

            if (!_includesChecksum && !string.IsNullOrWhiteSpace(_checksumMethod))
            {
                totalLength += ChecksumUtil.GetLength(_checksumMethod);
            }

            totalLength += _lengthAdjustment;

            if (totalLength < _minFrameLength || totalLength > _maxFrameLength)
            {
                return Resync(buffer);
            }

            if (buffer.ReadableBytes < totalLength) return DecodeResult.NeedMoreData;

            if (!string.IsNullOrWhiteSpace(_checksumMethod))
            {
                if (!ValidateChecksum(buffer, (int)totalLength))
                {
                    return Resync(buffer);
                }
            }

            frame = buffer.ReadBytes((int)totalLength);
            return DecodeResult.FrameAvailable;
        }

        private DecodeResult Resync(IByteBuffer buffer)
        {
            if (_resync == ResyncMode.DropOneByte)
            {
                buffer.Skip(1);
                return DecodeResult.Recovery;
            }

            if (_syncBytes.Length > 0)
            {
                int syncIndex = buffer.IndexOf(_syncBytes, 1, Math.Min(buffer.ReadableBytes - 1, _maxSkip));
                if (syncIndex > 0)
                {
                    buffer.Skip(syncIndex);
                    return DecodeResult.Recovery;
                }
            }

            buffer.Skip(1);
            return DecodeResult.Recovery;
        }

        private long ReadLength(IByteBuffer buffer)
        {
            switch (_lengthFieldLength)
            {
                case 1:
                    return buffer.GetByte(_lengthFieldOffset);
                case 2:
                    return buffer.GetUShort(_lengthFieldOffset, _littleEndian);
                case 3:
                    return buffer.GetUInt24(_lengthFieldOffset, _littleEndian);
                case 4:
                    return buffer.GetInt(_lengthFieldOffset, _littleEndian) & 0xFFFFFFFFL;
                default:
                    return -1;
            }
        }

        private bool ValidateChecksum(IByteBuffer buffer, int frameLength)
        {
            int checksumLen = ChecksumUtil.GetLength(_checksumMethod);
            int checksumOffset = _checksumOffset >= 0 ? _checksumOffset : frameLength - checksumLen;
            if (checksumOffset < 0 || checksumOffset + checksumLen > frameLength) return false;

            byte[] checksum = new byte[checksumLen];
            for (int i = 0; i < checksumLen; i++)
            {
                checksum[i] = buffer.GetByte(checksumOffset + i);
            }

            byte[] data = BuildChecksumRange(buffer, frameLength, checksumOffset, checksumLen);
            var expected = ChecksumUtil.Calculate(data, _checksumMethod);
            if (expected.Length != checksum.Length) return false;
            for (int i = 0; i < checksum.Length; i++)
            {
                if (checksum[i] != expected[i]) return false;
            }
            return true;
        }

        private byte[] BuildChecksumRange(IByteBuffer buffer, int frameLength, int checksumOffset, int checksumLen)
        {
            if (_checksumRangeStart >= 0 && _checksumRangeLength > 0)
            {
                if (_checksumRangeStart + _checksumRangeLength > frameLength) return Array.Empty<byte>();
                return buffer.PeekBytes(_checksumRangeStart, _checksumRangeLength);
            }

            int dataLen = frameLength - checksumLen;
            if (checksumOffset + checksumLen == frameLength)
            {
                return buffer.PeekBytes(0, dataLen);
            }

            int part1Len = checksumOffset;
            int part2Len = frameLength - (checksumOffset + checksumLen);
            var data = new byte[part1Len + part2Len];
            if (part1Len > 0)
            {
                var part1 = buffer.PeekBytes(0, part1Len);
                Buffer.BlockCopy(part1, 0, data, 0, part1Len);
            }
            if (part2Len > 0)
            {
                var part2 = buffer.PeekBytes(checksumOffset + checksumLen, part2Len);
                Buffer.BlockCopy(part2, 0, data, part1Len, part2Len);
            }
            return data;
        }
    }
}
