using System;
using System.Collections.Generic;
using System.Linq;
using ZL.ProtocolGateway.Framing;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class FrameSplitterTests
    {
        private static byte[][] ToArrays(IList<ReadOnlyMemory<byte>> frames)
        {
            return frames.Select(f => f.ToArray()).ToArray();
        }

        #region FixedLengthSplitter

        [Fact]
        public void FixedLength_NormalSingleFrame_ReturnsOneFrame()
        {
            var splitter = new FixedLengthSplitter(4);
            splitter.Append(new byte[] { 1, 2, 3, 4 }, 0, 4);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Single(frames);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, frames[0]);
        }

        [Fact]
        public void FixedLength_MultipleFramesInOneAppend_SplitsAll()
        {
            // 粘包：两个完整帧一次到达
            var splitter = new FixedLengthSplitter(3);
            splitter.Append(new byte[] { 1, 2, 3, 4, 5, 6 }, 0, 6);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Equal(2, frames.Length);
            Assert.Equal(new byte[] { 1, 2, 3 }, frames[0]);
            Assert.Equal(new byte[] { 4, 5, 6 }, frames[1]);
        }

        [Fact]
        public void FixedLength_PartialFrame_WaitsForMoreData()
        {
            // 半包：先收到 2 字节，再收到 2 字节
            var splitter = new FixedLengthSplitter(4);
            splitter.Append(new byte[] { 1, 2 }, 0, 2);

            var frames1 = ToArrays(splitter.ExtractFrames());
            Assert.Empty(frames1);

            splitter.Append(new byte[] { 3, 4 }, 0, 2);

            var frames2 = ToArrays(splitter.ExtractFrames());
            Assert.Single(frames2);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, frames2[0]);
        }

        [Fact]
        public void FixedLength_Reset_ClearsBuffer()
        {
            var splitter = new FixedLengthSplitter(4);
            splitter.Append(new byte[] { 1, 2 }, 0, 2);
            splitter.Reset();

            var frames = ToArrays(splitter.ExtractFrames());
            Assert.Empty(frames);
        }

        [Fact]
        public void FixedLength_ZeroFrameSize_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FixedLengthSplitter(0));
        }

        #endregion

        #region DelimiterSplitter

        [Fact]
        public void Delimiter_SingleByteDelimiter_ExtractsFrames()
        {
            var splitter = new DelimiterSplitter((byte)'\n');
            splitter.Append(System.Text.Encoding.ASCII.GetBytes("hello\nworld\n"), 0, 12);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Equal(2, frames.Length);
            Assert.Equal("hello\n", System.Text.Encoding.ASCII.GetString(frames[0]));
            Assert.Equal("world\n", System.Text.Encoding.ASCII.GetString(frames[1]));
        }

        [Fact]
        public void Delimiter_MultiByteDelimiter_Crlf_ExtractsFrames()
        {
            var splitter = new DelimiterSplitter("\r\n");
            splitter.Append(System.Text.Encoding.ASCII.GetBytes("line1\r\nline2\r\n"), 0, 14);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Equal(2, frames.Length);
            Assert.Equal("line1\r\n", System.Text.Encoding.ASCII.GetString(frames[0]));
            Assert.Equal("line2\r\n", System.Text.Encoding.ASCII.GetString(frames[1]));
        }

        [Fact]
        public void Delimiter_MultipleFramesInOneAppend_SplitsAll()
        {
            // 粘包
            var splitter = new DelimiterSplitter((byte)'\n');
            splitter.Append(System.Text.Encoding.ASCII.GetBytes("a\nb\nc\n"), 0, 6);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Equal(3, frames.Length);
        }

        [Fact]
        public void Delimiter_PartialFrame_WaitsForMoreData()
        {
            // 半包
            var splitter = new DelimiterSplitter((byte)'\n');
            splitter.Append(System.Text.Encoding.ASCII.GetBytes("hel"), 0, 3);

            var frames1 = ToArrays(splitter.ExtractFrames());
            Assert.Empty(frames1);

            splitter.Append(System.Text.Encoding.ASCII.GetBytes("lo\n"), 0, 3);

            var frames2 = ToArrays(splitter.ExtractFrames());
            Assert.Single(frames2);
            Assert.Equal("hello\n", System.Text.Encoding.ASCII.GetString(frames2[0]));
        }

        [Fact]
        public void Delimiter_EmptyString_Throws()
        {
            Assert.Throws<ArgumentException>(() => new DelimiterSplitter(""));
        }

        #endregion

        #region LengthFieldSplitter

        [Fact]
        public void LengthField_BigEndian_TwoByteOffset0_ExtractsFrames()
        {
            // [len_hi, len_lo, data...] — 长度字段表示整个帧长度
            var splitter = new LengthFieldSplitter(lengthFieldOffset: 0, lengthFieldSize: 2);
            // Frame: length=5, data="Hello" → [0, 5, 'H','e','l','l','o']
            splitter.Append(new byte[] { 0, 5, 72, 101, 108, 108, 111 }, 0, 7);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Single(frames);
            Assert.Equal(new byte[] { 0, 5 }, frames[0].Take(2).ToArray());
            Assert.Equal("Hello", System.Text.Encoding.ASCII.GetString(frames[0], 2, 5));
        }

        [Fact]
        public void LengthField_LittleEndian_ExtractsFrames()
        {
            var splitter = new LengthFieldSplitter(lengthFieldOffset: 0, lengthFieldSize: 2, littleEndian: true);
            // LE: [5, 0, 'A','B','C','D','E']
            splitter.Append(new byte[] { 5, 0, 65, 66, 67, 68, 69 }, 0, 7);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Single(frames);
            Assert.Equal(5, frames[0].Length);
        }

        [Fact]
        public void LengthField_MultipleFrames_ExtractsAll()
        {
            var splitter = new LengthFieldSplitter(lengthFieldOffset: 0, lengthFieldSize: 2);
            // Two frames: [0,3,A,B,C] [0,2,X,Y]
            splitter.Append(new byte[] { 0, 3, 65, 66, 67, 0, 2, 88, 89 }, 0, 9);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Equal(2, frames.Length);
            Assert.Equal(3, frames[0].Length);
            Assert.Equal(2, frames[1].Length);
        }

        [Fact]
        public void LengthField_PartialFrame_WaitsForMoreData()
        {
            // 半包
            var splitter = new LengthFieldSplitter(lengthFieldOffset: 0, lengthFieldSize: 2);
            splitter.Append(new byte[] { 0, 5, 72, 101 }, 0, 4); // 只收到 4/7 字节

            var frames1 = ToArrays(splitter.ExtractFrames());
            Assert.Empty(frames1);

            splitter.Append(new byte[] { 108, 108, 111 }, 0, 3);

            var frames2 = ToArrays(splitter.ExtractFrames());
            Assert.Single(frames2);
        }

        [Fact]
        public void LengthField_OneByteSize_ExtractsFrames()
        {
            var splitter = new LengthFieldSplitter(lengthFieldOffset: 0, lengthFieldSize: 1);
            // [3, 'A','B','C']
            splitter.Append(new byte[] { 3, 65, 66, 67 }, 0, 4);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Single(frames);
            Assert.Equal(3, frames[0].Length);
        }

        #endregion

        #region ModbusRtuSplitter

        [Fact]
        public void ModbusRtu_Crc16_KnownVector_MatchesStandard()
        {
            // Modbus CRC-16 standard test vector: "123456789" → 0x4B37
            var data = System.Text.Encoding.ASCII.GetBytes("123456789");
            var crc = ModbusRtuSplitter.Crc16Modbus(data, 0, data.Length);

            Assert.Equal((ushort)0x4B37, crc);
        }

        [Fact]
        public void ModbusRtu_ValidReadResponse_ExtractsFrame()
        {
            // FC03 (Read Holding Registers) response: [unitId=1, FC=03, byteCount=2, data=00,01, CRC_LO, CRC_HI]
            var frameWithoutCrc = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x01 };
            var crc = ModbusRtuSplitter.Crc16Modbus(frameWithoutCrc, 0, frameWithoutCrc.Length);
            byte[] frame = new byte[7];
            System.Buffer.BlockCopy(frameWithoutCrc, 0, frame, 0, frameWithoutCrc.Length);
            frame[5] = (byte)(crc & 0xFF);
            frame[6] = (byte)(crc >> 8);

            var splitter = new ModbusRtuSplitter();
            splitter.Append(frame, 0, frame.Length);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Single(frames);
            Assert.Equal(frame, frames[0]);
        }

        [Fact]
        public void ModbusRtu_CrcMismatch_SkipsByte()
        {
            // Valid FC03 response but with wrong CRC
            byte[] badFrame = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x01, 0xFF, 0xFF };

            var splitter = new ModbusRtuSplitter();
            splitter.Append(badFrame, 0, badFrame.Length);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Empty(frames); // CRC mismatch → skips bytes, no valid frame
        }

        [Fact]
        public void ModbusRtu_ExceptionResponse_ExtractsFrame()
        {
            // Exception response: FC = 0x83 (0x03 | 0x80), [unitId, 0x83, errorCode, CRC_LO, CRC_HI] = 5 bytes
            var frameWithoutCrc = new byte[] { 0x01, 0x83, 0x01 };
            var crc = ModbusRtuSplitter.Crc16Modbus(frameWithoutCrc, 0, frameWithoutCrc.Length);
            byte[] frame = new byte[5];
            System.Buffer.BlockCopy(frameWithoutCrc, 0, frame, 0, frameWithoutCrc.Length);
            frame[3] = (byte)(crc & 0xFF);
            frame[4] = (byte)(crc >> 8);

            var splitter = new ModbusRtuSplitter();
            splitter.Append(frame, 0, frame.Length);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Single(frames);
            Assert.Equal(5, frames[0].Length);
        }

        [Fact]
        public void ModbusRtu_MultipleValidFrames_ExtractsAll()
        {
            // Build two valid FC03 responses
            byte[] BuildFc03Response(byte unitId, byte dataByte)
            {
                var body = new byte[] { unitId, 0x03, 0x01, dataByte };
                var crc = ModbusRtuSplitter.Crc16Modbus(body, 0, body.Length);
                var frame = new byte[6];
                System.Buffer.BlockCopy(body, 0, frame, 0, body.Length);
                frame[4] = (byte)(crc & 0xFF);
                frame[5] = (byte)(crc >> 8);
                return frame;
            }

            var frame1 = BuildFc03Response(1, 0xAA);
            var frame2 = BuildFc03Response(2, 0xBB);
            byte[] combined = new byte[frame1.Length + frame2.Length];
            System.Buffer.BlockCopy(frame1, 0, combined, 0, frame1.Length);
            System.Buffer.BlockCopy(frame2, 0, combined, frame1.Length, frame2.Length);

            // 粘包：两个帧一次到达
            var splitter = new ModbusRtuSplitter();
            splitter.Append(combined, 0, combined.Length);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Equal(2, frames.Length);
            Assert.Equal(frame1, frames[0]);
            Assert.Equal(frame2, frames[1]);
        }

        [Fact]
        public void ModbusRtu_PartialFrame_WaitsForMoreData()
        {
            // 半包：先收到 3/7 字节
            var frameWithoutCrc = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x01 };
            var crc = ModbusRtuSplitter.Crc16Modbus(frameWithoutCrc, 0, frameWithoutCrc.Length);
            byte[] frame = new byte[7];
            System.Buffer.BlockCopy(frameWithoutCrc, 0, frame, 0, frameWithoutCrc.Length);
            frame[5] = (byte)(crc & 0xFF);
            frame[6] = (byte)(crc >> 8);

            var splitter = new ModbusRtuSplitter();
            splitter.Append(frame, 0, 3);

            var frames1 = ToArrays(splitter.ExtractFrames());
            Assert.Empty(frames1);

            splitter.Append(frame, 3, 4);

            var frames2 = ToArrays(splitter.ExtractFrames());
            Assert.Single(frames2);
        }

        #endregion

        #region BaseSplitter

        [Fact]
        public void Base_AppendWithOffset_OnlyCopiesSpecifiedRange()
        {
            var splitter = new FixedLengthSplitter(2);
            // data = [0, 1, 2, 3, 4], offset=1, count=2 → copies [1, 2]
            splitter.Append(new byte[] { 0, 1, 2, 3, 4 }, 1, 2);

            var frames = ToArrays(splitter.ExtractFrames());

            Assert.Single(frames);
            Assert.Equal(new byte[] { 1, 2 }, frames[0]);
        }

        [Fact]
        public void Base_AppendZeroCount_IsNoOp()
        {
            var splitter = new FixedLengthSplitter(2);
            splitter.Append(new byte[] { 1, 2 }, 0, 0);

            var frames = ToArrays(splitter.ExtractFrames());
            Assert.Empty(frames);
        }

        [Fact]
        public void Base_BufferOverflow_Throws()
        {
            var splitter = new DelimiterSplitter((byte)'\n');
            // Append more than MAX_BUFFER_SIZE (10MB)
            var large = new byte[11 * 1024 * 1024];
            Assert.Throws<InvalidOperationException>(() => splitter.Append(large, 0, large.Length));
        }

        #endregion
    }
}
