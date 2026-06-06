using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    public static class GatewayMetadataKeys
    {
        public const string TraceId = "TraceId";
        public const string Source = "Source";
        public const string Count = "Count";
        public const string OutputId = "OutputId";
        public const string OutputType = "OutputType";
        /// <summary>
        /// 目标地址（协议相关，如 S7 的 DB1.DBW0, Modbus 的 40001, OPC UA 的 NodeId）
        /// </summary>
        public const string TargetAddress = "TargetAddress";
        /// <summary>
        /// 数据类型（协议相关，如 S7 的 Word/DWord/Real, Modbus 的 Int16/UInt16）
        /// </summary>
        public const string DataType = "DataType";
        /// <summary>
        /// 死信重试计数 — 用于幂等保护，防止 RetryDeadLettersAsync 无限循环。
        /// </summary>
        public const string DeadLetterRetryCount = "DeadLetterRetryCount";
    }

    public static class GatewayErrorCodes
    {
        public const string None = "GW-0000";
        public const string ConfigurationInvalid = "GW-CFG-0001";
        public const string ConfigurationMissing = "GW-CFG-0002";
        public const string ConnectionFailed = "GW-CONN-0001";
        public const string ConnectionLost = "GW-CONN-0002";
        public const string ConnectionRefused = "GW-CONN-0003";
        public const string ConnectionTimeout = "GW-CONN-0004";
        public const string HandshakeFailed = "GW-HANDSHAKE-0001";
        public const string AuthenticationFailed = "GW-AUTH-0001";
        public const string AddressResolutionFailed = "GW-ADDR-0001";
        public const string DataTypeInvalid = "GW-DATA-0001";
        public const string DeviceRejected = "GW-DEVICE-0001";
        public const string Timeout = "GW-TIMEOUT-0001";
        public const string ProtocolFormatInvalid = "GW-PROTOCOL-0001";
        public const string InternalException = "GW-INTERNAL-0001";
        public const string Cached = "GW-DEGRADED-0001";
        public const string DeadLettered = "GW-DEGRADED-0002";
        public const string OutputCircuitOpen = "GW-DEGRADED-0003";
    }

    public enum GatewayAckLevel
    {
        None,
        Accepted,
        Delivered,
        Confirmed
    }

    public enum GatewaySendFinalStatus
    {
        Success,
        PartialSuccess,
        Failed,
        Cached,
        DeadLettered,
        Skipped
    }

    public sealed class GatewayErrorDescriptor
    {
        public string Code { get; init; } = GatewayErrorCodes.None;
        public string Category { get; init; } = "Normal";
        public string UserMessage { get; init; } = "操作成功";
        public string Advice { get; init; } = "无需额外处理。";
    }

    public sealed class GatewayDiagnosticInfo
    {
        public string ErrorCode { get; init; } = GatewayErrorCodes.None;
        public string Category { get; init; } = "Normal";
        public string TechnicalMessage { get; init; } = string.Empty;
        public string UserMessage { get; init; } = string.Empty;
        public string Advice { get; init; } = string.Empty;
        public string TraceId { get; init; } = string.Empty;
        public Exception? Exception { get; init; }

        public bool HasError => !string.Equals(ErrorCode, GatewayErrorCodes.None, StringComparison.Ordinal);

        public static GatewayDiagnosticInfo Success(string traceId, string? message = null)
        {
            return new GatewayDiagnosticInfo
            {
                TraceId = traceId ?? string.Empty,
                TechnicalMessage = message ?? "Success",
                UserMessage = message ?? "发送成功",
                Advice = "无需额外处理。"
            };
        }
    }

    public sealed class GatewaySendResult
    {
        public string TraceId { get; init; } = string.Empty;
        public string OutputId { get; init; } = string.Empty;
        public string OutputName { get; init; } = string.Empty;
        public string OutputType { get; init; } = string.Empty;
        public int AttemptCount { get; init; }
        public GatewaySendFinalStatus FinalStatus { get; init; }
        public GatewayAckLevel AckLevel { get; init; }
        public string ErrorCode { get; init; } = GatewayErrorCodes.None;
        public string ErrorMessage { get; init; } = string.Empty;
        public string UserMessage { get; init; } = string.Empty;
        public string Advice { get; init; } = string.Empty;
        public double DurationMs { get; init; }
        public string ResponseSummary { get; init; } = string.Empty;
        public int ItemCount { get; init; }
        public bool CachedToDisk { get; init; }
        public bool DeadLettered { get; init; }
        public string Source { get; init; } = string.Empty;

        public bool Success => FinalStatus is GatewaySendFinalStatus.Success or GatewaySendFinalStatus.PartialSuccess;
    }

    public static class GatewayTraceContext
    {
        public static string EnsureTraceId(Message message)
        {
            if (message == null)
            {
                return Guid.NewGuid().ToString("N");
            }

            if (message.Metadata.TryGetValue(GatewayMetadataKeys.TraceId, out var existing)
                && !string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            var traceId = !string.IsNullOrWhiteSpace(message.Id)
                ? message.Id
                : Guid.NewGuid().ToString("N");

            message.Metadata[GatewayMetadataKeys.TraceId] = traceId;
            return traceId;
        }
    }

    public static class GatewayErrorCatalog
    {
        public static GatewayDiagnosticInfo FromException(Exception? exception, string traceId, string? fallbackMessage = null)
        {
            if (exception == null)
            {
                return new GatewayDiagnosticInfo
                {
                    TraceId = traceId ?? string.Empty,
                    TechnicalMessage = fallbackMessage ?? string.Empty,
                    UserMessage = fallbackMessage ?? "未提供异常信息",
                    Advice = "请检查网关运行日志。"
                };
            }

            var descriptor = Classify(exception, fallbackMessage);
            return new GatewayDiagnosticInfo
            {
                TraceId = traceId ?? string.Empty,
                ErrorCode = descriptor.Code,
                Category = descriptor.Category,
                TechnicalMessage = exception.Message,
                UserMessage = descriptor.UserMessage,
                Advice = descriptor.Advice,
                Exception = exception
            };
        }

        public static GatewayDiagnosticInfo Create(string code, string technicalMessage, string userMessage, string advice, string traceId, Exception? exception = null)
        {
            return new GatewayDiagnosticInfo
            {
                TraceId = traceId ?? string.Empty,
                ErrorCode = string.IsNullOrWhiteSpace(code) ? GatewayErrorCodes.InternalException : code,
                Category = GetCategory(code),
                TechnicalMessage = technicalMessage ?? string.Empty,
                UserMessage = userMessage ?? string.Empty,
                Advice = advice ?? string.Empty,
                Exception = exception
            };
        }

        public static GatewayDiagnosticInfo CreateFromCode(string code, string technicalMessage, string traceId, Exception? exception = null)
        {
            var descriptor = Describe(code);
            return new GatewayDiagnosticInfo
            {
                TraceId = traceId ?? string.Empty,
                ErrorCode = descriptor.Code,
                Category = descriptor.Category,
                TechnicalMessage = technicalMessage ?? string.Empty,
                UserMessage = descriptor.UserMessage,
                Advice = descriptor.Advice,
                Exception = exception
            };
        }

        public static GatewayErrorDescriptor Describe(string code)
        {
            return code switch
            {
                GatewayErrorCodes.None => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.None,
                    Category = "Normal",
                    UserMessage = "操作成功",
                    Advice = "无需额外处理。"
                },
                GatewayErrorCodes.ConfigurationInvalid => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.ConfigurationInvalid,
                    Category = "Configuration",
                    UserMessage = "输出配置格式无效",
                    Advice = "请检查地址、端口、连接串或协议专有参数是否填写完整。"
                },
                GatewayErrorCodes.ConfigurationMissing => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.ConfigurationMissing,
                    Category = "Configuration",
                    UserMessage = "缺少必要的输出配置",
                    Advice = "请先补齐必填项，再重新执行预检或发送。"
                },
                GatewayErrorCodes.ConnectionFailed => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.ConnectionFailed,
                    Category = "Connection",
                    UserMessage = "目标端连接失败",
                    Advice = "请检查网络、端口监听状态、防火墙和目标服务是否已启动。"
                },
                GatewayErrorCodes.HandshakeFailed => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.HandshakeFailed,
                    Category = "Handshake",
                    UserMessage = "协议握手失败",
                    Advice = "请确认协议版本、会话初始化参数和目标端兼容性。"
                },
                GatewayErrorCodes.AuthenticationFailed => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.AuthenticationFailed,
                    Category = "Authentication",
                    UserMessage = "认证或会话建立失败",
                    Advice = "请检查用户名、密码、证书或会话令牌是否正确。"
                },
                GatewayErrorCodes.AddressResolutionFailed => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.AddressResolutionFailed,
                    Category = "Address",
                    UserMessage = "地址或对象路径无法解析",
                    Advice = "请核对点位地址、对象引用、Tag 名称或逻辑节点路径。"
                },
                GatewayErrorCodes.DataTypeInvalid => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.DataTypeInvalid,
                    Category = "DataType",
                    UserMessage = "数据类型与目标协议不匹配",
                    Advice = "请确认值类型、单位、枚举范围和目标寄存器/对象的数据定义。"
                },
                GatewayErrorCodes.DeviceRejected => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.DeviceRejected,
                    Category = "Device",
                    UserMessage = "设备拒绝当前写入请求",
                    Advice = "请检查权限、写入窗口、设备状态或目标对象是否允许写入。"
                },
                GatewayErrorCodes.Timeout => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.Timeout,
                    Category = "Timeout",
                    UserMessage = "目标响应超时",
                    Advice = "请检查网络时延、设备负载和超时参数配置。"
                },
                GatewayErrorCodes.ProtocolFormatInvalid => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.ProtocolFormatInvalid,
                    Category = "ProtocolFormat",
                    UserMessage = "报文格式或载荷内容无效",
                    Advice = "请检查发送模板、JSON/XML 格式以及协议报文构造逻辑。"
                },
                GatewayErrorCodes.Cached => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.Cached,
                    Category = "Degraded",
                    UserMessage = "发送失败，消息已缓存到磁盘",
                    Advice = "请排查链路故障后回放缓存数据，避免长期积压。"
                },
                GatewayErrorCodes.DeadLettered => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.DeadLettered,
                    Category = "Degraded",
                    UserMessage = "发送失败，消息已进入死信队列",
                    Advice = "请检查错误明细并人工处理死信，避免数据长期丢失。"
                },
                GatewayErrorCodes.OutputCircuitOpen => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.OutputCircuitOpen,
                    Category = "Degraded",
                    UserMessage = "输出插件断路器已打开",
                    Advice = "输出插件连续失败次数超过阈值，断路器已打开以保护系统。请检查输出插件状态，使用 ResetCircuitBreaker 重置断路器。"
                },
                _ => new GatewayErrorDescriptor
                {
                    Code = GatewayErrorCodes.InternalException,
                    Category = "Internal",
                    UserMessage = "网关内部异常",
                    Advice = "请查看日志和 TraceId，必要时联系开发排查。"
                }
            };
        }

        public static string GetCategory(string? code)
        {
            return Describe(code ?? GatewayErrorCodes.InternalException).Category;
        }

        private static GatewayErrorDescriptor Classify(Exception exception, string? fallbackMessage)
        {
            if (exception is TimeoutException || exception is TaskCanceledException)
            {
                return Describe(GatewayErrorCodes.Timeout);
            }

            if (exception is SocketException)
            {
                return Describe(GatewayErrorCodes.ConnectionFailed);
            }

            if (exception is IOException)
            {
                return Describe(GatewayErrorCodes.ConnectionFailed);
            }

            // HttpRequestException 包含 HTTP 连接失败、DNS 解析失败等
            if (exception is System.Net.Http.HttpRequestException)
            {
                return Describe(GatewayErrorCodes.ConnectionFailed);
            }

            var message = (exception.Message ?? fallbackMessage ?? string.Empty).ToLowerInvariant();
            if (message.Contains("timeout"))
            {
                return Describe(GatewayErrorCodes.Timeout);
            }

            if (message.Contains("auth") || message.Contains("login") || message.Contains("password") || message.Contains("certificate") || message.Contains("token"))
            {
                return Describe(GatewayErrorCodes.AuthenticationFailed);
            }

            if (message.Contains("handshake") || message.Contains("session handshake") || message.Contains("session timeout"))
            {
                return Describe(GatewayErrorCodes.HandshakeFailed);
            }

            // "object" 和 "path" 单字过于宽泛，仅保留短语匹配 "object path"/"object reference"
            // "address"/"tag"/"node" 在工控协议语境中特异性较高，保留
            if (message.Contains("address") || message.Contains("tag") || message.Contains("object path") || message.Contains("object reference") || message.Contains("node"))
            {
                return Describe(GatewayErrorCodes.AddressResolutionFailed);
            }

            if (message.Contains("config") || message.Contains("invalid connection string") || message.Contains("missing required") || message.Contains("missing configuration"))
            {
                return Describe(GatewayErrorCodes.ConfigurationInvalid);
            }

            if (message.Contains("denied") || message.Contains("reject") || message.Contains("readonly") || message.Contains("read-only"))
            {
                return Describe(GatewayErrorCodes.DeviceRejected);
            }

            if (message.Contains("format") || message.Contains("payload") || message.Contains("json") || message.Contains("xml"))
            {
                return Describe(GatewayErrorCodes.ProtocolFormatInvalid);
            }

            if (message.Contains("cannot convert type") || message.Contains("cast"))
            {
                return Describe(GatewayErrorCodes.ProtocolFormatInvalid);
            }

            if (message.Contains("invalid"))
            {
                return Describe(GatewayErrorCodes.ConfigurationInvalid);
            }

            return Describe(GatewayErrorCodes.InternalException);
        }
    }
}
