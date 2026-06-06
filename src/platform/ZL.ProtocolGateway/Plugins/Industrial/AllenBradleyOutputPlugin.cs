using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// Allen Bradley (EtherNet/IP) Output 插件配置
    /// 用于将数据转发到 Allen Bradley PLC (CompactLogix, ControlLogix, Micro800 系列)
    /// </summary>
    public class AllenBradleyOutputConfig
    {
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// 目标 PLC IP 地址
        /// </summary>
        public string ServerIp { get; set; } = "127.0.0.1";
        
        /// <summary>
        /// EtherNet/IP 端口，默认 44818
        /// </summary>
        public int Port { get; set; } = 44818;
        
        /// <summary>
        /// PLC 槽位号 (仅用于 ControlLogix/CompactLogix)
        /// </summary>
        public int Slot { get; set; } = 0;
        
        /// <summary>
        /// 连接超时 (毫秒)
        /// </summary>
        public int ConnectTimeoutMs { get; set; } = 5000;
        
        /// <summary>
        /// 发送超时 (毫秒)
        /// </summary>
        public int SendTimeoutMs { get; set; } = 3000;
        
        /// <summary>
        /// 连接重试间隔 (毫秒)
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 5000;
        
        /// <summary>
        /// 错误告警阈值（连续失败多少次后触发 Error 级别）
        /// </summary>
        public int ErrorThreshold { get; set; } = 10;
        
        /// <summary>
        /// 路径 (用于 CIP 连接，例如 "1,0" 表示槽位1，CPU)
        /// </summary>
        public string CipPath { get; set; } = "1,0";

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
            if (Slot < 0 || Slot > 255)
                errors.Add(new ConfigValidationError(nameof(Slot), $"Slot 无效: {Slot} (0-255)"));
            if (!ConfigValidation.IsValidTimeout(ConnectTimeoutMs))
                errors.Add(new ConfigValidationError(nameof(ConnectTimeoutMs), $"ConnectTimeoutMs 无效: {ConnectTimeoutMs}"));
            if (!ConfigValidation.IsValidTimeout(SendTimeoutMs))
                errors.Add(new ConfigValidationError(nameof(SendTimeoutMs), $"SendTimeoutMs 无效: {SendTimeoutMs}"));
            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
                errors.Add(new ConfigValidationError(nameof(ReconnectIntervalMs), $"ReconnectIntervalMs 无效: {ReconnectIntervalMs}"));
            if (!ConfigValidation.IsValidErrorThreshold(ErrorThreshold))
                errors.Add(new ConfigValidationError(nameof(ErrorThreshold), $"ErrorThreshold 无效: {ErrorThreshold}"));
            return errors;
        }
    }

    /// <summary>
    /// Allen Bradley (EtherNet/IP) Output 插件
    /// 将消息转发到 Allen Bradley PLC
    /// 支持 CIP 协议 Write 命令
    /// </summary>
    public class AllenBradleyOutputPlugin : IndustrialOutputPluginBase
    {
        // EtherNet/IP CIP 协议常量
        private const ushort EIP_PORT = 44818;
        private const byte CIP_SERVICE_CODE_WRITE = 0x4D;
        private const byte CIP_SERVICE_CODE_READ = 0x4C;
        private const byte CIP_CLASS_DATA = 0xA0;  // CIP Data class
        private const byte CIP_INSTANCE_DATA = 0x24;
        
        private readonly AllenBradleyOutputConfig _config;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private uint _sessionHandle;
        // P1 修复：异步串行化发送，防止多线程并发 Send 时竞争同一 NetworkStream（TOCTOU 竞态）
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public override string Name { get; }
        public override string ProtocolType => "AllenBradley";

        public AllenBradleyOutputPlugin(AllenBradleyOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
            Name = string.IsNullOrWhiteSpace(config.Name) 
                ? $"AB-{_config.ServerIp}:{_config.Port}" 
                : config.Name;
        }

        /// <summary>
        /// 建立 TCP 连接、注册 EtherNet/IP 会话，并保持连接直到断开或取消
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
            _stream.ReadTimeout = _config.SendTimeoutMs;
            _stream.WriteTimeout = _config.SendTimeoutMs;

            // 发送注册会话请求 (EtherNet/IP Encapsulation)
            if (!await RegisterSessionAsync())
            {
                throw new InvalidOperationException("Allen-Bradley Register Session failed");
            }

            // 保持连接直到断开或取消（NOP 保活 + 读取检测断开）
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
                            // 连接被对端关闭
                            break;
                        }
                    }
                    else
                    {
                        // 读取超时，发送 NOP 保持连接
                        await SendNopAsync();
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // 连接异常，退出由基类重连
                    break;
                }
            }
        }

        /// <summary>注册 EtherNet/IP 会话</summary>
        private async Task<bool> RegisterSessionAsync()
        {
            // P0-2 修复：Register 会话写入也必须串行化，与 SendAsync 共享同一 _stream
            await _sendLock.WaitAsync(CancellationToken.None);
            try
            {
                if (_stream == null) return false;

                try
                {
                    var registerRequest = new byte[]
                    {
                        0x65, 0x00,
                        0x04, 0x00,
                        0x00, 0x00, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00,
                        0x01, 0x00,
                        0x00, 0x00
                    };

                    await _stream.WriteAsync(registerRequest, 0, registerRequest.Length);

                    var response = new byte[28];
                    var bytesRead = await _stream.ReadAsync(response, 0, response.Length);

                    if (bytesRead >= 24)
                    {
                        var status = BitConverter.ToUInt32(response, 8);
                        if (status == 0)
                        {
                            _sessionHandle = BitConverter.ToUInt32(response, 4);
                            return _sessionHandle != 0;
                        }
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("AllenBradleyOutput", $"Register session failed: {ex.Message}", ex);
                    return false;
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>发送 NOP 命令保持连接活跃</summary>
        private async Task SendNopAsync()
        {
            await _sendLock.WaitAsync(CancellationToken.None);
            try
            {
                if (_stream == null || !_tcpClient!.Connected) return;

                try
                {
                    var nopRequest = new byte[]
                    {
                        0x00, 0x00,
                        0x00, 0x00,
                        (byte)(_sessionHandle & 0xFF),
                        (byte)((_sessionHandle >> 8) & 0xFF),
                        (byte)((_sessionHandle >> 16) & 0xFF),
                        (byte)((_sessionHandle >> 24) & 0xFF),
                        0x00, 0x00, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                        0x00, 0x00, 0x00, 0x00
                    };

                    await _stream.WriteAsync(nopRequest, 0, nopRequest.Length);
                }
                catch
                {
                    // NOP 失败忽略，下次循环会检测到连接断开
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>检查 TCP 连接和会话是否仍然有效</summary>
        protected override bool HasLiveConnection()
        {
            return _tcpClient != null && _tcpClient.Connected && _sessionHandle != 0;
        }

        /// <summary>同步清理 TCP 连接资源</summary>
        protected override void CleanupConnection()
        {
            try { _stream?.Close(); } catch { }
            _stream = null;
            try { _tcpClient?.Close(); _tcpClient?.Dispose(); } catch { }
            _tcpClient = null;
            _sessionHandle = 0;
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null || Status != PluginStatus.Running)
            {
                return;
            }

            // P1 修复：异步串行化整个发送流程，确保 stream 获取 → null 检查 → WriteTag
            // 在同一个异步临界区内完成，避免多线程并发 Send 时竞争同一 NetworkStream（TOCTOU 竞态条件修复）
            await _sendLock.WaitAsync();
            try
            {
                if (_stream == null)
                {
                    return;
                }

                // 优先使用协议无关的 TagWrite 列表（解决 Bridge→AllenBradley 消息格式不匹配的"暗契约"问题）
                if (message.Writes != null && message.Writes.Count > 0)
                {
                    var tw = message.Writes[0];
                    if (TryParseAndWrite(tw.Address + "=" + (tw.Value?.ToString() ?? ""), out var twErrorMessage))
                    {
                        ResetConnectFailureStreak();
                        SetLastException(null);
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, "Allen Bradley write successful (via TagWrite)");
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

                // 解析消息格式: "tagname=value" 或 "tagname,value"
                if (TryParseAndWrite(payloadString, out var errorMessage))
                {
                    ResetConnectFailureStreak();
                    SetLastException(null);
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, "Allen Bradley write successful");
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

                var errorMessage = $"Allen Bradley send error: {ex.Message}";
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
                // 格式: tagname=value 或 tagname,value
                // 例如: "PLC1.MyTag=25.5" 或 "MyDINT,100"
                string tagName;
                string valueStr;
                
                if (payload.Contains("="))
                {
                    var parts = payload.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                    {
                        errorMessage = $"Invalid Allen Bradley payload format: {payload}";
                        return false;
                    }
                    tagName = parts[0].Trim();
                    valueStr = parts[1].Trim();
                }
                else if (payload.Contains(","))
                {
                    var parts = payload.Split(new[] { ',' }, 2);
                    if (parts.Length != 2)
                    {
                        errorMessage = $"Invalid Allen Bradley payload format: {payload}";
                        return false;
                    }
                    tagName = parts[0].Trim();
                    valueStr = parts[1].Trim();
                }
                else
                {
                    errorMessage = $"Invalid Allen Bradley payload format: {payload}";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(tagName))
                {
                    errorMessage = "Allen Bradley tag name cannot be empty";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(valueStr))
                {
                    errorMessage = "Allen Bradley tag value cannot be empty";
                    return false;
                }

                if (tagName.Any(char.IsWhiteSpace))
                {
                    errorMessage = $"Allen Bradley tag name cannot contain whitespace: {tagName}";
                    return false;
                }
                
                return WriteTag(tagName, valueStr);
            }
            catch (Exception ex)
            {
                errorMessage = $"Allen Bradley parse error: {ex.Message}";
                return false;
            }
        }

        private bool WriteTag(string tagName, string valueStr)
        {
            if (_stream == null || _tcpClient?.Connected != true)
            {
                throw new InvalidOperationException("Not connected to PLC");
            }

            try
            {
                // 构造 CIP Write Request
                var tagBytes = Encoding.ASCII.GetBytes(tagName);
                
                // CIP Header
                var cipRequest = new MemoryStream();
                
                // Item count: 2 (Address + Data)
                cipRequest.WriteByte(0x02);
                cipRequest.WriteByte(0x02);
                
                // Address Item (Null Address)
                cipRequest.WriteByte(0x00);
                cipRequest.WriteByte(0x00);  // Type ID: Null Address
                cipRequest.WriteByte(0x00);
                cipRequest.WriteByte(0x00);  // Length: 0
                
                // Data Item (CIP Request)
                cipRequest.WriteByte(0xB2);  // Type ID: Unconnected Data
                var dataSizePos = cipRequest.Position;
                cipRequest.WriteByte(0x00);  // Placeholder for length
                cipRequest.WriteByte(0x00);
                
                // CIP Request (around the data item header)
                cipRequest.WriteByte(CIP_SERVICE_CODE_WRITE);  // Service: Write
                cipRequest.WriteByte(0x00);  // Request path size (in words)
                cipRequest.WriteByte(CIP_INSTANCE_DATA);  // Class: 0xA0 (Data)
                cipRequest.WriteByte(CIP_INSTANCE_DATA);  // Instance: 0x24
                
                // Tag path (simplified - tag name as ASCII)
                cipRequest.WriteByte((byte)(tagBytes.Length + 1));  // Path size
                cipRequest.WriteByte(0x91);  // Symbol segment: 8-bit ASCII
                cipRequest.Write(tagBytes, 0, tagBytes.Length);
                cipRequest.WriteByte(0x00);  // Null terminator
                
                // Pad to even byte boundary
                if ((tagBytes.Length + 1) % 2 != 0)
                {
                    cipRequest.WriteByte(0x00);
                }
                
                // Number of elements
                cipRequest.WriteByte(0x01);
                cipRequest.WriteByte(0x00);
                
                // Data type and value
                if (double.TryParse(valueStr, out double doubleValue))
                {
                    // REAL (32-bit float)
                    cipRequest.WriteByte(0xCA);  // Type: REAL
                    cipRequest.WriteByte(0x00);
                    var floatBytes = BitConverter.GetBytes((float)doubleValue);
                    cipRequest.Write(floatBytes, 0, floatBytes.Length);
                }
                else if (int.TryParse(valueStr, out int intValue))
                {
                    // DINT (32-bit signed int)
                    cipRequest.WriteByte(0xC4);  // Type: DINT
                    cipRequest.WriteByte(0x00);
                    var intBytes = BitConverter.GetBytes(intValue);
                    cipRequest.Write(intBytes, 0, intBytes.Length);
                }
                else if (valueStr.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                         valueStr.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    // BOOL
                    cipRequest.WriteByte(0xC1);  // Type: BOOL
                    cipRequest.WriteByte(0x00);
                    cipRequest.WriteByte(valueStr.Equals("true", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)0);
                    cipRequest.WriteByte(0x00);
                }
                else
                {
                    // STRING
                    cipRequest.WriteByte(0xDA);  // Type: STRING
                    cipRequest.WriteByte(0x00);
                    var strBytes = Encoding.ASCII.GetBytes(valueStr.PadRight(82).Substring(0, 82));
                    cipRequest.Write(strBytes, 0, strBytes.Length);
                }
                
                // Update data size in header
                var dataSize = (int)cipRequest.Position - 4;
                cipRequest.Position = dataSizePos;
                cipRequest.WriteByte((byte)(dataSize & 0xFF));
                cipRequest.WriteByte((byte)((dataSize >> 8) & 0xFF));
                
                // EtherNet/IP Encapsulation Header
                var encapRequest = new MemoryStream();
                encapRequest.WriteByte(0x6F);  // Command: Send RR Data
                encapRequest.WriteByte(0x00);
                
                var payloadLength = (int)cipRequest.Position + 8;  // +8 for RR Data header
                encapRequest.WriteByte((byte)(payloadLength & 0xFF));
                encapRequest.WriteByte((byte)((payloadLength >> 8) & 0xFF));
                
                encapRequest.WriteByte((byte)(_sessionHandle & 0xFF));
                encapRequest.WriteByte((byte)((_sessionHandle >> 8) & 0xFF));
                encapRequest.WriteByte((byte)((_sessionHandle >> 16) & 0xFF));
                encapRequest.WriteByte((byte)((_sessionHandle >> 24) & 0xFF));
                
                var statusContextOptions = new byte[8];
                encapRequest.Write(statusContextOptions, 0, statusContextOptions.Length);  // Status + Context + Options
                
                // Interface Handle (0x00000000 for CIP)
                encapRequest.WriteByte(0x00);
                encapRequest.WriteByte(0x00);
                encapRequest.WriteByte(0x00);
                encapRequest.WriteByte(0x00);
                
                // Timeout
                encapRequest.WriteByte(0x00);
                encapRequest.WriteByte(0x00);
                
                // Item Count: 2
                encapRequest.WriteByte(0x02);
                encapRequest.WriteByte(0x00);
                
                // Item 1: Connected Address (type 0xA1)
                encapRequest.WriteByte(0xA1);
                encapRequest.WriteByte(0x00);
                encapRequest.WriteByte(0x04);  // Length: 4
                encapRequest.WriteByte(0x00);
                var connectionId = new byte[] { 0x00, 0x00, 0x00, 0x00 };
                encapRequest.Write(connectionId, 0, connectionId.Length);  // Connection ID
                
                // Item 2: Connected Data (type 0xB2) - length placeholder
                encapRequest.WriteByte(0xB2);
                encapRequest.WriteByte(0x00);
                var connectedDataLenPos = encapRequest.Position;
                encapRequest.WriteByte(0x00);
                encapRequest.WriteByte(0x00);
                
                // Copy CIP data
                cipRequest.Position = 0;
                cipRequest.CopyTo(encapRequest);
                
                // Update connected data length
                var totalLen = (int)encapRequest.Position - 2;
                encapRequest.Position = connectedDataLenPos;
                encapRequest.WriteByte((byte)(totalLen & 0xFF));
                encapRequest.WriteByte((byte)((totalLen >> 8) & 0xFF));
                
                // Send request
                encapRequest.Position = 0;
                var requestBytes = encapRequest.ToArray();
                
                // Update encapsulation length
                var encapLen = requestBytes.Length - 4;
                requestBytes[2] = (byte)(encapLen & 0xFF);
                requestBytes[3] = (byte)((encapLen >> 8) & 0xFF);
                
                _stream.Write(requestBytes, 0, requestBytes.Length);
                
                // Read response
                var responseBuffer = new byte[1024];
                var bytesRead = _stream.Read(responseBuffer, 0, responseBuffer.Length);
                
                if (bytesRead > 0)
                {
                    // 检查响应状态
                    // 响应格式: Command (2), Length (2), Session Handle (4), Status (4), ... 
                    var status = BitConverter.ToUInt32(responseBuffer, 16);
                    if (status == 0)
                    {
                        return true;
                    }
                    else
                    {
                        throw new InvalidOperationException($"CIP write failed with status: 0x{status:X8}");
                    }
                }
                
                throw new InvalidOperationException("No response from PLC");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Write error: {ex.Message}", ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sendLock?.Dispose();
            }
            base.Dispose(disposing);
        }

/// <summary>Allen Bradley (EtherNet/IP) 协议特定的错误分类</summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            return level switch
            {
                OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, "Allen Bradley connected", "No action needed"),
                OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, "Allen Bradley connection retrying", "Check PLC device network connection"),
                OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, "Allen Bradley connection failed", "Check PLC IP, port, and slot configuration"),
                OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, "Allen Bradley configuration error", "Check Allen Bradley output configuration"),
                _ => (GatewayErrorCodes.InternalException, "Allen Bradley unknown error", "Check logs for details")
            };
        }
    }
}
