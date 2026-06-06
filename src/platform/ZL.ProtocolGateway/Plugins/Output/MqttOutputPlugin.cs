using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Client;
using MQTTnet.Exceptions;
using MQTTnet.Protocol;
using ZL.ProtocolGateway;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// MQTT 输出插件配置
    /// </summary>
    public class MqttOutputConfig
    {
        public string Name { get; set; }
        
        /// <summary>
        /// Broker 地址 (如: broker.emqx.io)
        /// </summary>
        public string Server { get; set; }

        /// <summary>
        /// 端口 (默认 1883, SSL 为 8883)
        /// </summary>
        public int Port { get; set; } = 1883;

        /// <summary>
        /// Client ID (留空则自动生成)
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// 用户名 (可选)
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// 密码 (可选)
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 默认发布主题 (可在消息路由中覆盖)
        /// </summary>
        public string DefaultTopic { get; set; } = "gateway/data";

        /// <summary>
        /// QoS 等级 (0, 1, 2)
        /// </summary>
        public MqttQualityOfServiceLevel QoS { get; set; } = MqttQualityOfServiceLevel.AtMostOnce;

        /// <summary>
        /// 错误告警阈值（连续失败多少次后触发 Error 级别）
        /// </summary>
        public int ErrorThreshold { get; set; } = 10;

        /// <summary>
        /// 是否启用 TLS/SSL 加密连接（默认 false）
        /// </summary>
        public bool UseTls { get; set; }

        /// <summary>
        /// TLS 证书验证回调 — 设置为 true 跳过验证（开发/测试环境）。
        /// 生产环境应配置 CA 证书而非跳过验证。
        /// </summary>
        public bool SkipCertificateValidation { get; set; }

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(Server))
                errors.Add(new ConfigValidationError(nameof(Server), "Server 不能为空"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            if (!ConfigValidation.IsValidErrorThreshold(ErrorThreshold))
                errors.Add(new ConfigValidationError(nameof(ErrorThreshold), $"ErrorThreshold {ErrorThreshold} 必须 > 0"));
            return errors;
        }
    }

    /// <summary>
    /// MQTT 输出插件 - 将消息发布到 MQTT Broker
    /// <para>继承 IndustrialOutputPluginBase，复用统一连接管理（指数退避 + 错误分类 + Recovering 状态）。</para>
    /// </summary>
    public class MqttOutputPlugin : IndustrialOutputPluginBase
    {
        private readonly MqttOutputConfig _config;
        private IMqttClient _mqttClient;

        // 命名事件处理方法，便于在 CleanupConnection 中取消订阅
        private Func<MqttClientConnectedEventArgs, Task> _onConnected;
        private Func<MqttClientDisconnectedEventArgs, Task> _onDisconnected;

        // TaskCompletionSource 用于在 TryConnectAsync 中等待断开事件
        private TaskCompletionSource<bool> _disconnectTcs;

        public override string Name { get; }
        public override string ProtocolType => "Mqtt";

        public MqttOutputPlugin(MqttOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Name = config.Name ?? $"Mqtt-{config.Server}";
            ConfigValidation.ThrowIfInvalid(_config.Validate());
        }

        /// <summary>
        /// 建立 MQTT 连接并等待直到断开或取消。
        /// 基类 ConnectionLoopAsync 负责循环调用此方法。
        /// </summary>
        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            // 使用命名方法绑定事件，便于在 CleanupConnection 中取消订阅
            _mqttClient.ConnectedAsync += _onConnected = OnConnected;
            _mqttClient.DisconnectedAsync += _onDisconnected = OnDisconnected;

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_config.Server, _config.Port)
                .WithClientId(_config.ClientId ?? Guid.NewGuid().ToString("N"))
                .WithCleanSession();

            if (!string.IsNullOrEmpty(_config.Username))
            {
                options.WithCredentials(_config.Username, _config.Password);
            }

            if (_config.UseTls)
            {
                options.WithTls(new MqttClientOptionsBuilderTlsParameters
                {
                    UseTls = true,
                    SslProtocol = System.Security.Authentication.SslProtocols.Tls12,
                    AllowUntrustedCertificates = _config.SkipCertificateValidation,
                    CertificateValidationHandler = e => _config.SkipCertificateValidation
                        ? true
                        : e.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None
                });
            }

            await _mqttClient.ConnectAsync(options.Build(), ct);

            // 连接成功，现在等待断开事件或取消
            _disconnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                using var reg = ct.Register(() => _disconnectTcs.TrySetResult(false));
                await _disconnectTcs.Task;
            }
            finally
            {
                _disconnectTcs = null;
            }
        }

        private Task OnConnected(MqttClientConnectedEventArgs args)
        {
            // 状态由基类 ConnectionLoopAsync 在 TryConnectAsync 成功后设置
            return Task.CompletedTask;
        }

        private Task OnDisconnected(MqttClientDisconnectedEventArgs args)
        {
            // 通知 TryConnectAsync 中的等待者：连接已断开
            _disconnectTcs?.TrySetResult(false);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 检查 MQTT 连接是否存活。
        /// </summary>
        protected override bool HasLiveConnection()
        {
            return _mqttClient?.IsConnected == true;
        }

        /// <summary>
        /// 清理 MQTT 连接资源。
        /// </summary>
        protected override void CleanupConnection()
        {
            if (_mqttClient != null)
            {
                _mqttClient.ConnectedAsync -= _onConnected;
                _mqttClient.DisconnectedAsync -= _onDisconnected;

                try
                {
                    _mqttClient.Dispose();
                }
                catch
                {
                    // Dispose 不应抛出
                }

                _mqttClient = null;
            }

            // 确保 TryConnectAsync 中的等待者被释放
            _disconnectTcs?.TrySetResult(false);
            _disconnectTcs = null;
        }

        /// <summary>
        /// 按错误类型分类，不同错误类型独立计数。
        /// </summary>
        protected override string GetFailureKind(Exception ex)
        {
            return ex switch
            {
                MqttConnectingFailedException => "MqttConnectionFailed",
                MqttCommunicationException => "MqttCommunicationError",
                MqttProtocolViolationException => "MqttProtocolError",
                OperationCanceledException => "Canceled",
                _ => ex.GetType().Name
            };
        }

        /// <summary>
        /// 提供 MQTT 协议特定的错误码和用户消息。
        /// </summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            if (level == OutputPluginHealthLevel.Healthy)
                return ("OK", "Connected", string.Empty);

            var ex = LastException;
            if (ex is MqttConnectingFailedException)
                return ("MQTT_CONN_REFUSED", "MQTT Broker 连接被拒绝", "检查 Broker 地址和端口是否正确");
            if (ex is MqttCommunicationException)
                return ("MQTT_COMM_ERROR", "MQTT 通信异常", "检查网络连接，Broker 可能已断开");
            if (ex is MqttProtocolViolationException)
                return ("MQTT_PROTO_ERROR", "MQTT 协议错误", "检查 ClientId 是否冲突或认证信息是否正确");

            return base.ClassifyError(level, message);
        }

        /// <summary>
        /// 发送消息到 MQTT Broker。
        /// </summary>
        protected override async Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            try
            {
                // 未连接时抛出异常，让调用方（ResilientMessagePipeline）决定重试或入死信队列
                if (_mqttClient == null || !_mqttClient.IsConnected)
                {
                    throw new InvalidOperationException("MQTT client is not connected");
                }

                // 确定主题：优先使用消息 Topic，否则使用配置默认值
                string topic = !string.IsNullOrEmpty(message.Topic) ? message.Topic : _config.DefaultTopic;

                // 支持在 Metadata 中覆盖 Topic
                if (message.Metadata.TryGetValue("MqttTopic", out var metaTopic))
                {
                    topic = metaTopic;
                }

                byte[] payloadToSend;
                if (message.Writes?.Count > 0)
                {
                    payloadToSend = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(message.Writes));
                }
                else
                {
                    payloadToSend = message.Payload ?? Array.Empty<byte>();
                }

                var mqttMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payloadToSend)
                    .WithQualityOfServiceLevel(_config.QoS)
                    .Build();

                await _mqttClient.PublishAsync(mqttMessage);
            }
            catch (Exception ex)
            {
                // P1 修复：不再在发送失败时调用 CleanupConnection。
                // 原因：CleanupConnection 会 Dispose _mqttClient 并置 null，
                // PipelineSendStrategy 的重试逻辑会再次调用 OnSendAsync，
                // 发现 _mqttClient == null 后再次抛异常——重试对 MQTT 无意义。
                // 直接抛异常，让 Pipeline 的断路器/死信机制处理。
                // 连接断开由 MQTTnet 内部事件（DisconnectedAsync）通知，
                // 基类 ConnectionLoopAsync 会自动重连。
                throw;
            }
        }

        /// <summary>
        /// 停止核心清理（基类 OnStopAsync 已处理 CTS 取消和连接循环等待）。
        /// </summary>
        protected override Task OnStopCoreAsync()
        {
            // CleanupConnection 由基类 OnStopAsync 在 _cts.Cancel() 后调用，
            // 此处无需额外操作。
            return Task.CompletedTask;
        }
    }
}
