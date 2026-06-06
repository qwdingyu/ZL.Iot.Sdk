using System;
using System.Collections.Generic;
using System.Text;

namespace ZL.ProtocolGateway.Framing
{
    #region 抽象定义

    /// <summary>
    /// 分帧器类型枚举
    /// </summary>
    public enum SplitterType
    {
        FixedLength,
        Delimiter,
        LengthField,
        ModbusRtu
    }

    /// <summary>
    /// 协议无关的分帧器接口
    /// 把流式字节切成完整的一帧帧数据。
    /// </summary>
    public interface IFrameSplitter
    {
        void Append(byte[] data, int offset, int count);
        IList<ReadOnlyMemory<byte>> ExtractFrames();
        void Reset();
    }

    #endregion

    #region 工厂类

    /// <summary>
    /// 分帧器工厂
    /// </summary>
    public static class FrameSplitterFactory
    {
        public static IFrameSplitter Create(SplitterType type, int param = 0, byte delimiter = (byte)'\n')
        {
            return type switch
            {
                SplitterType.FixedLength => new FixedLengthSplitter(param),
                SplitterType.Delimiter => new DelimiterSplitter(delimiter),
                SplitterType.ModbusRtu => new ModbusRtuSplitter(),
                SplitterType.LengthField => new LengthFieldSplitter(param),
                _ => throw new NotSupportedException($"不支持的分帧器类型: {type}")
            };
        }

        public static IFrameSplitter CreateDelimiterSplitter(string delimiter) => new DelimiterSplitter(delimiter);

        public static IFrameSplitter CreateLengthFieldSplitter(int lengthFieldOffset, int lengthFieldSize = 2, bool littleEndian = false)
            => new LengthFieldSplitter(lengthFieldOffset, lengthFieldSize, littleEndian);
    }

    #endregion

    #region 基础缓冲抽象

    /// <summary>
    /// 包含基础缓冲区管理的抽象基类 (借鉴 ZL.Gear 的高性能实现)
    /// 避免 List of bytes 的频繁扩容和 RemoveRange 造成的内存搬运
    /// </summary>
    public abstract class BaseSplitter : IFrameSplitter
    {
        protected byte[] _buffer;
        protected int _writeIndex;
        protected int _readIndex;
        protected const int MAX_BUFFER_SIZE = 10 * 1024 * 1024; // 10MB 熔断保护

        protected BaseSplitter(int initialCapacity = 4096)
        {
            _buffer = new byte[initialCapacity];
        }

        public void Append(byte[] data, int offset, int count)
        {
            if (count <= 0) return;

            if (_writeIndex + count > MAX_BUFFER_SIZE)
            {
                Reset();
                throw new InvalidOperationException($"分帧器缓冲区溢出 (>{MAX_BUFFER_SIZE} bytes)");
            }

            EnsureCapacity(count);
            Buffer.BlockCopy(data, offset, _buffer, _writeIndex, count);
            _writeIndex += count;
        }

        public abstract IList<ReadOnlyMemory<byte>> ExtractFrames();

        public virtual void Reset()
        {
            _writeIndex = 0;
            _readIndex = 0;
        }

        private void EnsureCapacity(int appendSize)
        {
            int remainingSpace = _buffer.Length - _writeIndex;
            if (remainingSpace >= appendSize) return;

            int availableData = _writeIndex - _readIndex;

            if (_buffer.Length - availableData >= appendSize)
            {
                if (availableData > 0)
                {
                    Buffer.BlockCopy(_buffer, _readIndex, _buffer, 0, availableData);
                }
                _writeIndex = availableData;
                _readIndex = 0;
                return;
            }

            int newSize = _buffer.Length;
            while (newSize < availableData + appendSize)
            {
                newSize *= 2;
                if (newSize > MAX_BUFFER_SIZE) newSize = MAX_BUFFER_SIZE;
            }

            var newBuffer = new byte[newSize];
            if (availableData > 0)
            {
                Buffer.BlockCopy(_buffer, _readIndex, newBuffer, 0, availableData);
            }
            _buffer = newBuffer;
            _writeIndex = availableData;
            _readIndex = 0;
        }

        protected void CompactBuffer()
        {
            int available = _writeIndex - _readIndex;
            if (available == 0)
            {
                _writeIndex = 0;
                _readIndex = 0;
            }
            else if (_readIndex > 0)
            {
                Buffer.BlockCopy(_buffer, _readIndex, _buffer, 0, available);
                _writeIndex = available;
                _readIndex = 0;
            }
        }
    }

    #endregion

    #region 具体实现

    /// <summary>
    /// 定长分包器
    /// </summary>
    public class FixedLengthSplitter : BaseSplitter
    {
        private readonly int _frameSize;

        public FixedLengthSplitter(int frameSize) : base(Math.Max(frameSize * 10, 4096))
        {
            if (frameSize <= 0) throw new ArgumentException("Frame size must be greater than 0", nameof(frameSize));
            _frameSize = frameSize;
        }

