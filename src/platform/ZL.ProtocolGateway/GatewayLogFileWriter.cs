// ============================================================
// 文件：GatewayLogFileWriter.cs
// 描述：GatewayLog 文件日志写入器 — 带自动轮转、按天分割、大小限制
// 功能：零依赖实现文件日志，无需 Serilog/NLog 等第三方库
// 修改日期：2026-06-05
// ============================================================

using System;
using System.IO;
using System.Threading;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 文件日志写入器 — 按天轮转、按大小截断、保留最近 N 个文件。
    /// 线程安全，写入失败不抛出异常。
    /// </summary>
    internal class GatewayLogFileWriter : IDisposable
    {
        private readonly string _logDir;
        private readonly GatewayLog.LogLevel _minLevel;
        private readonly long _maxFileSizeBytes;
        private readonly int _retainedFiles;
        private readonly object _lock = new();
        private StreamWriter? _writer;
        private string? _currentFile;
        private int _disposed;

        public GatewayLogFileWriter(string logDir, GatewayLog.LogLevel minLevel, int maxFileSizeMb, int retainedFiles)
        {
            _logDir = logDir;
            _minLevel = minLevel;
            _maxFileSizeBytes = maxFileSizeMb * 1024L * 1024L;
            _retainedFiles = retainedFiles;

            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
        }

        public void Write(GatewayLog.LogLevel level, string area, string message, Exception? exception)
        {
            if (level < _minLevel) return;
            if (Volatile.Read(ref _disposed) == 1) return;

            lock (_lock)
            {
                try
                {
                    EnsureWriter();

                    var timestamp = DateTimeOffset.Now.ToString("O");
                    var levelStr = level switch
                    {
                        GatewayLog.LogLevel.Debug => "DEBUG",
                        GatewayLog.LogLevel.Info => "INFO",
                        GatewayLog.LogLevel.Warn => "WARN",
                        GatewayLog.LogLevel.Error => "ERROR",
                        _ => "?????"
                    };

                    _writer!.WriteLine($"[{timestamp}] [{levelStr}] [{area}] {message}");

                    if (exception != null)
                    {
                        _writer.WriteLine(exception.ToString());
                    }

                    _writer.Flush();

                    // 检查文件大小，超限则轮转
                    if (_writer.BaseStream.Length >= _maxFileSizeBytes)
                    {
                        Rotate();
                    }
                }
                catch
                {
                    // 文件写入失败不抛出异常，避免影响核心功能
                }
            }
        }

        private void EnsureWriter()
        {
            var todayFile = Path.Combine(_logDir, $"gateway-{DateTime.Now:yyyyMMdd}.log");

            // 按天轮转：如果日期变了，关闭旧文件打开新文件
            if (_currentFile != todayFile)
            {
                _writer?.Dispose();
                _writer = File.AppendText(todayFile);
                _writer.AutoFlush = false; // 手动 Flush，减少 IO
                _currentFile = todayFile;
            }
        }

        private void Rotate()
        {
            _writer?.Dispose();
            _writer = null;
            _currentFile = null;

            // 清理过期文件：保留最近 _retainedFiles 个
            try
            {
                var dir = new DirectoryInfo(_logDir);
                var files = dir.GetFiles("gateway-*.log")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(_retainedFiles)
                    .ToList();

                foreach (var file in files)
                {
                    try { file.Delete(); } catch { }
                }
            }
            catch
            {
                // 清理失败不影响核心功能
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

            lock (_lock)
            {
                try { _writer?.Flush(); } catch { }
                try { _writer?.Dispose(); } catch { }
                _writer = null;
            }
        }
    }
}
