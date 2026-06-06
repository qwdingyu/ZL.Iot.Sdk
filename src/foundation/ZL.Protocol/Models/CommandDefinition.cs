namespace ZL.Protocol.Models
{
    /// <summary>
    /// 命令定义
    /// </summary>
    public sealed class CommandDefinition
    {
        /// <summary>命令模板（如 "VOLT {v}"）</summary>
        public string CommandTemplate { get; set; } = string.Empty;

        /// <summary>响应等待时间（毫秒）</summary>
        public int WaitAfterMs { get; set; }

        /// <summary>响应解析器</summary>
        public ResponseParserDefinition? Parser { get; set; }

        /// <summary>读取策略</summary>
        public ReadStrategyDefinition? ReadStrategy { get; set; }

        /// <summary>匹配模式（正则表达式）</summary>
        public string? MatchPattern { get; set; }

        /// <summary>响应模板（如 "{STATE:voltage}"）</summary>
        public string? ResponseTemplate { get; set; }

        /// <summary>是否自动追加校验和（覆盖全局）</summary>
        public bool? AutoAppendCheckSum { get; set; }

        /// <summary>校验和算法（覆盖全局）</summary>
        public string? CheckSum { get; set; }

        /// <summary>状态键名（设置后缓存响应）</summary>
        public string? SetStateKey { get; set; }

        /// <summary>状态组索引（用于多组状态管理）</summary>
        public int? SetStateGroupIndex { get; set; }

        /// <summary>状态重置列表（重置哪些状态键）</summary>
        public List<string>? StateReset { get; set; }

        /// <summary>是否为收藏指令</summary>
        public bool IsFavorite { get; set; }

        /// <summary>前置依赖命令列表</summary>
        public List<string>? DependsOn { get; set; }

        /// <summary>参数验证规则</summary>
        public ValidationRule? Validation { get; set; }

        /// <summary>验证失败时的响应</summary>
        public string? ValidationErrorResponse { get; set; }

        /// <summary>条件响应</summary>
        public ConditionalResponse? ConditionalResponse { get; set; }

        /// <summary>触发的事件列表</summary>
        public List<string>? Triggers { get; set; }

        /// <summary>响应计算表达式</summary>
        public string? ResponseExpression { get; set; }

        /// <summary>工作台命令区是否允许读取</summary>
        public bool? UiCanRead { get; set; }

        /// <summary>工作台命令区是否允许写入</summary>
        public bool? UiCanWrite { get; set; }
    }

    /// <summary>
    /// 读取策略定义
    /// </summary>
    public sealed class ReadStrategyDefinition
    {
        /// <summary>类型（Terminator / Length）</summary>
        public string Type { get; set; } = "Terminator";

        /// <summary>终止符（Type=Terminator 时使用）</summary>
        public string? Terminator { get; set; }

        /// <summary>固定长度（Type=Length 时使用）</summary>
        public int Length { get; set; }
    }

    /// <summary>
    /// 响应解析器定义
    /// </summary>
    public sealed class ResponseParserDefinition
    {
        /// <summary>类型（None / Regex / Index）</summary>
        public string Type { get; set; } = "None";

        /// <summary>正则表达式模式</summary>
        public string? Pattern { get; set; }

        /// <summary>从字符串开始位置提取的索引</summary>
        public int Index { get; set; }

        /// <summary>目标类型（String / Int / Double / Boolean）</summary>
        public string TargetType { get; set; } = "String";
    }

    /// <summary>
    /// 参数验证规则
    /// </summary>
    public sealed class ValidationRule
    {
        /// <summary>最小值</summary>
        public double? Min { get; set; }

        /// <summary>最大值</summary>
        public double? Max { get; set; }

        /// <summary>枚举值列表</summary>
        public List<string>? EnumValues { get; set; }

        /// <summary>正则表达式验证</summary>
        public string? Pattern { get; set; }

        /// <summary>数据类型（int / float / string / enum）</summary>
        public string? Type { get; set; }
    }

    /// <summary>
    /// 条件响应
    /// </summary>
    public sealed class ConditionalResponse
    {
        /// <summary>条件表达式</summary>
        public string? Condition { get; set; }

        /// <summary>条件为真时的响应</summary>
        public string? IfTrue { get; set; }

        /// <summary>条件为假时的响应</summary>
        public string? IfFalse { get; set; }

        /// <summary>默认值（无匹配时）</summary>
        public string? Default { get; set; }
    }
}
