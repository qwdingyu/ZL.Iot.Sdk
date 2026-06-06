// ============================================================
// 文件：PipelineDeadLetterManagerTests.cs
// 描述：PipelineDeadLetterManager 完整测试 — 入队、重试上限、容量控制
// 修改日期：2026-06-05
// ============================================================

using System;
using System.Linq;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    public class PipelineDeadLetterManagerTests
    {
        [Fact]
        public void Constructor_DefaultMaxSize()
        {
            var manager = new PipelineDeadLetterManager();
            Assert.Equal(0, manager.Count);
        }

        [Fact]
        public void Add_FirstTime_RetryCountBecomes1()
        {
            var manager = new PipelineDeadLetterManager();
            var msg = new Message { Topic = "test" };
            var ex = new InvalidOperationException("fail");

            manager.Add(msg, ex, null);

            Assert.Equal(1, manager.Count);
            var messages = manager.GetMessages();
            Assert.Single(messages);
            Assert.Equal(1, messages[0].RetryCount);
            // P0-5: Add() clones the message, so stored message is NOT the same reference.
            Assert.NotSame(msg, messages[0].Message);
            Assert.Same(ex, messages[0].Exception);
            // Original message must not be mutated
            Assert.Empty(msg.Metadata);
        }

        [Fact]
        public void Add_DoesNotMutateOriginalMessage()
        {
            var manager = new PipelineDeadLetterManager();
            var msg = new Message { Topic = "test" };
            msg.Metadata["original_key"] = "original_value";
            var ex = new InvalidOperationException("fail");

            manager.Add(msg, ex, null);

            // Original message must NOT have __dead_letter_retries injected
            Assert.False(msg.Metadata.ContainsKey("__dead_letter_retries"));
            Assert.Equal("original_value", msg.Metadata["original_key"]);

            // Stored clone should have retryCount=1
            var messages = manager.GetMessages();
            Assert.Single(messages);
            Assert.Equal(1, messages[0].RetryCount);
        }

        [Fact]
        public void Add_CapacityExceeded_DropsOldest()
        {
            var manager = new PipelineDeadLetterManager(maxSize: 3);

            for (int i = 0; i < 5; i++)
            {
                manager.Add(new Message { Topic = $"topic-{i}" }, new Exception("fail"), null);
            }

            Assert.Equal(3, manager.Count); // capped at maxSize
            var messages = manager.GetMessages();
            // Oldest (topic-0, topic-1) should be dropped, newest remain
            Assert.All(messages, m => Assert.True(int.Parse(m.Message.Topic.Replace("topic-", "")) >= 2));
        }

        [Fact]
        public void GetMessages_ReturnsSnapshot()
        {
            var manager = new PipelineDeadLetterManager();
            manager.Add(new Message { Topic = "a" }, new Exception("fail"), null);
            manager.Add(new Message { Topic = "b" }, new Exception("fail"), null);

            var snapshot = manager.GetMessages();
            Assert.Equal(2, snapshot.Count);

            // Clear should not affect snapshot
            manager.Clear();
            Assert.Equal(2, snapshot.Count); // snapshot unchanged
            Assert.Equal(0, manager.Count);  // manager cleared
        }

        [Fact]
        public void Clear_EmptiesQueue()
        {
            var manager = new PipelineDeadLetterManager();
            manager.Add(new Message { Topic = "a" }, new Exception("fail"), null);
            manager.Add(new Message { Topic = "b" }, new Exception("fail"), null);

            manager.Clear();
            Assert.Equal(0, manager.Count);
            Assert.Empty(manager.GetMessages());
        }

        [Fact]
        public void RecordDeadLetterMetric_CallbackInvoked()
        {
            int callbackCount = 0;
            var manager = new PipelineDeadLetterManager();
            var msg = new Message { Topic = "test" };

            manager.Add(msg, new Exception("fail"), () => callbackCount++);

            Assert.Equal(1, callbackCount);
        }

        [Fact]
        public void Store_SetAndGet()
        {
            var manager = new PipelineDeadLetterManager();
            Assert.Null(manager.Store);

            var store = new DeadLetterStore(":memory:");
            manager.Store = store;
            Assert.Same(store, manager.Store);

            // Setting new store disposes old one
            var store2 = new DeadLetterStore(":memory:");
            manager.Store = store2;
            Assert.Same(store2, manager.Store);

            store.Dispose();
            store2.Dispose();
        }

        [Fact]
        public void MaxDeadLetterRetries_Is3()
        {
            Assert.Equal(3, PipelineDeadLetterManager.MaxDeadLetterRetries);
        }
    }
}
