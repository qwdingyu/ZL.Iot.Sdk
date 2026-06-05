using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DynamicExpresso;

namespace ZL.Script
{
    public class ScriptEngine : IScriptEngine
    {
        private readonly Interpreter _interpreter;
        private readonly ConcurrentDictionary<string, Lambda> _cache = new ConcurrentDictionary<string, Lambda>();
        private static readonly Regex _interpolationRegex = new Regex(@"\$\{(.+?)\}", RegexOptions.Compiled);

        public ScriptEngine()
        {
            _interpreter = new Interpreter();
            
            // 默认导入基础库
            _interpreter.Reference(typeof(Math));
            _interpreter.Reference(typeof(Convert));
            _interpreter.Reference(typeof(TimeSpan));
            _interpreter.Reference(typeof(DateTime));

            // 注册工业级助手 (Industrial Extensions)
            RegisterLibrary("Hex", typeof(Industrial.HexHelper));
            RegisterLibrary("Binary", typeof(Industrial.BinaryHelper));
            RegisterLibrary("Bit", typeof(Industrial.BinaryHelper));
            RegisterLibrary("BinaryLogic", typeof(Industrial.BinaryHelper));
            RegisterLibrary("Checksum", typeof(Industrial.ChecksumHelper));
            RegisterLibrary("Crc", typeof(Industrial.Crc));
            RegisterLibrary("Format", typeof(Industrial.FormatHelper));
            RegisterLibrary("Sim", typeof(Industrial.SimHelper));
        }

        public object? Evaluate(string expression, IDictionary<string, object>? parameters = null)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;

            // 处理前缀
            string cleanExpr = expression;
            if (cleanExpr.StartsWith("@") || cleanExpr.StartsWith("="))
            {
                cleanExpr = cleanExpr.Substring(1);
            }

            try
            {
                var lambda = GetOrParse(cleanExpr, parameters);
                var args = BuildArgs(lambda, parameters);
                return lambda.Invoke(args);
            }
            catch (Exception ex)
            {
                // 可以考虑自定义异常包装
                throw new ScriptEvaluationException($"Failed to evaluate: {expression}", ex);
            }
        }

        public T? Evaluate<T>(string expression, IDictionary<string, object>? parameters = null)
        {
            var result = Evaluate(expression, parameters);
            if (result == null) return default;
            return (T)Convert.ChangeType(result, typeof(T));
        }

        public string Interpolate(string template, IDictionary<string, object>? parameters = null)
        {
            if (string.IsNullOrEmpty(template)) return template;

            return _interpolationRegex.Replace(template, match =>
            {
                var expr = match.Groups[1].Value;
                try
                {
                    var val = Evaluate(expr, parameters);
                    return val?.ToString() ?? "null";
                }
                catch
                {
                    return match.Value; // 保持原样以支持调试
                }
            });
        }

        public void RegisterLibrary(string name, object instanceOrType)
        {
            if (instanceOrType is Type type)
            {
                _interpreter.Reference(type);
                _interpreter.SetVariable(name, type);
            }
            else
            {
                _interpreter.SetVariable(name, instanceOrType);
            }
        }
        public void SetVariable(string name, object value)
        {
            _interpreter.SetVariable(name, value);
        }

        private Lambda GetOrParse(string expression, IDictionary<string, object>? parameters)
        {
            // 缓存 Key 必须包含参数定义的指纹，防止参数类型/数量变化导致的执行错误
            // 在通用库中，我们倾向于保持固定的上下文结构以提高缓存命中率
            return _cache.GetOrAdd(expression, expr => {
                var paramList = new List<Parameter>();
                if (parameters != null)
                {
                    foreach (var kv in parameters)
                    {
                        paramList.Add(new Parameter(kv.Key, kv.Value?.GetType() ?? typeof(object)));
                    }
                }
                return _interpreter.Parse(expr, paramList.ToArray());
            });
        }

        private object[] BuildArgs(Lambda lambda, IDictionary<string, object>? parameters)
        {
            var args = new List<object>();
            foreach (var p in lambda.UsedParameters)
            {
                if (parameters != null && parameters.TryGetValue(p.Name, out var val))
                {
                    args.Add(val);
                }
                else
                {
                    args.Add(null!); // 或者抛出异常
                }
            }
            return args.ToArray();
        }
    }

}
