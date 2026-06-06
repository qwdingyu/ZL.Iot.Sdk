using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using ZL.ProtocolGateway.Plugins;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// Sparkplug B Output 插件配置
    /// </summary>
    public class SparkplugBOutputConfig
    {
        /// <summary>插件名称</summary>
        public string Name { get; set; } = "SparkplugB";

        /// <summary>MQTT Broker 地址</summary>
        public string BrokerHost { get; set; } = "localhost";

        /// <summary>MQTT Broker 端口，默认 1883</summary>
        public int BrokerPort { get; set; } = 1883;

        /// <summary>MQTT 用户名（可选）</summary>
        public string? Username { get; set; }

        /// <summary>MQTT 密码（可选）</summary>
        public string? Password { get; set; }

        /// <summary>Sparkplug B 组 ID，默认 "DefaultGroup"</summary>
        public string GroupId { get; set; } = "DefaultGroup";

        /// <summary>Sparkplug B 边缘节点 ID，默认 "EdgeNode1"</summary>
        public string EdgeNodeId { get; set; } = "EdgeNode1";

        /// <summary>
        /// 设备标签映射：将 Message.Writes 中的 Address 映射为 Sparkplug B Metric Name。
        /// 如果为空，使用 Message.Writes[i].Alias ?? Address 作为 Metric Name。
        /// </summary>
        public Dictionary<string, string>? TagMappings { get; set; }

        /// <summary>是否使用 Protobuf 编码（默认 false，使用 JSON）</summary>
        [Obsolete("Protobuf encoding is not yet implemented. Always uses JSON. This property is preserved for API compatibility and will be removed in a future version.")]
        public bool UseProtobuf { get; set; } = false;

        /// <summary>MQTT QoS 等级 (0, 1, 2)，默认 1</summary>
        public int Qos { get; set; } = 1;

        /// <summary>是否 Retain 消息，默认 false</summary>
        public bool Retain { get; set; } = false;

        /// <summary>
        /// 数据聚合间隔（毫秒）。在此间隔内到达的多条消息合并为一次 NDATA 发布。
        /// 0 表示不聚合，每条消息立即发布。默认 100ms。
        /// </summary>
        public int AggregateIntervalMs { get; set; } = 100;

        /// <summary>
        /// NDALIVE 心跳间隔（毫秒）。Sparkplug B 规范要求边缘节点定期发送 NDALIVE
        /// 以确认自身存活。默认 30000ms（30 秒），0 表示禁用。
        /// </summary>
        public int KeepAliveIntervalMs { get; set; } = 30000;
    }

    /// <summary>
    /// Sparkplug B Output 插件 — 将 Message 转换为 Sparkplug B 协议格式并发布到 MQTT Broker。
    /// 
    /// Sparkplug B 是一种工业物联网数据模型规范，定义了设备元数据、状态管理和数据序列化的标准。
    /// 本插件实现以下功能：
    /// - 连接 MQTT Broker，发布 NBirth/NData/NDeath 生命周期消息
    /// - 将 Message.Writes 或 Message.Payload 转换为 Sparkplug B Metric
    /// - 支持数据聚合（多条消息合并为一次 NDATA）
    /// - 支持断线自动重连
    /// </summary>
    public class SparkplugBOutputPlugin : OutputPluginBase
    {
        private readonly SparkplugBOutputConfig _config;
        private IMqttClient? _mqttClient;
        private CancellationTokenSource? _cts;
        private long _sequenceNumber; // 仅通过 Interlocked.Increment 访问

        // 聚合缓冲区
        private readonly List<SparkplugMetricJson> _aggregateMetrics = new();
        private readonly object _aggregateLock = new();
        private Timer? _aggregateTimer;
        private volatile bool _aggregatePending;

        // NDALIVE 心跳定时器（Sparkplug B 规范要求）
        private Timer? _keepAliveTimer;

        // 生命周期状态机：Unknown → Born → Alive → Dead
        // 使用 int 而非 enum 以兼容 Interlocked.Exchange/CompareExchange
        private volatile int _lifecycleState;

        public override string Name => _config.Name;
        public override string ProtocolType => "sparkplug-b";

        /// <summary>
        /// 当前生命周期状态
        /// </summary>
        public SparkplugBState LifecycleState => (SparkplugBState)_lifecycleState;

        public SparkplugBOutputPlugin(SparkplugBOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(_config.BrokerHost, _config.BrokerPort)
                .WithClientId($"sparkplug-b-{_config.EdgeNodeId}-{Guid.NewGuid().ToString("N")[..8]}")
                .WithCleanSession();

            if (!string.IsNullOrEmpty(_config.Username))
            {
                builder.WithCredentials(_config.Username, _config.Password);
            }

            await _mqttClient.ConnectAsync(builder.Build(), ct);

            // 发布 NBirth（节点出生）
            long seq = Interlocked.Increment(ref _sequenceNumber);
            var nbirthPayload = BuildPayload(seq, new[]
            {
                new SparkplugMetricJson
                {
                    Name = "bdSeq",
                    DataType = 11, // Int64
                    Value = seq,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                },
                new SparkplugMetricJson
                {
                    Name = "MAC Address",
                    DataType = 20, // String
                    Value = _config.EdgeNodeId,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                },
                new SparkplugMetricJson
                {
                    Name = "Firmware Version",
                    DataType = 20, // String
                    Value = "ProtocolGateway/1.0",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });

            await PublishAsync(SparkplugBTopics.NBirth(_config.GroupId, _config.EdgeNodeId), nbirthPayload);

            // 状态转换: Unknown → Born
            _lifecycleState = (int)SparkplugBState.Born;
            GatewayLog.Info(Name, "Lifecycle: Born");

            // 启动 NDALIVE 心跳定时器（Sparkplug B 规范要求）
            if (_config.KeepAliveIntervalMs > 0)
            {
                _keepAliveTimer = new Timer(KeepAliveCallback, null,
                    TimeSpan.FromMilliseconds(_config.KeepAliveIntervalMs),
                    TimeSpan.FromMilliseconds(_config.KeepAliveIntervalMs));
            }

            // 启动聚合定时器
            if (_config.AggregateIntervalMs > 0)
            {
                _aggregateTimer = new Timer(AggregateTimerCallback, null,
                    TimeSpan.FromMilliseconds(_config.AggregateIntervalMs),
                    TimeSpan.FromMilliseconds(_config.AggregateIntervalMs));
            }

            GatewayLog.Info(Name, $"Sparkplug B started: Group={_config.GroupId}, EdgeNode={_config.EdgeNodeId}");
        }

        /// <summary>
        /// NDALIVE 心跳回调 — 定期发送 NDALIVE 消息确认节点存活。
        /// Sparkplug B 规范要求边缘节点在 Born 后持续发送 NDALIVE。
        /// </summary>
        private void KeepAliveCallback(object? state)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                if (Interlocked.Exchange(ref _lifecycleState, (int)SparkplugBState.Dead) != (int)SparkplugBState.Dead)
                {
                    GatewayLog.Warn(Name, "Lifecycle: Dead (MQTT disconnected)");
                    RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, "MQTT connection lost, lifecycle: Dead");
                }
                return;
            }

            // Fire-and-forget：避免 Timer 回调中 .Wait() 阻塞线程池线程。
            // 使用 ContinueWith 观察异常，防止未观察的 Task 异常被吞掉。
            var keepAliveTask = Task.Run(async () =>
            {
                try
                {
                    long seq = Interlocked.Increment(ref _sequenceNumber);
                    var nalivePayload = BuildPayload(seq, Array.Empty<SparkplugMetricJson>());
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await PublishAsync(SparkplugBTopics.NAlive(_config.GroupId, _config.EdgeNodeId), nalivePayload, cts.Token);

                    if (Interlocked.CompareExchange(ref _lifecycleState, (int)SparkplugBState.Alive, (int)SparkplugBState.Born) == (int)SparkplugBState.Born)
                    {
                        GatewayLog.Info(Name, "Lifecycle: Alive");
                    }
                }
                catch (OperationCanceledException)
                {
                    GatewayLog.Warn(Name, "NDALIVE publish timed out after 5s");
                    if (Interlocked.Exchange(ref _lifecycleState, (int)SparkplugBState.Dead) != (int)SparkplugBState.Dead)
                    {
                        GatewayLog.Warn(Name, "Lifecycle: Dead (heartbeat publish timed out)");
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, "Heartbeat publish timed out, lifecycle: Dead");
                    }
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn(Name, $"NDALIVE heartbeat failed: {ex.Message}");
                    if (Interlocked.Exchange(ref _lifecycleState, (int)SparkplugBState.Dead) != (int)SparkplugBState.Dead)
                    {
                        GatewayLog.Warn(Name, "Lifecycle: Dead (heartbeat failed)");
                        RaiseDetailedStatusChanged(OutputPluginHealthLevel.Warning, $"Heartbeat failed, lifecycle: Dead");
                    }
                }
            });
            _ = keepAliveTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    GatewayLog.Error(Name, $"KeepAlive task faulted: {t.Exception}", t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        protected override async Task OnSendAsync(Message message, CancellationToken ct)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
                throw new InvalidOperationException("MQTT client is not connected");

            var metrics = BuildMetricsFromMessage(message);
            if (metrics.Count == 0)
                return;

            if (_config.AggregateIntervalMs > 0)
            {
                // 聚合模式：加入缓冲区，由定时器批量发布
                lock (_aggregateLock)
                {
                    _aggregateMetrics.AddRange(metrics);
                    _aggregatePending = true;
                }
            }
            else
            {
                // 即时模式：立即发布 NDATA
                long seq = Interlocked.Increment(ref _sequenceNumber);
                var payload = BuildPayload(seq, metrics);
                await PublishAsync(SparkplugBTopics.NData(_config.GroupId, _config.EdgeNodeId), payload, ct);
            }
        }

        private void AggregateTimerCallback(object? state)
        {
            List<SparkplugMetricJson> toFlush;
            lock (_aggregateLock)
            {
                if (!_aggregatePending || _aggregateMetrics.Count == 0)
                    return;
                toFlush = new List<SparkplugMetricJson>(_aggregateMetrics); // 快照副本
                _aggregateMetrics.Clear(); // 清空原列表
                _aggregatePending = false;
            }

            // Fire-and-forget 异步发布，不阻塞 Timer 回调。
            _ = Task.Run(async () =>
            {
                try
                {
                    long seq = Interlocked.Increment(ref _sequenceNumber);
                    var payload = BuildPayload(seq, toFlush);
                    var topic = SparkplugBTopics.NData(_config.GroupId, _config.EdgeNodeId);
                    await PublishAsync(topic, payload);
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn(Name, $"Aggregate flush failed: {ex.Message}");
                }
            }).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    GatewayLog.Error(Name, $"Aggregate flush task faulted: {t.Exception}", t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private List<SparkplugMetricJson> BuildMetricsFromMessage(Message message)
        {
            var metrics = new List<SparkplugMetricJson>();
            var ts = new DateTimeOffset(message.Timestamp.ToUniversalTime()).ToUnixTimeMilliseconds();

            // 优先使用结构化 Writes
            if (message.Writes != null && message.Writes.Count > 0)
            {
                foreach (var write in message.Writes)
                {
                    var metricName = _config.TagMappings?.TryGetValue(write.Address, out var mapped) == true
                        ? mapped
                        : write.Alias ?? write.Address;

                    metrics.Add(new SparkplugMetricJson
                    {
                        Name = metricName,
                        DataType = MapDataType(write.DataType),
                        Value = write.Value,
                        Timestamp = ts
                    });
                }
            }
            // 降级：将整个 Payload 作为单个 metric
            else if (message.Payload != null && message.Payload.Length > 0)
            {
                var value = message.ContentType switch
                {
                    "json" => Encoding.UTF8.GetString(message.Payload),
                    "text" => Encoding.UTF8.GetString(message.Payload),
                    "hex" => BitConverter.ToString(message.Payload).Replace("-", ""),
                    _ => Convert.ToBase64String(message.Payload)
                };

                metrics.Add(new SparkplugMetricJson
                {
                    Name = message.Topic ?? "payload",
                    DataType = 20, // String
                    Value = value,
                    Timestamp = ts
                });
            }

            return metrics;
        }

        private static int MapDataType(string dataType)
        {
            return dataType.ToUpperInvariant() switch
            {
                "BOOL" or "BIT" => 1,    // Boolean
                "BYTE" => 2,             // Int8
                "INT16" or "WORD" => 3,  // Int16
                "UINT16" => 4,           // UInt16
                "INT32" or "DWORD" => 5, // Int32
                "UINT32" => 6,           // UInt32
                "INT64" => 11,           // Int64
                "UINT64" => 12,          // UInt64
                "FLOAT" or "FLOAT32" => 7,  // Float
                "DOUBLE" or "FLOAT64" => 8, // Double
                "STRING" => 20,          // String
                _ => 20                  // 默认 String
            };
        }

        private string BuildPayload(long seq, IEnumerable<SparkplugMetricJson> metrics)
        {
            var payload = new
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Seq = seq,
                Metrics = metrics
            };
            return JsonSerializer.Serialize(payload);
        }

        private async Task PublishAsync(string topic, string payloadJson, CancellationToken ct = default)
        {
            if (_mqttClient == null) throw new InvalidOperationException("MQTT client not initialized");

            var appMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payloadJson)
                .WithQualityOfServiceLevel((MQTTnet.Protocol.MqttQualityOfServiceLevel)_config.Qos)
                .WithRetainFlag(_config.Retain)
                .Build();

            await _mqttClient.PublishAsync(appMessage, ct);
        }

        protected override async Task OnStopAsync()
        {
            _aggregateTimer?.Dispose();
            _aggregateTimer = null;

            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                try
                {
                    // 发布 NDeath（节点死亡）
                    long seq = Interlocked.Increment(ref _sequenceNumber);
                    var ndeathPayload = JsonSerializer.Serialize(new
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Seq = seq,
                        Metrics = new[]
                        {
                            new
                            {
                                Name = "bdSeq",
                                DataType = 11,
                                Value = seq,
                                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            }
                        }
                    });
                    await PublishAsync(SparkplugBTopics.NDeath(_config.GroupId, _config.EdgeNodeId), ndeathPayload);
                }
                catch
                {
                    // NDeath 发布失败不影响停止流程
                }

                await _mqttClient.DisconnectAsync();
                _mqttClient.Dispose();
            }

            _mqttClient = null;
            _cts?.Dispose();
            _cts = null;

            GatewayLog.Info(Name, "Sparkplug B stopped");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _aggregateTimer?.Dispose();
                _aggregateTimer = null;
                _cts?.Dispose();
                _cts = null;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Sparkplug B Metric 轻量级表示（仅用于 JSON 序列化）
    /// </summary>
    public class SparkplugMetricJson
    {
        public string Name { get; set; } = "";
        public int DataType { get; set; }
        public object? Value { get; set; }
        public long Timestamp { get; set; }
        public bool IsHistorical { get; set; } = false;
        public bool IsDeadband { get; set; } = false;
        public bool IsTemplate { get; set; } = false;
        public bool IsAlias { get; set; } = false;
        public bool IsNull { get; set; } = false;
        public bool IsUUID { get; set; } = false;
    }

    /// <summary>
    /// Sparkplug B 主题构建工具类
    /// 主题格式: spBv1.0/{GroupId}/{MessageType}/{EdgeNodeId}[/DeviceId]
    /// </summary>
    public static class SparkplugBTopics
    {
        private const string Prefix = "spBv1.0";

        public static string NBirth(string groupId, string edgeNodeId) =>
            $"{Prefix}/{groupId}/NBIRTH/{edgeNodeId}";

        public static string NDeath(string groupId, string edgeNodeId) =>
            $"{Prefix}/{groupId}/NDEATH/{edgeNodeId}";

        public static string NAlive(string groupId, string edgeNodeId) =>
            $"{Prefix}/{groupId}/NDALIVE/{edgeNodeId}";

        public static string NData(string groupId, string edgeNodeId) =>
            $"{Prefix}/{groupId}/NDATA/{edgeNodeId}";

        public static string DBirth(string groupId, string edgeNodeId, string deviceId) =>
            $"{Prefix}/{groupId}/DBIRTH/{edgeNodeId}/{deviceId}";

        public static string DDeath(string groupId, string edgeNodeId, string deviceId) =>
            $"{Prefix}/{groupId}/DDEATH/{edgeNodeId}/{deviceId}";

        public static string DData(string groupId, string edgeNodeId, string deviceId) =>
            $"{Prefix}/{groupId}/DDATA/{edgeNodeId}/{deviceId}";

        public static string NCmd(string groupId, string edgeNodeId) =>
            $"{Prefix}/{groupId}/NCMD/{edgeNodeId}";

        public static string DCmd(string groupId, string edgeNodeId, string deviceId) =>
            $"{Prefix}/{groupId}/DCMD/{edgeNodeId}/{deviceId}";
    }

    /// <summary>
    /// Sparkplug B 节点生命周期状态
    /// 规范定义的状态转换: Unknown → Born → Alive → Dead
    /// - Born: NBirth 发布成功，节点已注册
    /// - Alive: NDALIVE 心跳正常，节点存活
    /// - Dead: NDeath 发布或连接断开，节点离线
    /// </summary>
    public enum SparkplugBState
    {
        Unknown = 0,
        Born = 1,
        Alive = 2,
        Dead = 3
    }
}
