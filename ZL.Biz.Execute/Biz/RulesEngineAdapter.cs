using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZL.Iot.Interface;
using Newtonsoft.Json;
using ZL.Biz.Execute.Conditions;

namespace ZL.Biz.Execute.Biz
{
    /// <summary>
    /// 自定义规则引擎适配器 (Edge compatible)
    /// <para>P4: 优先使用 ConditionTreeEvaluator 进行安全求值</para>
    /// </summary>
    public class RulesEngineAdapter : IRuleEngine
    {
        private readonly ILogger<RulesEngineAdapter> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ConditionTreeEvaluator _conditionEvaluator;

        public RulesEngineAdapter(ILogger<RulesEngineAdapter> logger, ILoggerFactory loggerFactory = null)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            // P4: 创建 ConditionTreeEvaluator，使用正确类型的 logger
            var evalLogger = loggerFactory?.CreateLogger<ConditionTreeEvaluator>();
            _conditionEvaluator = new ConditionTreeEvaluator(evalLogger);
        }

        public async Task<RuleEvaluationResult> EvaluateAsync(string ruleJson, Dictionary<string, object> facts)
        {
            var result = new RuleEvaluationResult { IsMatch = false };

            try
            {
                // P4: 优先尝试解析为 ConditionTree JSON（新格式）
                if (!string.IsNullOrWhiteSpace(ruleJson) && ruleJson.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        // 尝试解析为 ConditionTree
                        var condition = JsonConvert.DeserializeObject<ConditionTree>(ruleJson);
                        if (condition != null)
                        {
                            result.MatchedRuleName = "ConditionTree";
                            result.IsMatch = _conditionEvaluator.Evaluate(condition, facts);
                            return await Task.FromResult(result);
                        }
                    }
                    catch (JsonException)
                    {
                        // 不是 ConditionTree JSON，继续尝试旧格式
                        _logger?.LogDebug("Rule JSON is not a ConditionTree, trying legacy format");
                    }
                }

                // 旧格式：RuleDefinition JSON
                var ruleDefinition = JsonConvert.DeserializeObject<RuleDefinition>(ruleJson);

                if (ruleDefinition == null || string.IsNullOrWhiteSpace(ruleDefinition.Expression))
                {
                    result.Exception = new Exception("Invalid rule JSON or missing expression");
                    return await Task.FromResult(result);
                }

                result.MatchedRuleName = ruleDefinition.RuleName;
                
                // P4: 安全增强 - 不再使用不安全的 DataTable.Compute
                // 尝试将字符串表达式解析为 ConditionTree
                var simpleCondition = TryParseSimpleExpression(ruleDefinition.Expression);
                if (simpleCondition != null)
                {
                    result.IsMatch = _conditionEvaluator.Evaluate(simpleCondition, facts);
                }
                else
                {
                    _logger?.LogWarning("Could not parse expression as ConditionTree: {Expression}. Set `UseConditionTree` to true for new rules.", ruleDefinition.Expression);
                    result.Exception = new Exception("Expression format not supported. Use ConditionTree JSON format for new rules.");
                }
                
                return await Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Rule evaluation failed");
                result.Exception = ex;
                return result;
            }
        }

        public bool Validate(string ruleJson, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                if (string.IsNullOrWhiteSpace(ruleJson))
                {
                    errorMessage = "Rule content cannot be empty";
                    return false;
                }

                // P4: 支持新格式 ConditionTree
                if (ruleJson.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        var condition = JsonConvert.DeserializeObject<ConditionTree>(ruleJson);
                        if (condition != null)
                        {
                            return true;
                        }
                    }
                    catch (JsonException)
                    {
                        // 不是 ConditionTree JSON
                    }
                }

                // 旧格式验证
                var rule = JsonConvert.DeserializeObject<RuleDefinition>(ruleJson);
                if (rule == null || string.IsNullOrWhiteSpace(rule.Expression))
                {
                    errorMessage = "Invalid rule JSON or missing expression";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// P4: 尝试将简单表达式解析为 ConditionTree
        /// 支持格式: "FieldName Operator Value" 例如 "Temperature > 100"
        /// </summary>
        private ConditionTree TryParseSimpleExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return null;

            // 去除首尾空白
            expression = expression.Trim();
            
            // 尝试匹配简单的比较表达式
            // 支持的操作符: ==, !=, >, <, >=, <=, equals, contains, startswith, endswith
            var match = Regex.Match(expression, @"^(\w+)\s*(==|!=|>|<|>=|<=|equals|contains|startswith|endswith)\s*(.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var field = match.Groups[1].Value;
                var op = match.Groups[2].Value.ToLower();
                var value = match.Groups[3].Value.Trim();

                // 转换操作符
                var conditionOp = op switch
                {
                    "==" or "equals" => ConditionOperator.Equals,
                    "!=" => ConditionOperator.NotEquals,
                    ">" => ConditionOperator.GreaterThan,
                    "<" => ConditionOperator.LessThan,
                    ">=" => ConditionOperator.GreaterThanOrEqual,
                    "<=" => ConditionOperator.LessThanOrEqual,
                    "contains" => ConditionOperator.Contains,
                    "startswith" => ConditionOperator.StartsWith,
                    "endswith" => ConditionOperator.EndsWith,
                    _ => (ConditionOperator)Enum.Parse(typeof(ConditionOperator), op, true)
                };

                // 去除字符串值的引号
                if ((value.StartsWith("'") && value.EndsWith("'")) ||
                    (value.StartsWith("\"") && value.EndsWith("\"")))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                return new ConditionTree
                {
                    Type = ConditionType.Comparison,
                    Left = field,
                    Operator = conditionOp,
                    Right = value
                };
            }

            return null;
        }

        private class RuleDefinition
        {
            public string RuleName { get; set; }
            public string Expression { get; set; }
        }
    }
}
