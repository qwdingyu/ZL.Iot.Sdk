using System;

namespace ZL.Framing
{
    public sealed class FixedLengthFrameDecoder : IFrameDecoder
    {
        private readonly int _length;
        private readonly byte[] _sync;
        private readonly ResyncMode _resync;
        private readonly int _maxSkip;

        public FixedLengthFrameDecoder(int length, byte[] syncBytes, ResyncMode resync, int maxSkip)
        {
            _length = length;
            _sync = syncBytes ?? Array.Empty<byte>();
            _resync = resync;
            _maxSkip = Math.Max(1, maxSkip);
        }

        public DecodeResult TryDecode(IByteBuffer buffer, out byte[] frame)
        {
            frame = null;
            if (_length <= 0) return DecodeResult.NeedMoreData;

            if (_sync.Length > 0)
            {
                int syncIndex = buffer.IndexOf(_sync, 0, Math.Min(buffer.ReadableBytes, _maxSkip));
                if (syncIndex < 0)
                {
                    if (_resync == ResyncMode.DropOneByte && buffer.ReadableBytes > 0)
                    {
                        buffer.Skip(1);
                        return DecodeResult.Recovery;
                    }
                    if (buffer.ReadableBytes > _sync.Length)
                    {
                        buffer.Skip(buffer.ReadableBytes - _sync.Length + 1);
                        return DecodeResult.Recovery;
                    }
                    return DecodeResult.NeedMoreData;
                }
                if (syncIndex > 0)
                {
                    buffer.Skip(syncIndex);
                    return DecodeResult.Recovery;
                }
            }

            if (buffer.ReadableBytes < _length) return DecodeResult.NeedMoreData;
            frame = buffer.ReadBytes(_length);
            return DecodeResult.FrameAvailable;
        }
    }
}
