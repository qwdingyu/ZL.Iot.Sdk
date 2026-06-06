// ============================================================
// 文件：GatewayManagerDTOs.cs
// 描述：GatewayManager 使用的数据传输对象（DTO）
// 功能：定义输出插件状态、测试发送结果、指标快照、死信信息、管理器配置选项
//       这些类型供 TMom 和 PlcSimulator.UI 共用，消除两边的重复定义
// 修改日期：2026-06-01
// ============================================================

using System;
using System.Collections.Generic;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 输出插件状态快照
    /// </summary>
    public class OutputPluginStatus
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 协议类型
        /// </summary>
        public string ProtocolType { get; set; } = "";

        /// <summary>
        /// 插件状态字符串
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// PluginStatus 枚举值（强类型）
        /// </summary>
        public PluginStatus StatusEnum { get; set; }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// 断路器状态（Closed/Open/HalfOpen）
        /// </summary>
        public string CircuitBreakerState { get; set; } = "";

        /// <summary>
        /// 健康级别
        /// </summary>
        public OutputPluginHealthLevel HealthLevel { get; set; }

        /// <summary>
        /// 最近状态消息
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 统一错误码
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// 建议处置动作
        /// </summary>
        public string? Advice { get; set; }

        /// <summary>
        /// 最近一次异常
        /// </summary>
        public Exception? LastException { get; set; }

        /// <summary>
        /// 连续失败次数
        /// </summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>
        /// 状态变更时间戳
        /// </summary>
        public DateTime? Timestamp { get; set; }
    }

    /// <summary>
    /// 网关测试发送结果
    /// </summary>
    public class GatewayTestResult
    {
        /// <summary>
        /// 输出插件名称
        /// </summary>
        public string OutputName { get; set; } = "";

        /// <summary>
        /// 是否发送成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 发送耗时（毫秒）
        /// </summary>
        public double DurationMs { get; set; }

        /// <summary>
        /// 错误信息（失败时）
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 网关指标快照
    /// </summary>
    public class GatewayMetricsSnapshot
    {
        /// <summary>
        /// 快照时间
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 网关是否正在运行
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// 队列中的消息数量
        /// </summary>
        public int QueuedMessageCount { get; set; }

        /// <summary>
        /// 队列容量上限
        /// </summary>
        public int QueueCapacity { get; set; }

        /// <summary>
        /// 死信队列中的消息数量
        /// </summary>
        public int DeadLetterCount { get; set; }

        /// <summary>
        /// 所有输出插件的状态列表
        /// </summary>
        public IReadOnlyList<OutputPluginStatus> OutputPlugins { get; set; } = Array.Empty<OutputPluginStatus>();

        /// <summary>
        /// 各输出插件的断路器状态
        /// </summary>
        public IReadOnlyDictionary<string, string> CircuitBreakerStates { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 最近一次发送结果列表（强类型，保留完整诊断信息）
        /// </summary>
        public IReadOnlyList<GatewaySendResult> LastSendResults { get; set; } = Array.Empty<GatewaySendResult>();

        /// <summary>
        /// 流水线滑动窗口指标（P50/P95/P99 延迟、吞吐量、错误率）
        /// </summary>
        public PipelineMetricsSnapshot? PipelineMetrics { get; set; }

        /// <summary>
        /// 背压级别：根据队列使用率自动计算。
        /// "Normal" (&lt;50%), "Elevated" (50-75%), "High" (75-90%), "Critical" (&gt;90%)
        /// </summary>
        public string BackpressureLevel
        {
            get
            {
                if (QueueCapacity <= 0) return "Normal";
                double ratio = (double)QueuedMessageCount / QueueCapacity;
                if (ratio >= 0.9) return "Critical";
                if (ratio >= 0.75) return "High";
                if (ratio >= 0.5) return "Elevated";
                return "Normal";
            }
        }
    }

    /// <summary>
    /// 死信信息
    /// </summary>
    public class DeadLetterInfo
    {
        /// <summary>
        /// 链路追踪 ID
        /// </summary>
        public string TraceId { get; set; } = "";

        /// <summary>
        /// 消息主题
        /// </summary>
        public string Topic { get; set; } = "";

        /// <summary>
        /// 目标输出插件名称
        /// </summary>
        public string OutputName { get; set; } = "";

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 失败时间
        /// </summary>
        public DateTimeOffset FailedAt { get; set; }
    }

    /// <summary>
    /// GatewayManager 配置选项
    /// </summary>
    public class GatewayManagerOptions
    {
        /// <summary>
        /// 消息队列容量（默认 10000，最小 100）
        /// </summary>
        public int QueueCapacity { get; set; } = 10000;

        /// <summary>
        /// 发送超时（毫秒，默认 30000，最小 1000）
        /// </summary>
        public int SendTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// 最大重试次数（默认 3，最小 0）
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// 重试基础延迟（毫秒，默认 100，最小 10）
        /// </summary>
        public int RetryBaseDelayMs { get; set; } = 100;

        /// <summary>
        /// 断路器失败阈值（默认 5，最小 1）
        /// </summary>
        public int CircuitBreakerFailureThreshold { get; set; } = 5;

        /// <summary>
        /// 断路器恢复时间（毫秒，默认 60000，最小 1000）
        /// </summary>
        public int CircuitBreakerRecoveryTimeMs { get; set; } = 60000;

        /// <summary>
        /// 是否启用 HTTP 健康检查服务（默认 false）。
        /// 启用后暴露 /health、/health/live、/health/ready、/metrics 端点。
        /// </summary>
        public bool EnableHealthCheckHttp { get; set; }

        /// <summary>
        /// HTTP 健康检查监听端口（默认 5000）。仅在 EnableHealthCheckHttp = true 时生效。
        /// </summary>
        public int HealthCheckHttpPort { get; set; } = 5000;

        /// <summary>
        /// 是否启用消息去重过滤器（默认 false）。
        /// 启用后使用 5 分钟滑动窗口，基于 Topic+Payload 指纹拦截重复消息。
        /// 适用于 MQTT QoS 1 重传、HTTP 重试、Bridge 重复事件等场景。
        /// </summary>
        public bool EnableDeduplication { get; set; }

        /// <summary>
        /// 是否启用死信持久化（默认 true）。
        /// 启用后死信消息自动落盘到 SQLite，网关重启后可审计历史失败消息。
        /// </summary>
        public bool EnableDeadLetterPersistence { get; set; } = true;

        /// <summary>
        /// 死信持久化数据库路径（默认 ./gateway_deadletters.db）。
        /// 仅在 EnableDeadLetterPersistence = true 时生效。
        /// </summary>
        public string DeadLetterDbPath { get; set; } = "gateway_deadletters.db";

        /// <summary>
        /// 死信最大持久化行数（默认 5000）。超出时删除最旧记录。
        /// </summary>
        public int DeadLetterMaxCount { get; set; } = 5000;

        /// <summary>
        /// 死信保留小时数（默认 72 = 3 天）。0 表示不限制。
        /// </summary>
        public int DeadLetterRetentionHours { get; set; } = 72;

        /// <summary>
        /// 验证配置合法性 — 复用 ConfigValidation 工具类
        /// </summary>
        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (QueueCapacity < 100)
                errors.Add(new ConfigValidationError(nameof(QueueCapacity), $"队列容量最小 100，当前: {QueueCapacity}"));
            if (!ConfigValidation.IsValidTimeout(SendTimeoutMs, 1000))
                errors.Add(new ConfigValidationError(nameof(SendTimeoutMs), $"发送超时最小 1000ms，当前: {SendTimeoutMs}"));
            if (MaxRetryAttempts < 0)
                errors.Add(new ConfigValidationError(nameof(MaxRetryAttempts), $"最大重试次数不能为负，当前: {MaxRetryAttempts}"));
            if (RetryBaseDelayMs < 10)
                errors.Add(new ConfigValidationError(nameof(RetryBaseDelayMs), $"重试基础延迟最小 10ms，当前: {RetryBaseDelayMs}"));
            if (CircuitBreakerFailureThreshold < 1)
                errors.Add(new ConfigValidationError(nameof(CircuitBreakerFailureThreshold), $"断路器失败阈值最小 1，当前: {CircuitBreakerFailureThreshold}"));
            if (!ConfigValidation.IsValidTimeout(CircuitBreakerRecoveryTimeMs, 1000))
                errors.Add(new ConfigValidationError(nameof(CircuitBreakerRecoveryTimeMs), $"断路器恢复时间最小 1000ms，当前: {CircuitBreakerRecoveryTimeMs}"));
            return errors;
        }
    }
}
