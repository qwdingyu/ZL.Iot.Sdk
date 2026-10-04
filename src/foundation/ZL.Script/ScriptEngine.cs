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
                return lambda.Invoke(BuildArgs(lambda, parameters));
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

        /// <summary>
        /// 注册工业助手库：静态类以**别名**注册，实例以变量注册。
        ///
        /// <para>
        /// ⚠️ 静态类必须用 <c>Reference(type, alias)</c>，**不能** <c>SetVariable(name, type)</c>——
        /// 后者把标识符绑成一个 <c>System.Type</c> 对象，于是 <c>Format.Hex(255)</c> 被解析成
        /// 「对 Type 的实例调用 Hex」并抛 <c>No applicable method 'Hex' exists in type 'Type'</c>：
        /// Hex / Binary / Bit / BinaryLogic / Checksum / Crc / Format / Sim 全部工业助手
        /// **在表达式里都不可用**（2026-10-03 定位）。别名注册后
        /// <c>=Format.Hex(255)</c>、<c>=Crc.Modbus(...)</c>、<c>=Sim.RandomInt(1,10)</c> 均可求值。
        /// </para>
        /// </summary>
        public void RegisterLibrary(string name, object instanceOrType)
        {
            if (instanceOrType is Type type)
            {
                _interpreter.Reference(type, name);
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
            // ⚠️ 缓存键必须包含**参数签名**（名字 + 值类型）：Parse 时声明的形参集合决定了
            // 这个 lambda 能引用哪些标识符、以及形参的类型。只按表达式做键，会让
            // 「同一表达式、不同上下文」互相污染（例如先以 4 个形参解析、再用 0 个形参调用）。
            string signature = parameters == null || parameters.Count == 0
                ? string.Empty
                : string.Join("\u0001", parameters.Select(kv => $"{kv.Key}:{kv.Value?.GetType().Name ?? "null"}"));
            string key = expression + "\u0000" + signature;

            return _cache.GetOrAdd(key, _ =>
            {
                var paramList = parameters == null
                    ? Array.Empty<Parameter>()
                    : parameters.Select(kv => new Parameter(kv.Key, kv.Value?.GetType() ?? typeof(object))).ToArray();
                return _interpreter.Parse(expression, paramList);
            });
        }

        /// <summary>
        /// 构造实参：按 lambda **声明的形参顺序**逐个从上下文中取值。
        ///
        /// <para>
        /// ⚠️ 这里原先是「只按 <c>UsedParameters</c> 传」，会抛
        /// <c>InvalidOperationException: Arguments count mismatch</c>——DynamicExpresso 的
        /// <c>Lambda.Invoke(object[])</c> 要求实参个数与**声明的形参**一致，而表达式通常只用到其中几个。
        /// 后果（2026-10-03 定位）：**任何未用满上下文的表达式都失败**，例如
        /// <c>DataExpressionEngine.Process("= 1 + 2")</c>（它恒传完整上下文）长期返回
        /// <c>&lt;SCRIPT_ERROR: Failed to evaluate: = 1 + 2&gt;</c>——即模板的
        /// <c>=表达式</c>（ResponseExpression / 条件响应等）实际不可用。
        /// 按声明顺序全量传参即可；缺值传 null（DynamicExpresso 允许）。
        /// </para>
        /// </summary>
        private static object[] BuildArgs(Lambda lambda, IDictionary<string, object>? parameters)
        {
            var declared = lambda.DeclaredParameters.ToArray();
            var args = new object[declared.Length];
            for (int i = 0; i < declared.Length; i++)
            {
                args[i] = parameters != null && parameters.TryGetValue(declared[i].Name, out var val) ? val! : null!;
            }
            return args;
        }
    }

}
