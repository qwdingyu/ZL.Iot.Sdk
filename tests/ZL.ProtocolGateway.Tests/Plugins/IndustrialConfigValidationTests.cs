#nullable enable
using System.Collections.Generic;
using System.Linq;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins;

/// <summary>
/// 工业插件配置验证测试 — 所有 Validate() 方法均为纯函数，零基础设施依赖
/// </summary>
public class IndustrialConfigValidationTests
{
    #region AllenBradley 配置验证

    [Fact]
    public void AllenBradley_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new AllenBradleyOutputConfig
        {
            ServerIp = "192.168.1.10",
            Port = 44818,
            Slot = 0,
            ConnectTimeoutMs = 5000,
            SendTimeoutMs = 3000,
            ReconnectIntervalMs = 3000,
            ErrorThreshold = 10
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void AllenBradley_Validate_InvalidIp_ReturnsError()
    {
        var cfg = new AllenBradleyOutputConfig { ServerIp = "not-an-ip" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ServerIp");
    }

    [Fact]
    public void AllenBradley_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new AllenBradleyOutputConfig { Port = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    [Fact]
    public void AllenBradley_Validate_InvalidSlot_ReturnsError()
    {
        var cfg = new AllenBradleyOutputConfig { Slot = 300 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Slot");
    }

    [Fact]
    public void AllenBradley_Validate_InvalidTimeout_ReturnsError()
    {
        var cfg = new AllenBradleyOutputConfig { ConnectTimeoutMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ConnectTimeoutMs");
    }

    [Fact]
    public void AllenBradley_Validate_InvalidErrorThreshold_ReturnsError()
    {
        var cfg = new AllenBradleyOutputConfig { ErrorThreshold = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ErrorThreshold");
    }

    [Fact]
    public void AllenBradley_Validate_MultipleErrors_ReturnsAll()
    {
        var cfg = new AllenBradleyOutputConfig
        {
            ServerIp = "",
            Port = 99999,
            Slot = -1,
            ConnectTimeoutMs = 0,
            SendTimeoutMs = 0,
            ReconnectIntervalMs = 0,
            ErrorThreshold = -1
        };
        var errors = cfg.Validate();
        Assert.True(errors.Count >= 5, $"Expected >=5 errors, got {errors.Count}");
    }

    [Fact]
    public void AllenBradley_Constructor_ValidConfig_SetsName()
    {
        var cfg = new AllenBradleyOutputConfig
        {
            ServerIp = "10.0.0.1", Port = 44818, Name = "MyAB"
        };
        var plugin = new AllenBradleyOutputPlugin(cfg);
        Assert.Equal("MyAB", plugin.Name);
        Assert.Equal("AllenBradley", plugin.ProtocolType);
    }

    [Fact]
    public void AllenBradley_Constructor_DefaultName_UsesFormat()
    {
        var cfg = new AllenBradleyOutputConfig
        {
            ServerIp = "10.0.0.1", Port = 44818
        };
        var plugin = new AllenBradleyOutputPlugin(cfg);
        Assert.Equal("AB-10.0.0.1:44818", plugin.Name);
    }

    #endregion

    #region BacnetIp 配置验证

    [Fact]
    public void BacnetIp_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new BacnetIpOutputConfig
        {
            ServerIp = "192.168.1.100",
            Port = 47808,
            ApduTimeoutMs = 3000,
            ApduRetries = 3,
            ReconnectIntervalMs = 5000,
            ErrorThreshold = 10
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void BacnetIp_Validate_InvalidIp_ReturnsError()
    {
        var cfg = new BacnetIpOutputConfig { ServerIp = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ServerIp");
    }

    [Fact]
    public void BacnetIp_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new BacnetIpOutputConfig { Port = -1 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    [Fact]
    public void BacnetIp_Validate_InvalidApduTimeout_ReturnsError()
    {
        var cfg = new BacnetIpOutputConfig { ApduTimeoutMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ApduTimeoutMs");
    }

    [Fact]
    public void BacnetIp_Validate_InvalidApduRetries_ReturnsError()
    {
        var cfg = new BacnetIpOutputConfig { ApduRetries = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ApduRetries");
    }

    [Fact]
    public void BacnetIp_Validate_InvalidReconnectInterval_ReturnsError()
    {
        var cfg = new BacnetIpOutputConfig { ReconnectIntervalMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ReconnectIntervalMs");
    }

    [Fact]
    public void BacnetIp_Validate_InvalidErrorThreshold_ReturnsError()
    {
        var cfg = new BacnetIpOutputConfig { ErrorThreshold = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ErrorThreshold");
    }

    #endregion

    #region IEC61850 MMS 配置验证

    [Fact]
    public void IEC61850_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new IEC61850MmsOutputConfig
        {
            ServerIp = "10.0.0.1",
            Port = 102,
            LogicalDevice = "IED1",
            LogicalNode = "MMXU1",
            DataAttribute = "Mag.f",
            ConnectTimeoutMs = 15000,
            ApduTimeoutMs = 10000,
            ReconnectIntervalMs = 10000,
            ErrorThreshold = 10
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void IEC61850_Validate_InvalidIp_ReturnsError()
    {
        var cfg = new IEC61850MmsOutputConfig { ServerIp = "bad" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ServerIp");
    }

    [Fact]
    public void IEC61850_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new IEC61850MmsOutputConfig { Port = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    [Fact]
    public void IEC61850_Validate_EmptyLogicalDevice_ReturnsError()
    {
        var cfg = new IEC61850MmsOutputConfig { LogicalDevice = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "LogicalDevice");
    }

    [Fact]
    public void IEC61850_Validate_EmptyLogicalNode_ReturnsError()
    {
        var cfg = new IEC61850MmsOutputConfig { LogicalNode = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "LogicalNode");
    }

    [Fact]
    public void IEC61850_Validate_EmptyDataAttribute_ReturnsError()
    {
        var cfg = new IEC61850MmsOutputConfig { DataAttribute = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "DataAttribute");
    }

    #endregion

    #region Mitsubishi MC 配置验证

    [Fact]
    public void MitsubishiMc_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new MitsubishiMcOutputConfig
        {
            ServerIp = "192.168.1.10",
            Port = 5000,
            ConnectTimeoutMs = 5000,
            SendTimeoutMs = 3000,
            ReconnectIntervalMs = 3000,
            ErrorThreshold = 10
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void MitsubishiMc_Validate_InvalidIp_ReturnsError()
    {
        var cfg = new MitsubishiMcOutputConfig { ServerIp = "..." };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ServerIp");
    }

    [Fact]
    public void MitsubishiMc_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new MitsubishiMcOutputConfig { Port = -100 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    [Fact]
    public void MitsubishiMc_Validate_InvalidSendTimeout_ReturnsError()
    {
        var cfg = new MitsubishiMcOutputConfig { SendTimeoutMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "SendTimeoutMs");
    }

    [Fact]
    public void MitsubishiMc_Validate_InvalidReconnectInterval_ReturnsError()
    {
        var cfg = new MitsubishiMcOutputConfig { ReconnectIntervalMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ReconnectIntervalMs");
    }

    #endregion

    #region Modbus TCP 配置验证

    [Fact]
    public void ModbusTcp_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new ModbusTcpOutputConfig
        {
            ServerIp = "10.0.0.100",
            Port = 502,
            TimeoutMs = 3000,
            UnitId = 1
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void ModbusTcp_Validate_InvalidIp_ReturnsError()
    {
        var cfg = new ModbusTcpOutputConfig { ServerIp = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ServerIp");
    }

    [Fact]
    public void ModbusTcp_Validate_InvalidPort_ReturnsError()
    {
        var cfg = new ModbusTcpOutputConfig { Port = 70000 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Port");
    }

    [Fact]
    public void ModbusTcp_Validate_InvalidTimeout_ReturnsError()
    {
        var cfg = new ModbusTcpOutputConfig { TimeoutMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "TimeoutMs");
    }

    [Fact]
    public void ModbusTcp_Constructor_ValidConfig_SetsName()
    {
        var cfg = new ModbusTcpOutputConfig { ServerIp = "10.0.0.1", Port = 502 };
        var plugin = new ModbusTcpOutputPlugin(cfg);
        Assert.Equal("ModbusTcp-10.0.0.1:502", plugin.Name);
        Assert.Equal("ModbusTcp", plugin.ProtocolType);
    }

    #endregion

    #region Modbus RTU 配置验证

    [Fact]
    public void ModbusRtu_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new ModbusRtuOutputConfig
        {
            PortName = "COM1",
            BaudRate = 9600,
            DataBits = 8,
            TimeoutMs = 3000,
            UnitId = 1
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void ModbusRtu_Validate_EmptyPortName_ReturnsError()
    {
        var cfg = new ModbusRtuOutputConfig { PortName = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "PortName");
    }

    [Fact]
    public void ModbusRtu_Validate_InvalidBaudRate_ReturnsError()
    {
        var cfg = new ModbusRtuOutputConfig { PortName = "COM1", BaudRate = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "BaudRate");
    }

    [Fact]
    public void ModbusRtu_Validate_InvalidDataBits_ReturnsError()
    {
        var cfg = new ModbusRtuOutputConfig { PortName = "COM1", DataBits = 4 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "DataBits");
    }

    [Fact]
    public void ModbusRtu_Validate_InvalidTimeout_ReturnsError()
    {
        var cfg = new ModbusRtuOutputConfig { PortName = "COM1", TimeoutMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "TimeoutMs");
    }

    #endregion

    #region OPC-UA 配置验证

    [Fact]
    public void OpcUa_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new OpcUaOutputConfig
        {
            ServerUrl = "opc.tcp://192.168.1.10:4840",
            ConnectTimeoutMs = 10000,
            SendTimeoutMs = 5000,
            ReconnectIntervalMs = 5000,
            ErrorThreshold = 10
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void OpcUa_Validate_EmptyServerUrl_ReturnsError()
    {
        var cfg = new OpcUaOutputConfig { ServerUrl = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ServerUrl");
    }

    [Fact]
    public void OpcUa_Validate_InvalidConnectTimeout_ReturnsError()
    {
        var cfg = new OpcUaOutputConfig { ServerUrl = "opc.tcp://host:4840", ConnectTimeoutMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ConnectTimeoutMs");
    }

    [Fact]
    public void OpcUa_Validate_InvalidSendTimeout_ReturnsError()
    {
        var cfg = new OpcUaOutputConfig { ServerUrl = "opc.tcp://host:4840", SendTimeoutMs = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "SendTimeoutMs");
    }

    [Fact]
    public void OpcUa_Validate_InvalidErrorThreshold_ReturnsError()
    {
        var cfg = new OpcUaOutputConfig { ServerUrl = "opc.tcp://host:4840", ErrorThreshold = 0 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ErrorThreshold");
    }

    [Fact]
    public void OpcUa_Constructor_ValidConfig_SetsName()
    {
        var cfg = new OpcUaOutputConfig
        {
            ServerUrl = "opc.tcp://10.0.0.1:4840",
            Name = "MyOpcUa"
        };
        var plugin = new OpcUaOutputPlugin(cfg);
        Assert.Equal("MyOpcUa", plugin.Name);
        Assert.Equal("OpcUa", plugin.ProtocolType);
    }

    #endregion

    #region S7 配置验证

    [Fact]
    public void S7_Validate_ValidConfig_ReturnsNoErrors()
    {
        var cfg = new S7OutputConfig
        {
            ServerIp = "192.168.0.1",
            Port = 102,
            Rack = 0,
            Slot = 1,
            ConnectTimeoutMs = 5000,
            SendTimeoutMs = 3000,
            ReconnectIntervalMs = 3000,
            ErrorThreshold = 10
        };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void S7_Validate_EmptyServerIp_ReturnsError()
    {
        var cfg = new S7OutputConfig { ServerIp = "" };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "ServerIp");
    }

    [Fact]
    public void S7_Validate_InvalidRack_ReturnsError()
    {
        var cfg = new S7OutputConfig { ServerIp = "10.0.0.1", Port = 102, Rack = 255 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Rack");
    }

    [Fact]
    public void S7_Validate_InvalidSlot_ReturnsError()
    {
        var cfg = new S7OutputConfig { ServerIp = "10.0.0.1", Port = 102, Slot = 99 };
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.PropertyName == "Slot");
    }

    [Fact]
    public void S7_Validate_Hostname_Accepted()
    {
        var cfg = new S7OutputConfig { ServerIp = "plc-factory-01.example.com", Port = 102 };
        var errors = cfg.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void S7_Validate_InvalidHostname_Rejected()
    {
        var cfg = new S7OutputConfig { ServerIp = "-invalid-hostname", Port = 102 };
        var errors = cfg.Validate();
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void S7_Constructor_ValidConfig_SetsName()
    {
        var cfg = new S7OutputConfig { ServerIp = "10.0.0.50", Port = 102 };
        var plugin = new S7OutputPlugin(cfg);
        Assert.Equal("S7-10.0.0.50:102", plugin.Name);
        Assert.Equal("S7", plugin.ProtocolType);
    }

    #endregion

    #region GatewayManagerOptions 配置验证

    [Fact]
    public void GatewayManagerOptions_Validate_ValidConfig_ReturnsNoErrors()
    {
        var opts = new GatewayManagerOptions();
        var errors = opts.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void GatewayManagerOptions_Validate_QueueCapacityTooSmall_ReturnsError()
    {
        var opts = new GatewayManagerOptions { QueueCapacity = 50 };
        var errors = opts.Validate();
        Assert.Contains(errors, e => e.PropertyName == "QueueCapacity");
    }

    [Fact]
    public void GatewayManagerOptions_Validate_SendTimeoutTooSmall_ReturnsError()
    {
        var opts = new GatewayManagerOptions { SendTimeoutMs = 500 };
        var errors = opts.Validate();
        Assert.Contains(errors, e => e.PropertyName == "SendTimeoutMs");
    }

    [Fact]
    public void GatewayManagerOptions_Validate_MaxRetryNegative_ReturnsError()
    {
        var opts = new GatewayManagerOptions { MaxRetryAttempts = -1 };
        var errors = opts.Validate();
        Assert.Contains(errors, e => e.PropertyName == "MaxRetryAttempts");
    }

    [Fact]
    public void GatewayManagerOptions_Validate_RetryBaseDelayTooSmall_ReturnsError()
    {
        var opts = new GatewayManagerOptions { RetryBaseDelayMs = 5 };
        var errors = opts.Validate();
        Assert.Contains(errors, e => e.PropertyName == "RetryBaseDelayMs");
    }

    [Fact]
    public void GatewayManagerOptions_Validate_CircuitBreakerThresholdTooSmall_ReturnsError()
    {
        var opts = new GatewayManagerOptions { CircuitBreakerFailureThreshold = 0 };
        var errors = opts.Validate();
        Assert.Contains(errors, e => e.PropertyName == "CircuitBreakerFailureThreshold");
    }

    [Fact]
    public void GatewayManagerOptions_Validate_CircuitBreakerRecoveryTooSmall_ReturnsError()
    {
        var opts = new GatewayManagerOptions { CircuitBreakerRecoveryTimeMs = 500 };
        var errors = opts.Validate();
        Assert.Contains(errors, e => e.PropertyName == "CircuitBreakerRecoveryTimeMs");
    }

    [Fact]
    public void GatewayManagerOptions_Validate_MultipleErrors_ReturnsAll()
    {
        var opts = new GatewayManagerOptions
        {
            QueueCapacity = 50,
            SendTimeoutMs = 500,
            MaxRetryAttempts = -1,
            RetryBaseDelayMs = 5,
            CircuitBreakerFailureThreshold = 0,
            CircuitBreakerRecoveryTimeMs = 500
        };
        var errors = opts.Validate();
        Assert.Equal(6, errors.Count);
    }

    #endregion
}
