using System;

namespace ZL.Iot.Controls.Common
{
    /// <summary>
    /// 轻量日志辅助类，供控件库内部使用。
    /// 默认输出到 Debug 监听器；UI 层可通过 LogOutput 事件接入 UcConsolePanel。
    /// </summary>
    public static class LogHelper
    {
        public static Action<string, string>? LogOutput { get; set; }

        public static void Debug(string message)
        {
            try { System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}"); } catch { }
            LogOutput?.Invoke("DEBUG", message);
        }

        public static void Info(string message)
        {
            try { System.Diagnostics.Debug.WriteLine($"[INFO] {message}"); } catch { }
            LogOutput?.Invoke("INFO", message);
        }

        public static void Warn(string message)
        {
            try { System.Diagnostics.Debug.WriteLine($"[WARN] {message}"); } catch { }
            LogOutput?.Invoke("WARN", message);
        }

        public static void Error(string message)
        {
            try { System.Diagnostics.Debug.WriteLine($"[ERROR] {message}"); } catch { }
            LogOutput?.Invoke("ERROR", message);
        }
    }
}
