using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ZL.Shared
{
    /// <summary>
    /// 结构化日志入口：支持 Map 分流、可选 Seq、Payload 截断。
    /// </summary>
    public static class StructuredLog
    {
        private const string InstanceProperty = "Instance";
        private const string SettingsFileName = "logging.json";
        private const int DefaultPayloadMaxLength = 4096;
        private static bool _initialized;
        private static int _payloadMaxLength = DefaultPayloadMaxLength;

        public static void Initialize(LogBootstrapOptions? options = null)
        {
            if (_initialized) return;

            options ??= LogBootstrapOptions.Default;

            string settingsPath = Path.Combine(options.BaseDirectory ?? AppContext.BaseDirectory, SettingsFileName);
            var settings = LoadSettings(settingsPath, out bool loadedFromFile);
            _payloadMaxLength = settings.PayloadMaxLength > 0 ? settings.PayloadMaxLength : DefaultPayloadMaxLength;

            string logRoot = ResolveLogRoot(settings.LogRoot, options);
            Directory.CreateDirectory(logRoot);
            Directory.CreateDirectory(Path.Combine(logRoot, "instances"));

            string appName = ResolveAppName(settings.AppName, options);

            LoggerConfiguration config = new LoggerConfiguration()
                .MinimumLevel.Is(ParseLevel(settings.MinimumLevel))
                .Enrich.WithProperty("AppName", appName)
                .Enrich.WithThreadId();

            if (options.EnableConsole)
            {
                config = config.WriteTo.Console();
            }

            config = config.WriteTo.Async(a =>
            {
                a.Map(InstanceProperty, "System", (instance, wt) =>
                {
                    string safe = MakeSafeFileName(instance);
                    string path = Path.Combine(logRoot, "instances", $"sim-{safe}-.log");
                    wt.File(
                        path,
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{Instance}] [{Direction}] [T{ThreadId}] [S:{SessionId}] [L:{PayloadOriginalLength}] {Message:lj}{NewLine}{Exception}");
                }, sinkMapCountLimit: settings.MapCountLimit);

                if (settings.EnableSeq && !string.IsNullOrWhiteSpace(settings.SeqUrl))
                {
                    a.Seq(settings.SeqUrl);
                }
            });

            Log.Logger = config.CreateLogger();
            _initialized = true;

            Log.Information(
                "Logging initialized. SettingsFile={SettingsFile} Loaded={Loaded} SeqEnabled={SeqEnabled} SeqUrl={SeqUrl} PayloadMaxLength={PayloadMaxLength} LogRoot={LogRoot}",
                settingsPath,
                loadedFromFile,
                settings.EnableSeq,
                settings.SeqUrl,
                _payloadMaxLength,
                logRoot);

            if (settings.EnableSeq && string.IsNullOrWhiteSpace(settings.SeqUrl))
            {
                Log.Warning("Seq enabled but SeqUrl is empty; Seq sink disabled.");
            }
        }

        public static void Write(LogEvent payload, LogEventMetadata metadata)
        {
            if (!_initialized || payload == null || metadata == null) return;

            string instance = string.IsNullOrWhiteSpace(metadata.Instance) ? "System" : metadata.Instance;
            var logger = Log.ForContext(InstanceProperty, instance)
                .ForContext("Direction", metadata.Direction);

            if (!string.IsNullOrEmpty(metadata.SessionId))
            {
                logger = logger.ForContext("SessionId", metadata.SessionId);
            }

            if (!string.IsNullOrEmpty(metadata.Payload))
            {
                string body = TruncatePayload(metadata.Payload, out int originalLength, out bool truncated);
                if (originalLength > 0)
                {
                    logger = logger.ForContext("PayloadOriginalLength", originalLength);
                }
                if (truncated)
                {
                    logger = logger.ForContext("PayloadTruncated", true);
                }
                if (!string.IsNullOrEmpty(body))
                {
                    logger = logger.ForContext("PayloadData", body);
                }
            }

            logger.Write(payload);
        }

        public static void Write(LogEventMetadata metadata, LogEventLevel level, string messageTemplate, params object[] args)
        {
            if (!_initialized || metadata == null) return;

            string instance = string.IsNullOrWhiteSpace(metadata.Instance) ? "System" : metadata.Instance;
            var logger = Log.ForContext(InstanceProperty, instance)
                .ForContext("Direction", metadata.Direction);

            if (!string.IsNullOrEmpty(metadata.SessionId))
            {
                logger = logger.ForContext("SessionId", metadata.SessionId);
            }

            if (!string.IsNullOrEmpty(metadata.Payload))
            {
                string body = TruncatePayload(metadata.Payload, out int originalLength, out bool truncated);
                if (originalLength > 0)
                {
                    logger = logger.ForContext("PayloadOriginalLength", originalLength);
                }
                if (truncated)
                {
                    logger = logger.ForContext("PayloadTruncated", true);
                }
                if (!string.IsNullOrEmpty(body))
                {
                    logger = logger.ForContext("PayloadData", body);
                }
            }

            logger.Write(level, messageTemplate, args);
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            Log.CloseAndFlush();
            _initialized = false;
        }

        private static LogSettings LoadSettings(string settingsPath, out bool loadedFromFile)
        {
            loadedFromFile = false;
            if (!File.Exists(settingsPath))
            {
                return new LogSettings();
            }

            try
            {
                string json = File.ReadAllText(settingsPath, Encoding.UTF8);
                var settings = JsonSerializer.Deserialize<LogSettings>(json);
                loadedFromFile = true;
                return settings ?? new LogSettings();
            }
            catch
            {
                return new LogSettings();
            }
        }

        private static string ResolveLogRoot(string logRoot, LogBootstrapOptions options)
        {
            if (!string.IsNullOrWhiteSpace(logRoot))
            {
                return logRoot.Trim();
            }

            string appName = ResolveAppName(string.Empty, options);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName,
                "logs");
        }

        private static string ResolveAppName(string appName, LogBootstrapOptions options)
        {
            if (!string.IsNullOrWhiteSpace(appName))
            {
                return appName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(options.AppNameOverride))
            {
                return options.AppNameOverride.Trim();
            }

            var entry = Assembly.GetEntryAssembly();
            string fallback = entry?.GetName().Name;
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            return "ZL.Shared";
        }

        private static LogEventLevel ParseLevel(string? level)
        {
            if (Enum.TryParse(level, true, out LogEventLevel parsed))
            {
                return parsed;
            }
            return LogEventLevel.Debug;
        }

        private static string TruncatePayload(string payload, out int originalLength, out bool truncated)
        {
            if (string.IsNullOrEmpty(payload))
            {
                originalLength = 0;
                truncated = false;
                return payload;
            }

            originalLength = payload.Length;
            if (_payloadMaxLength <= 0 || payload.Length <= _payloadMaxLength)
            {
                truncated = false;
                return payload;
            }

            const string suffix = "...(truncated)";
            int max = Math.Max(0, _payloadMaxLength - suffix.Length);
            truncated = true;
            return payload.Substring(0, max) + suffix;
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unknown";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            string safe = builder.ToString().Trim();
            return string.IsNullOrEmpty(safe) ? "unknown" : safe;
        }

        private sealed class LogSettings
        {
            public bool EnableSeq { get; set; }
            public string SeqUrl { get; set; } = "http://localhost:5341";
            public int PayloadMaxLength { get; set; } = DefaultPayloadMaxLength;
            public int MapCountLimit { get; set; } = 100;
            public string LogRoot { get; set; } = string.Empty;
            public string AppName { get; set; } = string.Empty;
            public string MinimumLevel { get; set; } = "Debug";
        }
    }
}
