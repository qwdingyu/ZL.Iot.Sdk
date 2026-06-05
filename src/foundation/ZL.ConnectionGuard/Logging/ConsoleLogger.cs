using System;

namespace ZL.ConnectionGuard.Logging
{
    public sealed class ConsoleLogger : IGuardLogger
    {
        private readonly string _name;

        public ConsoleLogger(string name)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "ConnectionGuard" : name;
        }

        public void Debug(string message) => Write("DEBUG", message);
        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message, Exception? ex = null)
        {
            string detail = ex == null ? message : $"{message} | {ex.Message}";
            Write("ERROR", detail);
        }

        private void Write(string level, string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{_name}] [{level}] {message}");
        }
    }
}
