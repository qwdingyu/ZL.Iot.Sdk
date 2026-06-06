using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Transformers
{
    public class DeadbandTransformerTests
    {
        [Fact]
        public async Task Build_ZeroDeadband_PassesAllMessagesThrough()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 0.0).Build();

            var msg = CreateMessageWithWrites("DB1.DBD0", 100.0, "FLOAT");
            var result = await transformer(msg);

            Assert.Same(msg, result);
        }

        [Fact]
        public async Task Build_FirstValue_NeverFiltered()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 5.0).Build();

            var msg = CreateMessageWithWrites("DB1.DBD0", 100.0, "FLOAT");
            var result = await transformer(msg);

            Assert.NotNull(result);
            Assert.Equal(1, result.Writes.Count);
        }

        [Fact]
        public async Task Build_AbsoluteDeadband_SmallChange_Filtered()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 5.0, absolute: true).Build();

            // 首次值 100.0 — 不过滤
            await transformer(CreateMessageWithWrites("DB1.DBD0", 100.0, "FLOAT"));

            // 变化 2.0 < 5.0 死区 — 应过滤
            var msg2 = CreateMessageWithWrites("DB1.DBD0", 102.0, "FLOAT");
            var result = await transformer(msg2);

            Assert.Null(result); // 全部被过滤 → 返回 null
        }

        [Fact]
        public async Task Build_AbsoluteDeadband_LargeChange_Passes()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 5.0, absolute: true).Build();

            await transformer(CreateMessageWithWrites("DB1.DBD0", 100.0, "FLOAT"));

            // 变化 10.0 > 5.0 死区 — 应放行
            var msg2 = CreateMessageWithWrites("DB1.DBD0", 110.0, "FLOAT");
            var result = await transformer(msg2);

            Assert.NotNull(result);
            Assert.Equal(1, result.Writes.Count);
            Assert.Equal(110.0, ((TagWrite)result.Writes[0]).Value);
        }

        [Fact]
        public async Task Build_RelativeDeadband_SmallPercentChange_Filtered()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 0.05, absolute: false).Build();

            await transformer(CreateMessageWithWrites("DB1.DBD0", 100.0, "FLOAT"));

            // 变化 2% < 5% 死区 — 应过滤
            var msg2 = CreateMessageWithWrites("DB1.DBD0", 102.0, "FLOAT");
            var result = await transformer(msg2);

            Assert.Null(result);
        }

        [Fact]
        public async Task Build_RelativeDeadband_LargePercentChange_Passes()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 0.05, absolute: false).Build();

            await transformer(CreateMessageWithWrites("DB1.DBD0", 100.0, "FLOAT"));

            // 变化 10% > 5% 死区 — 应放行
            var msg2 = CreateMessageWithWrites("DB1.DBD0", 110.0, "FLOAT");
            var result = await transformer(msg2);

            Assert.NotNull(result);
            Assert.Equal(1, result.Writes.Count);
        }

        [Fact]
        public async Task Build_MultiTag_PartialFilter()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 5.0, absolute: true).Build();

            // 首次发送两个 tag
            var msg1 = CreateMessageWithWrites(new[]
            {
                ("DB1.DBD0", 100.0, "FLOAT"),
                ("DB1.DBD4", 50.0, "FLOAT")
            });
            await transformer(msg1);

            // DBD0 变化 2 (< 5, 过滤), DBD4 变化 10 (> 5, 放行)
            var msg2 = CreateMessageWithWrites(new[]
            {
                ("DB1.DBD0", 102.0, "FLOAT"),
                ("DB1.DBD4", 60.0, "FLOAT")
            });
            var result = await transformer(msg2);

            Assert.NotNull(result);
            Assert.Equal(1, result.Writes.Count); // DBD0 被过滤
            Assert.Equal("DB1.DBD4", result.Writes[0].Address);
        }

        [Fact]
        public async Task Build_BoolType_NeverFiltered()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 1.0, absolute: true).Build();

            await transformer(CreateMessageWithWrites("DB1.DBX0.0", true, "BOOL"));

            // BOOL 变化应始终放行
            var msg2 = CreateMessageWithWrites("DB1.DBX0.0", false, "BOOL");
            var result = await transformer(msg2);

            Assert.NotNull(result);
            Assert.Equal(1, result.Writes.Count);
        }

        [Fact]
        public async Task Build_StringType_NeverFiltered()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 1.0, absolute: true).Build();

            var msg = CreateMessageWithWrites("DB1.DBS0", "hello", "STRING");
            var result = await transformer(msg);

            Assert.NotNull(result);
            Assert.Equal(1, result.Writes.Count);
        }

        [Fact]
        public async Task Build_NoWrites_PassesThrough()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 5.0).Build();

            var msg = new Message { Topic = "test", Payload = new byte[] { 1, 2, 3 } };
            var result = await transformer(msg);

            Assert.Same(msg, result);
        }

        [Fact]
        public async Task Build_PerTypeDeadband_UsesSpecificThreshold()
        {
            var transformer = new DeadbandTransformer(defaultDeadband: 100.0)
                .SetDeadband("FLOAT", 0.1)
                .Build();

            await transformer(CreateMessageWithWrites("temp", 25.0, "FLOAT"));

            // FLOAT 使用 0.1 死区，变化 0.05 < 0.1 → 过滤
            var msg2 = CreateMessageWithWrites("temp", 25.05, "FLOAT");
            var result = await transformer(msg2);

            Assert.Null(result);
        }

        [Fact]
        public async Task Reset_ClearsHistory()
        {
            var db = new DeadbandTransformer(defaultDeadband: 5.0);

            var fn = db.Build();

            // 先发送 100.0，等待完成
            await fn(CreateMessageWithWrites("A", 100.0, "FLOAT"));

            db.Reset();

            // 重置后，100.0 再次被视为首次值，不应被过滤
            var msg = CreateMessageWithWrites("A", 100.0, "FLOAT");
            var result = await fn(msg);

            Assert.NotNull(result);
            Assert.Equal(1, result.Writes.Count);
        }

        #region Helpers

        private static Message CreateMessageWithWrites(string address, object value, string dataType)
        {
            return new Message
            {
                Topic = "test",
                Writes = new List<TagWrite>
                {
                    new TagWrite(address, value, dataType, null, DateTime.UtcNow)
                }
            };
        }

        private static Message CreateMessageWithWrites((string, double, string)[] tags)
        {
            var writes = new List<TagWrite>();
            foreach (var t in tags)
            {
                writes.Add(new TagWrite(t.Item1, t.Item2, t.Item3, null, DateTime.UtcNow));
            }
            return new Message { Topic = "test", Writes = writes };
        }

        #endregion
    }
}
