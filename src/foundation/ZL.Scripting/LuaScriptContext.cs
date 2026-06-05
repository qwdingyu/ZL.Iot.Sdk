using System;
using System.Collections.Generic;

namespace ZL.Scripting
{
    /// <summary>
    /// Lua 脚本执行上下文 — 暴露给 Lua 脚本的 API 集合。
    /// 每个脚本执行获得独立上下文，防止状态污染。
    /// </summary>
    public class LuaScriptContext
    {
        private readonly Message _message;
        private readonly Action<string, string> _logInfo;
        private readonly Action<string, string> _logWarn;
        private readonly Action<string, string> _logError;
        private readonly Dictionary<string, object> _state;

        internal LuaScriptContext(
            Message message,
            Action<string, string> logInfo,
            Action<string, string> logWarn,
            Action<string, string> logError,
            Dictionary<string, object>? state = null)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
            _logWarn = logWarn ?? throw new ArgumentNullException(nameof(logWarn));
            _logError = logError ?? throw new ArgumentNullException(nameof(logError));
            _state = state ?? new Dictionary<string, object>();
        }

        // ── 消息访问 ──

        public string Topic => _message.Topic;

        public string Timestamp => _message.Timestamp.ToString("O");

        public string? JsonContent
        {
            get
            {
                if (_message.ContentType != "json") return null;
                try { return _message.GetJsonContent(); }
                catch { return null; }
            }
        }

        public string? TextContent
        {
            get
            {
                try { return _message.GetTextContent(); }
                catch { return null; }
            }
        }

        public string? HexContent
        {
            get
            {
                try { return _message.GetHexContent(); }
                catch { return null; }
            }
        }

        // ── Lua 函数绑定用的 getter 方法 ──

        public string GetTopic() => Topic;
        public string GetTimestamp() => Timestamp;
        public string GetJson() => JsonContent ?? "";
        public string GetText() => TextContent ?? "";
        public string GetHex() => HexContent ?? "";

        public string? GetMetadata(string key)
            => _message.Metadata.TryGetValue(key, out var v) ? v : null;

        public void SetMetadata(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null)
            {
                _message.Metadata.Remove(key);
            }
            else
            {
                _message.Metadata[key] = value;
            }
        }

        // ── 标签写入 ──

        public List<TagWrite> Writes => _message.Writes;

        public void AddWrite(string address, object value, string dataType, string? alias = null)
            => _message.Writes.Add(new TagWrite(address, value, dataType, alias, DateTime.UtcNow));

        // ── 日志 ──

        public void LogInfo(string msg) => _logInfo("Lua", msg);
        public void LogWarn(string msg) => _logWarn("Lua", msg);
        public void LogError(string msg) => _logError("Lua", msg);

        // ── 状态管理 ──

        public object? GetState(string key)
            => _state.TryGetValue(key, out var v) ? v : null;

        public void SetState(string key, object value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null)
            {
                _state.Remove(key);
            }
            else
            {
                _state[key] = value;
            }
        }

        // ── 数学辅助 ──

        public double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        public bool IsInDeadband(double value, double center, double deadband)
            => Math.Abs(value - center) < deadband;
    }
}
