using System;
using System.IO;
using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Tests.Scenarios;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// GatewayLog 单元测试
    /// 测试日志级别过滤和日志输出功能
    /// 与 TcpForwardingScenarioTests 共用串行集合，避免静态 _customWriter 冲突
    /// </summary>
    [Collection(TcpScenarioTestCollection.Name)]
    public class GatewayLogTests : IDisposable
    {
        private readonly StringWriter _outputCapture;
        private readonly GatewayLog.LogLevel _originalLevel;

        public GatewayLogTests()
        {
            _outputCapture = new StringWriter();
            _originalLevel = GatewayLog.LogLevel.Info; // 默认值
            GatewayLog.SetOutput(_outputCapture);
        }

        public void Dispose()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.ResetOutput();
            _outputCapture.Dispose();
        }

        [Fact]
        public void SetMinLevel_ShouldSetLogLevel()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Error);
            // 如果设置为 Error，Debug 和 Info 不应该输出
            GatewayLog.Debug("TestArea", "Debug message");
            var output = _outputCapture.ToString();
            Assert.DoesNotContain("Debug message", output);
        }

        [Fact]
        public void Debug_WhenLevelIsDebug_ShouldOutput()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Debug);
            GatewayLog.Debug("TestArea", "Debug message");
            var output = _outputCapture.ToString();
            Assert.Contains("DEBUG", output);
            Assert.Contains("TestArea", output);
            Assert.Contains("Debug message", output);
        }

        [Fact]
        public void Debug_WhenLevelIsInfo_ShouldNotOutput()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.Debug("TestArea", "Debug message");
            var output = _outputCapture.ToString();
            Assert.DoesNotContain("Debug message", output);
        }

        [Fact]
        public void Info_WhenLevelIsInfo_ShouldOutput()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.Info("TestArea", "Info message");
            var output = _outputCapture.ToString();
            Assert.Contains("INFO", output);
            Assert.Contains("TestArea", output);
            Assert.Contains("Info message", output);
        }

        [Fact]
        public void Info_WhenLevelIsWarn_ShouldNotOutput()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Warn);
            GatewayLog.Info("TestArea", "Info message");
            var output = _outputCapture.ToString();
            Assert.DoesNotContain("Info message", output);
        }

        [Fact]
        public void Warn_WhenLevelIsWarn_ShouldOutput()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Warn);
            GatewayLog.Warn("TestArea", "Warn message");
            var output = _outputCapture.ToString();
            Assert.Contains("WARN", output);
            Assert.Contains("TestArea", output);
            Assert.Contains("Warn message", output);
        }

        [Fact]
        public void Warn_WithException_ShouldOutputException()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Warn);
            var exception = new InvalidOperationException("Test exception");
            GatewayLog.Warn("TestArea", "Warn message", exception);
            var output = _outputCapture.ToString();
            Assert.Contains("WARN", output);
            Assert.Contains("Test exception", output);
        }

        [Fact]
        public void Warn_WhenLevelIsError_ShouldNotOutput()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Error);
            GatewayLog.Warn("TestArea", "Warn message");
            var output = _outputCapture.ToString();
            Assert.DoesNotContain("Warn message", output);
        }

        [Fact]
        public void Error_WhenLevelIsError_ShouldOutput()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Error);
            GatewayLog.Error("TestArea", "Error message");
            var output = _outputCapture.ToString();
            Assert.Contains("ERROR", output);
            Assert.Contains("TestArea", output);
            Assert.Contains("Error message", output);
        }

        [Fact]
        public void Error_WithException_ShouldOutputException()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Error);
            var exception = new Exception("Test exception");
            GatewayLog.Error("TestArea", "Error message", exception);
            var output = _outputCapture.ToString();
            Assert.Contains("ERROR", output);
            Assert.Contains("Test exception", output);
        }

        [Fact]
        public void LogOutput_ShouldIncludeTimestamp()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.Info("TestArea", "Test message");
            var output = _outputCapture.ToString();
            // 时间戳格式：[2024-01-01T12:00:00.0000000+00:00]
            Assert.Matches(@"\[\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", output);
        }

        [Fact]
        public void LogOutput_WithEmptyArea_ShouldUseDefault()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.Info("", "Test message");
            var output = _outputCapture.ToString();
            Assert.Contains("[Gateway]", output);
        }

        [Fact]
        public void LogOutput_WithWhitespaceArea_ShouldUseDefault()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.Info("   ", "Test message");
            var output = _outputCapture.ToString();
            Assert.Contains("[Gateway]", output);
        }

        [Fact]
        public void LogOutput_WithEmptyMessage_ShouldOutputEmpty()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.Info("TestArea", "");
            var output = _outputCapture.ToString();
            Assert.Contains("[TestArea]", output);
        }

        [Fact]
        public void LogOutput_WithWhitespaceMessage_ShouldTrim()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.Info("TestArea", "  Test message  ");
            var output = _outputCapture.ToString();
            Assert.Contains("Test message", output);
            Assert.DoesNotContain("  Test message  ", output);
        }

        [Fact]
        public void LogLevel_EnumValues_ShouldBeCorrect()
        {
            Assert.Equal(0, (int)GatewayLog.LogLevel.Debug);
            Assert.Equal(1, (int)GatewayLog.LogLevel.Info);
            Assert.Equal(2, (int)GatewayLog.LogLevel.Warn);
            Assert.Equal(3, (int)GatewayLog.LogLevel.Error);
        }

        [Fact]
        public void AllLevels_WhenLevelIsDebug_ShouldAllOutput()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Debug);
            GatewayLog.Debug("Area", "Debug");
            GatewayLog.Info("Area", "Info");
            GatewayLog.Warn("Area", "Warn");
            GatewayLog.Error("Area", "Error");
            var output = _outputCapture.ToString();
            Assert.Contains("Debug", output);
            Assert.Contains("Info", output);
            Assert.Contains("Warn", output);
            Assert.Contains("Error", output);
        }

        [Fact]
        public void LogLevelFilter_WhenSetToError_ShouldOnlyOutputError()
        {
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Error);
            GatewayLog.Debug("Area", "Debug");
            GatewayLog.Info("Area", "Info");
            GatewayLog.Warn("Area", "Warn");
            GatewayLog.Error("Area", "Error");
            var output = _outputCapture.ToString();
            Assert.DoesNotContain("Debug", output);
            Assert.DoesNotContain("Info", output);
            Assert.DoesNotContain("Warn", output);
            Assert.Contains("Error", output);
        }
    }
}
