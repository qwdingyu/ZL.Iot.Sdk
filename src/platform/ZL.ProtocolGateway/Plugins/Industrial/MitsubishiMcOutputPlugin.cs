using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// Mitsubishi MC 输出插件配置
    /// </summary>
    public class MitsubishiMcOutputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// PLC IP 地址
        /// </summary>
        public string ServerIp { get; set; } = "192.168.1.10";

        /// <summary>
        /// 端口号 (默认 5000)
        /// </summary>
        public int Port { get; set; } = 5000;

        /// <summary>
        /// 网络号 (0-255)
        /// </summary>
        public byte NetworkNumber { get; set; } = 0;

        /// <summary>
        /// PC 编号 (0-255)
        /// </summary>
        public byte PcNumber { get; set; } = 0xFF;

        /// <summary>
        /// 目标 IO 编号 (0-255)
        /// </summary>
        public byte DestinationIoNumber { get; set; } = 0xFF;

        /// <summary>
        /// 目标站号 (0-255)
        /// </summary>
        public byte DestinationStationNumber { get; set; } = 0;

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
        /// 错误告警阈值
        /// </summary>
        public int ErrorThreshold { get; set; } = 10;

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
    /// Mitsubishi MC (3E Binary) 输出插件
    /// 支持连接到三菱 PLC 并写入数据 (D区, X区, Y区, M区, W区等)
    /// </summary>
    public class MitsubishiMcOutputPlugin : IndustrialOutputPluginBase
    {
        private readonly MitsubishiMcOutputConfig _config;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        // 异步发送串行化锁：防止多线程并发 Send 时竞争同一 NetworkStream（TOCTOU 竞态）
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        // 心跳机制：定期发送读取请求保持连接活跃
        private DateTime _lastHeartbeat;

        public override string Name { get; }
        public override string ProtocolType => "MitsubishiMc";

        public MitsubishiMcOutputPlugin(MitsubishiMcOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
            Name = config.Name ?? $"MitsubishiMc-{config.ServerIp}:{config.Port}";
        }

        /// <summary>
        /// 建立 TCP 连接并等待直到连接断开或取消
        /// <para>基类 ConnectionLoopAsync 调用此方法，返回后自动调用 CleanupConnection</para>
        /// </summary>
        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            var tcpClient = new TcpClient();

            var connectTask = tcpClient.ConnectAsync(_config.ServerIp, _config.Port);
            var timeoutTask = Task.Delay(_config.ConnectTimeoutMs, ct);
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask != connectTask)
            {
                tcpClient.Dispose();
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
                throw new TimeoutException($"Connect timeout after {_config.ConnectTimeoutMs} ms");
            }

            await connectTask;
            tcpClient.ReceiveTimeout = _config.SendTimeoutMs;
            tcpClient.SendTimeout = _config.SendTimeoutMs;

            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _lastHeartbeat = DateTime.UtcNow;

            // 等待连接断开或取消请求，期间定期发送心跳
            while (!ct.IsCancellationRequested && HasLiveConnection())
            {
                // 心跳机制：每隔 30 秒发送读取请求保持连接
                if ((DateTime.UtcNow - _lastHeartbeat).TotalSeconds >= 30)
                {
                    try
                    {
                        await SendHeartbeatAsync(_stream!);
                        _lastHeartbeat = DateTime.UtcNow;
                    }
                    catch (Exception hbEx)
                    {
                        GatewayLog.Warn("MitsubishiMcOutput", $"Heartbeat failed: {hbEx.Message}");
                        break;
                    }
                }
                await Task.Delay(1000, ct);
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
            try { _stream?.Dispose(); } catch { }
            _stream = null;
            try { _tcpClient?.Dispose(); } catch { }
            _tcpClient = null;
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            // 使用 SemaphoreSlim 异步串行化整个发送流程，
            // 确保 stream 获取 → null 检查 → WriteDeviceAsync 在同一个异步临界区内完成，
            // 避免多线程并发 Send 时竞争同一 NetworkStream（TOCTOU 竞态条件修复）
            await _sendLock.WaitAsync();
            try
            {
                var stream = _stream;
                var client = _tcpClient;

                if (stream == null || client == null || !client.Connected)
                {
                    throw new InvalidOperationException("Not connected to Mitsubishi PLC");
                }

                // 优先使用协议无关的 TagWrite 列表（解决 Bridge→Mitsubishi 消息格式不匹配的"暗契约"问题）
                if (message.Writes != null && message.Writes.Count > 0)
                {
                    var tw = message.Writes[0];
                    var (twDeviceCode, twAddress) = ParseDeviceAddress(tw.Address);
                    short twValue;
                    if (tw.Value is short sv)
                        twValue = sv;
                    else if (tw.Value is int iv)
                        twValue = (short)iv;
                    else
                        twValue = short.TryParse(tw.Value?.ToString(), out var parsed) ? parsed : (short)0;

                    await WriteDeviceAsync(stream, twDeviceCode, twAddress, twValue);
                    return;
                }

                var data = message.Payload;
                if (data == null || data.Length == 0)
                {
                    var textContent = message.GetTextContent();
                    if (!string.IsNullOrEmpty(textContent))
                    {
                        data = Encoding.UTF8.GetBytes(textContent);
                    }
                    else
                    {
                        return;
                    }
                }

                // 解析消息格式: "D100:123" 或 "D100;123" 表示 D区 地址100 写入值123
                var content = Encoding.UTF8.GetString(data);
                var parts = content.Contains(";") ? content.Split(';') : content.Split(':');

                if (parts.Length < 2)
                {
                    GatewayLog.Info("MitsubishiMcOutput", $"Invalid message format: {content}");
                    return;
                }

                var addressPart = parts[0].Trim();
                var valuePart = parts[1].Trim();

                // 解析地址 (例如 D100, X10, Y20, M100, W100)
                var (deviceCode, address) = ParseDeviceAddress(addressPart);
                var value = short.TryParse(valuePart, out var v) ? v : (short)0;

                await WriteDeviceAsync(stream, deviceCode, address, value);
            }
            catch (Exception ex)
            {
                GatewayLog.Error("MitsubishiMcOutput", $"Send error: {ex.Message}");
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task WriteDeviceAsync(NetworkStream stream, byte deviceCode, int address, short value)
        {
            // 构建 3E Binary 批量写入请求帧
            // 50 00 [Network] [PC] [IO] [Station] [LenL] [LenH] [CmdL] [CmdH] [SubCmdL] [SubCmdH] [Addr3] [Addr2] [Addr1] [Code] [CountL] [CountH] [Data...]

            var requestFrame = new byte[27];
            int index = 0;

            // Header
            requestFrame[index++] = 0x50;
            requestFrame[index++] = 0x00;

            // 网络号
            requestFrame[index++] = _config.NetworkNumber;

            // PC 编号
            requestFrame[index++] = _config.PcNumber;

            // 目标 IO 编号
            requestFrame[index++] = (byte)((_config.DestinationIoNumber >> 8) & 0xFF);
            requestFrame[index++] = (byte)(_config.DestinationIoNumber & 0xFF);

            // 目标站号
            requestFrame[index++] = _config.DestinationStationNumber;

            // 数据长度 (从 Command 到 Data) = 16 bytes + 2 bytes data
            short dataLength = 18;
            requestFrame[index++] = (byte)(dataLength & 0xFF);
            requestFrame[index++] = (byte)((dataLength >> 8) & 0xFF);

            // Command: 批量写入 0x1401
            requestFrame[index++] = 0x01;
            requestFrame[index++] = 0x14;

            // SubCommand: 批量写入字设备 0x0002
            requestFrame[index++] = 0x02;
            requestFrame[index++] = 0x00;

            // 地址 (3 bytes, big-endian)
            requestFrame[index++] = (byte)((address >> 16) & 0xFF);
            requestFrame[index++] = (byte)((address >> 8) & 0xFF);
            requestFrame[index++] = (byte)(address & 0xFF);

            // 设备代码
            requestFrame[index++] = deviceCode;

            // 写入点数 (1)
            requestFrame[index++] = 0x01;
            requestFrame[index++] = 0x00;

            // 写入值 (2 bytes, big-endian)
            var valueBytes = BitConverter.GetBytes(value);
            Array.Reverse(valueBytes); // Big-endian
            requestFrame[index++] = valueBytes[0];
            requestFrame[index++] = valueBytes[1];

            // 发送请求
            await stream.WriteAsync(requestFrame, 0, requestFrame.Length);

            // 读取响应
            var responseBuffer = new byte[1024];
            var readTask = stream.ReadAsync(responseBuffer, 0, responseBuffer.Length);

            var completedTask = await Task.WhenAny(readTask, Task.Delay(_config.SendTimeoutMs));
            if (completedTask != readTask)
            {
                throw new TimeoutException("Write timeout - no response from PLC");
            }

            var bytesRead = await readTask;
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed by PLC");
            }

            // 检查响应状态
            // 响应格式: D0 00 [Network] [PC] [IO] [Station] [LenL] [LenH] [EndCodeL] [EndCodeH]
            if (responseBuffer[0] == 0xD0 && responseBuffer[1] == 0x00)
            {
                var endCode = BitConverter.ToUInt16(responseBuffer, 8);
                if (endCode != 0)
                {
                    GatewayLog.Error("MitsubishiMcOutput", $"Write failed with end code: {endCode:X4}");
                }
            }
        }

        /// <summary>
        /// 发送心跳请求：读取 D0 寄存器一个字，用于保持连接活跃
        /// </summary>
        private async Task SendHeartbeatAsync(NetworkStream stream)
        {
            // 构建 3E Binary 批量读取请求帧 (读取 D0 一个字)
            var requestFrame = new byte[25];
            int index = 0;

            // Header
            requestFrame[index++] = 0x50;
            requestFrame[index++] = 0x00;

            // 网络号
            requestFrame[index++] = _config.NetworkNumber;

            // PC 编号
            requestFrame[index++] = _config.PcNumber;

            // 目标 IO 编号
            requestFrame[index++] = (byte)((_config.DestinationIoNumber >> 8) & 0xFF);
            requestFrame[index++] = (byte)(_config.DestinationIoNumber & 0xFF);

            // 目标站号
            requestFrame[index++] = _config.DestinationStationNumber;

            // 数据长度 (从 Command 到 CountH) = 16 bytes
            short dataLength = 16;
            requestFrame[index++] = (byte)(dataLength & 0xFF);
            requestFrame[index++] = (byte)((dataLength >> 8) & 0xFF);

            // Command: 批量读取 0x0401
            requestFrame[index++] = 0x01;
            requestFrame[index++] = 0x04;

            // SubCommand: 批量读取字设备 0x0002
            requestFrame[index++] = 0x02;
            requestFrame[index++] = 0x00;

            // 地址 D0 (3 bytes, big-endian)
            requestFrame[index++] = 0x00;
            requestFrame[index++] = 0x00;
            requestFrame[index++] = 0x00;

            // 设备代码: D区 0xA8
            requestFrame[index++] = 0xA8;

            // 读取点数 (1)
            requestFrame[index++] = 0x01;
            requestFrame[index++] = 0x00;

            // 发送请求
            await stream.WriteAsync(requestFrame, 0, requestFrame.Length);

            // 读取响应
            var responseBuffer = new byte[1024];
            var readTask = stream.ReadAsync(responseBuffer, 0, responseBuffer.Length);

            var completedTask = await Task.WhenAny(readTask, Task.Delay(_config.SendTimeoutMs));
            if (completedTask != readTask)
            {
                throw new TimeoutException("Heartbeat timeout - no response from PLC");
            }

            var bytesRead = await readTask;
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed by PLC");
            }
        }

        /// <summary>
        /// 解析设备地址
        /// 支持格式: D100, X10, Y20, M100, W100, B100, L100, etc.
        /// </summary>
        private (byte deviceCode, int address) ParseDeviceAddress(string addressStr)
        {
            if (string.IsNullOrEmpty(addressStr))
            {
                return (0xA8, 0); // 默认 D区
            }

            // 提取设备代码和地址
            var code = addressStr[0];
            var addressStrPart = addressStr.Substring(1);
            var address = int.TryParse(addressStrPart, out var addr) ? addr : 0;

            // 设备代码映射 (3E Binary 协议)
            byte deviceCode = code switch
            {
                'D' or 'd' => 0xA8,  // D区 (数据寄存器)
                'X' or 'x' => 0x9C,  // X区 (输入)
                'Y' or 'y' => 0x9D,  // Y区 (输出)
                'M' or 'm' => 0x90,  // M区 (辅助继电器)
                'L' or 'l' => 0x92,  // L区 (锁存继电器)
                'W' or 'w' => 0xB4,  // W区 (链接寄存器)
                'B' or 'b' => 0xA0,  // B区 (链接继电器)
                'F' or 'f' => 0x93,  // F区 (报警器)
                'V' or 'v' => 0x94,  // V区 (边沿继电器)
                'S' or 's' => 0x98,  // S区 (步进继电器)
                'Z' or 'z' => 0xAF,  // Z区 (文件寄存器)
                'R' or 'r' => 0xAF,  // R区 (文件寄存器，同 Z)
                _ => 0xA8             // 默认 D区
            };

            return (deviceCode, address);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sendLock?.Dispose();
            }
            base.Dispose(disposing);
        }

/// <summary>Mitsubishi MC 协议特定的错误分类</summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            return level switch
            {
                OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, "Mitsubishi MC connected", "No action needed"),
                OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, "Mitsubishi MC connection retrying", "Check PLC device network connection"),
                OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, "Mitsubishi MC connection failed", "Check PLC IP, port, and network settings"),
                OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, "Mitsubishi MC configuration error", "Check Mitsubishi MC output configuration"),
                _ => (GatewayErrorCodes.InternalException, "Mitsubishi MC unknown error", "Check logs for details")
            };
        }
    }
}
