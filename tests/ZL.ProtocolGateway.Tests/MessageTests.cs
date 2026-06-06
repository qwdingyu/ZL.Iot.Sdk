using System;
using System.Text;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// Message 类单元测试
    /// </summary>
    public class MessageTests
    {
        [Fact]
        public void Constructor_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var message = new Message();

            // Assert
            Assert.NotNull(message.Id);
            Assert.Equal(32, message.Id.Length); // GUID without hyphens
            Assert.Null(message.Topic);
            Assert.Null(message.Payload);
            Assert.Equal("binary", message.ContentType);
            Assert.NotNull(message.Metadata);
            Assert.Empty(message.Metadata);
        }

        [Fact]
        public void Topic_ShouldBeSettable()
        {
            // Arrange
            var message = new Message();

            // Act
            message.Topic = "test/topic";

            // Assert
            Assert.Equal("test/topic", message.Topic);
        }

        [Fact]
        public void SetJsonContent_ShouldSetPayloadAndContentType()
        {
            // Arrange
            var message = new Message();
            var json = "{\"name\":\"test\",\"value\":123}";

            // Act
            message.SetJsonContent(json);

            // Assert
            Assert.Equal("json", message.ContentType);
            Assert.NotNull(message.Payload);
            Assert.Equal(json, message.GetJsonContent());
        }

        [Fact]
        public void SetTextContent_ShouldSetPayloadAndContentType()
        {
            // Arrange
            var message = new Message();
            var text = "Hello, World!";

            // Act
            message.SetTextContent(text);

            // Assert
            Assert.Equal("text", message.ContentType);
            Assert.NotNull(message.Payload);
            Assert.Equal(text, message.GetTextContent());
        }

        [Fact]
        public void SetHexContent_ShouldConvertHexStringToBytes()
        {
            // Arrange
            var message = new Message();
            var hex = "48656C6C6F"; // "Hello" in hex

            // Act
            message.SetHexContent(hex);

            // Assert
            Assert.Equal("hex", message.ContentType);
            Assert.NotNull(message.Payload);
            Assert.Equal(5, message.Payload.Length);
            Assert.Equal("Hello", message.GetTextContent());
        }

        [Fact]
        public void SetHexContent_ShouldThrowOnOddLength()
        {
            // Arrange
            var message = new Message();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => message.SetHexContent("486")); // 奇数长度（3 个字符）
        }

        [Fact]
        public void GetHexContent_ShouldReturnHexString()
        {
            // Arrange
            var message = new Message
            {
                Payload = Encoding.ASCII.GetBytes("Hi")
            };

            // Act
            var hex = message.GetHexContent();

            // Assert
            Assert.Equal("4869", hex); // "H"=0x48, "i"=0x69
        }

        [Fact]
        public void GetJsonContent_ShouldThrowOnWrongContentType()
        {
            // Arrange
            var message = new Message
            {
                Payload = Encoding.ASCII.GetBytes("test"),
                ContentType = "text"
            };

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => message.GetJsonContent());
        }

        [Fact]
        public void Clone_ShouldCreateDeepCopy()
        {
            // Arrange
            var original = new Message
            {
                Topic = "test/topic",
                Payload = Encoding.ASCII.GetBytes("test data"),
                ContentType = "text",
                Metadata = { ["key1"] = "value1", ["key2"] = "value2" }
            };

            // Act
            var clone = original.Clone();

            // Assert
            Assert.Equal(original.Id, clone.Id);
            Assert.Equal(original.Topic, clone.Topic);
            Assert.Equal(original.ContentType, clone.ContentType);
            Assert.Equal(original.Timestamp, clone.Timestamp);
            Assert.Equal(original.Metadata.Count, clone.Metadata.Count);

            // Verify deep copy of payload
            Assert.NotSame(original.Payload, clone.Payload);
            Assert.Equal(original.Payload, clone.Payload);

            // Verify deep copy of metadata
            Assert.NotSame(original.Metadata, clone.Metadata);
            clone.Metadata["key1"] = "modified";
            Assert.Equal("value1", original.Metadata["key1"]); // Original unchanged
        }

        [Fact]
        public void Clone_WithNullPayload_ShouldHandleCorrectly()
        {
            // Arrange
            var original = new Message
            {
                Topic = "test/topic",
                Payload = null
            };

            // Act
            var clone = original.Clone();

            // Assert
            Assert.Null(clone.Payload);
        }

        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            // Arrange
            var message = new Message
            {
                Topic = "test/topic",
                Payload = Encoding.ASCII.GetBytes("12345"),
                ContentType = "text"
            };

            // Act
            var result = message.ToString();

            // Assert
            Assert.Contains("Message", result);
            Assert.Contains(message.Id, result);
            Assert.Contains("test/topic", result);
            Assert.Contains("5 bytes", result);
        }

        [Fact]
        public void Metadata_ShouldBeModifiable()
        {
            // Arrange
            var message = new Message();

            // Act
            message.Metadata["key1"] = "value1";
            message.Metadata["key2"] = "value2";
            message.Metadata.Remove("key1");

            // Assert
            Assert.Single(message.Metadata);
            Assert.Equal("value2", message.Metadata["key2"]);
            Assert.False(message.Metadata.ContainsKey("key1"));
        }

        [Theory]
        [InlineData("", "json")]
        [InlineData("not-json", "json")]
        [InlineData("{}", "text")]
        public void ContentConversion_ShouldHandleVariousInputs(string content, string contentType)
        {
            // Arrange
            var message = new Message();

            // Act
            if (contentType == "json")
                message.SetJsonContent(content);
            else
                message.SetTextContent(content);

            // Assert
            Assert.NotNull(message.Payload);
            Assert.Equal(contentType, message.ContentType);
        }
    }
}
