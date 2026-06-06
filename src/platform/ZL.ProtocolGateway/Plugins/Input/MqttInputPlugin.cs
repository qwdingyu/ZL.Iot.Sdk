using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace ZL.ProtocolGateway.Plugins
{
    public class MqttInputConfig
    {
        public string Name { get; set; }
        public string Server { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 1883;
        public string ClientId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public List<string> Topics { get; set; } = new() { "#" };
        public MqttQualityOfServiceLevel QoS { get; set; } = MqttQualityOfServiceLevel.AtMostOnce;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(Server))
                errors.Add(new ConfigValidationError(nameof(Server), $"Server 不能为空"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            if (Topics == null || Topics.Count == 0)
                errors.Add(new ConfigValidationError(nameof(Topics), $"Topics 不能为空"));
            return errors;
        }
    }

    public class MqttInputPlugin : InputPluginBase
    {
        private readonly MqttInputConfig _config;
        private IMqttClient _mqttClient;
        private Task _reconnectTask;

        public MqttInputPlugin(MqttInputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Name = string.IsNullOrWhiteSpace(config.Name) ? $"MqttInput-{config.Server}" : config.Name;
        }

        public override string Name { get; }
        public override string ProtocolType => "Mqtt";

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();
            _mqttClient.ConnectedAsync += HandleConnectedAsync;
            _mqttClient.DisconnectedAsync += HandleDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;

            var loopTask = Task.Run(() => ReconnectLoopAsync(ct), ct);
            _reconnectTask = loopTask;
            // 捕获重连循环的未处理异常，避免 fire-and-forget 静默失败
            _ = loopTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    GatewayLog.Error("MqttInput", $"Reconnect loop failed: {t.Exception}", t.Exception);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task HandleConnectedAsync(MqttClientConnectedEventArgs _)
        {
            await SubscribeTopicsAsync();
        }

        private Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            GatewayLog.Info("MqttInput", $"Disconnected: {e.Reason}");
            return Task.CompletedTask;
        }

        private async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var appMessage = e.ApplicationMessage;
                if (appMessage == null)
                {
                    return;
                }

                var payload = appMessage.PayloadSegment.Array == null
                    ? Array.Empty<byte>()
                    : appMessage.PayloadSegment.ToArray();

                var msg = new Message
                {
                    Topic = appMessage.Topic,
                    ContentType = "binary",
                    Metadata =
                    {
                        ["Protocol"] = "Mqtt",
                        ["Topic"] = appMessage.Topic ?? string.Empty,
                        ["QoS"] = ((int)appMessage.QualityOfServiceLevel).ToString()
                    }
                };
                msg.SetPayload(payload);

                GatewayTraceContext.EnsureTraceId(msg);
                await InvokeMessageHandler(msg);
            }
            catch (Exception ex)
            {
                GatewayLog.Error("MqttInput", $"Message handle error: {ex.Message}");
            }
        }

        private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
        {
            int connectFailureStreak = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_mqttClient.IsConnected)
                    {
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    Status = PluginStatus.Starting;
                    var optionsBuilder = new MqttClientOptionsBuilder()
                        .WithTcpServer(_config.Server, _config.Port)
                        .WithClientId(string.IsNullOrWhiteSpace(_config.ClientId) ? Guid.NewGuid().ToString("N") : _config.ClientId)
                        .WithCleanSession();

                    if (!string.IsNullOrWhiteSpace(_config.Username))
                    {
                        optionsBuilder.WithCredentials(_config.Username, _config.Password);
                    }

                    await _mqttClient.ConnectAsync(optionsBuilder.Build(), cancellationToken);
                    connectFailureStreak = 0;
                    Status = PluginStatus.Running;
                }
                catch (Exception ex)
                {
                    connectFailureStreak++;
                    Status = PluginStatus.Recovering;

                    // 指数退避重连（Equal Jitter），避免固定间隔导致 Broker 冲击
                    int delayMs = OutputPluginBase.CalculateBackoffDelay(connectFailureStreak, 3000, 30_000);
                    GatewayLog.Warn("MqttInput", $"Connect failed (streak={connectFailureStreak}): {ex.Message}, retrying in {delayMs}ms");
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
        }

        private async Task SubscribeTopicsAsync()
        {
            if (!_mqttClient.IsConnected)
            {
                return;
            }

            foreach (var topic in (_config.Topics ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var filter = new MqttTopicFilterBuilder()
                    .WithTopic(topic)
                    .WithQualityOfServiceLevel(_config.QoS)
                    .Build();

                await _mqttClient.SubscribeAsync(filter);
            }
        }

        protected override async Task OnStopAsync()
        {
            if (_reconnectTask != null)
            {
                try { await _reconnectTask; } catch { }
                _reconnectTask = null;
            }

            try
            {
                if (_mqttClient?.IsConnected == true)
                {
                    await _mqttClient.DisconnectAsync();
                }
            }
            catch
            {
                // ignore
            }

            if (_mqttClient != null)
            {
                _mqttClient.ConnectedAsync -= HandleConnectedAsync;
                _mqttClient.DisconnectedAsync -= HandleDisconnectedAsync;
                _mqttClient.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;
                _mqttClient.Dispose();
                _mqttClient = null;
            }
        }
    }
}
