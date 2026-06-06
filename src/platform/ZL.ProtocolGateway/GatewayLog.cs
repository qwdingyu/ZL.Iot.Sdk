using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// Gateway 统一日志组件
    /// 支持日志级别过滤（DEBUG/INFO/WARN/ERROR），默认级别为 INFO
    /// 支持自定义输出目标（测试时可注入 StringWriter，避免 Console.SetOut 并行冲突）
    /// 支持外部日志桥接（SetExternalSink），可将日志转发到 Serilog/NLog/ILogger 等
    /// 支持 Microsoft.Extensions.Logging 结构化日志（通过 Initialize(ILoggerFactory) 启用）
    /// </summary>
    public static class GatewayLog
    {
        /// <summary>
        /// 日志级别枚举
        /// </summary>
        public enum LogLevel
        {
            Debug = 0,
            Info = 1,
            Warn = 2,
            Error = 3
        }

        /// <summary>
        /// 当前最低日志级别。低于此级别的日志将被丢弃。
        /// 默认 INFO，可通过 SetMinLevel 调整。
        /// </summary>
        private static volatile LogLevel _minLevel = LogLevel.Info;

        // 自定义输出目标（测试用），线程安全
        private static readonly object _outputLock = new();
        private static TextWriter? _customWriter;

        /// <summary>
        /// 外部日志接收器 — 可桥接到 Serilog/NLog/ILogger 等
        /// 签名: (LogLevel level, string area, string message, Exception? exception)
        /// </summary>
        private static Action<LogLevel, string, string, Exception?>? _externalSink;

        // 文件日志输出（生产环境用）
        private static GatewayLogFileWriter? _fileWriter;
        private static readonly object _fileLock = new();

        // Microsoft.Extensions.Logging 适配器
        private static ILogger? _ilogger;

        /// <summary>
        /// 启用 Microsoft.Extensions.Logging 结构化日志。
        /// <para>调用此方法后，GatewayLog 的日志将同时输出到 ILogger（结构化、可对接 Serilog/ELK/Loki）
        /// 和原有输出目标（Console/文件/ExternalSink）。</para>
        /// <para>传入 null 可禁用 ILogger 输出。</para>
        /// </summary>
        public static void Initialize(ILoggerFactory? factory)
        {
            if (factory != null)
            {
                _ilogger = factory.CreateLogger("ProtocolGateway");
            }
            else
            {
                _ilogger = null;
            }
        }

        /// <summary>
        /// 启用文件日志输出（带自动轮转）。
        /// <para>日志文件命名: {logDir}/gateway-{yyyyMMdd}.log，按天轮转，单文件最大 maxFileSizeMb MB。</para>
        /// <para>调用此方法后，日志同时输出到 Console 和文件（除非设置了 ExternalSink）。</para>
        /// </summary>
        /// <param name="logDir">日志目录，null 表示使用 %TEMP%\ProtocolGateway</param>
        /// <param name="minLevel">文件日志最低级别（默认 Debug）</param>
        /// <param name="maxFileSizeMb">单文件最大 MB（默认 50）</param>
        /// <param name="retainedFiles">保留最近多少个日志文件（默认 7）</param>
        public static void EnableFileOutput(string? logDir = null, LogLevel minLevel = LogLevel.Debug, int maxFileSizeMb = 50, int retainedFiles = 7)
        {
            lock (_fileLock)
            {
                _fileWriter?.Dispose();
                _fileWriter = new GatewayLogFileWriter(
                    logDir ?? Path.Combine(Path.GetTempPath(), "ProtocolGateway"),
                    minLevel, maxFileSizeMb, retainedFiles);
            }
        }

        /// <summary>
        /// 禁用文件日志输出。
        /// </summary>
        public static void DisableFileOutput()
        {
            lock (_fileLock)
            {
                _fileWriter?.Dispose();
                _fileWriter = null;
            }
        }

        /// <summary>
        /// 设置最低日志级别
        /// </summary>
        public static void SetMinLevel(LogLevel level)
        {
            _minLevel = level;
        }

        /// <summary>
        /// 设置自定义输出目标。设为 null 恢复默认 Console 输出。
        /// 线程安全。
        /// </summary>
        public static void SetOutput(TextWriter? writer)
        {
            lock (_outputLock)
            {
                _customWriter = writer;
            }
        }

        /// <summary>
        /// 设置外部日志接收器 — 将 GatewayLog 的日志转发到外部日志系统（如 Serilog/NLog/ILogger）
        /// 设为 null 取消桥接。线程安全。
        /// <para>示例：GatewayLog.SetExternalSink((level, area, msg, ex) => Serilog.Log.Write(...));</para>
        /// </summary>
        public static void SetExternalSink(Action<LogLevel, string, string, Exception?>? sink)
        {
            _externalSink = sink;
        }

        /// <summary>
        /// 恢复 Console 默认输出。
        /// </summary>
        public static void ResetOutput()
        {
            SetOutput(null);
        }

        public static void Debug(string area, string message)
        {
            if (_minLevel > LogLevel.Debug) return;
            Write(LogLevel.Debug, "DEBUG", area, message, null);
        }

        public static void Info(string area, string message)
        {
            if (_minLevel > LogLevel.Info) return;
            Write(LogLevel.Info, "INFO", area, message, null);
        }

        public static void Warn(string area, string message, Exception? exception = null)
        {
            if (_minLevel > LogLevel.Warn) return;
            Write(LogLevel.Warn, "WARN", area, message, exception);
        }

        public static void Error(string area, string message, Exception? exception = null)
        {
            if (_minLevel > LogLevel.Error) return;
            Write(LogLevel.Error, "ERROR", area, message, exception);
        }

        private static void Write(LogLevel logLevel, string level, string area, string message, Exception? exception)
        {
            var normalizedArea = string.IsNullOrWhiteSpace(area) ? "Gateway" : area.Trim();
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();

            // 1. Microsoft.Extensions.Logging 结构化日志（并行输出，不影响原有路径）
            var ilogger = _ilogger;
            if (ilogger != null)
            {
                try
                {
                    var log = (Microsoft.Extensions.Logging.ILogger)ilogger;
                    switch (logLevel)
                    {
                        case LogLevel.Debug:
                            log.LogDebug("{Area} {Message}", normalizedArea, normalizedMessage);
                            break;
                        case LogLevel.Info:
                            log.LogInformation("{Area} {Message}", normalizedArea, normalizedMessage);
                            break;
                        case LogLevel.Warn:
                            if (exception != null)
                                log.LogWarning(exception, "{Area} {Message}", normalizedArea, normalizedMessage);
                            else
                                log.LogWarning("{Area} {Message}", normalizedArea, normalizedMessage);
                            break;
                        case LogLevel.Error:
                            if (exception != null)
                                log.LogError(exception, "{Area} {Message}", normalizedArea, normalizedMessage);
                            else
                                log.LogError("{Area} {Message}", normalizedArea, normalizedMessage);
                            break;
                    }
                }
                catch
                {
                    // ILogger 异常不应影响核心功能
                }
            }

            // 2. 优先转发到外部接收器（如 Serilog），如果已设置则不再写 Console
            var sink = _externalSink;
            if (sink != null)
            {
                try
                {
                    sink(logLevel, normalizedArea, normalizedMessage, exception);
                }
                catch
                {
                    // 外部接收器异常不应影响核心功能，降级到 Console
                }
                return;
            }

            // 3. 文件日志输出
            var fileWriter = _fileWriter;
            if (fileWriter != null)
            {
                try { fileWriter.Write(logLevel, normalizedArea, normalizedMessage, exception); }
                catch { /* 文件写入失败不影响核心功能 */ }
            }

            // 4. Console / 自定义 TextWriter 输出
            var timestamp = DateTimeOffset.Now.ToString("O");
            var outputText = $"[{timestamp}] [{level}] [{normalizedArea}] {normalizedMessage}";

            lock (_outputLock)
            {
                var writer = _customWriter ?? Console.Out;
                try
                {
                    writer.WriteLine(outputText);
                }
                catch (ObjectDisposedException)
                {
                    // Console capture may have been closed by the test runner.
                }

                if (exception != null)
                {
                    try
                    {
                        writer.WriteLine(exception);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Console capture may have been closed by the test runner.
                    }
                }
            }
        }
    }
}
