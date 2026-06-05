using System;
using System.Reflection;

namespace ZL.ConnectionGuard.Logging
{
    /// <summary>
    /// NLog 适配器（无需强依赖 NLog，运行时通过反射绑定）。
    /// 如果 NLog 不存在，将自动退化为 ConsoleLogger。
    /// </summary>
    public sealed class NLogLogger : IGuardLogger
    {
        private readonly IGuardLogger _fallback;
        private readonly object? _logger;
        private readonly MethodInfo? _debug;
        private readonly MethodInfo? _info;
        private readonly MethodInfo? _warn;
        private readonly MethodInfo? _error;

        public NLogLogger(string loggerName)
        {
            _fallback = new ConsoleLogger(loggerName);
            try
            {
                var logManagerType = Type.GetType("NLog.LogManager, NLog");
                if (logManagerType == null) return;
                var getLogger = logManagerType.GetMethod("GetLogger", new[] { typeof(string) });
                if (getLogger == null) return;
                _logger = getLogger.Invoke(null, new object?[] { loggerName });
                if (_logger == null) return;

                var loggerType = _logger.GetType();
                _debug = loggerType.GetMethod("Debug", new[] { typeof(string) });
                _info = loggerType.GetMethod("Info", new[] { typeof(string) });
                _warn = loggerType.GetMethod("Warn", new[] { typeof(string) });
                _error = loggerType.GetMethod("Error", new[] { typeof(Exception), typeof(string) });
            }
            catch
            {
                // Ignore NLog binding failures and fallback to console.
            }
        }

        public void Debug(string message)
        {
            if (_logger == null || _debug == null) { _fallback.Debug(message); return; }
            _debug.Invoke(_logger, new object?[] { message });
        }

        public void Info(string message)
        {
            if (_logger == null || _info == null) { _fallback.Info(message); return; }
            _info.Invoke(_logger, new object?[] { message });
        }

        public void Warn(string message)
        {
            if (_logger == null || _warn == null) { _fallback.Warn(message); return; }
            _warn.Invoke(_logger, new object?[] { message });
        }

        public void Error(string message, Exception? ex = null)
        {
            if (_logger == null || _error == null)
            {
                _fallback.Error(message, ex);
                return;
            }
            _error.Invoke(_logger, new object?[] { ex ?? new Exception(message), message });
        }
    }
}
