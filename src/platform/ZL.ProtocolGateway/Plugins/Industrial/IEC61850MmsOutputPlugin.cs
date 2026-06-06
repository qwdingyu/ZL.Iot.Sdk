using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// IEC 61850 MMS (Manufacturing Message Specification) Output 插件配置
    /// 用于将数据转发到 IEC 61850 变电站自动化设备 (IED)
    /// 标准端口: 102 (TCP)
    /// </summary>
    public class IEC61850MmsOutputConfig
    {
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// 目标 IED (Intelligent Electronic Device) IP 地址
        /// </summary>
        public string ServerIp { get; set; } = "127.0.0.1";
        
        /// <summary>
        /// MMS 端口，默认 102
        /// </summary>
        public int Port { get; set; } = 102;
        
        /// <summary>
        /// 逻辑设备名 (Logical Device)
        /// </summary>
        public string LogicalDevice { get; set; } = "IED1";
        
        /// <summary>
        /// 逻辑节点前缀 (例如: MMXU, yymmxu, ctlstm)
        /// </summary>
        public string LogicalNode { get; set; } = "MMXU1";
        
        /// <summary>
        /// 数据属性引用 (例如: Mag.f, mag.f, stVal, ctlVal)
        /// </summary>
        public string DataAttribute { get; set; } = "Mag.f";
        
        /// <summary>
        /// APDU 超时时间 (毫秒)
        /// </summary>
        public int ApduTimeoutMs { get; set; } = 10000;
        
        /// <summary>
        /// 连接超时 (毫秒)
        /// </summary>
        public int ConnectTimeoutMs { get; set; } = 15000;
        
        /// <summary>
        /// 连接重试间隔 (毫秒)
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 10000;
        
        /// <summary>
        /// 错误告警阈值（连续失败多少次后触发 Error 级别）
        /// </summary>
        public int ErrorThreshold { get; set; } = 10;
        
        /// <summary>
        /// IEC 61850 配置文件路径 (可选，用于完整实现)
        /// </summary>
        public string ConfigFilePath { get; set; } = string.Empty;

        /// <summary>
        /// 验证配置合法性
        /// </summary>
        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(ServerIp))
                errors.Add(new ConfigValidationError(nameof(ServerIp), $"ServerIp 无效: {ServerIp}"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port 无效: {Port}"));
            if (string.IsNullOrWhiteSpace(LogicalDevice))
                errors.Add(new ConfigValidationError(nameof(LogicalDevice), "LogicalDevice 不能为空"));
            if (string.IsNullOrWhiteSpace(LogicalNode))
                errors.Add(new ConfigValidationError(nameof(LogicalNode), "LogicalNode 不能为空"));
            if (string.IsNullOrWhiteSpace(DataAttribute))
                errors.Add(new ConfigValidationError(nameof(DataAttribute), "DataAttribute 不能为空"));
            if (!ConfigValidation.IsValidTimeout(ConnectTimeoutMs, 1000))
                errors.Add(new ConfigValidationError(nameof(ConnectTimeoutMs), $"ConnectTimeoutMs 无效: {ConnectTimeoutMs}"));
            if (!ConfigValidation.IsValidTimeout(ApduTimeoutMs, 1000))
                errors.Add(new ConfigValidationError(nameof(ApduTimeoutMs), $"ApduTimeoutMs 无效: {ApduTimeoutMs}"));
            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
                errors.Add(new ConfigValidationError(nameof(ReconnectIntervalMs), $"ReconnectIntervalMs 无效: {ReconnectIntervalMs}"));
            if (!ConfigValidation.IsValidErrorThreshold(ErrorThreshold))
                errors.Add(new ConfigValidationError(nameof(ErrorThreshold), $"ErrorThreshold 无效: {ErrorThreshold}"));
            return errors;
        }
    }

    /// <summary>
    /// IEC 61850 MMS Output 插件
    /// 将消息转发到 IEC 61850 变电站自动化设备
    /// 支持 Write Request 服务
    /// </summary>
    public class IEC61850MmsOutputPlugin : IndustrialOutputPluginBase
    {
        // MMS 协议常量
        private const byte MMS_SERVICE_WRITE = 0x4B;
        private const byte MMS_SERVICE_READ = 0x4A;
        private const byte MMS_PDU_INITIATE = 0x01;
        private const byte MMS_PDU_CONFIRMED = 0xA0;
        private const byte ISO_SESSION = 0x01;
        private const byte ISO_PRESENTATION = 0x03;

        private readonly IEC61850MmsOutputConfig _config;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private byte _invokeId = 0;
        // P1 修复：异步串行化发送，防止多线程并发 Send 时竞争同一 NetworkStream（TOCTOU 竞态）
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public override string Name { get; }
        public override string ProtocolType => "IEC61850";

        public IEC61850MmsOutputPlugin(IEC61850MmsOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
            Name = string.IsNullOrWhiteSpace(config.Name)
                ? $"IEC61850-{_config.ServerIp}:{_config.Port}"
                : config.Name;
        }

        /// <summary>
        /// 建立 TCP 连接、发送 MMS Initiate，并保持连接直到断开或取消
        /// <para>基类 ConnectionLoopAsync 调用此方法，返回后自动调用 CleanupConnection</para>
        /// </summary>
        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            // 创建新的 TCP 连接
            var endpoint = new IPEndPoint(IPAddress.Parse(_config.ServerIp), _config.Port);
            _tcpClient = new TcpClient();

            using var connectCts = new CancellationTokenSource(_config.ConnectTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);

            var connectTask = _tcpClient.ConnectAsync(endpoint.Address, endpoint.Port);
            var connectCompleted = await Task.WhenAny(connectTask, Task.Delay(_config.ConnectTimeoutMs, linkedCts.Token));
            if (connectCompleted != connectTask)
            {
                throw new TimeoutException($"Connect timeout after {_config.ConnectTimeoutMs}ms");
            }

            await connectTask;
            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = _config.ApduTimeoutMs;
            _stream.WriteTimeout = _config.ApduTimeoutMs;

            // 发送 MMS Initiate 请求
            if (!await SendMmsInitiateAsync())
            {
                throw new InvalidOperationException("IEC 61850 MMS Initiate failed");
            }

            // 保持连接直到断开或取消
            var buffer = new byte[1];
            while (!ct.IsCancellationRequested && HasLiveConnection())
            {
                try
                {
                    var readTask = _stream!.ReadAsync(buffer, 0, buffer.Length, ct);
                    var completedTask = await Task.WhenAny(readTask, Task.Delay(5000, ct));

                    if (completedTask == readTask)
                    {
                        var bytesRead = await readTask;
                        if (bytesRead == 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // 发送 keepalive/轮询
                        await SendMmsStatusRequestAsync();
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }
        }

        /// <summary>发送 MMS Initiate 请求</summary>
        private async Task<bool> SendMmsInitiateAsync()
        {
            if (_stream == null) return false;
            try
            {
                var initiateRequest = BuildMmsWriteRequest("Initiate", "Initiate", "Initiate", "1");
                await _stream.WriteAsync(initiateRequest, 0, initiateRequest.Length);
                var responseBuffer = new byte[1024];
                var bytesRead = await _stream.ReadAsync(responseBuffer, 0, responseBuffer.Length);
                return bytesRead > 0 && IsLikelyConfirmedResponse(responseBuffer, bytesRead);
            }
            catch { return false; }
        }

        /// <summary>发送 MMS 状态请求（keepalive）</summary>
        private async Task SendMmsStatusRequestAsync()
        {
            if (_stream == null) return;
            try
            {
                var statusRequest = BuildMmsWriteRequest("Status", "Status", "Poll", "1");
                await _stream.WriteAsync(statusRequest, 0, statusRequest.Length);
            }
            catch
            {
                // keepalive 失败忽略，由连接检测处理
            }
        }

        /// <summary>检查 TCP 连接是否仍然有效</summary>
        protected override bool HasLiveConnection()
        {
            return _tcpClient != null && _tcpClient.Connected;
        }

        /// <summary>同步清理 TCP 连接资源</summary>
        protected override void CleanupConnection()
        {
            try { _stream?.Close(); } catch { }
            _stream = null;
            try { _tcpClient?.Close(); _tcpClient?.Dispose(); } catch { }
            _tcpClient = null;
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null || Status != PluginStatus.Running)
            {
                return;
            }

            // P1 修复：异步串行化整个发送流程，确保 stream 获取 → null 检查 → WriteMmsValue
            // 在同一个异步临界区内完成，避免多线程并发 Send 时竞争同一 NetworkStream（TOCTOU 竞态条件修复）
            await _sendLock.WaitAsync();
            try
            {
                if (_stream == null)
                {
                    return;
                }

                // 优先使用协议无关的 TagWrite 列表（解决 Bridge→IEC61850 消息格式不匹配的"暗契约"问题）
                if (message.Writes != null && message.Writes.Count > 0)
                {
                    var tw = message.Writes[0];
                    // tw.Address 格式如 "IED1/MMXU1.Mag.f"
                    if (TryParseAndWrite(tw.Address + "=" + (tw.Value?.ToString() ?? ""), out var twErrorMessage))
                    {
                        ResetConnectFailureStreak();
                        SetLastException(null);
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, "IEC 61850 MMS write successful (via TagWrite)");
                    }
                    else if (!string.IsNullOrEmpty(twErrorMessage))
                    {
                        int streak = IncrementConnectFailureStreak();
                        var level = streak >= _config.ErrorThreshold
                            ? OutputPluginHealthLevel.Error
                            : OutputPluginHealthLevel.Warning;
                        RaiseDetailedStatusChanged(level, twErrorMessage);
                    }
                    return;
                }

                var payload = message.Payload ?? Array.Empty<byte>();
                var payloadString = Encoding.UTF8.GetString(payload);

                // 解析消息格式: "LD/LN.DA=value" 或 "LD.LN.DA value"
                // 例如: "IED1/MMXU1.Mag.f=25.5" 或 "IED1.MMXU1.Mag.f 25.5"
                if (TryParseAndWrite(payloadString, out var errorMessage))
                {
                    ResetConnectFailureStreak();
                    SetLastException(null);
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, "IEC 61850 MMS write successful");
                }
                else if (!string.IsNullOrEmpty(errorMessage))
                {
                    int streak = IncrementConnectFailureStreak();
                    var level = streak >= _config.ErrorThreshold
                        ? OutputPluginHealthLevel.Error
                        : OutputPluginHealthLevel.Warning;
                    RaiseDetailedStatusChanged(level, errorMessage);
                }
            }
            catch (Exception ex)
            {
                Status = PluginStatus.Error;
                SetLastException(ex);
                IncrementConnectFailureStreak();

                var errorMessage = $"IEC 61850 MMS send error: {ex.Message}";
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, errorMessage);
                SetConnectionState(false, OutputPluginHealthLevel.Error);
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
                string path;
                string valueStr;

                if (payload.Contains("="))
                {
                    var parts = payload.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                    {
                        errorMessage = $"Invalid IEC 61850 MMS payload format: {payload}";
                        return false;
                    }

                    path = parts[0].Trim();
                    valueStr = parts[1].Trim();
                }
                else
                {
                    var firstWhitespace = payload.IndexOf(' ');
                    if (firstWhitespace <= 0 || firstWhitespace == payload.Length - 1)
                    {
                        errorMessage = $"Invalid IEC 61850 MMS payload format: {payload}";
                        return false;
                    }

                    path = payload.Substring(0, firstWhitespace).Trim();
                    valueStr = payload.Substring(firstWhitespace + 1).Trim();
                }

                if (string.IsNullOrWhiteSpace(valueStr))
                {
                    errorMessage = "IEC 61850 MMS value cannot be empty";
                    return false;
                }

                if (!TryParseObjectReference(path, out var ld, out var ln, out var da, out errorMessage))
                {
                    return false;
                }

                return WriteMmsValue(ld, ln, da, valueStr);
            }
            catch (Exception ex)
            {
                errorMessage = $"IEC 61850 MMS parse error: {ex.Message}";
                return false;
            }
        }

        private bool TryParseObjectReference(string path, out string logicalDevice, out string logicalNode, out string dataAttribute, out string errorMessage)
        {
            logicalDevice = string.Empty;
            logicalNode = string.Empty;
            dataAttribute = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "IEC 61850 path cannot be empty";
                return false;
            }

            var slashIndex = path.IndexOf('/');
            if (slashIndex <= 0 || slashIndex == path.Length - 1)
            {
                errorMessage = $"Invalid IEC 61850 path: {path}";
                return false;
            }

            logicalDevice = path.Substring(0, slashIndex).Trim();
            var remainder = path.Substring(slashIndex + 1).Trim();
            var dotIndex = remainder.IndexOf('.');
            if (dotIndex <= 0 || dotIndex == remainder.Length - 1)
            {
                errorMessage = $"Invalid IEC 61850 object reference: {path}";
                return false;
            }

            logicalNode = remainder.Substring(0, dotIndex).Trim();
            dataAttribute = remainder.Substring(dotIndex + 1).Trim();

            if (string.IsNullOrWhiteSpace(logicalDevice) || string.IsNullOrWhiteSpace(logicalNode) || string.IsNullOrWhiteSpace(dataAttribute))
            {
                errorMessage = $"Invalid IEC 61850 object reference: {path}";
                return false;
            }

            return true;
        }

        private bool WriteMmsValue(string logicalDevice, string logicalNode, string dataAttribute, string valueStr)
        {
            if (_stream == null || _tcpClient?.Connected != true)
            {
                throw new InvalidOperationException("Not connected to IED");
            }

            try
            {
                // 构造 MMS Write Request (简化版本)
                // 真实实现需要完整的 ASN.1 编码 (BER/DER)
                _invokeId = (byte)((_invokeId + 1) % 256);

                var mmsRequest = BuildMmsWriteRequest(logicalDevice, logicalNode, dataAttribute, valueStr);
                _stream.Write(mmsRequest, 0, mmsRequest.Length);

                var responseBuffer = new byte[1024];
                var bytesRead = _stream.Read(responseBuffer, 0, responseBuffer.Length);
                if (bytesRead <= 0)
                {
                    throw new InvalidOperationException("MMS write failed: no response from IED");
                }

                if (!IsLikelyConfirmedResponse(responseBuffer, bytesRead))
                {
                    throw new InvalidOperationException("MMS write failed: unexpected response frame from IED");
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"MMS write error: {ex.Message}", ex);
            }
        }

        private byte[] BuildMmsWriteRequest(string ld, string ln, string da, string valueStr)
        {
            // 简化的 MMS Write Request
            // 格式: invoke-id (1), service (1), object-spec (variable), data (variable)
            using var ms = new MemoryStream();
            
            // MMS Confirmed Request PDU (tag 0xA0)
            ms.WriteByte(MMS_PDU_CONFIRMED);
            
            var confirmedRequest = new MemoryStream();
            
            // Invoke ID
            confirmedRequest.WriteByte(0x02);  // Integer
            confirmedRequest.WriteByte(0x01);  // Length 1
            confirmedRequest.WriteByte(_invokeId);
            
            // Service: Write
            confirmedRequest.WriteByte(0x4B);  // Write
            confirmedRequest.WriteByte(0x00);  // Length placeholder
            
            // Object Specification (Variable Access Specification)
            confirmedRequest.WriteByte(0x28);  // listOfAccessSelection
            
            // Domain Name (Logical Device)
            confirmedRequest.WriteByte(0xA0);  // context tag for domain name
            var domainName = Encoding.ASCII.GetBytes(ld);
            confirmedRequest.WriteByte(0x80);  // Object Name (domain)
            confirmedRequest.WriteByte((byte)domainName.Length);
            confirmedRequest.Write(domainName, 0, domainName.Length);
            
            // Item ID (Logical Node.DataAttribute)
            var itemIdStr = $"{ln}.{da}";
            var itemId = Encoding.ASCII.GetBytes(itemIdStr);
            confirmedRequest.WriteByte(0xA1);  // context tag for itemId
            confirmedRequest.WriteByte(0x80);  // Object Name (vnam)
            confirmedRequest.WriteByte((byte)itemId.Length);
            confirmedRequest.Write(itemId, 0, itemId.Length);
            
            // Data Value
            confirmedRequest.WriteByte(0x80);  // type specification
            if (double.TryParse(valueStr, out double dVal))
            {
                confirmedRequest.WriteByte(0x86);  // Floating Point
                var floatBytes = BitConverter.GetBytes(dVal);
                confirmedRequest.WriteByte(0x08);  // Length 8
                confirmedRequest.Write(floatBytes, 0, floatBytes.Length);
            }
            else if (int.TryParse(valueStr, out int iVal))
            {
                confirmedRequest.WriteByte(0x02);  // Integer
                confirmedRequest.WriteByte(0x04);  // Length 4
                var intBytes = BitConverter.GetBytes(iVal);
                confirmedRequest.Write(intBytes, 0, intBytes.Length);
            }
            else
            {
                // Default to Visible String
                var strBytes = Encoding.ASCII.GetBytes(valueStr);
                confirmedRequest.WriteByte(0x9A);  // Visible String
                confirmedRequest.WriteByte((byte)strBytes.Length);
                confirmedRequest.Write(strBytes, 0, strBytes.Length);
            }
            
            // Get confirmed request bytes and calculate length
            var crBytes = confirmedRequest.ToArray();
            
            // Update length at position 3
            ms.WriteByte((byte)crBytes.Length);
            ms.Write(crBytes, 0, crBytes.Length);
            
            return ms.ToArray();
        }

        private static bool IsLikelyConfirmedResponse(byte[] responseBuffer, int bytesRead)
        {
            if (bytesRead <= 0)
            {
                return false;
            }

            for (var i = 0; i < bytesRead; i++)
            {
                var current = responseBuffer[i];
                if (current == 0xA1 || current == 0xA2 || current == 0xA3)
                {
                    return true;
                }
            }

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sendLock.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>IEC 61850 MMS 协议特定的错误分类</summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            return level switch
            {
                OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, "IEC 61850 MMS connected", "No action needed"),
                OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, "IEC 61850 MMS connection retrying", "Check IED device network connection"),
                OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, "IEC 61850 MMS connection failed", "Check IED device IP, port, and firewall settings"),
                OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, "IEC 61850 MMS configuration error", "Check logical device name and MMS configuration"),
                _ => (GatewayErrorCodes.InternalException, "IEC 61850 MMS unknown error", "Check logs for details")
            };
        }
    }
}
