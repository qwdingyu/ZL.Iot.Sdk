using System;
using System.Collections.Generic;

namespace ZL.Script
{
    /// <summary>
    /// 脚本引擎核心接口，定义跨项目的统一执行规范。
    /// </summary>
    public interface IScriptEngine
    {
        /// <summary>
        /// 执行表达式并返回结果。
        /// </summary>
        /// <param name="expression">脚本内容（支持 @ 或 = 前缀）</param>
        /// <param name="parameters">执行时的局部变量映射</param>
        object? Evaluate(string expression, IDictionary<string, object>? parameters = null);

        /// <summary>
        /// 执行表达式并返回强类型结果。
        /// </summary>
        T? Evaluate<T>(string expression, IDictionary<string, object>? parameters = null);

        /// <summary>
        /// 执行插值字符串（如 "Volt: ${Vars.Volt}"）。
        /// </summary>
        string Interpolate(string template, IDictionary<string, object>? parameters = null);
        
        /// <summary>
        /// 注册全局可见的静态类型或对象（如 Math, Crc）。
        /// </summary>
        void RegisterLibrary(string name, object instanceOrType);

        /// <summary>
        /// 注入全局变量实例。
        /// </summary>
        void SetVariable(string name, object value);
    }

    public class ScriptEvaluationException : Exception
    {
        public ScriptEvaluationException(string message, Exception inner) : base(message, inner) { }
    }
}
