using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    public class UdpInputConfig
    {
        public string Name { get; set; }
        public string LocalIp { get; set; } = "0.0.0.0";
        public int Port { get; set; }

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(LocalIp))
                errors.Add(new ConfigValidationError(nameof(LocalIp), $"LocalIp '{LocalIp}' 不是有效的 IP 地址"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            return errors;
        }
    }

    /// <summary>
    /// UDP 输入插件 — 继承 InputPluginBase，消除状态管理样板代码
    /// </summary>
    public class UdpInputPlugin : InputPluginBase
    {
        private readonly UdpInputConfig _config;
        private UdpClient _udpClient;
        private Task _receiveTask;

        public UdpInputPlugin(UdpInputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Name = string.IsNullOrWhiteSpace(config.Name) ? $"UdpInput-{config.Port}" : config.Name;
        }

        public override string Name { get; }
        public override string ProtocolType => "Udp";

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            var bindIp = IPAddress.Parse(_config.LocalIp);
            _udpClient = new UdpClient(new IPEndPoint(bindIp, _config.Port));

            var receiveTask = Task.Run(() => ReceiveLoopAsync(ct), ct);
            _receiveTask = receiveTask;
            // 捕获接收循环的未处理异常，避免 fire-and-forget 静默失败
            _ = receiveTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    GatewayLog.Error("UdpInput", $"Receive loop failed: {t.Exception}", t.Exception);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    var msg = new Message
                    {
                        Topic = _config.Port.ToString(),
                        ContentType = "binary",
                        Metadata =
                        {
                            ["Protocol"] = "Udp",
                            ["Source"] = result.RemoteEndPoint.ToString(),
                            ["LocalPort"] = _config.Port.ToString()
                        }
                    };
                    msg.SetPayload(result.Buffer);

                    GatewayTraceContext.EnsureTraceId(msg);
                    await InvokeMessageHandler(msg);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    GatewayLog.Warn("UdpInput", $"Receive error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }
        }

        protected override async Task OnStopAsync()
        {
            try
            {
                _udpClient?.Close();
                _udpClient?.Dispose();
                _udpClient = null;
            }
            catch
            {
                // ignore
            }

            if (_receiveTask != null)
            {
                try { await _receiveTask; } catch { }
                _receiveTask = null;
            }
        }
    }
}
