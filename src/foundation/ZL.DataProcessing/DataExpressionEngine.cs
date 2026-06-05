using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using ZL.Script;
using ZL.Script.Industrial;

namespace ZL.DataProcessing
{
    public sealed class DataExpressionEngine
    {
        private readonly IScriptEngine _scriptEngine;
        private readonly ScriptContext _contextModel;
        private readonly Random _random;
        private readonly ConcurrentDictionary<string, Dictionary<string, object>> _sessions;
        private Dictionary<string, object> _session;
        private string _currentSessionId;
        private readonly object _sync = new object();
        private static readonly object LogLock = new object();

        public DataExpressionEngine()
        {
            _scriptEngine = new ScriptEngine();
            _contextModel = new ScriptContext();
            _random = new Random();
            _sessions = new ConcurrentDictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            
            // 初始化默认会话
            var defaultSession = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _sessions["default"] = defaultSession;
            _session = defaultSession;
            _currentSessionId = "default";

            // 注册必要变量到引擎
            _scriptEngine.SetVariable("Random", _random);
            _scriptEngine.SetVariable("State", _contextModel.Global);
            _scriptEngine.SetVariable("Global", _contextModel.Global);
            _scriptEngine.SetVariable("Session", _session);
        }

        public string ErrorResponse { get; set; } = string.Empty;
        public Action<string>? Trace { get; set; }

        public void SetState(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_sync)
            {
                _contextModel.Global[key] = value ?? string.Empty;
            }
        }

        public object? GetState(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            lock (_sync)
            {
                return _contextModel.Global.TryGetValue(key, out var val) ? val : null;
            }
        }

