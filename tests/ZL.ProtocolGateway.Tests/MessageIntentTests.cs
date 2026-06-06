using System;
using System.Collections.Generic;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// MessageIntent 和 TagWrite 测试 — 验证枚举值、Clone 传播、ToString。
    /// </summary>
    public class MessageIntentTests
    {
        [Fact]
        public void Message_DefaultIntent_IsForward()
        {
            var msg = new Message();
            Assert.Equal(MessageIntent.Forward, msg.Intent);
        }

        [Fact]
        public void Message_SetIntent_Persists()
        {
            var msg = new Message { Intent = MessageIntent.TagWrite };
            Assert.Equal(MessageIntent.TagWrite, msg.Intent);
        }

        [Fact]
        public void Message_Clone_PreservesIntent()
        {
            var original = new Message
            {
                Topic = "test",
                Intent = MessageIntent.TagWrite,
                Writes = new List<TagWrite>
                {
                    new TagWrite("DB1.DBW0", 42, "INT16", "temp", DateTime.UtcNow)
                }
            };

            var clone = original.Clone();

            Assert.Equal(MessageIntent.TagWrite, clone.Intent);
            Assert.NotSame(original.Writes, clone.Writes);
            Assert.Equal(original.Writes.Count, clone.Writes.Count);
        }

        [Fact]
        public void Message_ToString_ContainsIntent()
        {
            var msg = new Message
            {
                Topic = "test",
                Intent = MessageIntent.ScriptTrigger,
                Payload = new byte[] { 1, 2, 3 }
            };

            var str = msg.ToString();

            Assert.Contains("ScriptTrigger", str);
            Assert.Contains("Intent=", str);
        }

        [Fact]
        public void MessageIntent_EnumValues_AreCorrect()
        {
            var values = (MessageIntent[])Enum.GetValues(typeof(MessageIntent));
            Assert.Equal(4, values.Length);
            Assert.Contains(values, v => v == MessageIntent.Forward);
            Assert.Contains(values, v => v == MessageIntent.TagWrite);
            Assert.Contains(values, v => v == MessageIntent.TagRead);
            Assert.Contains(values, v => v == MessageIntent.ScriptTrigger);
        }

        [Fact]
        public void TagWrite_RecordProperties_AreCorrect()
        {
            var now = DateTime.UtcNow;
            var tw = new TagWrite("DB1.DBD0", 3.14, "FLOAT", "temperature", now);

            Assert.Equal("DB1.DBD0", tw.Address);
            Assert.Equal(3.14, tw.Value);
            Assert.Equal("FLOAT", tw.DataType);
            Assert.Equal("temperature", tw.Alias);
            Assert.Equal(now, tw.Timestamp);
        }

        [Fact]
        public void TagWrite_NullAlias_Allowed()
        {
            var tw = new TagWrite("40001", true, "BOOL", null, DateTime.UtcNow);
            Assert.Null(tw.Alias);
        }

        [Fact]
        public void Message_Writes_DefaultEmptyList()
        {
            var msg = new Message();
            Assert.NotNull(msg.Writes);
            Assert.Empty(msg.Writes);
        }

        [Fact]
        public void Message_TagWriteIntent_WithWrites()
        {
            var msg = new Message
            {
                Topic = "plc/write",
                Intent = MessageIntent.TagWrite,
                Writes = new List<TagWrite>
                {
                    new TagWrite("DB1.DBW0", 100, "INT16", null, DateTime.UtcNow),
                    new TagWrite("DB1.DBW2", 200, "INT16", null, DateTime.UtcNow)
                }
            };

            Assert.Equal(MessageIntent.TagWrite, msg.Intent);
            Assert.Equal(2, msg.Writes.Count);

            var clone = msg.Clone();
            Assert.Equal(MessageIntent.TagWrite, clone.Intent);
            Assert.Equal(2, clone.Writes.Count);
            Assert.NotSame(msg.Writes, clone.Writes);
        }
    }
}
