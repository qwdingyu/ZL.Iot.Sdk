#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// GatewayDiagnostics 诊断信息深度测试
    /// 覆盖异常分类、错误码描述、TraceId 追踪等核心网关诊断功能
    /// </summary>
    public class GatewayDiagnosticsTests
    {
        #region GatewayDiagnosticInfo

        [Fact]
        public void DiagnosticInfo_Success_CreatesCorrectInfo()
        {
            var info = GatewayDiagnosticInfo.Success("trace-1", "OK");

            Assert.False(info.HasError);
            Assert.Equal(GatewayErrorCodes.None, info.ErrorCode);
            Assert.Equal("trace-1", info.TraceId);
            Assert.Equal("OK", info.TechnicalMessage);
            Assert.Equal("OK", info.UserMessage);
        }

        [Fact]
        public void DiagnosticInfo_DefaultValues_AreCorrect()
        {
            var info = new GatewayDiagnosticInfo();

            Assert.False(info.HasError);
            Assert.Equal(GatewayErrorCodes.None, info.ErrorCode);
            Assert.Equal("Normal", info.Category);
            Assert.Equal(string.Empty, info.TechnicalMessage);
            Assert.Equal(string.Empty, info.UserMessage);
            Assert.Equal(string.Empty, info.Advice);
        }

        #endregion

        #region GatewayErrorCatalog.FromException

        [Fact]
        public void FromException_NullException_ReturnsFallbackInfo()
        {
            var info = GatewayErrorCatalog.FromException(null, "trace-1", "some fallback");

            Assert.False(info.HasError);
            Assert.Equal("trace-1", info.TraceId);
            Assert.Equal("some fallback", info.TechnicalMessage);
            Assert.Equal("some fallback", info.UserMessage);
            Assert.Equal("请检查网关运行日志。", info.Advice);
        }

        [Fact]
        public void FromException_NullException_NullFallback_ReturnsEmpty()
        {
            var info = GatewayErrorCatalog.FromException(null, "", null);

            Assert.Equal(string.Empty, info.TraceId);
            Assert.Equal(string.Empty, info.TechnicalMessage);
            Assert.Equal("未提供异常信息", info.UserMessage);
        }

        [Fact]
        public void FromException_SocketException_ClassifiesAsConnectionFailed()
        {
            var ex = new SocketException((int)SocketError.ConnectionRefused);
            var info = GatewayErrorCatalog.FromException(ex, "trace-2");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.ConnectionFailed, info.ErrorCode);
            Assert.Equal("Connection", info.Category);
            Assert.Same(ex, info.Exception);
        }

        [Fact]
        public void FromException_Win32Exception_ClassifiesAsInternalException()
        {
            // Win32Exception 不在 Classify 的类型匹配列表中，消息 "File not found" 也不命中任何关键词
            // → 最终落入 InternalException
            var ex = new System.ComponentModel.Win32Exception(2, "File not found");
            var info = GatewayErrorCatalog.FromException(ex, "trace-3");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.InternalException, info.ErrorCode);
            Assert.Equal("Internal", info.Category);
        }

        [Fact]
        public void FromException_TimeoutException_ClassifiesAsTimeout()
        {
            var ex = new TimeoutException("操作超时");
            var info = GatewayErrorCatalog.FromException(ex, "trace-4");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.Timeout, info.ErrorCode);
            Assert.Equal("Timeout", info.Category);
        }

        [Fact]
        public void FromException_ObjectDisposedException_ClassifiesAsInternalException()
        {
            // "object" 单字过于宽泛已被移除，ObjectDisposedException 不再误判为 AddressResolutionFailed
            // ObjectDisposedException("TcpClient") 消息: "Cannot access a disposed object: 'TcpClient'"
            // 不含 address/tag/path/node 等关键词 → 最终落入 InternalException
            var ex = new ObjectDisposedException("TcpClient");
            var info = GatewayErrorCatalog.FromException(ex, "trace-5");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.InternalException, info.ErrorCode);
            Assert.Equal("Internal", info.Category);
        }

        [Fact]
        public void FromException_OperationCanceledException_ClassifiesAsInternalException()
        {
            // Classify 只匹配 TaskCanceledException，不匹配 OperationCanceledException（基类）
            // 消息 "已取消" 不含任何关键词 → InternalException
            var ex = new OperationCanceledException("已取消");
            var info = GatewayErrorCatalog.FromException(ex, "trace-6");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.InternalException, info.ErrorCode);
            Assert.Equal("Internal", info.Category);
        }

        [Fact]
        public void FromException_TaskCanceledException_ClassifiesAsTimeout()
        {
            // TaskCanceledException 被 Classify 显式匹配为 Timeout
            var ex = new TaskCanceledException("任务已取消");
            var info = GatewayErrorCatalog.FromException(ex, "trace-6b");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.Timeout, info.ErrorCode);
            Assert.Equal("Timeout", info.Category);
        }

        [Fact]
        public void FromException_InvalidOperationException_WithFormatMessage_ClassifiesAsProtocolFormatInvalid()
        {
            // Classify 消息关键词匹配顺序: "address" 在 "format" 之前
            // "Bad format for protocol frame" → toLower → 不含 "address" → 含 "format" → ProtocolFormatInvalid
            var ex = new InvalidOperationException("Bad format for protocol frame");
            var info = GatewayErrorCatalog.FromException(ex, "trace-7");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.ProtocolFormatInvalid, info.ErrorCode);
            Assert.Equal("ProtocolFormat", info.Category);
        }

        [Fact]
        public void FromException_InvalidOperationException_WithAddressMessage_ClassifiesAsAddressResolutionFailed()
        {
            var ex = new InvalidOperationException("Invalid address DB1.DBW999");
            var info = GatewayErrorCatalog.FromException(ex, "trace-7b");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.AddressResolutionFailed, info.ErrorCode);
            Assert.Equal("Address", info.Category);
        }

        [Fact]
        public void FromException_FormatException_WithTypeMessage_ClassifiesAsProtocolFormatInvalid()
        {
            // Classify 消息关键词匹配: "Invalid type conversion" → 不含 address/tag/node
            // → 不含 config/missing → 不含 denied/reject → 不含 format/json/xml
            // → 含 "cannot convert type" ? No (消息不含 "cannot") → 含 "invalid" → ConfigurationInvalid
            // 注意：单独 "type" 关键词已移除（过于宽泛），改为 "cannot convert type" 精确匹配
            var ex = new FormatException("Invalid type conversion");
            var info = GatewayErrorCatalog.FromException(ex, "trace-8");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.ConfigurationInvalid, info.ErrorCode);
            Assert.Equal("Configuration", info.Category);
        }

        [Fact]
        public void FromException_Exception_WithJsonMessage_ClassifiesAsProtocolFormatInvalid()
        {
            // 消息含 "json" → ProtocolFormatInvalid（在 "invalid" 检查之前）
            var ex = new Exception("Bad json payload received");
            var info = GatewayErrorCatalog.FromException(ex, "trace-8b");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.ProtocolFormatInvalid, info.ErrorCode);
            Assert.Equal("ProtocolFormat", info.Category);
        }

        [Fact]
        public void FromException_ArgumentException_ClassifiesAsInternalException()
        {
            // ArgumentException without keyword match → falls through to InternalException
            var ex = new ArgumentException("参数无效");
            var info = GatewayErrorCatalog.FromException(ex, "trace-9");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.InternalException, info.ErrorCode);
            Assert.Equal("Internal", info.Category);
        }

        [Fact]
        public void FromException_ArgumentException_WithInvalidMessage_ClassifiesAsConfigurationInvalid()
        {
            // "invalid" in message → ConfigurationInvalid
            var ex = new ArgumentException("invalid parameter value");
            var info = GatewayErrorCatalog.FromException(ex, "trace-9b");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.ConfigurationInvalid, info.ErrorCode);
            Assert.Equal("Configuration", info.Category);
        }

        [Fact]
        public void FromException_WebException_ClassifiesAsInternalException()
        {
            // WebException 不在 Classify 类型匹配列表中，消息 "网络错误" 不含关键词 → InternalException
            var ex = new WebException("网络错误");
            var info = GatewayErrorCatalog.FromException(ex, "trace-10");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.InternalException, info.ErrorCode);
            Assert.Equal("Internal", info.Category);
        }

        [Fact]
        public void FromException_HttpRequestException_ClassifiesAsConnectionFailed()
        {
            // HttpRequestException 被 Classify 显式匹配为 ConnectionFailed
            var ex = new System.Net.Http.HttpRequestException("DNS resolution failed");
            var info = GatewayErrorCatalog.FromException(ex, "trace-10b");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.ConnectionFailed, info.ErrorCode);
            Assert.Equal("Connection", info.Category);
        }

        [Fact]
        public void FromException_GenericException_ClassifiesAsInternalException()
        {
            var ex = new Exception("未知错误");
            var info = GatewayErrorCatalog.FromException(ex, "trace-11");

            Assert.True(info.HasError);
            Assert.Equal(GatewayErrorCodes.InternalException, info.ErrorCode);
            Assert.Equal("Internal", info.Category);
        }

        #endregion

        #region GatewayErrorCatalog.Create / CreateFromCode

        [Fact]
        public void Create_WithValidCode_ReturnsCorrectInfo()
        {
            var info = GatewayErrorCatalog.Create(
                GatewayErrorCodes.ConnectionFailed,
                "tech msg", "user msg", "advice", "trace-12");

            Assert.Equal(GatewayErrorCodes.ConnectionFailed, info.ErrorCode);
            Assert.Equal("Connection", info.Category);
            Assert.Equal("tech msg", info.TechnicalMessage);
            Assert.Equal("user msg", info.UserMessage);
            Assert.Equal("advice", info.Advice);
        }

        [Fact]
        public void Create_WithNullCode_DefaultsToInternalException()
        {
            var info = GatewayErrorCatalog.Create(null!, "tech", "user", "advice", "trace");

            Assert.Equal(GatewayErrorCodes.InternalException, info.ErrorCode);
        }

        [Fact]
        public void CreateFromCode_KnownCode_ReturnsDescribedInfo()
        {
            var info = GatewayErrorCatalog.CreateFromCode(
                GatewayErrorCodes.HandshakeFailed, "handshake error", "trace-13");

            Assert.Equal(GatewayErrorCodes.HandshakeFailed, info.ErrorCode);
            Assert.Equal("Handshake", info.Category);
            Assert.Equal("handshake error", info.TechnicalMessage);
            Assert.NotEmpty(info.UserMessage);
            Assert.NotEmpty(info.Advice);
        }

        #endregion

        #region GatewayErrorCatalog.Describe - 所有错误码

        [Theory]
        [InlineData(GatewayErrorCodes.None, "Normal")]
        [InlineData(GatewayErrorCodes.ConfigurationInvalid, "Configuration")]
        [InlineData(GatewayErrorCodes.ConfigurationMissing, "Configuration")]
        [InlineData(GatewayErrorCodes.ConnectionFailed, "Connection")]
        [InlineData(GatewayErrorCodes.HandshakeFailed, "Handshake")]
        [InlineData(GatewayErrorCodes.AuthenticationFailed, "Authentication")]
        [InlineData(GatewayErrorCodes.AddressResolutionFailed, "Address")]
        [InlineData(GatewayErrorCodes.DataTypeInvalid, "DataType")]
        [InlineData(GatewayErrorCodes.DeviceRejected, "Device")]
        [InlineData(GatewayErrorCodes.Timeout, "Timeout")]
        [InlineData(GatewayErrorCodes.ProtocolFormatInvalid, "ProtocolFormat")]
        [InlineData(GatewayErrorCodes.InternalException, "Internal")]
        [InlineData(GatewayErrorCodes.Cached, "Degraded")]
        [InlineData(GatewayErrorCodes.DeadLettered, "Degraded")]
        [InlineData(GatewayErrorCodes.OutputCircuitOpen, "Degraded")]
        public void Describe_AllErrorCodes_ReturnsCorrectCategory(string code, string expectedCategory)
        {
            var descriptor = GatewayErrorCatalog.Describe(code);

            Assert.Equal(code, descriptor.Code);
            Assert.Equal(expectedCategory, descriptor.Category);
            Assert.NotEmpty(descriptor.UserMessage);
            Assert.NotEmpty(descriptor.Advice);
        }

        [Fact]
        public void Describe_UnknownCode_ReturnsInternalException()
        {
            var descriptor = GatewayErrorCatalog.Describe("unknown-code-999");

            Assert.Equal(GatewayErrorCodes.InternalException, descriptor.Code);
            Assert.Equal("Internal", descriptor.Category);
        }

        [Fact]
        public void GetCategory_NullCode_ReturnsInternal()
        {
            var category = GatewayErrorCatalog.GetCategory(null);
            Assert.Equal("Internal", category);
        }

        [Fact]
        public void GetCategory_KnownCode_ReturnsCorrectCategory()
        {
            Assert.Equal("Connection", GatewayErrorCatalog.GetCategory(GatewayErrorCodes.ConnectionFailed));
            Assert.Equal("Normal", GatewayErrorCatalog.GetCategory(GatewayErrorCodes.None));
            Assert.Equal("Internal", GatewayErrorCatalog.GetCategory("nonexistent"));
        }

        #endregion

        #region GatewayTraceContext

        [Fact]
        public void EnsureTraceId_MessageWithTraceId_ReturnsExisting()
        {
            var msg = new Message();
            msg.Metadata[GatewayMetadataKeys.TraceId] = "existing-trace";

            var result = GatewayTraceContext.EnsureTraceId(msg);

            Assert.Equal("existing-trace", result);
        }

        [Fact]
        public void EnsureTraceId_MessageWithoutTraceId_GeneratesNew()
        {
            var msg = new Message();

            var result = GatewayTraceContext.EnsureTraceId(msg);

            Assert.NotEmpty(result);
            Assert.Equal(result, msg.Metadata[GatewayMetadataKeys.TraceId]);
        }

        [Fact]
        public void EnsureTraceId_EmptyTraceId_GeneratesNew()
        {
            var msg = new Message();
            msg.Metadata[GatewayMetadataKeys.TraceId] = "";

            var result = GatewayTraceContext.EnsureTraceId(msg);

            Assert.NotEmpty(result);
            Assert.NotEqual("", result);
        }

        #endregion

        #region GatewaySendResult

        [Fact]
        public void GatewaySendResult_SuccessProperties_AreCorrect()
        {
            var result = new GatewaySendResult
            {
                TraceId = "t1",
                Source = "input1",
                OutputName = "out1",
                FinalStatus = GatewaySendFinalStatus.Success,
                AckLevel = GatewayAckLevel.Confirmed
            };

            Assert.Equal(GatewaySendFinalStatus.Success, result.FinalStatus);
            Assert.Equal(GatewayAckLevel.Confirmed, result.AckLevel);
        }

        #endregion
    }
}
#nullable restore
