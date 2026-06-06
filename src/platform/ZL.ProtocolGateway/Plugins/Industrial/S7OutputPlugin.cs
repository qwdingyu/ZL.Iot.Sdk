using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins.Shared;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// Siemens S7 输出插件配置
    /// </summary>
    public class S7OutputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 目标 PLC IP 地址
        /// </summary>
        public string ServerIp { get; set; }

        /// <summary>
        /// 目标 PLC 端口 (默认 102)
        /// </summary>
        public int Port { get; set; } = 102;

        /// <summary>
        /// PLC 机架号 (通常为 0)
        /// </summary>
        public byte Rack { get; set; } = 0;

        /// <summary>
        /// PLC 插槽号 (S7-1200 通常为 1, S7-300/400 通常为 2)
        /// </summary>
        public byte Slot { get; set; } = 1;

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
        /// 本地目标首次监听宽限期 (毫秒)
        /// </summary>
        public int LocalStartupQuietPeriodMs { get; set; } = 5000;

        /// <summary>
        /// 默认写入地址（当消息 Metadata 中无 TargetAddress 时使用）
        /// 支持格式: DB1.DBW0, DB1.DBB10, DB1.DBX0.0, M0, I0.0, Q0
        /// </summary>
        public string DefaultWriteAddress { get; set; } = "DB1.DBW0";

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();

            if (string.IsNullOrEmpty(ServerIp))
            {
                errors.Add(new ConfigValidationError("ServerIp", "Host address is required"));
            }
            else if (!ConfigValidation.IsValidIpAddress(ServerIp) && !IsValidHostname(ServerIp))
            {
                errors.Add(new ConfigValidationError("ServerIp", $"Invalid IP address or hostname: {ServerIp}"));
            }

            if (!ConfigValidation.IsValidPort(Port))
            {
                errors.Add(new ConfigValidationError("Port", $"Port must be between 1 and 65535, got {Port}"));
            }

            if (Rack < 0 || Rack > 7)
            {
                errors.Add(new ConfigValidationError("Rack", $"Rack must be between 0 and 7, got {Rack}"));
            }

            if (Slot < 0 || Slot > 31)
            {
                errors.Add(new ConfigValidationError("Slot", $"Slot must be between 0 and 31, got {Slot}"));
            }

            if (!ConfigValidation.IsValidTimeout(ConnectTimeoutMs))
            {
                errors.Add(new ConfigValidationError("ConnectTimeoutMs", $"Invalid connect timeout: {ConnectTimeoutMs}"));
            }

            if (!ConfigValidation.IsValidTimeout(SendTimeoutMs))
            {
                errors.Add(new ConfigValidationError("SendTimeoutMs", $"Invalid send timeout: {SendTimeoutMs}"));
            }

            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
            {
                errors.Add(new ConfigValidationError("ReconnectIntervalMs", $"Invalid reconnect interval: {ReconnectIntervalMs}"));
            }

            if (!ConfigValidation.IsValidErrorThreshold(ErrorThreshold))
            {
                errors.Add(new ConfigValidationError("ErrorThreshold", $"Invalid error threshold: {ErrorThreshold}"));
            }

            return errors;
        }

        private static bool IsValidHostname(string host)
        {
            if (host.Length > 253)
                return false;

            foreach (char c in host)
            {
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '.')
                    return false;
            }

            if (host[0] == '-' || host[0] == '.' || host[host.Length - 1] == '-' || host[host.Length - 1] == '.')
                return false;

            return true;
        }
    }

    /// <summary>
    /// Siemens S7 输出插件 - 将消息以 S7 协议转发到 Siemens PLC
    /// 支持 S7-1200, S7-1500, S7-300, S7-400 系列
    /// </summary>
    public class S7OutputPlugin : IndustrialOutputPluginBase
    {
        private readonly S7OutputConfig _config;
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _hasConnectedOnce;
        private int _pduReference = 0x1234; // int for Interlocked.Increment; cast to ushort when building PDU
        private readonly SemaphoreSlim _sendLock = new(1, 1); // 防止多消息并发写入同一 TCP 流导致字节交错

        public override string Name { get; }
        public override string ProtocolType => "S7";

        protected override int ErrorThreshold => _config.ErrorThreshold;
        protected override int BaseReconnectIntervalMs => _config.ReconnectIntervalMs;

        public S7OutputPlugin(S7OutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
            Name = config.Name ?? $"S7-{config.ServerIp}:{config.Port}";
        }

        /// <summary>
        /// 子类实现：尝试建立 S7 连接（TCP 连接 + COTP/S7 握手）
        /// </summary>
        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            _client = new TcpClient();
            _client.SendTimeout = _config.SendTimeoutMs;

            GatewayLog.Info("S7Output", $"Connecting to {_config.ServerIp}:{_config.Port}...");
            var connectTask = _client.ConnectAsync(_config.ServerIp, _config.Port);
            var timeoutTask = Task.Delay(_config.ConnectTimeoutMs, ct);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            if (completedTask == timeoutTask && !connectTask.IsCompleted)
            {
                // 超时：关闭 client 以终止后台连接尝试，避免孤儿连接
                try { _client.Close(); } catch { }
                throw new TimeoutException($"Connection timeout after {_config.ConnectTimeoutMs}ms");
            }

            await connectTask;

            _stream = _client.GetStream();

            // 执行 S7 协议握手
            var handshakeSuccess = await PerformS7HandshakeAsync(ct);
            if (!handshakeSuccess)
            {
                CloseConnection();
                throw new IOException("S7 handshake failed");
            }

            _hasConnectedOnce = true;
        }

        /// <summary>
        /// 子类实现：停止时的清理逻辑
        /// </summary>
        protected override Task OnStopCoreAsync()
        {
            GatewayLog.Info("S7Output", $"Stopped {_config.ServerIp}:{_config.Port}");
            return Task.CompletedTask;
        }

        protected override string OnConnected()
            => $"Connected to S7 PLC at {_config.ServerIp}:{_config.Port}";

        /// <summary>
        /// 执行 S7 协议握手 — 委托给共享 S7ProtocolEngine 以消除重复代码。
        /// </summary>
        private async Task<bool> PerformS7HandshakeAsync(CancellationToken ct)
        {
            try
            {
                var success = await S7ProtocolEngine.PerformHandshakeAsync(
                    _stream, _config.Rack, _config.Slot, _config.ConnectTimeoutMs, ct);
                return success;
            }
            catch (Exception ex)
            {
                GatewayLog.Warn("S7Output", $"Handshake error: {ex.Message}");
                return false;
            }
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null || Status != PluginStatus.Running || _stream == null)
            {
                return;
            }

            await _sendLock.WaitAsync(CtsToken ?? CancellationToken.None);
            try
            {
                // 优先使用协议无关的 TagWrite 列表（解决 Bridge→S7 消息格式不匹配的"暗契约"问题）
                if (message.Writes != null && message.Writes.Count > 0)
                {
                    // 批量写入：遍历所有 TagWrite，逐条发送 S7 写入请求
                    int successCount = 0;
                    int failCount = 0;
                    foreach (var tw in message.Writes)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            var writeValueBytes = S7AddressParser.EncodeValue(tw.Value, tw.DataType);
                            // 使用共享 S7ProtocolEngine 确保与轮询输入插件一致的线缆格式
                            var pduRef = (ushort)Interlocked.Increment(ref _pduReference);
                            var writePacket = S7ProtocolEngine.BuildWriteRequest(tw.Address, writeValueBytes, pduRef);
                            await _stream.WriteAsync(writePacket, 0, writePacket.Length, CtsToken ?? CancellationToken.None);

                            var writeResponse = await S7ProtocolEngine.ReadTpktPacketAsync(_stream, _config.SendTimeoutMs, CtsToken ?? CancellationToken.None);
                            // S7 Write Response: TPKT(4) + COTP(3) + S7Header(10) + Parameter(4) + Data(≥4)
                            // Minimum = 25 bytes. S7Header[7]=0x32, [8]=0x03(Ack_Data), [9]=error_class, [10]=error_code
                            // Parameter[17]=0x05(Function), Data[21]=return_code(0xFF=success)
                            bool writeOk = writeResponse != null && writeResponse.Length >= 25
                                && writeResponse[7] == 0x32 && writeResponse[8] == 0x03
                                && writeResponse[9] == 0x00 && writeResponse[10] == 0x00
                                && writeResponse[21] == 0xFF;

                            if (writeOk)
                            {
                                sw.Stop();
                                successCount++;
                            }
                            else
                            {
                                sw.Stop();
                                failCount++;
                                int respLen = writeResponse?.Length ?? 0;
                                byte h7 = respLen > 7 ? writeResponse[7] : (byte)0;
                                byte h8 = respLen > 8 ? writeResponse[8] : (byte)0;
                                byte ec = respLen > 9 ? writeResponse[9] : (byte)0;
                                byte ec2 = respLen > 10 ? writeResponse[10] : (byte)0;
                                var errMsg = $"response={respLen} bytes, header={h7:X2}/{h8:X2}, error={ec:X2}/{ec2:X2}";
                                GatewayLog.Warn("S7Output", $"Write failed for {tw.Address}: {errMsg}");

                                message.WriteResults.Add(new TagWriteResult
                                {
                                    Address = tw.Address,
                                    Success = false,
                                    ErrorMessage = errMsg,
                                    Latency = sw.Elapsed
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            sw.Stop();
                            failCount++;
                            GatewayLog.Warn("S7Output", $"Write exception for {tw.Address}: {ex.Message}");
                            message.WriteResults.Add(new TagWriteResult
                            {
                                Address = tw.Address,
                                Success = false,
                                ErrorMessage = ex.Message,
                                Latency = sw.Elapsed
                            });
                        }
                    }

                    // 填充成功的写入结果
                    if (successCount > 0)
                    {
                        foreach (var tw in message.Writes)
                        {
                            bool alreadyRecorded = message.WriteResults.Any(r => r.Address == tw.Address);
                            if (!alreadyRecorded)
                            {
                                message.WriteResults.Add(new TagWriteResult
                                {
                                    Address = tw.Address,
                                    Success = true,
                                    Latency = TimeSpan.Zero
                                });
                            }
                        }
                    }

                    if (failCount == 0)
                    {
                        ResetConnectFailureStreak();
                        SetLastException(null);
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"S7 batch write successful ({successCount} tags)");
                    }
                    else
                    {
                        var ex = new InvalidOperationException($"S7 batch write: {successCount} succeeded, {failCount} failed out of {message.Writes.Count}");
                        SetLastException(ex);
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, ex.Message);
                        throw ex;
                    }
                    return;
                }

                // 原有文本解析路径（向后兼容）— 单条写入
                var content = message.GetTextContent();
                if (string.IsNullOrWhiteSpace(content))
                {
                    content = Encoding.UTF8.GetString(message.Payload);
                }

                var legacyTargetAddress = message.Metadata != null
                    && message.Metadata.TryGetValue(GatewayMetadataKeys.TargetAddress, out var addr)
                    && !string.IsNullOrWhiteSpace(addr)
                    ? addr
                    : _config.DefaultWriteAddress;

                var legacyValueBytes = Encoding.UTF8.GetBytes(content);

                // 使用共享 S7ProtocolEngine 确保一致的线缆格式
                var legacyPduRef = (ushort)Interlocked.Increment(ref _pduReference);
                var s7Packet = S7ProtocolEngine.BuildWriteRequest(legacyTargetAddress, legacyValueBytes, legacyPduRef);

                await _stream.WriteAsync(s7Packet, 0, s7Packet.Length, CtsToken ?? CancellationToken.None);

                // 等待响应 — 使用 TPKT 协议正确读取（先读头，根据 Length 读负载）
                // S7 Write Response structure:
                // TPKT (4 bytes) + COTP DT (3 bytes) + S7 Header (12 bytes) + Parameter + Data
                // S7 Header: [7]=0x32 (Protocol ID), [8]=ROSCTR (0x03=Ack_Data), [17]=error_class, [18]=error_code
                // Parameter: [19]=Function (0x05=Write Var), [24]=return_code (0xFF=success)
                var response = await S7ProtocolEngine.ReadTpktPacketAsync(_stream, _config.SendTimeoutMs, CtsToken ?? CancellationToken.None);

                // Minimum response: TPKT(4) + COTP(3) + S7 Header(12) + Parameter(6) = 25 bytes
                if (response != null && response.Length >= 25
                    && response[7] == 0x32 && response[8] == 0x03
                    && response[17] == 0x00 && response[18] == 0x00)
                {
                    // Write confirmed (S7 Ack_Data with no error)
                    ResetConnectFailureStreak();
                    SetLastException(null);
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, "S7 write successful");
                    GatewayLog.Info("S7Output", "Data sent successfully");
                }
                else
                {
                    int respLen = response?.Length ?? 0;
                    byte h7 = response != null && response.Length > 7 ? response[7] : (byte)0;
                    byte h8 = response != null && response.Length > 8 ? response[8] : (byte)0;
                    byte e17 = response != null && response.Length > 17 ? response[17] : (byte)0;
                    byte e18 = response != null && response.Length > 18 ? response[18] : (byte)0;
                    GatewayLog.Warn("S7Output", $"Write response unexpected: {respLen} bytes, header={h7:X2}/{h8:X2}, error={e17:X2}/{e18:X2}");
                }
            }
            catch (OperationCanceledException) when (CtsToken?.IsCancellationRequested == true)
            {
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or TimeoutException)
            {
                if (CtsToken?.IsCancellationRequested == true)
                {
                    return;
                }

                var errorMessage = $"Send failed: {ex.Message}";
                GatewayLog.Error("S7Output", errorMessage);
                Status = PluginStatus.Error;
                SetLastException(ex);
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, errorMessage);
                SetConnectionState(false, OutputPluginHealthLevel.Error);
                CloseConnection();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        protected override bool HasLiveConnection()
        {
            if (_client?.Client == null || !_client.Connected || _stream == null)
            {
                return false;
            }

            try
            {
                if (_client.Client.Poll(0, SelectMode.SelectRead) && _client.Client.Available == 0)
                {
                    var message = $"Remote closed S7 connection to {_config.ServerIp}:{_config.Port}. Reconnecting...";
                    GatewayLog.Info("S7Output", message);
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, message);
                    SetConnectionState(false, OutputPluginHealthLevel.Warning);
                    CloseConnection();
                    return false;
                }

                return true;
            }
            catch (SocketException ex)
            {
                var message = $"S7 connection health check failed: {ex.Message}";
                GatewayLog.Info("S7Output", message);
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, message);
                SetConnectionState(false, OutputPluginHealthLevel.Error);
                CloseConnection();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private void CloseConnection()
        {
            // P1-4 修复：CloseConnection 必须串行化，与 SendAsync 共享同一 _stream/_client，
            // 防止 ConnectionLoop 线程关闭 _stream 时 SendAsync 仍在使用。
            // 注意：SemaphoreSlim.WaitAsync 与 lock(object) 是不同的锁机制，互不排斥！
            // 此同步方法使用 Task.Run + WaitAsync 模式来获取异步锁，避免混用 lock/SemaphoreSlim。
            try { _sendLock.Wait(); } catch (ObjectDisposedException) { return; }
            try
            {
                try { _stream?.Close(); } catch { }
                try { _stream?.Dispose(); } catch { }
                try { _client?.Close(); } catch { }
                try { _client?.Dispose(); } catch { }
                _stream = null;
                _client = null;
            }
            finally
            {
                try { _sendLock.Release(); } catch (ObjectDisposedException) { }
            }
        }

        protected override string GetFailureKind(Exception ex)
        {
            return ex switch
            {
                SocketException socketEx => $"SocketError: {socketEx.SocketErrorCode}",
                TimeoutException => "Timeout",
                IOException => "IO",
                ObjectDisposedException => "Disposed",
                _ => ex.GetType().Name
            };
        }

        protected override string OnFormatFailureMessage(Exception ex, string failureKind, int streak)
        {
            if (failureKind.StartsWith("SocketError: ConnectionRefused") || failureKind.Contains("ConnectionRefused"))
            {
                if (streak == 1)
                {
                    return $"S7 target {_config.ServerIp}:{_config.Port} is not listening (connection refused). Retrying every {_config.ReconnectIntervalMs}ms...";
                }
                return $"S7 target {_config.ServerIp}:{_config.Port} still not listening after {streak} attempts. Retrying every {_config.ReconnectIntervalMs}ms...";
            }

            if (failureKind == "Timeout")
            {
                return $"S7 connection to {_config.ServerIp}:{_config.Port} timed out after {_config.ConnectTimeoutMs}ms. Retrying in {_config.ReconnectIntervalMs}ms...";
            }

            return $"S7 connection failed: {ex.Message}. Retrying in {_config.ReconnectIntervalMs}ms...";
        }

        protected override bool ShouldLogConnectionFailure(int streak, string failureKind)
        {
            if (!failureKind.Contains("ConnectionRefused") && failureKind != "Timeout")
            {
                return true;
            }

            if (streak == 1)
            {
                return true;
            }

            if (!_hasConnectedOnce && IsLoopbackTarget())
            {
                if (ConnectFailureStreak <= 2)
                {
                    return false;
                }
            }

            return streak == 5 || streak % 10 == 0;
        }

        private bool IsLoopbackTarget()
        {
            string host = (_config.ServerIp ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (System.Net.IPAddress.TryParse(host, out var ipAddress))
            {
                return System.Net.IPAddress.IsLoopback(ipAddress);
            }

return false;
        }

/// <summary>S7 协议特定的错误分类</summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            return level switch
            {
                OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, "Siemens S7 connected", "No action needed"),
                OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, "Siemens S7 connection retrying", "Check PLC device network connection"),
                OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, "Siemens S7 connection failed", "Check PLC IP, port, and firewall settings"),
                OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, "Siemens S7 configuration error", "Check rack, slot, and S7 configuration"),
                _ => (GatewayErrorCodes.InternalException, "Siemens S7 unknown error", "Check logs for details")
            };
        }
    }
}
