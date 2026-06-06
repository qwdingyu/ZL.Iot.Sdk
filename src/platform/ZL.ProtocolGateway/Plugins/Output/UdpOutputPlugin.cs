using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    public class UdpOutputConfig
    {
        public string Name { get; set; }
        public string ServerIp { get; set; } = "127.0.0.1";
        public int Port { get; set; }

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(ServerIp))
                errors.Add(new ConfigValidationError(nameof(ServerIp), $"ServerIp '{ServerIp}' 不是有效的 IP 地址"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            return errors;
        }
    }

    /// <summary>
    /// UDP 输出插件 — 继承 OutputPluginBase，消除状态管理样板代码
    /// </summary>
    public class UdpOutputPlugin : OutputPluginBase
    {
        private readonly UdpOutputConfig _config;
        private UdpClient _udpClient;

        public UdpOutputPlugin(UdpOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        public override string Name =>
            string.IsNullOrWhiteSpace(_config.Name) ? $"Udp-{_config.ServerIp}:{_config.Port}" : _config.Name;

        public override string ProtocolType => "Udp";

        protected override Task OnStartAsync(CancellationToken ct)
        {
            _udpClient = new UdpClient();
            _udpClient.Connect(_config.ServerIp, _config.Port);
            RaiseDetailedStatusChanged(OutputPluginHealthLevel.Healthy, $"UDP output ready: {_config.ServerIp}:{_config.Port}");
            return Task.CompletedTask;
        }

        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null || _udpClient == null)
            {
                return;
            }

            try
            {
                var payload = message.Payload ?? Array.Empty<byte>();
                await _udpClient.SendAsync(payload, payload.Length);
            }
            catch (Exception ex)
            {
                GatewayLog.Error("UdpOutput", $"Send error: {ex.Message}");
                throw; // 让基类处理状态变更和通知
            }
        }

        protected override Task OnStopAsync()
        {
            try
            {
                _udpClient?.Close();
                _udpClient?.Dispose();
            }
            catch
            {
                // ignore
            }

            _udpClient = null;
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _udpClient?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
