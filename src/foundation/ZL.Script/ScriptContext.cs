using System;
using System.Collections.Generic;

namespace ZL.Script
{
    /// <summary>
    /// 脚本上下文，承载变量桶（Buckets）和作用域逻辑。
    /// 这里的命名兼容 ZL.Gear (Vars) 和 ZL.Simulator (Session/State)。
    /// </summary>
    public class ScriptContext
    {
        /// <summary>
        /// 全局状态 / 变量池 (等同于 ZL.Gear 的 Vars 或 Simulator 的 Global/State)
        /// </summary>
        public IDictionary<string, object> Global { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// 会话状态 / 局部变量池 (等同于 Simulator 的 Session)
        /// </summary>
        public IDictionary<string, object> Local { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        
        public IDictionary<string, object> Vars => Global;
        
        public IDictionary<string, object> Session => Local;

        /// <summary>
        /// 构建用于传递给 DynamicExpresso 的平铺参数字典。
        /// </summary>
        public IDictionary<string, object> ToParameterMap()
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "Global", Global },
                { "Local", Local },
                { "Vars", Global },
                { "Session", Local },
                { "State", Global }
            };
        }
    }
}
