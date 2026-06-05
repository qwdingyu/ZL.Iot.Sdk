using System;
using System.Collections.Generic;

namespace ZL.Biz.Execute.Conditions
{
    /// <summary>
    /// 条件树模型（受限 DSL）
    /// <para>替代自由表达式，提供安全、可验证的条件判断能力</para>
    /// </summary>
    /// <remarks>
    /// P2 阶段实现：
    /// - 规则表达式从自由输入改成受限 DSL 或标准 JSON 条件树
    /// - 支持安全的条件判断，防止代码注入风险
    /// - 可序列化为 JSON 存储
    ///
    /// 设计原则：
    /// - 只支持预定义的操作符
    /// - 只支持简单的比较和逻辑运算
    /// - 不支持任意代码执行
    /// </remarks>
    public class ConditionTree
    {
        /// <summary>
        /// 条件类型
        /// </summary>
        public ConditionType Type { get; set; }

        /// <summary>
        /// 左操作数（字段名或值）
        /// </summary>
        public string Left { get; set; }

        /// <summary>
        /// 操作符
        /// </summary>
        public ConditionOperator Operator { get; set; }

        /// <summary>
        /// 右操作数（值或字段名）
        /// </summary>
        public object Right { get; set; }

        /// <summary>
        /// 子条件列表（用于 And/Or 逻辑组合）
        /// </summary>
        public List<ConditionTree> Children { get; set; } = new();

        /// <summary>
        /// 创建比较条件
        /// </summary>
        public static ConditionTree Compare(string left, ConditionOperator op, object right)
        {
            return new ConditionTree
            {
                Type = ConditionType.Comparison,
                Left = left,
                Operator = op,
                Right = right
            };
        }

        /// <summary>
        /// 创建 AND 组合条件
        /// </summary>
        public static ConditionTree And(params ConditionTree[] children)
        {
            return new ConditionTree
            {
                Type = ConditionType.And,
                Children = new List<ConditionTree>(children)
            };
        }

        /// <summary>
        /// 创建 OR 组合条件
        /// </summary>
        public static ConditionTree Or(params ConditionTree[] children)
        {
            return new ConditionTree
            {
                Type = ConditionType.Or,
                Children = new List<ConditionTree>(children)
            };
        }

        /// <summary>
        /// 创建 NOT 条件
        /// </summary>
        public static ConditionTree Not(ConditionTree child)
        {
            return new ConditionTree
            {
                Type = ConditionType.Not,
                Children = new List<ConditionTree> { child }
            };
        }

        // 便捷工厂方法
        public static ConditionTree Equals(string field, object value) => Compare(field, ConditionOperator.Equals, value);
        public static ConditionTree NotEquals(string field, object value) => Compare(field, ConditionOperator.NotEquals, value);
        public static ConditionTree GreaterThan(string field, object value) => Compare(field, ConditionOperator.GreaterThan, value);
        public static ConditionTree GreaterThanOrEqual(string field, object value) => Compare(field, ConditionOperator.GreaterThanOrEqual, value);
        public static ConditionTree LessThan(string field, object value) => Compare(field, ConditionOperator.LessThan, value);
        public static ConditionTree LessThanOrEqual(string field, object value) => Compare(field, ConditionOperator.LessThanOrEqual, value);
        public static ConditionTree Contains(string field, string value) => Compare(field, ConditionOperator.Contains, value);
        public static ConditionTree StartsWith(string field, string value) => Compare(field, ConditionOperator.StartsWith, value);
        public static ConditionTree EndsWith(string field, string value) => Compare(field, ConditionOperator.EndsWith, value);
        public static ConditionTree IsNull(string field) => Compare(field, ConditionOperator.IsNull, null);
        public static ConditionTree IsNotNull(string field) => Compare(field, ConditionOperator.IsNotNull, null);
        public static ConditionTree In(string field, object[] values) => Compare(field, ConditionOperator.In, values);
        public static ConditionTree Between(string field, object min, object max) => Compare(field, ConditionOperator.Between, new[] { min, max });
    }

    /// <summary>
    /// 条件类型枚举
    /// </summary>
    public enum ConditionType
    {
        /// <summary>
        /// 比较条件（字段与值比较）
        /// </summary>
        Comparison = 1,

        /// <summary>
        /// 逻辑 AND（所有子条件都为真）
        /// </summary>
        And = 2,

        /// <summary>
        /// 逻辑 OR（任一子条件为真）
        /// </summary>
        Or = 3,

        /// <summary>
        /// 逻辑 NOT（子条件取反）
        /// </summary>
        Not = 4
    }

    /// <summary>
    /// 条件操作符枚举（受限集合）
    /// </summary>
    public enum ConditionOperator
    {
        /// <summary>等于 (=)</summary>
        Equals = 1,

        /// <summary>不等于 (!=)</summary>
        NotEquals = 2,

        /// <summary>大于 (>)</summary>
        GreaterThan = 3,

        /// <summary>大于等于 (>=)</summary>
        GreaterThanOrEqual = 4,

        /// <summary>小于 (<)</summary>
        LessThan = 5,

        /// <summary>小于等于 (<=)</summary>
        LessThanOrEqual = 6,

        /// <summary>包含（字符串）</summary>
        Contains = 7,

        /// <summary>开头是（字符串）</summary>
        StartsWith = 8,

        /// <summary>结尾是（字符串）</summary>
        EndsWith = 9,

        /// <summary>为空</summary>
        IsNull = 10,

        /// <summary>不为空</summary>
        IsNotNull = 11,

        /// <summary>在集合中</summary>
        In = 12,

        /// <summary>在范围内</summary>
        Between = 13
    }
}