        public override IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();
            while (_writeIndex - _readIndex >= _frameSize)
            {
                var frameData = new byte[_frameSize];
                Buffer.BlockCopy(_buffer, _readIndex, frameData, 0, _frameSize);
                frames.Add(new ReadOnlyMemory<byte>(frameData));
                _readIndex += _frameSize;
            }
            CompactBuffer();
            return frames;
        }
    }

    /// <summary>
    /// 分隔符分包器
    /// </summary>
    public class DelimiterSplitter : BaseSplitter
    {
        private readonly byte[] _delimiter;

        public DelimiterSplitter(byte delimiter) : this(new[] { delimiter }) { }

        public DelimiterSplitter(string delimiter) : this(Encoding.ASCII.GetBytes(delimiter))
        {
            if (string.IsNullOrEmpty(delimiter)) throw new ArgumentException("Delimiter cannot be empty");
        }

        public DelimiterSplitter(byte[] delimiterBytes) : base()
        {
            _delimiter = delimiterBytes;
        }

        public override IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();
            while (true)
            {
                int count = _writeIndex - _readIndex;
                if (count < _delimiter.Length) break;

                int index = IndexOf(_buffer, _readIndex, count, _delimiter);
                if (index < 0) break;

                int frameLen = (index - _readIndex) + _delimiter.Length;

                var frameData = new byte[frameLen];
                Buffer.BlockCopy(_buffer, _readIndex, frameData, 0, frameLen);
                frames.Add(new ReadOnlyMemory<byte>(frameData));

                _readIndex += frameLen;
            }
            CompactBuffer();
            return frames;
        }

        private static int IndexOf(byte[] buffer, int offset, int count, byte[] pattern)
        {
            int limit = offset + count - pattern.Length;
            for (int i = offset; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// 长度字段分包器
    /// </summary>
    public class LengthFieldSplitter : BaseSplitter
    {
        private readonly int _lengthFieldOffset;
        private readonly int _lengthFieldSize;
        private readonly bool _littleEndian;

        public LengthFieldSplitter(int lengthFieldOffset, int lengthFieldSize = 2, bool littleEndian = false) : base()
        {
            if (lengthFieldOffset < 0) throw new ArgumentException("Offset must >= 0");
            if (lengthFieldSize != 1 && lengthFieldSize != 2 && lengthFieldSize != 4)
                throw new ArgumentException("Size must be 1, 2 or 4");

            _lengthFieldOffset = lengthFieldOffset;
            _lengthFieldSize = lengthFieldSize;
            _littleEndian = littleEndian;
        }

        public override IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();
            while (true)
            {
                int available = _writeIndex - _readIndex;
                if (available < _lengthFieldOffset + _lengthFieldSize) break;

                int totalLength = ReadLength(_buffer, _readIndex + _lengthFieldOffset);

                if (totalLength <= 0)
                {
                    _readIndex++;
                    continue;
                }

                if (available < totalLength) break;

                var frameData = new byte[totalLength];
                Buffer.BlockCopy(_buffer, _readIndex, frameData, 0, totalLength);
                frames.Add(new ReadOnlyMemory<byte>(frameData));

                _readIndex += totalLength;
            }
            CompactBuffer();
            return frames;
        }

        private int ReadLength(byte[] buffer, int index)
        {
            return _lengthFieldSize switch
            {
                1 => buffer[index],
                2 => _littleEndian
                    ? buffer[index] | (buffer[index + 1] << 8)
                    : (buffer[index] << 8) | buffer[index + 1],
                4 => _littleEndian
                    ? buffer[index] | (buffer[index + 1] << 8) | (buffer[index + 2] << 16) | (buffer[index + 3] << 24)
                    : (buffer[index] << 24) | (buffer[index + 1] << 16) | (buffer[index + 2] << 8) | buffer[index + 3],
                _ => 0
            };
        }
    }

    /// <summary>
    /// Modbus RTU 分帧器 (带 CRC 校验)
    /// </summary>
    public class ModbusRtuSplitter : BaseSplitter
    {
        public ModbusRtuSplitter() : base() { }

        public override IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();
            while (true)
            {
                int available = _writeIndex - _readIndex;
                if (available < 5) break;

                byte funcCode = _buffer[_readIndex + 1];
                int frameLen = 0;

                if ((funcCode & 0x80) != 0)
                {
                    frameLen = 5;
                }
                else if (funcCode == 0x01 || funcCode == 0x02 || funcCode == 0x03 || funcCode == 0x04)
                {
                    if (available < 3) break;
                    int dataBytes = _buffer[_readIndex + 2];
                    frameLen = 3 + dataBytes + 2;
                }
                else if (funcCode == 0x05 || funcCode == 0x06 || funcCode == 0x0F || funcCode == 0x10)
                {
                    frameLen = 8;
                }
                else
                {
                    _readIndex++;
                    continue;
                }

                if (available < frameLen) break;

                ushort calculatedCrc = Crc16Modbus(_buffer, _readIndex, frameLen - 2);
                ushort receivedCrc = (ushort)(_buffer[_readIndex + frameLen - 2] | (_buffer[_readIndex + frameLen - 1] << 8));

                if (calculatedCrc == receivedCrc)
                {
                    var frameData = new byte[frameLen];
                    Buffer.BlockCopy(_buffer, _readIndex, frameData, 0, frameLen);
                    frames.Add(new ReadOnlyMemory<byte>(frameData));
                    _readIndex += frameLen;
                }
                else
                {
                    _readIndex++;
                }
            }
            CompactBuffer();
            return frames;
        }

        public static ushort Crc16Modbus(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return crc;
        }
    }

    #endregion
}
