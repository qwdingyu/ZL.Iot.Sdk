// ============================================================
// 文件：IndustrialOutputPluginConfigTests.cs
// 描述：工业输出插件配置校验测试 — AllenBradley / BacnetIp / IEC61850 / OpcUa / MitsubishiMc
// 修改日期：2026-06-03
// ============================================================

using System;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// AllenBradleyOutputConfig 配置校验测试
    /// </summary>
    public class AllenBradleyOutputConfigTests
    {
        [Fact]
        public void Validate_DefaultValues_Passes()
        {
            var config = new ProtocolGateway.Plugins.AllenBradleyOutputConfig();
            var errors = config.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_InvalidIp_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.AllenBradleyOutputConfig { ServerIp = "not-an-ip" };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ServerIp", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidPort_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.AllenBradleyOutputConfig { Port = 0 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("Port", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_SlotOutOfRange_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.AllenBradleyOutputConfig { Slot = 256 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("Slot", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_SlotNegative_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.AllenBradleyOutputConfig { Slot = -1 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("Slot", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_SlotBoundaryValues_Pass()
        {
            var config0 = new ProtocolGateway.Plugins.AllenBradleyOutputConfig { Slot = 0 };
            Assert.Empty(config0.Validate());

            var config255 = new ProtocolGateway.Plugins.AllenBradleyOutputConfig { Slot = 255 };
            Assert.Empty(config255.Validate());
        }

        [Fact]
        public void Validate_AllInvalid_ReturnsAllErrors()
        {
            var config = new ProtocolGateway.Plugins.AllenBradleyOutputConfig
            {
                ServerIp = "",
                Port = 0,
                Slot = 300,
                ConnectTimeoutMs = 0,
                SendTimeoutMs = 0,
                ReconnectIntervalMs = 0,
                ErrorThreshold = 0
            };
            var errors = config.Validate();
            Assert.Equal(7, errors.Count);
        }
    }

    /// <summary>
    /// BacnetIpOutputConfig 配置校验测试
    /// </summary>
    public class BacnetIpOutputConfigTests
    {
        [Fact]
        public void Validate_DefaultValues_Passes()
        {
            var config = new ProtocolGateway.Plugins.BacnetIpOutputConfig();
            var errors = config.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_InvalidIp_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.BacnetIpOutputConfig { ServerIp = "bad" };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ServerIp", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidPort_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.BacnetIpOutputConfig { Port = 70000 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("Port", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_ApduRetriesZero_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.BacnetIpOutputConfig { ApduRetries = 0 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ApduRetries", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_ApduRetriesNegative_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.BacnetIpOutputConfig { ApduRetries = -1 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ApduRetries", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_ApduRetriesOne_Passes()
        {
            var config = new ProtocolGateway.Plugins.BacnetIpOutputConfig { ApduRetries = 1 };
            Assert.Empty(config.Validate());
        }

        [Fact]
        public void Validate_AllInvalid_ReturnsAllErrors()
        {
            var config = new ProtocolGateway.Plugins.BacnetIpOutputConfig
            {
                ServerIp = null!,
                Port = -1,
                ApduTimeoutMs = 0,
                ApduRetries = 0,
                ReconnectIntervalMs = 0,
                ErrorThreshold = -5
            };
            var errors = config.Validate();
            Assert.Equal(6, errors.Count);
        }
    }

    /// <summary>
    /// IEC61850MmsOutputConfig 配置校验测试
    /// </summary>
    public class IEC61850MmsOutputConfigTests
    {
        [Fact]
        public void Validate_DefaultValues_Passes()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig();
            var errors = config.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_InvalidIp_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig { ServerIp = "invalid" };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ServerIp", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_EmptyLogicalDevice_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig { LogicalDevice = "" };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("LogicalDevice", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_NullLogicalDevice_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig { LogicalDevice = null };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("LogicalDevice", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_EmptyLogicalNode_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig { LogicalNode = "   " };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("LogicalNode", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_EmptyDataAttribute_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig { DataAttribute = "" };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("DataAttribute", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_ConnectTimeoutMinIs1000()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig { ConnectTimeoutMs = 500 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ConnectTimeoutMs", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_ApduTimeoutMinIs1000()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig { ApduTimeoutMs = 100 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ApduTimeoutMs", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_AllInvalid_ReturnsAllErrors()
        {
            var config = new ProtocolGateway.Plugins.IEC61850MmsOutputConfig
            {
                ServerIp = "",
                Port = 0,
                LogicalDevice = "",
                LogicalNode = "",
                DataAttribute = "",
                ConnectTimeoutMs = 0,
                ApduTimeoutMs = 0,
                ReconnectIntervalMs = 0,
                ErrorThreshold = 0
            };
            var errors = config.Validate();
            Assert.Equal(9, errors.Count);
        }
    }

    /// <summary>
    /// OpcUaOutputConfig 配置校验测试
    /// </summary>
    public class OpcUaOutputConfigTests
    {
        [Fact]
        public void Validate_DefaultValues_Passes()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig();
            var errors = config.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_EmptyServerUrl_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig { ServerUrl = "" };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ServerUrl", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_NullServerUrl_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig { ServerUrl = null };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ServerUrl", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidConnectTimeout_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig { ConnectTimeoutMs = 50 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ConnectTimeoutMs", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidSendTimeout_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig { SendTimeoutMs = 0 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("SendTimeoutMs", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidReconnectInterval_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig { ReconnectIntervalMs = 100 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ReconnectIntervalMs", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_AllInvalid_ReturnsAllErrors()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig
            {
                ServerUrl = "",
                ConnectTimeoutMs = 0,
                SendTimeoutMs = 0,
                ReconnectIntervalMs = 0,
                ErrorThreshold = 0
            };
            var errors = config.Validate();
            Assert.Equal(5, errors.Count);
        }

        [Fact]
        public void Validate_ValidOpcTcpUrl_Passes()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig { ServerUrl = "opc.tcp://192.168.1.100:4840" };
            Assert.Empty(config.Validate());
        }

        [Fact]
        public void Validate_ValidHttpsUrl_Passes()
        {
            var config = new ProtocolGateway.Plugins.OpcUaOutputConfig { ServerUrl = "https://localhost:4840" };
            Assert.Empty(config.Validate());
        }
    }

    /// <summary>
    /// MitsubishiMcOutputConfig 配置校验测试
    /// </summary>
    public class MitsubishiMcOutputConfigTests
    {
        [Fact]
        public void Validate_DefaultValues_Passes()
        {
            var config = new ProtocolGateway.Plugins.MitsubishiMcOutputConfig();
            var errors = config.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_InvalidIp_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.MitsubishiMcOutputConfig { ServerIp = "not-ip" };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ServerIp", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidPort_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.MitsubishiMcOutputConfig { Port = 0 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("Port", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidConnectTimeout_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.MitsubishiMcOutputConfig { ConnectTimeoutMs = 50 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ConnectTimeoutMs", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidSendTimeout_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.MitsubishiMcOutputConfig { SendTimeoutMs = 99 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("SendTimeoutMs", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_InvalidReconnectInterval_ReturnsError()
        {
            var config = new ProtocolGateway.Plugins.MitsubishiMcOutputConfig { ReconnectIntervalMs = 100 };
            var errors = config.Validate();
            Assert.Single(errors);
            Assert.Equal("ReconnectIntervalMs", errors[0].PropertyName);
        }

        [Fact]
        public void Validate_AllInvalid_ReturnsAllErrors()
        {
            var config = new ProtocolGateway.Plugins.MitsubishiMcOutputConfig
            {
                ServerIp = "",
                Port = -1,
                ConnectTimeoutMs = 0,
                SendTimeoutMs = 0,
                ReconnectIntervalMs = 0,
                ErrorThreshold = 0
            };
            var errors = config.Validate();
            Assert.Equal(6, errors.Count);
        }

        [Fact]
        public void Validate_CustomName_DoesNotAffectValidation()
        {
            var config = new ProtocolGateway.Plugins.MitsubishiMcOutputConfig { Name = "MyPLC" };
            Assert.Empty(config.Validate());
        }
    }
}
