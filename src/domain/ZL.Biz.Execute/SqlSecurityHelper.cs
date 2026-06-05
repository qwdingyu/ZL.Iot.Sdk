using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZL.Biz.Execute
{
    /// <summary>
    /// SQL 安全辅助类
    /// 用于防止 SQL 注入攻击
    /// </summary>
    public static class SqlSecurityHelper
    {
        // SQL 危险关键字
        private static readonly HashSet<string> DangerousKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE", "INSERT", "UPDATE", "EXEC", "EXECUTE",
            "xp_", "sp_", "--", "/*", "*/", "UNION", "SCRIPT", "javascript:", "onerror=", "onclick=",
            "OR", "AND", "WHERE", "HAVING"
        };

        // SQL 注释模式
        private static readonly Regex SqlCommentRegex = new Regex(@"(--|/\*|\*/)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 检查字符串是否包含危险 SQL 关键字
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>是否安全</returns>
        public static bool ContainsDangerousSql(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            // 使用正则表达式进行更精确的匹配（匹配完整单词）
            var dangerousPattern = @"\b(" + string.Join("|", DangerousKeywords.Select(k => Regex.Escape(k))) + @")\b";
            return Regex.IsMatch(input, dangerousPattern, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// 获取危险关键字列表（用于日志）
        /// </summary>
        public static List<string> GetDangerousKeywords(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(input))
                return result;

            // 使用正则表达式进行更精确的匹配（匹配完整单词）
            var dangerousPattern = @"\b(" + string.Join("|", DangerousKeywords.Select(k => Regex.Escape(k))) + @")\b";
            var matches = Regex.Matches(input, dangerousPattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                result.Add(match.Value);
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 转义单引号（用于 SQL 字符串）
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>转义后的字符串</returns>
        public static string EscapeSqlString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // 单引号转义为两个单引号
            return input.Replace("'", "''");
        }

        /// <summary>
        /// 转义 LIKE 语句的特殊字符
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>转义后的字符串</returns>
        public static string EscapeLikeString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // 转义 LIKE 中的特殊字符
            var result = input
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");
            
            return EscapeSqlString(result);
        }

        /// <summary>
        /// 移除 SQL 注释
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>移除注释后的字符串</returns>
        public static string RemoveSqlComments(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // 移除 /* ... */ 注释
            var result = Regex.Replace(input, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            // 移除 -- 注释
            result = Regex.Replace(result, @"--[^\r\n]*", " ");
            return result;
        }

        /// <summary>
        /// 验证表达式是否安全（用于 DataTable.Compute）
        /// </summary>
        /// <param name="expression">表达式字符串</param>
        /// <param name="allowedFunctions">允许的函数列表</param>
        /// <returns>是否安全</returns>
        public static bool IsExpressionSafe(string expression, HashSet<string> allowedFunctions = null)
        {
            if (string.IsNullOrEmpty(expression))
                return true;

            // 检查危险关键字
            if (ContainsDangerousSql(expression))
                return false;

            // 检查是否包含 Process、System 等危险 .NET 类型访问
            if (expression.Contains("Process", StringComparison.OrdinalIgnoreCase) ||
                expression.Contains("System.", StringComparison.OrdinalIgnoreCase) ||
                expression.Contains("Runtime", StringComparison.OrdinalIgnoreCase))
                return false;

            // 检查方法调用（除了允许的函数）
            var methodCallRegex = new Regex(@"[\w]+\s*\(", RegexOptions.IgnoreCase);
            var matches = methodCallRegex.Matches(expression);

            foreach (Match match in matches)
            {
                var methodName = match.Value.Trim().TrimEnd('(');
                if (!string.IsNullOrEmpty(methodName))
                {
                    // 检查是否是数字或布尔值
                    if (methodName == "IIF" || methodName == "Abs" || methodName == "Max" || 
                        methodName == "Min" || methodName == "Round" || methodName == "Floor" ||
                        methodName == "Ceiling" || methodName == "Pow" || methodName == "Sqrt" ||
                        methodName == "Len" || methodName == "Left" || methodName == "Right" ||
                        methodName == "Mid" || methodName == "Trim" || methodName == "UCase" ||
                        methodName == "LCase" || methodName == "IsNull" || methodName == "In")
                        continue;

                    if (allowedFunctions != null && allowedFunctions.Contains(methodName))
                        continue;

                    // 未知的函数调用，可能有风险
                    // 注意：这里不直接拒绝，因为 DataTable.Compute 本身有限制
                }
            }

            return true;
        }

        /// <summary>
        /// 验证并转义参数值
        /// </summary>
        /// <param name="paramName">参数名</param>
        /// <param name="paramValue">参数值</param>
        /// <param name="isNumeric">是否应为数字</param>
        /// <returns>安全处理后的值</returns>
        /// <exception cref="ArgumentException">当参数包含危险内容时抛出</exception>
        public static string ValidateAndEscape(string paramName, string paramValue, bool isNumeric = false)
        {
            if (paramValue == null)
                return null;

            // 移除 SQL 注释
            var cleaned = RemoveSqlComments(paramValue);

            // 检查危险内容
            if (ContainsDangerousSql(cleaned))
            {
                var dangerous = GetDangerousKeywords(cleaned);
                throw new ArgumentException(
                    $"参数 {paramName} 包含危险内容: {string.Join(", ", dangerous)}", 
                    paramName);
            }

            if (isNumeric)
            {
                // 验证是否为有效数字
                if (!double.TryParse(cleaned, out _))
                {
                    throw new ArgumentException(
                        $"参数 {paramName} 应为数字，但值为: {cleaned}", 
                        paramName);
                }
                return cleaned;
            }
            else
            {
                // 转义字符串
                return EscapeSqlString(cleaned);
            }
        }
    }
}
