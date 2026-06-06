using System;
using System.Linq;
using ZL.ProtocolGateway;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    /// <summary>
    /// FixedSizeRingBuffer 单元测试 — 验证追加、覆盖、读取顺序正确性。
    /// </summary>
    public class FixedSizeRingBufferTests
    {
        [Fact]
        public void Append_GetNewest_ReturnsInChronologicalOrder()
        {
            var buf = new FixedSizeRingBuffer<int>(10);
            buf.Append(1);
            buf.Append(2);
            buf.Append(3);

            var result = buf.GetNewest(3);
            Assert.Equal(new[] { 1, 2, 3 }, result);
        }

        [Fact]
        public void Append_BeyondCapacity_OverwritesOldest()
        {
            var buf = new FixedSizeRingBuffer<int>(3);
            buf.Append(1);
            buf.Append(2);
            buf.Append(3);
            buf.Append(4); // overwrites 1

            var result = buf.GetNewest(3);
            Assert.Equal(new[] { 2, 3, 4 }, result);
        }

        [Fact]
        public void Append_ManyItems_KeepsOnlyLastN()
        {
            var buf = new FixedSizeRingBuffer<int>(5);
            for (int i = 0; i < 20; i++)
                buf.Append(i);

            var result = buf.GetNewest(5);
            Assert.Equal(new[] { 15, 16, 17, 18, 19 }, result);
        }

        [Fact]
        public void GetNewest_LessThanCount_ReturnsAll()
        {
            var buf = new FixedSizeRingBuffer<int>(10);
            buf.Append(10);
            buf.Append(20);

            var result = buf.GetNewest(5);
            Assert.Equal(new[] { 10, 20 }, result);
        }

        [Fact]
        public void GetNewest_ZeroOrNegative_ReturnsEmpty()
        {
            var buf = new FixedSizeRingBuffer<int>(5);
            buf.Append(1);

            Assert.Empty(buf.GetNewest(0));
            Assert.Empty(buf.GetNewest(-1));
        }

        [Fact]
        public void GetNewest_EmptyBuffer_ReturnsEmpty()
        {
            var buf = new FixedSizeRingBuffer<int>(5);
            Assert.Empty(buf.GetNewest(10));
        }

        [Fact]
        public void Constructor_ZeroCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedSizeRingBuffer<int>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedSizeRingBuffer<int>(-1));
        }

        [Fact]
        public void Append_CapacityOne_OverwritesCorrectly()
        {
            var buf = new FixedSizeRingBuffer<int>(1);
            buf.Append(1);
            Assert.Equal(new[] { 1 }, buf.GetNewest(1));

            buf.Append(2);
            Assert.Equal(new[] { 2 }, buf.GetNewest(1));
        }

        [Fact]
        public void GetNewest_Partial_ReturnsSubset()
        {
            var buf = new FixedSizeRingBuffer<int>(10);
            for (int i = 1; i <= 8; i++)
                buf.Append(i);

            var result = buf.GetNewest(3);
            Assert.Equal(new[] { 6, 7, 8 }, result);
        }

        [Theory]
        [InlineData(100, 10)]
        [InlineData(500, 50)]
        [InlineData(7, 3)]
        public void Append_LargeCapacity_CorrectTail(int capacity, int readCount)
        {
            var buf = new FixedSizeRingBuffer<int>(capacity);
            for (int i = 0; i < capacity + 5; i++)
                buf.Append(i);

            var result = buf.GetNewest(readCount);
            int expectedStart = (capacity + 5) - readCount;
            var expected = Enumerable.Range(expectedStart, readCount).ToArray();
            Assert.Equal(expected, result);
        }
    }
}
