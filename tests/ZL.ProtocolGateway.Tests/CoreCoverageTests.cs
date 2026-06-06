// ============================================================
// 文件：CoreCoverageTests.cs
// 描述：核心类覆盖率提升测试 — Message, GatewayDiagnosticInfo,
//       GatewaySendResult, GatewayErrorCatalog, TagWrite
// 注意：ConfigValidation 测试已在 ConfigValidationTests.cs 中
// 修改日期：2026-06-05
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class MessageCoverageTests
    {
        [Fact]
        public void Message_DefaultConstructor_HasGuid()
        {
            var msg = new Message();
            Assert.NotEmpty(msg.Id);
            Assert.Equal(MessageIntent.Forward, msg.Intent);
            Assert.Null(msg.Payload);
            Assert.NotNull(msg.Metadata);
            Assert.NotNull(msg.Writes);
        }

        [Fact]
        public void Message_SetIntent_SetsIntent()
        {
            var msg = new Message { Intent = MessageIntent.TagWrite };
            Assert.Equal(MessageIntent.TagWrite, msg.Intent);
        }

        [Theory]
        [InlineData(MessageIntent.Forward)]
        [InlineData(MessageIntent.TagWrite)]
        [InlineData(MessageIntent.TagRead)]
        [InlineData(MessageIntent.ScriptTrigger)]
        public void MessageIntent_AllValuesValid(MessageIntent intent)
        {
            var msg = new Message { Intent = intent };
            Assert.Equal(intent, msg.Intent);
        }

        [Fact]
        public void Message_Clone_DeepCopiesMetadata()
        {
            var original = new Message { Topic = "test" };
            original.Metadata["key"] = "value";
            var clone = original.Clone();

            clone.Metadata["key"] = "modified";
            Assert.Equal("value", original.Metadata["key"]);
            Assert.Equal("modified", clone.Metadata["key"]);
        }

        [Fact]
        public void Message_Clone_DeepCopiesWrites()
        {
            var original = new Message { Writes = { new TagWrite("DB1.DBD0", 42.0, "Double", null, DateTime.UtcNow) } };
            var clone = original.Clone();

            Assert.NotSame(original.Writes, clone.Writes);
            Assert.Single(clone.Writes);
            Assert.Equal("DB1.DBD0", clone.Writes[0].Address);
        }

        [Fact]
        public void Message_Clone_CopiesPayload()
        {
            var original = new Message();
            original.SetPayload(new byte[] { 1, 2, 3 });
            var clone = original.Clone();

            Assert.NotSame(original.Payload, clone.Payload);
            Assert.Equal(original.Payload, clone.Payload);
        }

        [Fact]
        public void Message_SetJsonContent_SetsPayloadAndType()
        {
            var msg = new Message();
            msg.SetJsonContent("{\"key\":\"value\"}");
            Assert.Equal("json", msg.ContentType);
            Assert.NotNull(msg.Payload);
            Assert.Equal("{\"key\":\"value\"}", msg.GetJsonContent());
        }

        [Fact]
        public void Message_SetTextContent_SetsPayloadAndType()
        {
            var msg = new Message();
            msg.SetTextContent("hello");
            Assert.Equal("text", msg.ContentType);
            Assert.Equal("hello", msg.GetTextContent());
        }

        [Fact]
        public void Message_SetHexContent_SetsPayloadAndType()
        {
            var msg = new Message();
            msg.SetHexContent("DEADBEEF");
            Assert.Equal("hex", msg.ContentType);
            Assert.Equal("DEADBEEF", msg.GetHexContent());
        }

        [Fact]
        public void Message_TraceId_Property_ReadsAndWrites()
        {
            var msg = new Message();
            Assert.Empty(msg.TraceId);
            msg.TraceId = "abc-123";
            Assert.Equal("abc-123", msg.TraceId);
        }

        [Fact]
        public void TagWrite_Ctor_SetsValues()
        {
            var tw = new TagWrite("DB1.DBD0", 42.0, "Double", "Temp", DateTime.UtcNow);
            Assert.Equal("DB1.DBD0", tw.Address);
            Assert.Equal(42.0, tw.Value);
            Assert.Equal("Double", tw.DataType);
            Assert.Equal("Temp", tw.Alias);
        }

        [Fact]
        public void TagWrite_WithNullValue_UsesNullDataType()
        {
            var tw = new TagWrite("DB1.DBX0.0", null, "NULL", null, DateTime.UtcNow);
            Assert.Null(tw.Value);
            Assert.Equal("NULL", tw.DataType);
        }
    }

    public class GatewayDiagnosticInfoTests
    {
        [Fact]
        public void Success_CreatesNonErrorResult()
        {
            var result = GatewayDiagnosticInfo.Success("test", "worked fine");
            Assert.False(result.HasError);
            Assert.Equal("test", result.TraceId);
            Assert.Equal("worked fine", result.TechnicalMessage);
        }

        [Fact]
        public void FromException_ReturnsErrorInfo()
        {
            var ex = new InvalidOperationException("test error");
            var result = GatewayErrorCatalog.FromException(ex, "trace123");
            Assert.True(result.HasError);
            Assert.Equal("trace123", result.TraceId);
            Assert.Same(ex, result.Exception);
        }

        [Fact]
        public void FromException_PreservesErrorDetails()
        {
            var ex = new TimeoutException("connection timeout");
            var result = GatewayErrorCatalog.FromException(ex, "t1");
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.Category);
            Assert.NotNull(result.UserMessage);
            Assert.NotNull(result.Advice);
        }
    }

    public class GatewaySendResultCoverageTests
    {
        [Fact]
        public void CreateSuccessResult_ReturnsSuccess()
        {
            var mockOutput = new TestOutputForSendResult("out1");
            var msg = new Message { Topic = "test" };
            var result = PipelineSendStrategy.CreateSuccessResult("t1", mockOutput, msg, 10.5, 1);

            Assert.Equal("t1", result.TraceId);
            Assert.Equal("out1", result.OutputName);
            Assert.Equal(GatewaySendFinalStatus.Success, result.FinalStatus);
            Assert.Equal(10.5, result.DurationMs);
            Assert.Equal(1, result.AttemptCount);
        }

        [Fact]
        public void CreateCircuitOpenResult_ReturnsDeadLettered()
        {
            var msg = new Message { Topic = "test" };
            var result = PipelineSendStrategy.CreateCircuitOpenResult("t1", "out1", msg);

            Assert.Equal(GatewaySendFinalStatus.DeadLettered, result.FinalStatus);
            Assert.Equal("out1", result.OutputName);
        }

        [Fact]
        public void CreateSkippedResult_ReturnsSkipped()
        {
            var msg = new Message { Topic = "test" };
            var result = PipelineSendStrategy.CreateSkippedResult("t1", "out1", msg, "not registered");

            Assert.Equal(GatewaySendFinalStatus.Skipped, result.FinalStatus);
            Assert.Equal("out1", result.OutputName);
        }
    }

    public class GatewayErrorCatalogCoverageTests
    {
        [Fact]
        public void FromException_WithNullException_ReturnsInfo()
        {
            var result = GatewayErrorCatalog.FromException(null!, "trace1");
            Assert.NotNull(result);
        }

        [Fact]
        public void FromException_WithTimeoutException_ReturnsTimeoutCategory()
        {
            var result = GatewayErrorCatalog.FromException(new TimeoutException(), "trace2");
            Assert.NotNull(result);
            Assert.Equal("Timeout", result.Category);
        }

        [Fact]
        public void FromException_WithOperationCanceledException_ReturnsTimeoutCategory()
        {
            var result = GatewayErrorCatalog.FromException(new TaskCanceledException(), "trace3");
            Assert.NotNull(result);
            Assert.Equal("Timeout", result.Category);
        }

        [Fact]
        public void FromException_WithSocketException_ReturnsConnectionCategory()
        {
            var result = GatewayErrorCatalog.FromException(new System.Net.Sockets.SocketException(), "trace4");
            Assert.NotNull(result);
            Assert.Equal("Connection", result.Category);
        }

        [Fact]
        public void FromException_WithUnknownException_ReturnsInternalCategory()
        {
            var result = GatewayErrorCatalog.FromException(new StackOverflowException(), "trace5");
            Assert.NotNull(result);
            Assert.Equal("Internal", result.Category);
        }

        [Fact]
        public void Describe_KnownCodes_ReturnsCorrectDescriptor()
        {
            var descriptor = GatewayErrorCatalog.Describe(GatewayErrorCodes.Timeout);
            Assert.Equal(GatewayErrorCodes.Timeout, descriptor.Code);
            Assert.Equal("Timeout", descriptor.Category);
        }

        [Fact]
        public void Describe_UnknownCode_ReturnsInternal()
        {
            var descriptor = GatewayErrorCatalog.Describe("UNKNOWN-CODE");
            Assert.Equal(GatewayErrorCodes.InternalException, descriptor.Code);
            Assert.Equal("Internal", descriptor.Category);
        }

        [Fact]
        public void GetCategory_NullCode_ReturnsInternal()
        {
            var category = GatewayErrorCatalog.GetCategory(null);
            Assert.Equal("Internal", category);
        }
    }

    // Minimal test output for GatewaySendResult tests
    public class TestOutputForSendResult : IOutputPlugin
    {
        public string Name { get; }
        public string ProtocolType { get; }
        public string Version => "1.0.0";
        public PluginStatus Status { get; } = PluginStatus.Stopped;
        public event Action<string, bool>? ConnectionChanged;
        public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

        public TestOutputForSendResult(string name)
        {
            Name = name;
            ProtocolType = "Test";
        }

        public Task SendAsync(Message message, CancellationToken ct = default) => Task.CompletedTask;
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
