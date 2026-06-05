using System;

namespace ZL.Framing
{
    public sealed class StickyWindowBuffer : IByteBuffer
    {
        private byte[] _buffer;
        private int _writeIndex;
        private int _readIndex;
        private readonly int _maxCapacity;

        public StickyWindowBuffer(int initialCapacity = 4096, int maxCapacity = 1024 * 1024)
        {
            if (initialCapacity <= 0) initialCapacity = 4096;
            if (maxCapacity <= 0) maxCapacity = 1024 * 1024;
            if (initialCapacity > maxCapacity) initialCapacity = maxCapacity;
            _buffer = new byte[initialCapacity];
            _maxCapacity = maxCapacity;
        }

        public int ReadableBytes => _writeIndex - _readIndex;

        public void Write(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            EnsureCapacity(data.Length);
            Buffer.BlockCopy(data, 0, _buffer, _writeIndex, data.Length);
            _writeIndex += data.Length;
        }

        public byte GetByte(int index)
        {
            CheckBounds(index, 1);
            return _buffer[_readIndex + index];
        }

        public ushort GetUShort(int index, bool littleEndian)
        {
            CheckBounds(index, 2);
            int start = _readIndex + index;
            if (littleEndian)
            {
                return (ushort)(_buffer[start] | (_buffer[start + 1] << 8));
            }
            return (ushort)((_buffer[start] << 8) | _buffer[start + 1]);
        }

        public int GetInt(int index, bool littleEndian)
        {
            CheckBounds(index, 4);
            int start = _readIndex + index;
            if (littleEndian)
            {
                return _buffer[start]
                    | (_buffer[start + 1] << 8)
                    | (_buffer[start + 2] << 16)
                    | (_buffer[start + 3] << 24);
            }
            return (_buffer[start] << 24)
                | (_buffer[start + 1] << 16)
                | (_buffer[start + 2] << 8)
                | _buffer[start + 3];
        }

        public int GetUInt24(int index, bool littleEndian)
        {
            CheckBounds(index, 3);
            int start = _readIndex + index;
            if (littleEndian)
            {
                return _buffer[start] | (_buffer[start + 1] << 8) | (_buffer[start + 2] << 16);
            }
            return (_buffer[start] << 16) | (_buffer[start + 1] << 8) | _buffer[start + 2];
        }

        public byte[] ReadBytes(int length)
        {
            CheckBounds(0, length);
            var data = new byte[length];
            Buffer.BlockCopy(_buffer, _readIndex, data, 0, length);
            _readIndex += length;
            return data;
        }

        public byte[] PeekBytes(int offset, int length)
        {
            CheckBounds(offset, length);
            var data = new byte[length];
            Buffer.BlockCopy(_buffer, _readIndex + offset, data, 0, length);
            return data;
        }

        public void Skip(int length)
        {
            CheckBounds(0, length);
            _readIndex += length;
        }

        public int IndexOf(byte[] pattern, int offset, int count)
        {
            if (pattern == null || pattern.Length == 0) return -1;
            int end = Math.Min(_readIndex + offset + count, _writeIndex);
            int start = _readIndex + offset;

            for (int i = start; i <= end - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (_buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i - _readIndex;
            }
            return -1;
        }

        public void DiscardReadBytes()
        {
            if (_readIndex == 0) return;
            if (ReadableBytes > 0)
            {
                Buffer.BlockCopy(_buffer, _readIndex, _buffer, 0, ReadableBytes);
            }
            _writeIndex -= _readIndex;
            _readIndex = 0;
        }

        public void Clear()
        {
            _readIndex = 0;
            _writeIndex = 0;
        }

        private void EnsureCapacity(int appendSize)
        {
            if (_buffer.Length - _writeIndex >= appendSize) return;

            if (_buffer.Length - ReadableBytes >= appendSize)
            {
                DiscardReadBytes();
                return;
            }

            Resize(appendSize);
        }

        private void Resize(int appendSize)
        {
            int newSize = _buffer.Length;
            while (newSize < ReadableBytes + appendSize)
            {
                newSize *= 2;
                if (newSize < 0) newSize = int.MaxValue;
            }

            if (newSize > _maxCapacity)
            {
                if (ReadableBytes + appendSize > _maxCapacity)
                {
                    throw new InvalidOperationException($"Buffer max capacity ({_maxCapacity}) exceeded.");
                }
                newSize = _maxCapacity;
            }

            var newBuf = new byte[newSize];
            if (ReadableBytes > 0)
            {
                Buffer.BlockCopy(_buffer, _readIndex, newBuf, 0, ReadableBytes);
            }

            _buffer = newBuf;
            _writeIndex = ReadableBytes;
            _readIndex = 0;
        }

        private void CheckBounds(int index, int length)
        {
            if (_readIndex + index + length > _writeIndex)
            {
                throw new IndexOutOfRangeException("Not enough bytes in buffer.");
            }
        }
    }
}
