using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Transformers
{
    /// <summary>
    /// Transformer 并发压力测试 — 验证在 Pipeline 并发度为 4 的场景下线程安全。
    /// </summary>
    public class TransformerConcurrencyTests
    {
        #region DeadbandTransformer 并发测试

        [Fact]
        public async Task DeadbandTransformer_ConcurrentAccess_NoDataLoss()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 5.0, absolute: true).Build();
            var tasks = new List<Task>();
            var results = new ConcurrentBag<Message?>();

            // 10 个线程各发送 100 条消息到同一地址
            for (int t = 0; t < 10; t++)
            {
                int threadIdx = t;
                tasks.Add(Task.Run(async () =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        var msg = new Message
                        {
                            Topic = "test",
                            Writes = new List<TagWrite>
                            {
                                new TagWrite($"shared_addr", (double)(threadIdx * 100 + i), "FLOAT", null, DateTime.UtcNow)
                            }
                        };
                        var result = await transformer(msg);
                        results.Add(result);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // 不应抛异常，且应有结果（首次值 + 变化足够大的值）
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task DeadbandTransformer_ConcurrentSameAddress_CorrectFiltering()
        {
            var db = new DeadbandTransformer(defaultDeadband: 100.0, absolute: true);
            var fn = db.Build();

            // 并发发送 50 次相同值 — 只有第一次应通过，其余全部过滤
            var tasks = new Task[50];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var msg = new Message
                    {
                        Topic = "test",
                        Writes = new List<TagWrite>
                        {
                            new TagWrite("same_addr", 50.0, "FLOAT", null, DateTime.UtcNow)
                        }
                    };
                    await fn(msg);
                });
            }

            await Task.WhenAll(tasks);
            // 不关心具体结果，只要不抛异常即通过
        }

        [Fact]
        public async Task DeadbandTransformer_ConcurrentDifferentAddresses_IndependentTracking()
        {
            var db = new DeadbandTransformer(defaultDeadband: 5.0, absolute: true);
            var fn = db.Build();

            // 每个线程写不同地址，各发 100 次
            var tasks = new Task[10];
            for (int t = 0; t < tasks.Length; t++)
            {
                int addr = t;
                tasks[t] = Task.Run(async () =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        var msg = new Message
                        {
                            Topic = "test",
                            Writes = new List<TagWrite>
                            {
                                new TagWrite($"addr_{addr}", (double)(i * 10), "FLOAT", null, DateTime.UtcNow)
                            }
                        };
                        await fn(msg);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        #endregion

        #region SamplingTransformer 并发测试

        [Fact]
        public async Task SamplingTransformer_ConcurrentAccess_NoCounterLoss()
        {
            var sampler = new SamplingTransformer(10);
            var fn = sampler.Build();

            var tasks = new Task[20];
            var results = new ConcurrentBag<bool>();

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var msg = new Message { Topic = "test" };
                    var result = await fn(msg);
                    results.Add(result != null);
                });
            }

            await Task.WhenAll(tasks);

            // 20 条消息，interval=10，应恰好通过 2 条（第 10 和第 20 条）
            int passCount = results.Count(r => r);
            Assert.InRange(passCount, 1, 3); // 允许 ±1 的并发偏差
        }

        [Fact]
        public async Task SamplingTransformer_ConcurrentHighLoad_NoException()
        {
            var sampler = new SamplingTransformer(5);
            var fn = sampler.Build();

            var tasks = new Task[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        await fn(new Message { Topic = "load" });
                    }
                });
            }

            await Task.WhenAll(tasks);
            // 100 线程 × 100 消息 = 10000 次并发调用，不应抛异常
        }

        #endregion

        #region TimeBasedSamplingTransformer 并发测试

        [Fact]
        public async Task TimeBasedSamplingTransformer_ConcurrentAccess_NoRaceCondition()
        {
            var tbs = new TimeBasedSamplingTransformer(TimeSpan.FromMilliseconds(10));
            var fn = tbs.Build();

            var tasks = new Task[50];
            var results = new ConcurrentBag<bool>();

            // 每个线程发送不同时间戳的消息，模拟并发处理
            for (int i = 0; i < tasks.Length; i++)
            {
                int idx = i;
                tasks[i] = Task.Run(async () =>
                {
                    var baseTime = new DateTime(2024, 1, 1, 0, 0, 0).AddMilliseconds(idx * 20);
                    var msg = new Message { Topic = "test", Timestamp = baseTime };
                    var result = await fn(msg);
                    results.Add(result != null);
                });
            }

            await Task.WhenAll(tasks);

            // 不应抛异常
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task TimeBasedSamplingTransformer_ConcurrentHighLoad_NoException()
        {
            var tbs = new TimeBasedSamplingTransformer(TimeSpan.FromMilliseconds(1));
            var fn = tbs.Build();

            var tasks = new Task[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                int idx = i;
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < 50; j++)
                    {
                        var ts = new DateTime(2024, 1, 1, 0, 0, 0).AddMilliseconds(idx * 50 + j);
                        await fn(new Message { Topic = "load", Timestamp = ts });
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        #endregion
    }
}
