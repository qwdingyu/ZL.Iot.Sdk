using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// OPC-UA 输出插件配置
    /// </summary>
    public class OpcUaOutputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// OPC-UA 服务器端点 URL
        /// 例如: opc.tcp://localhost:4840 或 https://localhost:4840
        /// </summary>
        public string ServerUrl { get; set; } = "opc.tcp://localhost:4840";

        /// <summary>
        /// 认证模式: Anonymous, UserName, Certificate
        /// </summary>
        public string AuthMode { get; set; } = "Anonymous";

        /// <summary>
        /// 用户名（当 AuthMode=UserName 时使用）
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// 密码（当 AuthMode=UserName 时使用）
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 客户端证书路径（当 AuthMode=Certificate 时使用）
        /// </summary>
        public string CertificatePath { get; set; }

        /// <summary>
        /// 客户端证书私钥密码
        /// </summary>
        public string CertificatePassword { get; set; }

        /// <summary>
        /// 连接超时 (毫秒)
        /// </summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 发送超时 (毫秒)
        /// </summary>
        public int SendTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 重连间隔 (毫秒)
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 3000;

        /// <summary>
        /// 错误告警阈值（连续失败多少次后触发 Error 级别）
        /// </summary>
        public int ErrorThreshold { get; set; } = 10;

        /// <summary>
        /// 订阅模式: Write, Subscribe, ReadSubscribe
        /// Write - 仅写入值到服务器
        /// Subscribe - 创建订阅并接收数据变化通知
        /// ReadSubscribe - 读取并订阅
        /// </summary>
        public string SubscriptionMode { get; set; } = "Write";

        /// <summary>
        /// 发布间隔 (毫秒)，当 SubscriptionMode != Write 时使用
        /// </summary>
        public int PublishingIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 监控节点列表，逗号分隔的 NodeId
        /// 当 SubscriptionMode != Write 时使用
        /// </summary>
        public string MonitoredNodes { get; set; } = "";

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();

            if (string.IsNullOrEmpty(ServerUrl))
            {
                errors.Add(new ConfigValidationError("ServerUrl", "ServerUrl 不能为空"));
            }

            if (!ConfigValidation.IsValidTimeout(ConnectTimeoutMs))
            {
                errors.Add(new ConfigValidationError("ConnectTimeoutMs", $"ConnectTimeoutMs 无效: {ConnectTimeoutMs}"));
            }

            if (!ConfigValidation.IsValidTimeout(SendTimeoutMs))
            {
                errors.Add(new ConfigValidationError("SendTimeoutMs", $"SendTimeoutMs 无效: {SendTimeoutMs}"));
            }

            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
            {
                errors.Add(new ConfigValidationError("ReconnectIntervalMs", $"ReconnectIntervalMs 无效: {ReconnectIntervalMs}"));
            }

            if (!ConfigValidation.IsValidErrorThreshold(ErrorThreshold))
            {
                errors.Add(new ConfigValidationError("ErrorThreshold", $"ErrorThreshold 无效: {ErrorThreshold}"));
            }

            return errors;
        }
    }

    /// <summary>
    /// OPC-UA 输出插件 - 将消息以 OPC-UA 协议转发到 OPC-UA 服务器
    /// 支持 OPC-UA 服务器连接，支持 Anonymous/UserName/Certificate 认证
    /// </summary>
    public class OpcUaOutputPlugin : IndustrialOutputPluginBase
    {
        private readonly OpcUaOutputConfig _config;
        private volatile Session? _session;
        private ApplicationConfiguration? _applicationConfiguration;
        // P1 修复：异步串行化发送，防止多线程并发 Send 时竞争非线程安全的 Session.Write()
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public override string Name { get; }
        public override string ProtocolType => "OpcUa";

        protected override int BaseReconnectIntervalMs => _config.ReconnectIntervalMs;

        public OpcUaOutputPlugin(OpcUaOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(config.Validate());
            Name = config.Name ?? $"OpcUa-{config.ServerUrl}";
        }

        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            await DisconnectAsync();

            GatewayLog.Info("OpcUaOutput", $"Connecting to {_config.ServerUrl}...");
            await ConnectAsync(ct);

            if (_session != null && _session.Connected)
            {
                // 保持连接直到断开或取消
                while (!ct.IsCancellationRequested && HasLiveConnection())
                {
                    await Task.Delay(5000, ct);
                }
            }
        }

        protected override bool HasLiveConnection()
        {
            return _session != null && _session.Connected;
        }

        protected override void CleanupConnection()
        {
            if (_session != null)
            {
                try { if (_session.Connected) _session.Close(true); } catch { }
                try { _session.Dispose(); } catch { }
                _session = null;
            }
        }

        protected override string OnConnected()
        {
            return $"Connected to OPC-UA server at {_config.ServerUrl}";
        }

        protected override string GetFailureKind(Exception ex)
        {
            return ex switch
            {
                TimeoutException => "Timeout",
                ServiceResultException => "Service",
                OperationCanceledException => "Cancelled",
                _ => "General"
            };
        }

        protected override bool ShouldLogConnectionFailure(int streak, string failureKind)
        {
            if (streak == 1) return true;
            if (streak <= 10 && streak % 5 == 0) return true;
            if (streak <= 100 && streak % 20 == 0) return true;
            if (streak > 100 && streak % 50 == 0) return true;
            return false;
        }

        private async Task ConnectAsync(CancellationToken ct)
        {
            var applicationName = Name.Replace(" ", "_");
            var applicationUri = $"urn:{applicationName}";

            // 创建应用配置
            var applicationConfiguration = new ApplicationConfiguration
            {
                ApplicationName = applicationName,
                ApplicationUri = applicationUri,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier(),
                    TrustedPeerCertificates = new CertificateTrustList(),
                    TrustedIssuerCertificates = new CertificateTrustList(),
                    RejectedCertificateStore = new CertificateStoreIdentifier()
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = _config.ConnectTimeoutMs
                },
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 30000,
                    WellKnownDiscoveryUrls = new StringCollection()
                }
            };

            _applicationConfiguration = applicationConfiguration;

            // 创建 endpoint 描述符
            var selectedEndpoint = new EndpointDescription
            {
                EndpointUrl = _config.ServerUrl,
                ServerCertificate = null,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            };

            var endpointConfiguration = EndpointConfiguration.Create(_applicationConfiguration);
            var configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);

            // 创建用户身份
            IUserIdentity? userIdentity = null;
            switch (_config.AuthMode?.ToLowerInvariant())
            {
                case "username":
                    if (!string.IsNullOrWhiteSpace(_config.UserName))
                    {
                        userIdentity = new UserIdentity(_config.UserName, System.Text.Encoding.UTF8.GetBytes(_config.Password ?? ""));
                    }
                    break;
                case "certificate":
                    if (!string.IsNullOrWhiteSpace(_config.CertificatePath))
                    {
                        var certificate = new X509Certificate2(_config.CertificatePath, _config.CertificatePassword);
                        userIdentity = new UserIdentity(certificate);
                    }
                    break;
                default:
                    userIdentity = new UserIdentity(new AnonymousIdentityToken());
                    break;
            }

            // 创建会话 — Session.Create 是同步方法，内部执行网络I/O。
            // 使用 Task.Run 避免阻塞调用线程，并用 Task.WhenAny 支持取消。
            var createTask = Task.Run(() => Session.Create(
                _applicationConfiguration,
                configuredEndpoint,
                false,
                applicationName,
                (uint)_config.ConnectTimeoutMs,
                userIdentity,
                null as IList<string>), ct);

            var completed = await Task.WhenAny(createTask, Task.Delay(_config.ConnectTimeoutMs, ct));
            if (completed != createTask)
            {
                ct.ThrowIfCancellationRequested();
            }

            var session = await createTask;
            _session = session;
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            // P1 修复：异步串行化整个发送流程，防止多线程并发 Send 时竞争非线程安全的 Session.Write()
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                var session = _session;
                if (session == null || !session.Connected)
                {
                    var notConnectedMessage = "Not connected to OPC-UA server";
                    SetLastException(new InvalidOperationException(notConnectedMessage));
                    IncrementConnectFailureStreak();
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, notConnectedMessage);
                    throw new InvalidOperationException(notConnectedMessage);
                }

                try
                {
                    // 优先使用协议无关的 TagWrite 列表（解决 Bridge→OPC-UA 消息格式不匹配的"暗契约"问题）
                    if (message.Writes != null && message.Writes.Count > 0)
                    {
                        int successCount = 0;
                        int failCount = 0;
                        foreach (var tw in message.Writes)
                        {
                            try
                            {
                                var addressParts = tw.Address.Split(';');
                                ushort namespaceIndex = 0;
                                string nodeIdStr = tw.Address;
                                if (addressParts.Length >= 2 && addressParts[0].StartsWith("ns=") && addressParts[1].StartsWith("s="))
                                {
                                    _ = ushort.TryParse(addressParts[0].Substring(3), out namespaceIndex);
                                    nodeIdStr = addressParts[1].Substring(2);
                                }
                                else if (addressParts.Length >= 1 && addressParts[0].StartsWith("ns="))
                                {
                                    _ = ushort.TryParse(addressParts[0].Substring(3), out namespaceIndex);
                                }

                                var nodeId = new NodeId(nodeIdStr, namespaceIndex);
                                var dataValue = new DataValue(new Variant(tw.Value));

                                var writeValue = new WriteValue
                                {
                                    NodeId = nodeId,
                                    AttributeId = Attributes.Value,
                                    Value = dataValue
                                };

                                var writeValues = new WriteValueCollection { writeValue };
                                session.Write(
                                    null,
                                    writeValues,
                                    out var writeResults,
                                    out var writeDiagnosticInfos);

                                if (writeResults[0] != StatusCodes.Good)
                                {
                                    failCount++;
                                    GatewayLog.Warn("OpcUaOutput", $"Write failed for {tw.Address}: {writeResults[0]}");
                                }
                                else
                                {
                                    successCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                failCount++;
                                GatewayLog.Warn("OpcUaOutput", $"Write error for {tw.Address}: {ex.Message}");
                            }
                        }

                        if (failCount == 0)
                        {
                            ResetConnectFailureStreak();
                            SetLastException(null);
                            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"OPC-UA batch write successful ({successCount} tags)");
                        }
                        else if (successCount > 0)
                        {
                            var partialMessage = $"OPC-UA batch write: {successCount} succeeded, {failCount} failed out of {message.Writes.Count}";
                            IncrementConnectFailureStreak();
                            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, partialMessage);
                        }
                        else
                        {
                            var ex = new InvalidOperationException($"OPC-UA batch write: all {message.Writes.Count} tags failed");
                            SetLastException(ex);
                            IncrementConnectFailureStreak();
                            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, ex.Message);
                            throw ex;
                        }
                        return;
                    }

                    // 原有文本解析路径（向后兼容）
                    var data = message.Payload;
                    if (data == null || data.Length == 0)
                    {
                        var textContent = message.GetTextContent();
                        if (!string.IsNullOrEmpty(textContent))
                        {
                            data = System.Text.Encoding.UTF8.GetBytes(textContent);
                        }
                        else
                        {
                            return;
                        }
                    }

                    var content = System.Text.Encoding.UTF8.GetString(data);
                    var parts = content.Split(';');
                    if (parts.Length >= 2)
                    {
                        var nsPart = parts[0];
                        var idPart = parts[1];

                        if (nsPart.StartsWith("ns=") && idPart.StartsWith("id="))
                        {
                            if (!ushort.TryParse(nsPart.Substring(3), out var namespaceIndex))
                            {
                                var parseErrorMessage = $"Invalid OPC-UA namespace index: '{nsPart.Substring(3)}'";
                                IncrementConnectFailureStreak();
                                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, parseErrorMessage);
                                return;
                            }
                            var nodeIdStr = idPart.Substring(3);

                            var nodeId = new NodeId(nodeIdStr, namespaceIndex);

                            var value = parts.Length > 2 ? parts[2] : content;
                            var dataValue = new DataValue(new Variant(value));

                            // 写入值 - 使用正确的签名
                            var writeValue = new WriteValue
                            {
                                NodeId = nodeId,
                                AttributeId = Attributes.Value,
                                Value = dataValue
                            };

                            var writeValues = new WriteValueCollection { writeValue };
                            var results = new StatusCodeCollection { 0 };
                            var diagnosticInfos = new DiagnosticInfoCollection();

                            // 同步写入
                            session.Write(
                                null,
                                writeValues,
                                out var writeResults,
                                out var writeDiagnosticInfos);

                            if (writeResults[0] != StatusCodes.Good)
                            {
                                var writeFailedMessage = $"Write failed: {writeResults[0]}";
                                GatewayLog.Error("OpcUaOutput", writeFailedMessage);
                                SetLastException(new Exception(writeFailedMessage));
                                IncrementConnectFailureStreak();
                                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, writeFailedMessage);
                            }
                            else
                            {
                                ResetConnectFailureStreak();
                                SetLastException(null);
                                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, "OPC-UA write successful");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    var errorMessage = $"OPC-UA send error: {ex.Message}";
                    GatewayLog.Error("OpcUaOutput", errorMessage);
                    SetLastException(ex);
                    IncrementConnectFailureStreak();
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, errorMessage);
                    throw;
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        protected override async Task OnStopCoreAsync()
        {
            await DisconnectAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sendLock?.Dispose();
            }
            base.Dispose(disposing);
        }

        private async Task DisconnectAsync()
        {
            if (_session != null)
            {
                try
                {
                    if (_session.Connected)
                    {
                        _session.Close(true);
                    }
                    _session.Dispose();
                }
                catch
                {
                    // Ignore disconnect errors
                }
                finally
                {
                    _session = null;
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>OPC-UA 协议特定的错误分类</summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            return level switch
            {
                OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, "OPC-UA connected", "No action needed"),
                OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, "OPC-UA connection retrying", "Check OPC-UA server status"),
                OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, "OPC-UA connection failed", "Check server URL, network, and authentication"),
                OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, "OPC-UA configuration error", "Check output plugin configuration"),
                _ => (GatewayErrorCodes.InternalException, "OPC-UA unknown error", "Check logs for details")
            };
        }
    }
}
