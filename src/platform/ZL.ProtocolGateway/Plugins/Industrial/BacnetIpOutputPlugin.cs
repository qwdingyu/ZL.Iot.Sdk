using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO.BACnet;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// BACnet/IP Output 插件配置
    /// 用于将数据转发到 BACnet/IP 楼宇自动化设备
    /// </summary>
    public class BacnetIpOutputConfig
    {
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// 目标 BACnet 设备 IP 地址
        /// </summary>
        public string ServerIp { get; set; } = "127.0.0.1";
        
        /// <summary>
        /// BACnet/IP 端口，默认 47808
        /// </summary>
        public int Port { get; set; } = 47808;
        
        /// <summary>
        /// BACnet 网络号（可选）
        /// </summary>
        public ushort NetworkNumber { get; set; }
        
        /// <summary>
        /// BACnet 设备实例号（可选）
        /// </summary>
        public uint DeviceInstance { get; set; } = 100;
        
        /// <summary>
        /// APDU 超时时间 (毫秒)
        /// </summary>
        public int ApduTimeoutMs { get; set; } = 3000;
        
        /// <summary>
        /// APDU 重试次数
        /// </summary>
        public int ApduRetries { get; set; } = 3;
        
        /// <summary>
        /// 连接重试间隔 (毫秒)
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 5000;
        
        /// <summary>
        /// 错误告警阈值（连续失败多少次后触发 Error 级别）
        /// </summary>
        public int ErrorThreshold { get; set; } = 10;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(ServerIp))
                errors.Add(new ConfigValidationError(nameof(ServerIp), $"ServerIp 无效: {ServerIp}"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port 无效: {Port}"));
            if (!ConfigValidation.IsValidTimeout(ApduTimeoutMs))
                errors.Add(new ConfigValidationError(nameof(ApduTimeoutMs), $"ApduTimeoutMs 无效: {ApduTimeoutMs}"));
            if (ApduRetries < 1)
                errors.Add(new ConfigValidationError(nameof(ApduRetries), $"ApduRetries 必须大于 0: {ApduRetries}"));
            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
                errors.Add(new ConfigValidationError(nameof(ReconnectIntervalMs), $"ReconnectIntervalMs 无效: {ReconnectIntervalMs}"));
            if (!ConfigValidation.IsValidErrorThreshold(ErrorThreshold))
                errors.Add(new ConfigValidationError(nameof(ErrorThreshold), $"ErrorThreshold 无效: {ErrorThreshold}"));
            return errors;
        }
    }

    /// <summary>
    /// BACnet/IP Output 插件
    /// 将消息转发到 BACnet/IP 楼宇自动化设备
    /// 支持 WriteProperty 服务
    /// </summary>
    public class BacnetIpOutputPlugin : IndustrialOutputPluginBase
    {
        private readonly BacnetIpOutputConfig _config;
        private BacnetClient? _bacnetClient;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private Task? _probeLoop;

        public override string Name { get; }
        public override string ProtocolType => "BacnetIp";

        public BacnetIpOutputPlugin(BacnetIpOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
            Name = string.IsNullOrWhiteSpace(config.Name)
                ? $"BacnetIp-{config.ServerIp}:{config.Port}"
                : config.Name;
        }

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var transport = new BacnetIpUdpProtocolTransport(_config.Port, false);
            _bacnetClient = new BacnetClient(transport, _config.ApduTimeoutMs, _config.ApduRetries);
            _bacnetClient.Start();

            ResetConnectFailureStreak();
            SetLastException(null);

            var connectedMessage = $"BACnet/IP output ready: {_config.ServerIp}:{_config.Port}";
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, connectedMessage);
            SetConnectionState(true, OutputPluginHealthLevel.Healthy);

            _probeLoop = Task.Run(() => ProbeLoopAsync(_cts.Token));
            await Task.CompletedTask;
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            // P1 修复：使用 _cts 令牌使关闭时能中断 Semaphore 等待，避免 StopAsync 无限挂起
            await _sendLock.WaitAsync(_cts?.Token ?? CancellationToken.None);
            try
            {
                if (message == null || _bacnetClient == null || Status != PluginStatus.Running)
                {
                    return;
                }

                // 优先使用协议无关的 TagWrite 列表（解决 Bridge→BACnet 消息格式不匹配的"暗契约"问题）
                if (message.Writes != null && message.Writes.Count > 0)
                {
                    int successCount = 0;
                    int failCount = 0;
                    foreach (var tw in message.Writes)
                    {
                        // BACnet 地址格式: "object-type,instance,property"
                        if (TryParseAndWrite(tw.Address + "=" + (tw.Value?.ToString() ?? ""), out var twErrorMessage))
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                            if (!string.IsNullOrEmpty(twErrorMessage))
                            {
                                GatewayLog.Warn("BacnetIpOutput", $"Write failed for {tw.Address}: {twErrorMessage}");
                            }
                        }
                    }

                    if (failCount == 0)
                    {
                        ResetConnectFailureStreak();
                        SetLastException(null);
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"BACnet/IP batch write successful ({successCount} tags)");
                    }
                    else if (successCount > 0)
                    {
                        var partialMessage = $"BACnet/IP batch write: {successCount} succeeded, {failCount} failed out of {message.Writes.Count}";
                        IncrementConnectFailureStreak();
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, partialMessage);
                    }
                    else
                    {
                        var ex = new InvalidOperationException($"BACnet/IP batch write: all {message.Writes.Count} tags failed");
                        SetLastException(ex);
                        IncrementConnectFailureStreak();
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, ex.Message);
                        throw ex;
                    }
                    return;
                }

                try
                {
                    // 解析消息格式: "object-type,instance,property,value"
                    // 例如: "analog-value,1,present-value,25.5" 或 "multi-state-value,2,present-value,3"
                    var payload = message.Payload ?? Array.Empty<byte>();
                    var payloadString = System.Text.Encoding.UTF8.GetString(payload);

                    // 尝试解析 BACnet WriteProperty 请求
                    if (TryParseAndWrite(payloadString, out var errorMessage))
                    {
                        ResetConnectFailureStreak();
                        SetLastException(null);
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, "BACnet/IP write successful");
                    }
                    else if (!string.IsNullOrEmpty(errorMessage))
                    {
                        SetLastException(new Exception(errorMessage));
                        IncrementConnectFailureStreak();
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, errorMessage);
                    }
                }
                catch (Exception ex)
                {
                    SetLastException(ex);
                    IncrementConnectFailureStreak();

                    var errorMessage = $"BACnet/IP send error: {ex.Message}";
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, errorMessage);
                    SetConnectionState(false, OutputPluginHealthLevel.Error);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private bool TryParseAndWrite(string payload, out string errorMessage)
        {
            errorMessage = string.Empty;
            
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            try
            {
                string objectTypeName;
                string instanceText;
                string propertyName;
                string valueStr;

                if (payload.Contains("="))
                {
                    var kvParts = payload.Split(new[] { '=' }, 2);
                    if (kvParts.Length != 2)
                    {
                        errorMessage = $"Invalid BACnet payload format: {payload}";
                        return false;
                    }

                    var addressParts = kvParts[0].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (addressParts.Length != 3)
                    {
                        errorMessage = $"Invalid BACnet address format: {kvParts[0]}";
                        return false;
                    }

                    objectTypeName = addressParts[0].Trim();
                    instanceText = addressParts[1].Trim();
                    propertyName = addressParts[2].Trim();
                    valueStr = kvParts[1].Trim();
                }
                else
                {
                    var parts = payload.Split(new[] { ',' }, 4, StringSplitOptions.None);
                    if (parts.Length != 4)
                    {
                        errorMessage = $"Invalid BACnet payload format: {payload}";
                        return false;
                    }

                    objectTypeName = parts[0].Trim();
                    instanceText = parts[1].Trim();
                    propertyName = parts[2].Trim();
                    valueStr = parts[3].Trim();
                }

                if (!uint.TryParse(instanceText, out var instance))
                {
                    errorMessage = $"Invalid BACnet instance number: {instanceText}";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(valueStr))
                {
                    errorMessage = "BACnet payload value cannot be empty";
                    return false;
                }

                return TryWriteBacnetValue(objectTypeName, instance, propertyName, valueStr, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = $"BACnet parse error: {ex.Message}";
                return false;
            }
        }

        private bool TryWriteBacnetValue(BacnetObjectTypes objectType, uint instance, BacnetPropertyIds propertyId, string valueStr, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (_bacnetClient == null)
            {
                errorMessage = "BACnet client is not started";
                return false;
            }

            try
            {
                var bacnetAddress = new BacnetAddress(BacnetAddressTypes.IP, _config.ServerIp, (ushort)_config.Port);
                var objectId = new BacnetObjectId(objectType, instance);
                var value = CreateBacnetValue(objectType, propertyId, valueStr, out errorMessage);
                if (value == null)
                {
                    return false;
                }

                _bacnetClient.WritePropertyRequest(
                    bacnetAddress,
                    objectId,
                    propertyId,
                    value,
                    0);

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"BACnet write failed: {ex.Message}";
                return false;
            }
        }

        private bool TryWriteBacnetValue(string objectTypeName, uint instance, string propertyName, string valueStr, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!TryMapObjectType(objectTypeName, out var objectType))
            {
                errorMessage = $"Unsupported BACnet object type: {objectTypeName}";
                return false;
            }

            if (!TryMapPropertyId(propertyName, out var propertyId))
            {
                errorMessage = $"Unsupported BACnet property: {propertyName}";
                return false;
            }

            return TryWriteBacnetValue(objectType, instance, propertyId, valueStr, out errorMessage);
        }

        private static bool TryMapObjectType(string objectTypeName, out BacnetObjectTypes objectType)
        {
            switch ((objectTypeName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "av":
                case "analog-value":
                    objectType = BacnetObjectTypes.OBJECT_ANALOG_VALUE;
                    return true;
                case "bv":
                case "binary-value":
                    objectType = BacnetObjectTypes.OBJECT_BINARY_VALUE;
                    return true;
                case "mv":
                case "multi-state-value":
                    objectType = BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE;
                    return true;
                case "ai":
                case "analog-input":
                    objectType = BacnetObjectTypes.OBJECT_ANALOG_INPUT;
                    return true;
                case "ao":
                case "analog-output":
                    objectType = BacnetObjectTypes.OBJECT_ANALOG_OUTPUT;
                    return true;
                case "bi":
                case "binary-input":
                    objectType = BacnetObjectTypes.OBJECT_BINARY_INPUT;
                    return true;
                case "bo":
                case "binary-output":
                    objectType = BacnetObjectTypes.OBJECT_BINARY_OUTPUT;
                    return true;
                case "msi":
                case "multi-state-input":
                    objectType = BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT;
                    return true;
                default:
                    objectType = default;
                    return false;
            }
        }

        private static bool TryMapPropertyId(string propertyName, out BacnetPropertyIds propertyId)
        {
            switch ((propertyName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "85":
                case "pv":
                case "present-value":
                case "value":
                    propertyId = BacnetPropertyIds.PROP_PRESENT_VALUE;
                    return true;
                case "111":
                case "status-flags":
                    propertyId = BacnetPropertyIds.PROP_STATUS_FLAGS;
                    return true;
                case "28":
                case "description":
                    propertyId = BacnetPropertyIds.PROP_DESCRIPTION;
                    return true;
                default:
                    propertyId = default;
                    return false;
            }
        }

        private static IList<BacnetValue>? CreateBacnetValue(BacnetObjectTypes objectType, BacnetPropertyIds propertyId, string valueStr, out string errorMessage)
        {
            errorMessage = string.Empty;
            var trimmedValue = (valueStr ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(trimmedValue))
            {
                errorMessage = "BACnet value cannot be empty";
                return null;
            }

            if (propertyId == BacnetPropertyIds.PROP_DESCRIPTION)
            {
                return new BacnetValue[]
                {
                    new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_CHARACTER_STRING, trimmedValue)
                };
            }

            switch (objectType)
            {
                case BacnetObjectTypes.OBJECT_ANALOG_INPUT:
                case BacnetObjectTypes.OBJECT_ANALOG_OUTPUT:
                case BacnetObjectTypes.OBJECT_ANALOG_VALUE:
                    if (double.TryParse(trimmedValue, out var analogValue))
                    {
                        return new BacnetValue[]
                        {
                            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, (float)analogValue)
                        };
                    }
                    errorMessage = $"BACnet analog object requires numeric value: {trimmedValue}";
                    return null;

                case BacnetObjectTypes.OBJECT_BINARY_INPUT:
                case BacnetObjectTypes.OBJECT_BINARY_OUTPUT:
                case BacnetObjectTypes.OBJECT_BINARY_VALUE:
                    if (trimmedValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                        trimmedValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        return new BacnetValue[]
                        {
                            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_BOOLEAN,
                                trimmedValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                        };
                    }
                    errorMessage = $"BACnet binary object requires boolean value: {trimmedValue}";
                    return null;

                case BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT:
                case BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE:
                    if (uint.TryParse(trimmedValue, out var stateValue))
                    {
                        return new BacnetValue[]
                        {
                            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT, stateValue)
                        };
                    }
                    errorMessage = $"BACnet multi-state object requires unsigned integer value: {trimmedValue}";
                    return null;

                default:
                    return new BacnetValue[]
                    {
                        new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_CHARACTER_STRING, trimmedValue)
                    };
            }
        }

