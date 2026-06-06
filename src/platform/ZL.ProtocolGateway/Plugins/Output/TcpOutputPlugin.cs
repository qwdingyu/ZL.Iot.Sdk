using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// TCP 输出插件配置
    /// </summary>
    public class TcpOutputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 目标服务器 IP
        /// </summary>
        public string ServerIp { get; set; }

        /// <summary>
        /// 目标服务器端口
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 发送后追加的结束符（例如 "\n" 或 "\r\n"）
        /// </summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>
        /// 重连间隔 (毫秒)
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 3000;

        /// <summary>
        /// 本地目标首次监听宽限期 (毫秒)。
        /// 在该时间窗内，连续 ConnectionRefused 只做一次提示，避免启动时序造成日志刷屏。
        /// </summary>
        public int LocalStartupQuietPeriodMs { get; set; } = 5000;

        /// <summary>
        /// 错误告警阈值（连续失败多少次后触发 Error 级别）
        /// </summary>
        public int ErrorThreshold { get; set; } = 10;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(ServerIp))
                errors.Add(new ConfigValidationError(nameof(ServerIp), $"ServerIp '{ServerIp}' 不是有效的 IP 地址"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            if (!ConfigValidation.IsValidReconnectInterval(ReconnectIntervalMs))
                errors.Add(new ConfigValidationError(nameof(ReconnectIntervalMs), $"ReconnectIntervalMs {ReconnectIntervalMs} 必须 >= 500"));
            if (!ConfigValidation.IsValidErrorThreshold(ErrorThreshold))
                errors.Add(new ConfigValidationError(nameof(ErrorThreshold), $"ErrorThreshold {ErrorThreshold} 必须 > 0"));
            return errors;
        }
    }

    /// <summary>
    /// TCP 输出插件 - 将消息转发到目标 TCP Server
    /// 内置断线自动重连机制
    /// 继承 OutputPluginBase，消除状态管理样板代码
    /// </summary>
    public class TcpOutputPlugin : OutputPluginBase
    {
        private readonly TcpOutputConfig _config;
        private TcpClient _client;
        private CancellationTokenSource _cts;
        private Task _connectionLoop;
        private int _connectFailureStreak;
        private string _lastFailureKind;
        private bool _hasConnectedOnce;
        private DateTimeOffset? _firstConnectionRefusedAt;

        public TcpOutputPlugin(TcpOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        public override string Name => _config.Name ?? $"Tcp-{_config.ServerIp}:{_config.Port}";
        public override string ProtocolType => "Tcp";

        /// <summary>
        /// 子类覆盖基础重连间隔，使用配置值
        /// </summary>
        protected override int BaseReconnectIntervalMs => _config.ReconnectIntervalMs;

        /// <summary>
        /// 启动：重置状态并启动连接循环。
        /// 注意：Status=Starting 由基类 StartAsync 设置，OnStartAsync 返回后基类设为 Running。
        /// 实际连接状态由 ConnectionLoopAsync 动态更新。
        /// </summary>
        protected override async Task OnStartAsync(CancellationToken ct)
        {
            
            _hasConnectedOnce = false;
            _connectFailureStreak = 0;
            _lastFailureKind = string.Empty;
            _firstConnectionRefusedAt = null;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _connectionLoop = Task.Run(() => ConnectionLoopAsync(_cts.Token));
            await Task.CompletedTask;
        }

        private async Task ConnectionLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (HasLiveConnection())
                    {
                        Status = PluginStatus.Running;
                        await Task.Delay(1000, ct);
                        continue;
                    }

                    Status = PluginStatus.Starting;
                    CloseCurrentClient();
                    _client = new TcpClient();

                    GatewayLog.Info("TcpOutput", $"Connecting to {_config.ServerIp}:{_config.Port}...");
                    await _client.ConnectAsync(_config.ServerIp, _config.Port);

                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    Status = PluginStatus.Running;
                    _hasConnectedOnce = true;
                    _connectFailureStreak = 0;
                    _lastFailureKind = string.Empty;
                    _firstConnectionRefusedAt = null;
                    var connectedMessage = $"Connected to {_config.ServerIp}:{_config.Port}";
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, connectedMessage, customConsecutiveFailures: 0);
                    SetConnectionState(true, OutputPluginHealthLevel.Healthy);
                    GatewayLog.Info("TcpOutput", connectedMessage);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    GatewayLog.Info("TcpOutput", $"Stop requested for {_config.ServerIp}:{_config.Port}. Connection loop exiting.");
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        GatewayLog.Info("TcpOutput", $"Stop requested for {_config.ServerIp}:{_config.Port}. Connection loop exiting.");
                        break;
                    }

                    Status = PluginStatus.Error;
                    CloseCurrentClient();

                    if (!ct.IsCancellationRequested)
                    {
                        string failureKind = GetFailureKind(ex);
                        _connectFailureStreak = string.Equals(_lastFailureKind, failureKind, StringComparison.Ordinal)
                            ? _connectFailureStreak + 1
                            : 1;
                        _lastFailureKind = failureKind;

                        if (string.Equals(failureKind, SocketError.ConnectionRefused.ToString(), StringComparison.Ordinal))
                        {
                            _firstConnectionRefusedAt ??= DateTimeOffset.UtcNow;
                        }

                        // 判断健康级别
                        var level = _connectFailureStreak >= _config.ErrorThreshold
                            ? OutputPluginHealthLevel.Error
                            : OutputPluginHealthLevel.Warning;

                        var message = FormatConnectionFailureMessage(ex, failureKind, _connectFailureStreak);

                        if (ShouldLogConnectionFailure(_connectFailureStreak, failureKind, _firstConnectionRefusedAt))
                        {
                            GatewayLog.Info("TcpOutput", message);
                        }

                        // 使用基类状态事件（传递真实连续失败次数）
                        RaiseDetailedStatusChanged(level, message, ex, _connectFailureStreak);
                        SetConnectionState(false, level);
                    }

                    try
                    {
                        // 指数退避重连：3s → 6s → 12s → 24s → 48s → 60s（上限）
                        int delayMs = Math.Min(
                            _config.ReconnectIntervalMs * (int)Math.Pow(2, Math.Min(_connectFailureStreak - 1, 5)),
                            60_000);
                        await Task.Delay(delayMs, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 发送：通过 TCP 连接发送消息。
        /// 连接异常时自动断开，由 ConnectionLoopAsync 重连。
        /// </summary>
        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (Status != PluginStatus.Running || _client == null || !_client.Connected)
            {
                throw new IOException($"TCP output is not connected: {_config.ServerIp}:{_config.Port}");
            }

            try
            {
                var stream = _client.GetStream();
                await stream.WriteAsync(message.Payload, 0, message.Payload.Length);

                if (!string.IsNullOrEmpty(_config.Suffix))
                {
                    var suffixBytes = Encoding.UTF8.GetBytes(_config.Suffix);
                    await stream.WriteAsync(suffixBytes, 0, suffixBytes.Length);
                }
            }
            catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
            {
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                if (_cts?.IsCancellationRequested == true)
                {
                    return;
                }

                var errorMessage = $"Send failed: {ClassifyConnectionError(ex)}";
                GatewayLog.Error("TcpOutput", errorMessage);
                Status = PluginStatus.Error;
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, errorMessage, ex);
                SetConnectionState(false, OutputPluginHealthLevel.Error);
                CloseCurrentClient();
                throw;
            }
        }

        /// <summary>
        /// 停止：取消连接循环，关闭 TCP 连接。
        /// </summary>
        protected override async Task OnStopAsync()
        {
            _cts?.Cancel();

            if (_connectionLoop != null)
            {
                try { await _connectionLoop; } catch { }
            }

            CloseCurrentClient();
            _cts?.Dispose();
            _cts = null;
            GatewayLog.Info("TcpOutput", $"Stopped {_config.ServerIp}:{_config.Port}");
        }

        #region 连接管理

        private bool HasLiveConnection()
        {
            if (_client?.Client == null || !_client.Connected)
            {
                return false;
            }

            try
            {
                if (_client.Client.Poll(0, SelectMode.SelectRead) && _client.Client.Available == 0)
                {
                    var message = $"Remote closed connection to {_config.ServerIp}:{_config.Port}. Reconnecting...";
                    GatewayLog.Info("TcpOutput", message);
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, message);
                    SetConnectionState(false, OutputPluginHealthLevel.Warning);
                    CloseCurrentClient();
                    return false;
                }

                return true;
            }
            catch (SocketException ex)
            {
                var message = $"Connection health check failed: {ex.Message}";
                GatewayLog.Info("TcpOutput", message);
                RaiseDetailedStatusChanged(OutputPluginHealthLevel.Error, message, ex);
                SetConnectionState(false, OutputPluginHealthLevel.Error);
                CloseCurrentClient();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private void CloseCurrentClient()
        {
            try { _client?.Close(); } catch { }
            try { _client?.Dispose(); } catch { }
            _client = null;
        }

        #endregion

        #region 连接失败降噪与分类

        /// <summary>
        /// 将连接异常分类为可读描述
        /// </summary>
        private static string ClassifyConnectionError(Exception ex)
        {
            return ex switch
            {
                SocketException socketEx => $"socket error: {socketEx.Message}",
                IOException ioEx => $"io error: {ioEx.Message}",
                ObjectDisposedException => "socket disposed",
                _ => ex.Message
            };
        }

        /// <summary>
        /// 获取失败类型标识（用于连续失败计数去重）
        /// </summary>
        private static string GetFailureKind(Exception ex)
        {
            return ex is SocketException socketEx
                ? socketEx.SocketErrorCode.ToString()
                : ex.GetType().Name;
        }

        /// <summary>
        /// 格式化连接失败消息
        /// </summary>
        private string FormatConnectionFailureMessage(Exception ex, string failureKind, int streak)
        {
            if (ex is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionRefused)
            {
                if (streak == 1)
                {
                    return $"Target {_config.ServerIp}:{_config.Port} is not listening yet (connection refused). Retrying every {_config.ReconnectIntervalMs}ms...";
                }

                return $"Target {_config.ServerIp}:{_config.Port} still not listening after {streak} attempts. Retrying every {_config.ReconnectIntervalMs}ms...";
            }

            return $"Connection failed: {ClassifyConnectionError(ex)}. Retrying in {_config.ReconnectIntervalMs}ms...";
        }

        /// <summary>
        /// 判断是否应该记录连接失败日志（降噪策略）
        /// </summary>
        private bool ShouldLogConnectionFailure(int streak, string failureKind, DateTimeOffset? firstRefusedAt)
        {
            if (!string.Equals(failureKind, SocketError.ConnectionRefused.ToString(), StringComparison.Ordinal))
            {
                return true;
            }

            if (streak == 1)
            {
                return true;
            }

            if (!_hasConnectedOnce && IsLoopbackTarget() && firstRefusedAt.HasValue)
            {
                if ((DateTimeOffset.UtcNow - firstRefusedAt.Value).TotalMilliseconds < _config.LocalStartupQuietPeriodMs)
                {
                    return false;
                }
            }

            return streak == 5 || streak % 10 == 0;
        }

        /// <summary>
        /// 判断目标是否为回环地址（localhost / 127.x.x.x）
        /// </summary>
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

            if (IPAddress.TryParse(host, out var ipAddress))
            {
                return IPAddress.IsLoopback(ipAddress);
            }

            return false;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Dispose();
                _client?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
