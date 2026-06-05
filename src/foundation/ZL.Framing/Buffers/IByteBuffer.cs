using System;

namespace ZL.Framing
{
    public interface IByteBuffer
    {
        int ReadableBytes { get; }
        byte GetByte(int index);
        ushort GetUShort(int index, bool littleEndian);
        int GetInt(int index, bool littleEndian);
        int GetUInt24(int index, bool littleEndian);
        byte[] PeekBytes(int offset, int length);
        byte[] ReadBytes(int length);
        void Skip(int length);
        void DiscardReadBytes();
        int IndexOf(byte[] pattern, int offset, int count);
    }
}