        public void SetSession(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_sync)
            {
                _session[key] = value ?? string.Empty;
            }
        }

        public object? GetSession(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            lock (_sync)
            {
                return _session.TryGetValue(key, out var val) ? val : null;
            }
        }

        public void ResetSession()
        {
            lock (_sync)
            {
                _session.Clear();
            }
        }

        public void UseSession(string sessionId)
        {
            lock (_sync)
            {
                string id = string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId;
                if (!_sessions.TryGetValue(id, out var session))
                {
                    session = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    _sessions[id] = session;
                }
                _session = session;
                _currentSessionId = id;
                _scriptEngine.SetVariable("Session", _session);
            }
        }

        public void RemoveSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            lock (_sync)
            {
                _sessions.TryRemove(sessionId, out _);
                if (string.Equals(_currentSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    _currentSessionId = "default";
                    _session = _sessions.GetOrAdd("default", _ => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));
                    _scriptEngine.SetVariable("Session", _session);
                }
            }
        }

        public string Process(string template, string argsText, string[] groups)
        {
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(template)) return string.Empty;
                groups ??= Array.Empty<string>();

                if (template.StartsWith("=") || template.StartsWith("@"))
                {
                    return ExecuteScript(template, argsText, groups);
                }

                if (TemplateParser.HasInlineScript(template))
                {
                    return ProcessInlineTemplate(template, argsText, groups);
                }

                return ReplaceTokens(template, argsText, groups);
            }
        }

        public bool TryEvaluateBool(string expression, string input, string argsText, string[] groups, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(expression)) return false;

            lock (_sync)
            {
                try
                {
                    var parameters = _contextModel.ToParameterMap();
                    parameters["Args"] = groups ?? Array.Empty<string>();
                    parameters["ArgsText"] = argsText ?? string.Empty;
                    parameters["Input"] = input ?? string.Empty;

                    var val = _scriptEngine.Evaluate(expression, parameters);
                    if (val is bool b)
                    {
                        result = b;
                        Trace?.Invoke($"EvalBool: {expression} -> {result}");
                        return true;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    HandleScriptError(expression, ex);
                    return false;
                }
            }
        }

        private string ExecuteScript(string script, string argsText, string[] groups)
        {
            try
            {
                var parameters = _contextModel.ToParameterMap();
                parameters["Args"] = groups ?? Array.Empty<string>();
                parameters["ArgsText"] = argsText ?? string.Empty;
                parameters["Input"] = argsText;

                var res = _scriptEngine.Evaluate(script, parameters);
                Trace?.Invoke($"Eval: {script} -> {res}");
                return res?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                return HandleScriptError(script, ex);
            }
        }

        private string ProcessInlineTemplate(string template, string argsText, string[] groups)
        {
            var segments = TemplateParser.Parse(template);
            var sb = new StringBuilder();

            foreach (var segment in segments)
            {
                if (segment.Type == TemplateSegmentType.Text)
                {
                    sb.Append(ReplaceTokens(segment.Content, argsText, groups));
                    continue;
                }

                try
                {
                    var parameters = _contextModel.ToParameterMap();
                    parameters["Args"] = groups ?? Array.Empty<string>();
                    parameters["ArgsText"] = argsText ?? string.Empty;
                    parameters["Input"] = argsText;

                    var res = _scriptEngine.Evaluate(segment.Content, parameters);
                    Trace?.Invoke($"Inline: {segment.Content} -> {res}");
                    sb.Append(res?.ToString() ?? string.Empty);
                }
                catch (Exception ex)
                {
                    string fallback = HandleScriptError(segment.Content, ex);
                    if (!string.IsNullOrEmpty(ErrorResponse))
                    {
                        return fallback;
                    }
                    sb.Append(fallback);
                }
            }

            return sb.ToString();
        }

        private string ReplaceTokens(string template, string argsText, string[] groups)
        {
            string result = template ?? string.Empty;
            result = result.Replace("{ARG}", argsText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < (groups?.Length ?? 0); i++)
            {
                result = result.Replace($"{{ARG{i}}}", groups[i], StringComparison.OrdinalIgnoreCase);
            }

            result = ReplaceStateTokens(result);
            result = ReplaceSessionTokens(result);
            result = ReplaceRandomTokens(result);
            return result;
        }

        private string ReplaceStateTokens(string input)
        {
            return Regex.Replace(input, @"\{STATE:([a-zA-Z0-9_]+)\}", m =>
            {
                string key = m.Groups[1].Value;
                if (_contextModel.Global.TryGetValue(key, out var val))
                {
                    return val?.ToString() ?? string.Empty;
                }
                return "0";
            }, RegexOptions.IgnoreCase);
        }

        private string ReplaceSessionTokens(string input)
        {
            return Regex.Replace(input, @"\{SESSION:([a-zA-Z0-9_]+)\}", m =>
            {
                string key = m.Groups[1].Value;
                if (_session.TryGetValue(key, out var val))
                {
                    return val?.ToString() ?? string.Empty;
                }
                return "0";
            }, RegexOptions.IgnoreCase);
        }

        private string ReplaceRandomTokens(string input)
        {
            return Regex.Replace(input, @"\{RAND:([^}]+)\}", m =>
            {
                try
                {
                    var parts = m.Groups[1].Value.Split(',');
                    double min = 0;
                    double max = 1;
                    string fmt = "0.##";

                    if (parts.Length > 0) double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out min);
                    if (parts.Length > 1) double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out max);
                    if (parts.Length > 2) fmt = parts[2];

                    double val = min + _random.NextDouble() * (max - min);
                    return val.ToString(fmt, CultureInfo.InvariantCulture);
                }
                catch { return m.Value; }
            });
        }

        private string HandleScriptError(string script, Exception ex)
        {
            string message = ex?.Message ?? "Unknown error";
            _contextModel.Global["_LAST_ERROR"] = message;
            string payload = string.IsNullOrEmpty(ErrorResponse) ? $"<SCRIPT_ERROR: {message}>" : ErrorResponse;
            Trace?.Invoke($"ScriptError: {message}");
            return payload;
        }
    }
}
