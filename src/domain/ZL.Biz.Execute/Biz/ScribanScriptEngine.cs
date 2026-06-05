using Microsoft.Extensions.Logging;
using Scriban;
using Scriban.Runtime;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ZL.Iot.Interface;

namespace ZL.Biz.Execute.Biz
{
    /// <summary>
    /// Scriban 脚本引擎适配器 (Edge compatible)
    /// </summary>
    public class ScribanScriptEngine : IScriptEngine
    {
        private readonly ILogger<ScribanScriptEngine> _logger;
        private static readonly ConcurrentDictionary<string, Template> _templateCache = new();

        // 兼容遗留格式的正则
        private static readonly Regex LegacyValuePattern = new(@"\?(\w+)\?", RegexOptions.Compiled);
        private static readonly Regex LegacyFieldPattern = new(@"#(\w+)#", RegexOptions.Compiled);
        private static readonly Regex LegacyNamePattern = new(@"@(\w+)@", RegexOptions.Compiled);

        public ScribanScriptEngine(ILogger<ScribanScriptEngine> logger)
        {
            _logger = logger;
        }

        public string Render(string template, Dictionary<string, object> variables)
        {
            if (string.IsNullOrWhiteSpace(template))
                return template;

            try
            {
                if (ContainsScribanSyntax(template))
                {
                    return RenderWithScriban(template, variables);
                }

                return ReplaceLegacyVariables(template, variables);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Render template failed, using raw template");
                return template;
            }
        }

        public IEnumerable<string> GetVariables(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
                return Enumerable.Empty<string>();

            var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var scribanVars = ExtractScribanVariables(template);
                foreach (var v in scribanVars) variables.Add(v);

                var legacyVars = ExtractLegacyVariables(template);
                foreach (var v in legacyVars) variables.Add(v);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Extract variables failed");
            }

            return variables;
        }

        public bool Validate(string template, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(template))
            {
                errorMessage = "Template cannot be empty";
                return false;
            }

            try
            {
                if (ContainsScribanSyntax(template))
                {
                    var scribanTemplate = Template.Parse(template);
                    if (scribanTemplate.HasErrors)
                    {
                        errorMessage = string.Join("; ", scribanTemplate.Messages);
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private bool ContainsScribanSyntax(string template)
        {
            return template.Contains("{{") && template.Contains("}}");
        }

        private string RenderWithScriban(string template, Dictionary<string, object> variables)
        {
            var scribanTemplate = _templateCache.GetOrAdd(template, t => Template.Parse(t));
            if (scribanTemplate.HasErrors)
            {
                throw new Exception(string.Join("; ", scribanTemplate.Messages));
            }

            var context = new TemplateContext();
            var scriptObject = new ScriptObject();
            scriptObject.Import(variables);
            context.PushGlobal(scriptObject);

            return scribanTemplate.Render(context);
        }

        private string ReplaceLegacyVariables(string template, Dictionary<string, object> variables)
        {
            string result = template;

            result = LegacyValuePattern.Replace(result, m =>
            {
                var key = m.Groups[1].Value;
                return variables.TryGetValue(key, out var val) ? val?.ToString() ?? "" : m.Value;
            });

            result = LegacyFieldPattern.Replace(result, m =>
            {
                var key = m.Groups[1].Value;
                return variables.TryGetValue(key, out var val) ? val?.ToString() ?? "" : m.Value;
            });

            result = LegacyNamePattern.Replace(result, m =>
            {
                var key = m.Groups[1].Value;
                return variables.TryGetValue(key, out var val) ? val?.ToString() ?? "" : m.Value;
            });

            return result;
        }

        private IEnumerable<string> ExtractScribanVariables(string template)
        {
            var scribanTemplate = Template.Parse(template);
            if (scribanTemplate.HasErrors) return Enumerable.Empty<string>();

            // 简单实现：使用正则抓取 {{ }} 中的标识符，更复杂的需要解析 AST
            var matches = Regex.Matches(template, @"\{\{\s*(\w+)\s*\}\}");
            return matches.Cast<Match>().Select(m => m.Groups[1].Value).Distinct();
        }

        private IEnumerable<string> ExtractLegacyVariables(string template)
        {
            var vars = new List<string>();
            vars.AddRange(LegacyValuePattern.Matches(template).Cast<Match>().Select(m => m.Groups[1].Value));
            vars.AddRange(LegacyFieldPattern.Matches(template).Cast<Match>().Select(m => m.Groups[1].Value));
            vars.AddRange(LegacyNamePattern.Matches(template).Cast<Match>().Select(m => m.Groups[1].Value));
            return vars.Distinct();
        }
    }
}