protected override async Task OnStopAsync()
        {
            _cts?.Cancel();

            try
            {
                if (_probeLoop != null)
                {
                    var delay = Task.Delay(TimeSpan.FromSeconds(5));
                    await Task.WhenAny(_probeLoop, delay);
                }
            }
            catch
            {
                // ignore cancellation or timeout
            }
            finally
            {
                _probeLoop = null;
            }

            try
            {
                _bacnetClient?.Dispose();
                _bacnetClient = null;
            }
            catch
            {
                // ignore
            }

            _cts?.Dispose();
            _cts = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sendLock?.Dispose();
            }
            base.Dispose(disposing);
        }

        private async Task ProbeLoopAsync(CancellationToken cancellationToken)
        {
            var probeInterval = TimeSpan.FromSeconds(30);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(probeInterval, cancellationToken);

                    if (_bacnetClient == null || Status != PluginStatus.Running)
                    {
                        continue;
                    }

                    try
                    {
                        // BACnet/IP 是 UDP 协议，无连接状态。使用 UDP 端口可达性探测：
                        // 向目标 IP:Port 发送一个最小 BACnet BVLC 原始包（0x81 0x0b 0x00 0x04 = 
                        // BVLL-Type: 0x81, Function: 0x0b=Reject, Length: 4），若无 ICMP 不可达响应则认为可达。
                        // 这比 Who-Is 更可靠，因为 Who-Is 需要对方响应 I-Am，而探测只需确认端口可达。
                        using var probeClient = new System.Net.Sockets.UdpClient();
                        try
                        {
                            probeClient.Connect(_config.ServerIp, _config.Port);
                            // BVLC Reject 包（最小有效 BACnet/IP 帧）— 目标端口如果存在 BACnet 设备会回复 Reject
                            var probePacket = new byte[] { 0x81, 0x0b, 0x00, 0x04 };
                            await probeClient.SendAsync(probePacket, probePacket.Length);
                            // 发送成功即认为端口可达（UDP 无连接，发送成功 = 无 ICMP 不可达）
                            ResetConnectFailureStreak();
                            SetLastException(null);
                            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, "BACnet/IP probe successful");
                        }
                        finally
                        {
                            probeClient.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        IncrementConnectFailureStreak();
                        SetLastException(ex);
                        var probeFailed = $"BACnet/IP probe failed: {ex.Message}";

                        var level = ConnectFailureStreak >= _config.ErrorThreshold
                            ? OutputPluginHealthLevel.Error
                            : OutputPluginHealthLevel.Warning;

                        RaiseDetailedStatusChanged(level, probeFailed);
                        SetConnectionState(false, level);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // ignore unexpected errors in probe loop
                }
            }
        }

        /// <summary>BACnet/IP 协议特定的错误分类</summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            return level switch
            {
                OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, "BACnet/IP connected", "No action needed"),
                OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, "BACnet/IP communication warning", "Check BACnet device status"),
                OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, "BACnet/IP connection failed", "Check network and target device"),
                OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, "BACnet/IP configuration error", "Check BACnet output configuration"),
                _ => (GatewayErrorCodes.InternalException, "BACnet/IP unknown error", "Check logs for details")
            };
        }
    }
}
