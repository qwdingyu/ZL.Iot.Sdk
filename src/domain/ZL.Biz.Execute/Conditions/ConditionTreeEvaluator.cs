using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ZL.Biz.Execute.Conditions
{
    /// <summary>
    /// 条件树求值器（安全实现）
    /// <para>替代 DataTable.Compute 的不安全表达式计算</para>
    /// </summary>
    /// <remarks>
    /// P2 阶段实现：
    /// - 提供安全的条件判断，防止代码注入风险
    /// - 只支持预定义的操作符和类型安全的比较
    /// - 支持条件树的递归求值
    /// </remarks>
    public class ConditionTreeEvaluator
    {
        private readonly ILogger<ConditionTreeEvaluator> _logger;

        public ConditionTreeEvaluator(ILogger<ConditionTreeEvaluator> logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// 求值条件树
        /// </summary>
        /// <param name="condition">条件树</param>
        /// <param name="facts">事实数据字典</param>
        /// <returns>条件是否满足</returns>
        public bool Evaluate(ConditionTree condition, Dictionary<string, object> facts)
        {
            if (condition == null)
            {
                _logger?.LogWarning("Condition tree is null, returning false");
                return false;
            }

            try
            {
                return EvaluateInternal(condition, facts);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Condition evaluation failed");
                return false;
            }
        }

        private bool EvaluateInternal(ConditionTree condition, Dictionary<string, object> facts)
        {
            switch (condition.Type)
            {
                case ConditionType.Comparison:
                    return EvaluateComparison(condition, facts);

                case ConditionType.And:
                    return condition.Children.All(c => EvaluateInternal(c, facts));

                case ConditionType.Or:
                    return condition.Children.Any(c => EvaluateInternal(c, facts));

                case ConditionType.Not:
                    if (condition.Children.Count == 0) return false;
                    return !EvaluateInternal(condition.Children[0], facts);

                default:
                    _logger?.LogWarning("Unknown condition type: {Type}", condition.Type);
                    return false;
            }
        }

        private bool EvaluateComparison(ConditionTree condition, Dictionary<string, object> facts)
        {
            // 获取左操作数的值
            object leftValue = GetOperandValue(condition.Left, facts);
            object rightValue = condition.Right;

            return EvaluateOperator(leftValue, condition.Operator, rightValue);
        }

        private object GetOperandValue(string operand, Dictionary<string, object> facts)
        {
            if (string.IsNullOrEmpty(operand))
                return null;

            // 如果是事实字典中的字段，获取其值
            if (facts != null && facts.TryGetValue(operand, out var value))
                return value;

            // 字段不在 facts 中，返回 null（表示字段不存在/缺失）
            return null;
        }

        private bool EvaluateOperator(object left, ConditionOperator op, object right)
        {
            switch (op)
            {
                case ConditionOperator.Equals:
                    return AreEqual(left, right);

                case ConditionOperator.NotEquals:
                    return !AreEqual(left, right);

                case ConditionOperator.GreaterThan:
                    return CompareValues(left, right) > 0;

                case ConditionOperator.GreaterThanOrEqual:
                    return CompareValues(left, right) >= 0;

                case ConditionOperator.LessThan:
                    return CompareValues(left, right) < 0;

                case ConditionOperator.LessThanOrEqual:
                    return CompareValues(left, right) <= 0;

                case ConditionOperator.Contains:
                    return StringContains(left, right);

                case ConditionOperator.StartsWith:
                    return StringStartsWith(left, right);

                case ConditionOperator.EndsWith:
                    return StringEndsWith(left, right);

                case ConditionOperator.IsNull:
                    return left == null || (left is string s && string.IsNullOrEmpty(s));

                case ConditionOperator.IsNotNull:
                    return left != null && !(left is string str && string.IsNullOrEmpty(str));

                case ConditionOperator.In:
                    return IsInCollection(left, right);

                case ConditionOperator.Between:
                    return IsBetween(left, right);

                default:
                    _logger?.LogWarning("Unknown operator: {Operator}", op);
                    return false;
            }
        }

        private bool AreEqual(object left, object right)
        {
            if (left == null && right == null) return true;
            if (left == null || right == null) return false;

            // 尝试转换为相同类型比较
            if (TryConvertToComparable(ref left, ref right))
            {
                return Equals(left, right);
            }

            return Equals(left, right);
        }

        private int CompareValues(object left, object right)
        {
            if (left == null || right == null)
                return Comparer<object>.Default.Compare(left, right);

            // 尝试转换为可比较类型
            if (TryConvertToComparable(ref left, ref right))
            {
                if (left is IComparable comparable)
                    return comparable.CompareTo(right);
            }

            // 尝试数值比较
            if (TryGetNumericValue(left, out var leftNum) && TryGetNumericValue(right, out var rightNum))
            {
                return leftNum.CompareTo(rightNum);
            }

            return Comparer<object>.Default.Compare(left, right);
        }

        private bool TryConvertToComparable(ref object left, ref object right)
        {
            // 如果两者都是字符串
            if (left is string && right is string)
                return true;

            // 尝试将两者转换为 double
            if (TryGetNumericValue(left, out var leftNum) && TryGetNumericValue(right, out var rightNum))
            {
                left = leftNum;
                right = rightNum;
                return true;
            }

            // 尝试将两者转换为 bool
            if (left is bool leftBool && right is bool rightBool)
            {
                return true;
            }

            return false;
        }

        private bool TryGetNumericValue(object value, out double result)
        {
            result = 0;
            if (value == null) return false;

            if (value is double d) { result = d; return true; }
            if (value is float f) { result = f; return true; }
            if (value is int i) { result = i; return true; }
            if (value is long l) { result = l; return true; }
            if (value is decimal dec) { result = (double)dec; return true; }
            if (value is short sh) { result = sh; return true; }
            if (value is byte b) { result = b; return true; }

            if (value is string str && double.TryParse(str, out var parsed))
            {
                result = parsed;
                return true;
            }

            return false;
        }

        private bool StringContains(object left, object right)
        {
            if (left == null || right == null) return false;
            var leftStr = left.ToString();
            var rightStr = right.ToString();
            return leftStr?.Contains(rightStr) ?? false;
        }

        private bool StringStartsWith(object left, object right)
        {
            if (left == null || right == null) return false;
            var leftStr = left.ToString();
            var rightStr = right.ToString();
            return leftStr?.StartsWith(rightStr) ?? false;
        }

        private bool StringEndsWith(object left, object right)
        {
            if (left == null || right == null) return false;
            var leftStr = left.ToString();
            var rightStr = right.ToString();
            return leftStr?.EndsWith(rightStr) ?? false;
        }

        private bool IsInCollection(object left, object right)
        {
            if (left == null || right == null) return false;

            if (right is object[] array)
            {
                return array.Any(item => AreEqual(left, item));
            }

            if (right is IEnumerable<object> enumerable)
            {
                return enumerable.Any(item => AreEqual(left, item));
            }

            return false;
        }

        private bool IsBetween(object left, object right)
        {
            if (left == null || right == null) return false;

            if (right is object[] range && range.Length == 2)
            {
                return CompareValues(left, range[0]) >= 0 && CompareValues(left, range[1]) <= 0;
            }

            return false;
        }
    }
}
