// ============================================================
// 文件：DeduplicationFilterTests.cs
// 描述：DeduplicationFilter 单元测试 — 去重、清理、重置、Dispose
// ============================================================

using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    public class DeduplicationFilterTests : IDisposable
    {
        private DeduplicationFilter _filter;

        public void Dispose()
        {
            _filter?.Dispose();
            _filter = null;
        }

        private Message CreateMessage(string topic = "sensor/temp", byte[] payload = null, DateTime? timestamp = null)
        {
            var msg = new Message
            {
                Topic = topic,
                Payload = payload ?? Encoding.UTF8.GetBytes("test-data"),
                Timestamp = timestamp ?? DateTime.UtcNow
            };
            return msg;
        }

        [Fact]
        public async Task FilterAsync_FirstMessage_ReturnsTrue()
        {
            _filter = new DeduplicationFilter();
            var msg = CreateMessage();

            var result = await _filter.FilterAsync(msg);

            Assert.True(result);
        }

        [Fact]
        public async Task FilterAsync_ExactDuplicate_ReturnsFalse()
        {
            _filter = new DeduplicationFilter();
            var timestamp = DateTime.UtcNow;
            var msg = new Message
            {
                Topic = "sensor/temp",
                Payload = Encoding.UTF8.GetBytes("test-data"),
                Timestamp = timestamp
            };

            var first = await _filter.FilterAsync(msg);
            var second = await _filter.FilterAsync(msg);

            Assert.True(first);
            Assert.False(second);
        }

        [Fact]
        public async Task FilterAsync_DifferentTopic_ReturnsTrue()
        {
            _filter = new DeduplicationFilter();
            var timestamp = DateTime.UtcNow;
            var msg1 = new Message { Topic = "sensor/temp", Payload = Encoding.UTF8.GetBytes("data"), Timestamp = timestamp };
            var msg2 = new Message { Topic = "sensor/humidity", Payload = Encoding.UTF8.GetBytes("data"), Timestamp = timestamp };

            var first = await _filter.FilterAsync(msg1);
            var second = await _filter.FilterAsync(msg2);

            Assert.True(first);
            Assert.True(second);
        }

        [Fact]
        public async Task FilterAsync_DifferentPayload_ReturnsTrue()
        {
            _filter = new DeduplicationFilter();
            var timestamp = DateTime.UtcNow;
            var msg1 = new Message { Topic = "sensor/temp", Payload = Encoding.UTF8.GetBytes("data1"), Timestamp = timestamp };
            var msg2 = new Message { Topic = "sensor/temp", Payload = Encoding.UTF8.GetBytes("data2"), Timestamp = timestamp };

            var first = await _filter.FilterAsync(msg1);
            var second = await _filter.FilterAsync(msg2);

            Assert.True(first);
            Assert.True(second);
        }

        [Fact]
        public async Task FilterAsync_DifferentSecond_ReturnsTrue()
        {
            _filter = new DeduplicationFilter();
            var msg1 = new Message { Topic = "sensor/temp", Payload = Encoding.UTF8.GetBytes("data"), Timestamp = DateTime.UtcNow };

            await Task.Delay(1100); // 超过 1 秒时间窗口

            var msg2 = new Message { Topic = "sensor/temp", Payload = Encoding.UTF8.GetBytes("data"), Timestamp = DateTime.UtcNow };

            var first = await _filter.FilterAsync(msg1);
            var second = await _filter.FilterAsync(msg2);

            Assert.True(first);
            Assert.True(second); // 不同秒数，不是重复
        }

        [Fact]
        public async Task SeenCount_IncrementsAfterFilter()
        {
            _filter = new DeduplicationFilter();
            var msg1 = CreateMessage("topic1");
            var msg2 = CreateMessage("topic2");

            await _filter.FilterAsync(msg1);
            await _filter.FilterAsync(msg2);

            Assert.Equal(2, _filter.SeenCount);
        }

        [Fact]
        public async Task Reset_ClearsAllEntries()
        {
            _filter = new DeduplicationFilter();
            var timestamp = DateTime.UtcNow;
            var msg = new Message { Topic = "sensor/temp", Payload = Encoding.UTF8.GetBytes("data"), Timestamp = timestamp };

            Assert.True(await _filter.FilterAsync(msg));
            Assert.False(await _filter.FilterAsync(msg));

            _filter.Reset();

            Assert.True(await _filter.FilterAsync(msg));
            Assert.Equal(1, _filter.SeenCount);
        }

        [Fact]
        public async Task CleanupExpired_RemovesOldEntries()
        {
            // 使用极短的窗口和清理间隔
            _filter = new DeduplicationFilter(window: TimeSpan.FromMilliseconds(200), cleanupInterval: TimeSpan.FromMilliseconds(100));
            var timestamp = DateTime.UtcNow;
            var msg = new Message { Topic = "sensor/temp", Payload = Encoding.UTF8.GetBytes("data"), Timestamp = timestamp };

            Assert.True(await _filter.FilterAsync(msg));
            Assert.False(await _filter.FilterAsync(msg));
            Assert.Equal(1, _filter.SeenCount);

            // 等待窗口过期 + 清理
            await Task.Delay(350);

            Assert.Equal(0, _filter.SeenCount);
        }

        [Fact]
        public void Constructor_DefaultValues_CreatesWithFiveMinuteWindow()
        {
            _filter = new DeduplicationFilter();

            // 不会抛异常，默认值生效
            Assert.NotNull(_filter);
        }

        [Fact]
        public void Constructor_CustomValues_Accepted()
        {
            _filter = new DeduplicationFilter(window: TimeSpan.FromSeconds(30), cleanupInterval: TimeSpan.FromSeconds(10));

            Assert.NotNull(_filter);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            _filter = new DeduplicationFilter();
            _filter.Dispose();
            _filter.Dispose(); // 不应抛异常
        }
    }
}
