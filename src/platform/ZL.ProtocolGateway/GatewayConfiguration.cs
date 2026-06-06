// ============================================================
// 文件：GatewayConfiguration.cs
// 描述：ProtocolGateway 配置系统 — JSON 文件 + 环境变量 + 命令行参数
// 功能：零第三方依赖的配置加载，支持多层覆盖（文件 < 环境变量 < 命令行）
// 修改日期：2026-06-05
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// ProtocolGateway 完整配置 — 支持 JSON 文件、环境变量、命令行参数多层覆盖。
    /// <para>优先级：默认值 < JSON 文件 < 环境变量 < 命令行参数</para>
    /// </summary>
    public class GatewayConfiguration
    {
        #region Pipeline

        /// <summary>消息队列容量（默认 10000，最小 100）</summary>
        [JsonPropertyName("queueCapacity")]
        public int QueueCapacity { get; set; } = 10000;

        /// <summary>发送超时毫秒（默认 30000，最小 1000）</summary>
        [JsonPropertyName("sendTimeoutMs")]
        public int SendTimeoutMs { get; set; } = 30000;

        /// <summary>最大重试次数（默认 3，最小 0）</summary>
        [JsonPropertyName("maxRetryAttempts")]
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>重试基础延迟毫秒（默认 100，最小 10）</summary>
        [JsonPropertyName("retryBaseDelayMs")]
        public int RetryBaseDelayMs { get; set; } = 100;

        /// <summary>断路器失败阈值（默认 5，最小 1）</summary>
        [JsonPropertyName("circuitBreakerFailureThreshold")]
        public int CircuitBreakerFailureThreshold { get; set; } = 5;

        /// <summary>断路器恢复时间毫秒（默认 60000，最小 1000）</summary>
        [JsonPropertyName("circuitBreakerRecoveryTimeMs")]
        public int CircuitBreakerRecoveryTimeMs { get; set; } = 60000;

        #endregion

        #region 健康检查

        /// <summary>是否启用 HTTP 健康检查服务</summary>
        [JsonPropertyName("enableHealthCheckHttp")]
        public bool EnableHealthCheckHttp { get; set; }

        /// <summary>健康检查 HTTP 端口（默认 5000）</summary>
        [JsonPropertyName("healthCheckHttpPort")]
        public int HealthCheckHttpPort { get; set; } = 5000;

        #endregion

        #region 日志

        /// <summary>日志级别：Debug/Info/Warn/Error（默认 Info）</summary>
        [JsonPropertyName("logLevel")]
        public string LogLevel { get; set; } = "Info";

        /// <summary>日志文件目录（默认 %TEMP%\ProtocolGateway）</summary>
        [JsonPropertyName("logDir")]
        public string? LogDir { get; set; }

        /// <summary>是否启用文件日志输出</summary>
        [JsonPropertyName("enableFileLog")]
        public bool EnableFileLog { get; set; }

        /// <summary>单日志文件最大 MB（默认 50）</summary>
        [JsonPropertyName("logMaxFileSizeMb")]
        public int LogMaxFileSizeMb { get; set; } = 50;

        /// <summary>保留最近多少个日志文件（默认 7）</summary>
        [JsonPropertyName("logRetainedFiles")]
        public int LogRetainedFiles { get; set; } = 7;

        #endregion

        #region 限流

        /// <summary>全局速率限制（每秒消息数，0 表示不限流）</summary>
        [JsonPropertyName("rateLimitPerSecond")]
        public double RateLimitPerSecond { get; set; }

        #endregion

        #region 死信

        /// <summary>死信最大保留条数（默认 5000）</summary>
        [JsonPropertyName("deadLetterMaxCount")]
        public int DeadLetterMaxCount { get; set; } = 5000;

        /// <summary>死信保留小时数（默认 72）</summary>
        [JsonPropertyName("deadLetterRetentionHours")]
        public int DeadLetterRetentionHours { get; set; } = 72;

        /// <summary>死信持久化数据库路径（默认 ./gateway_deadletter.db）</summary>
        [JsonPropertyName("deadLetterDbPath")]
        public string DeadLetterDbPath { get; set; } = "gateway_deadletter.db";

        #endregion

        /// <summary>
        /// 转换为 GatewayManagerOptions。
        /// </summary>
        public GatewayManagerOptions ToManagerOptions()
        {
            return new GatewayManagerOptions
            {
                QueueCapacity = QueueCapacity,
                SendTimeoutMs = SendTimeoutMs,
                MaxRetryAttempts = MaxRetryAttempts,
                RetryBaseDelayMs = RetryBaseDelayMs,
                CircuitBreakerFailureThreshold = CircuitBreakerFailureThreshold,
                CircuitBreakerRecoveryTimeMs = CircuitBreakerRecoveryTimeMs,
                EnableHealthCheckHttp = EnableHealthCheckHttp,
                HealthCheckHttpPort = HealthCheckHttpPort
            };
        }

        /// <summary>
        /// 验证配置合法性。
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

    /// <summary>
    /// 配置加载器 — 支持 JSON 文件 + 环境变量 + 命令行参数多层覆盖。
    /// <para>优先级：默认值 < JSON 文件 < 环境变量 < 命令行参数</para>
    /// <para>环境变量前缀: GATEWAY_（如 GATEWAY_QUEUE_CAPACITY=20000）</para>
    /// </summary>
    public static class GatewayConfigurationLoader
    {
        private const string EnvPrefix = "GATEWAY_";

        /// <summary>
        /// 从 JSON 配置文件加载。
        /// </summary>
        /// <param name="configPath">JSON 配置文件路径，null 表示使用默认路径（./appsettings.json）</param>
        /// <param name="args">命令行参数（可选）</param>
        public static GatewayConfiguration LoadFromJson(string? configPath = null, string[]? args = null)
        {
            configPath ??= "appsettings.json";
            var config = new GatewayConfiguration();

            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    var fileConfig = JsonSerializer.Deserialize<GatewayConfiguration>(json, options);
                    if (fileConfig != null)
                    {
                        CopyNonDefaults(fileConfig, config);
                    }
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("GatewayConfiguration", $"Failed to load config from {configPath}: {ex.Message}");
                }
            }

            // 环境变量覆盖
            ApplyEnvironmentVariables(config);

            // 命令行参数覆盖
            if (args != null)
            {
                ApplyCommandLineArgs(args, config);
            }

            return config;
        }

        /// <summary>
        /// 仅使用默认值。
        /// </summary>
        public static GatewayConfiguration LoadDefaults()
        {
            return new GatewayConfiguration();
        }

        private static void CopyNonDefaults(GatewayConfiguration source, GatewayConfiguration target)
        {
            // JSON 反序列化会将所有字段设为文件中的值（包括默认值 0/false），
            // 所以我们直接覆盖所有属性 — 文件中的值优先级高于默认值。
            target.QueueCapacity = source.QueueCapacity;
            target.SendTimeoutMs = source.SendTimeoutMs;
            target.MaxRetryAttempts = source.MaxRetryAttempts;
            target.RetryBaseDelayMs = source.RetryBaseDelayMs;
            target.CircuitBreakerFailureThreshold = source.CircuitBreakerFailureThreshold;
            target.CircuitBreakerRecoveryTimeMs = source.CircuitBreakerRecoveryTimeMs;
            target.EnableHealthCheckHttp = source.EnableHealthCheckHttp;
            target.HealthCheckHttpPort = source.HealthCheckHttpPort;
            target.LogLevel = source.LogLevel;
            target.LogDir = source.LogDir;
            target.EnableFileLog = source.EnableFileLog;
            target.LogMaxFileSizeMb = source.LogMaxFileSizeMb;
            target.LogRetainedFiles = source.LogRetainedFiles;
            target.RateLimitPerSecond = source.RateLimitPerSecond;
            target.DeadLetterMaxCount = source.DeadLetterMaxCount;
            target.DeadLetterRetentionHours = source.DeadLetterRetentionHours;
            target.DeadLetterDbPath = source.DeadLetterDbPath;
        }

        private static void ApplyEnvironmentVariables(GatewayConfiguration config)
        {
            SetEnv<int>(nameof(config.QueueCapacity), "QUEUE_CAPACITY", v => config.QueueCapacity = v);
            SetEnv<int>(nameof(config.SendTimeoutMs), "SEND_TIMEOUT_MS", v => config.SendTimeoutMs = v);
            SetEnv<int>(nameof(config.MaxRetryAttempts), "MAX_RETRY_ATTEMPTS", v => config.MaxRetryAttempts = v);
            SetEnv<int>(nameof(config.RetryBaseDelayMs), "RETRY_BASE_DELAY_MS", v => config.RetryBaseDelayMs = v);
            SetEnv<int>(nameof(config.CircuitBreakerFailureThreshold), "CB_FAILURE_THRESHOLD", v => config.CircuitBreakerFailureThreshold = v);
            SetEnv<int>(nameof(config.CircuitBreakerRecoveryTimeMs), "CB_RECOVERY_TIME_MS", v => config.CircuitBreakerRecoveryTimeMs = v);
            SetEnv<bool>(nameof(config.EnableHealthCheckHttp), "ENABLE_HEALTH_CHECK_HTTP", v => config.EnableHealthCheckHttp = v);
            SetEnv<int>(nameof(config.HealthCheckHttpPort), "HEALTH_CHECK_HTTP_PORT", v => config.HealthCheckHttpPort = v);
            SetEnv<string>(nameof(config.LogLevel), "LOG_LEVEL", v => config.LogLevel = v);
            SetEnv<string>(nameof(config.LogDir), "LOG_DIR", v => config.LogDir = v);
            SetEnv<bool>(nameof(config.EnableFileLog), "ENABLE_FILE_LOG", v => config.EnableFileLog = v);
            SetEnv<int>(nameof(config.LogMaxFileSizeMb), "LOG_MAX_FILE_SIZE_MB", v => config.LogMaxFileSizeMb = v);
            SetEnv<int>(nameof(config.LogRetainedFiles), "LOG_RETAINED_FILES", v => config.LogRetainedFiles = v);
            SetEnv<double>(nameof(config.RateLimitPerSecond), "RATE_LIMIT_PER_SECOND", v => config.RateLimitPerSecond = v);
            SetEnv<int>(nameof(config.DeadLetterMaxCount), "DEAD_LETTER_MAX_COUNT", v => config.DeadLetterMaxCount = v);
            SetEnv<int>(nameof(config.DeadLetterRetentionHours), "DEAD_LETTER_RETENTION_HOURS", v => config.DeadLetterRetentionHours = v);
            SetEnv<string>(nameof(config.DeadLetterDbPath), "DEAD_LETTER_DB_PATH", v => config.DeadLetterDbPath = v);
        }

        private static void SetEnv<T>(string propName, string envName, Action<T> setter)
        {
            var val = Environment.GetEnvironmentVariable(EnvPrefix + envName);
            if (val != null && TryParse<T>(val, out var parsed))
            {
                setter(parsed);
            }
        }

        private static void ApplyCommandLineArgs(string[] args, GatewayConfiguration config)
        {
            var dict = args.Where(a => a.StartsWith("--"))
                .Select(a => a.Substring(2).Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].ToLower(), p => p[1]);

            if (dict.TryGetValue("queue-capacity", out var v)) Set<int>(v, config.QueueCapacity, x => config.QueueCapacity = x);
            if (dict.TryGetValue("send-timeout-ms", out v)) Set<int>(v, config.SendTimeoutMs, x => config.SendTimeoutMs = x);
            if (dict.TryGetValue("max-retry-attempts", out v)) Set<int>(v, config.MaxRetryAttempts, x => config.MaxRetryAttempts = x);
            if (dict.TryGetValue("retry-base-delay-ms", out v)) Set<int>(v, config.RetryBaseDelayMs, x => config.RetryBaseDelayMs = x);
            if (dict.TryGetValue("cb-failure-threshold", out v)) Set<int>(v, config.CircuitBreakerFailureThreshold, x => config.CircuitBreakerFailureThreshold = x);
            if (dict.TryGetValue("cb-recovery-time-ms", out v)) Set<int>(v, config.CircuitBreakerRecoveryTimeMs, x => config.CircuitBreakerRecoveryTimeMs = x);
            if (dict.TryGetValue("enable-health-check-http", out v)) Set<bool>(v, config.EnableHealthCheckHttp, x => config.EnableHealthCheckHttp = x);
            if (dict.TryGetValue("health-check-http-port", out v)) Set<int>(v, config.HealthCheckHttpPort, x => config.HealthCheckHttpPort = x);
            if (dict.TryGetValue("log-level", out v)) Set<string>(v, config.LogLevel, x => config.LogLevel = x);
            if (dict.TryGetValue("log-dir", out v)) Set<string>(v, config.LogDir, x => config.LogDir = x);
            if (dict.TryGetValue("enable-file-log", out v)) Set<bool>(v, config.EnableFileLog, x => config.EnableFileLog = x);
            if (dict.TryGetValue("rate-limit", out v)) Set<double>(v, config.RateLimitPerSecond, x => config.RateLimitPerSecond = x);
        }

        private static void Set<T>(string val, T current, Action<T> setter)
        {
            if (TryParse<T>(val, out var parsed))
            {
                setter(parsed);
            }
        }

        private static bool TryParse<T>(string value, out T result)
        {
            if (typeof(T) == typeof(int) && int.TryParse(value, out var i))
            {
                result = (T)(object)i;
                return true;
            }
            if (typeof(T) == typeof(double) && double.TryParse(value, out var d))
            {
                result = (T)(object)d;
                return true;
            }
            if (typeof(T) == typeof(bool) && bool.TryParse(value, out var b))
            {
                result = (T)(object)b;
                return true;
            }
            if (typeof(T) == typeof(string))
            {
                result = (T)(object)value;
                return true;
            }
            result = default!;
            return false;
        }
    }
}
