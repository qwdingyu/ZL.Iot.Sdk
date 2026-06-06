// ============================================================
// 文件：DeadLetterStoreTests.cs
// 描述：DeadLetterStore SQLite 持久化测试
// 修改日期：2026-06-05
// ============================================================

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    public class DeadLetterStoreTests : IDisposable
    {
        private readonly string _testDbPath;

        public DeadLetterStoreTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"dl_test_{Guid.NewGuid():N}.db");
        }

        public void Dispose()
        {
            if (File.Exists(_testDbPath))
            {
                try { File.Delete(_testDbPath); } catch { }
            }
            var wal = _testDbPath + "-wal";
            if (File.Exists(wal))
            {
                try { File.Delete(wal); } catch { }
            }
            var shm = _testDbPath + "-shm";
            if (File.Exists(shm))
            {
                try { File.Delete(shm); } catch { }
            }
        }

        [Fact]
        public async Task AddAndGetAll_PersistsAndRetrieves()
        {
            using var store = new DeadLetterStore(_testDbPath);
            var entry = new DeadLetterStore.DeadLetterEntry
            {
                Topic = "test/topic",
                ContentType = "json",
                PayloadText = "{\"key\":\"value\"}",
                ExceptionMessage = "test error",
                ExceptionType = "System.Exception",
                OutputName = "TestOutput",
                RetryCount = 1,
                FailedAt = DateTimeOffset.UtcNow.ToString("O"),
                TraceId = "trace-123"
            };

            await store.AddAsync(entry);

            var all = await store.GetAllAsync();
            Assert.Single(all);
            Assert.Equal("test/topic", all[0].Topic);
            Assert.Equal("trace-123", all[0].TraceId);
        }

        [Fact]
        public async Task AddNull_DoesNotThrow()
        {
            using var store = new DeadLetterStore(_testDbPath);
            await store.AddAsync(null); // should be no-op
            Assert.Equal(0, await store.GetCountAsync());
        }

        [Fact]
        public async Task GetCountAsync_ReturnsCorrectCount()
        {
            using var store = new DeadLetterStore(_testDbPath);

            for (int i = 0; i < 5; i++)
            {
                await store.AddAsync(new DeadLetterStore.DeadLetterEntry
                {
                    Topic = $"topic-{i}",
                    FailedAt = DateTimeOffset.UtcNow.ToString("O")
                });
            }

            Assert.Equal(5, await store.GetCountAsync());
        }

        [Fact]
        public async Task ClearAsync_RemovesAllEntries()
        {
            using var store = new DeadLetterStore(_testDbPath);

            await store.AddAsync(new DeadLetterStore.DeadLetterEntry
            {
                Topic = "test",
                FailedAt = DateTimeOffset.UtcNow.ToString("O")
            });

            Assert.Equal(1, await store.GetCountAsync());

            await store.ClearAsync();
            Assert.Equal(0, await store.GetCountAsync());
        }

        [Fact]
        public async Task GetAllAsync_Limit_RespectsLimit()
        {
            using var store = new DeadLetterStore(_testDbPath);

            for (int i = 0; i < 10; i++)
            {
                await store.AddAsync(new DeadLetterStore.DeadLetterEntry
                {
                    Topic = $"topic-{i}",
                    FailedAt = DateTimeOffset.UtcNow.ToString("O")
                });
            }

            var result = await store.GetAllAsync(limit: 3);
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public async Task GetAllAsync_ReturnsNewestFirst()
        {
            using var store = new DeadLetterStore(_testDbPath);

            await store.AddAsync(new DeadLetterStore.DeadLetterEntry { Topic = "old", FailedAt = DateTimeOffset.UtcNow.ToString("O") });
            await store.AddAsync(new DeadLetterStore.DeadLetterEntry { Topic = "new", FailedAt = DateTimeOffset.UtcNow.ToString("O") });

            var all = await store.GetAllAsync();
            Assert.Equal("new", all[0].Topic);
        }

        [Fact]
        public async Task MaxRows_ExceedsCapacity_DeletesOldest()
        {
            using var store = new DeadLetterStore(_testDbPath, maxRows: 5, retentionHours: 0);

            for (int i = 0; i < 10; i++)
            {
                await store.AddAsync(new DeadLetterStore.DeadLetterEntry
                {
                    Topic = $"topic-{i}",
                    FailedAt = DateTimeOffset.UtcNow.ToString("O")
                });
            }

            int count = await store.GetCountAsync();
            Assert.True(count <= 5, $"Expected <= 5 rows, got {count}");
        }

        [Fact]
        public async Task Truncate_PayloadText_LongTextTruncated()
        {
            using var store = new DeadLetterStore(_testDbPath);

            var longText = new string('x', 5000);
            await store.AddAsync(new DeadLetterStore.DeadLetterEntry
            {
                Topic = "test",
                PayloadText = longText,
                FailedAt = DateTimeOffset.UtcNow.ToString("O")
            });

            var all = await store.GetAllAsync();
            Assert.Single(all);
            Assert.Equal(4096, all[0].PayloadText.Length); // truncated to 4096
        }

        [Fact]
        public async Task RetentionHours_Zero_DisablesPurge()
        {
            using var store = new DeadLetterStore(_testDbPath, maxRows: 100000, retentionHours: 0);

            await store.AddAsync(new DeadLetterStore.DeadLetterEntry
            {
                Topic = "test",
                FailedAt = "2020-01-01T00:00:00Z" // very old
            });

            Assert.Equal(1, await store.GetCountAsync()); // not purged
        }

        [Fact]
        public void Dispose_CleansUp()
        {
            var store = new DeadLetterStore(_testDbPath);
            store.Dispose();
            // Should not throw on double dispose
            store.Dispose();
        }
    }
}
