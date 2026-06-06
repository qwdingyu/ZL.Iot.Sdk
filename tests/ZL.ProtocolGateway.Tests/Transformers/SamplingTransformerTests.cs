using System;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Transformers
{
    public class SamplingTransformerTests
    {
        [Fact]
        public async Task Build_Interval1_PassesAllMessages()
        {
            var transformer = new SamplingTransformer(1).Build();

            for (int i = 0; i < 10; i++)
            {
                var msg = new Message { Topic = "test", Payload = new byte[] { (byte)i } };
                var result = await transformer(msg);
                Assert.Same(msg, result);
            }
        }

        [Fact]
        public async Task Build_Interval5_PassesEveryFifth()
        {
            var transformer = new SamplingTransformer(5).Build();
            int passCount = 0;

            for (int i = 0; i < 20; i++)
            {
                var msg = new Message { Topic = "test", Payload = new byte[] { (byte)i } };
                var result = await transformer(msg);
                if (result != null) passCount++;
            }

            Assert.Equal(4, passCount); // 第 5,10,15,20 条
        }

        [Fact]
        public async Task Build_Interval10_FirstPassAtTenth()
        {
            var transformer = new SamplingTransformer(10).Build();

            for (int i = 0; i < 9; i++)
            {
                var msg = new Message { Topic = "test", Payload = new byte[] { (byte)i } };
                var r = await transformer(msg);
                Assert.Null(r);
            }

            var tenth = new Message { Topic = "test", Payload = new byte[] { 9 } };
            var result = await transformer(tenth);
            Assert.NotNull(result);
        }

        [Fact]
        public void Build_IntervalZero_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SamplingTransformer(0));
        }

        [Fact]
        public void Build_IntervalNegative_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SamplingTransformer(-1));
        }

        [Fact]
        public async Task Reset_RestartsCounting()
        {
            var sampler = new SamplingTransformer(3);
            var fn = sampler.Build();

            // 发送 2 条（都不过）
            await fn(new Message { Topic = "a" });
            await fn(new Message { Topic = "b" });

            sampler.Reset();

            // 重置后重新计数，第 3 条才过
            var result1 = await fn(new Message { Topic = "c" });
            Assert.Null(result1);

            var result2 = await fn(new Message { Topic = "d" });
            Assert.Null(result2);

            var result3 = await fn(new Message { Topic = "e" });
            Assert.NotNull(result3);
        }
    }

    public class TimeBasedSamplingTransformerTests
    {
        [Fact]
        public async Task Build_OneSecondInterval_PassesAfterInterval()
        {
            var transformer = new TimeBasedSamplingTransformer(TimeSpan.FromSeconds(1)).Build();

            var msg1 = new Message { Topic = "test", Timestamp = new DateTime(2024, 1, 1, 0, 0, 0) };
            var result1 = await transformer(msg1);
            Assert.NotNull(result1);

            // 500ms 后 — 不到 1s，应丢弃
            var msg2 = new Message { Topic = "test", Timestamp = new DateTime(2024, 1, 1, 0, 0, 0).AddMilliseconds(500) };
            var result2 = await transformer(msg2);
            Assert.Null(result2);

            // 1.5s 后 — 超过 1s，应放行
            var msg3 = new Message { Topic = "test", Timestamp = new DateTime(2024, 1, 1, 0, 0, 0).AddMilliseconds(1500) };
            var result3 = await transformer(msg3);
            Assert.NotNull(result3);
        }

        [Fact]
        public async Task Build_100MsInterval_RateLimiting()
        {
            var transformer = new TimeBasedSamplingTransformer(TimeSpan.FromMilliseconds(100)).Build();

            var baseTime = new DateTime(2024, 1, 1, 0, 0, 0);
            int passCount = 0;

            for (int i = 0; i < 1000; i += 10) // 每 10ms 一条，共 100 条
            {
                var msg = new Message { Topic = "test", Timestamp = baseTime.AddMilliseconds(i) };
                var result = await transformer(msg);
                if (result != null) passCount++;
            }

            // 1000ms / 100ms = 10 条
            Assert.Equal(10, passCount);
        }

        [Fact]
        public void Build_ZeroInterval_Throws()
        {
            Assert.Throws<ArgumentException>(() => new TimeBasedSamplingTransformer(TimeSpan.Zero));
        }

        [Fact]
        public async Task Reset_RestartsTimer()
        {
            var tbs = new TimeBasedSamplingTransformer(TimeSpan.FromSeconds(1));
            var fn = tbs.Build();

            var baseTime = new DateTime(2024, 1, 1, 0, 0, 0);
            // 第一条消息放行，_lastPassed 更新为 baseTime
            await fn(new Message { Topic = "a", Timestamp = baseTime });

            tbs.Reset();

            // 重置后，即使时间很近也应放行
            var result = await fn(new Message { Topic = "b", Timestamp = baseTime.AddMilliseconds(10) });
            Assert.NotNull(result);
        }
    }
}
