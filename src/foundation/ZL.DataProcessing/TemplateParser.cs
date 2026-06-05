using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ZL.DataProcessing
{
    internal enum TemplateSegmentType
    {
        Text,
        Expression,
        Statement
    }

    internal sealed class TemplateSegment
    {
        public TemplateSegment(TemplateSegmentType type, string content)
        {
            Type = type;
            Content = content ?? string.Empty;
        }

        public TemplateSegmentType Type { get; }
        public string Content { get; }
    }

    internal static class TemplateParser
    {
        private static readonly ConcurrentDictionary<string, List<TemplateSegment>> Cache =
            new ConcurrentDictionary<string, List<TemplateSegment>>(StringComparer.Ordinal);

        public static bool HasInlineScript(string template)
        {
            if (string.IsNullOrEmpty(template)) return false;
            return template.IndexOf("${", StringComparison.Ordinal) >= 0
                || template.IndexOf("@{", StringComparison.Ordinal) >= 0;
        }

        public static IReadOnlyList<TemplateSegment> Parse(string template)
        {
            if (string.IsNullOrEmpty(template)) return Array.Empty<TemplateSegment>();
            return Cache.GetOrAdd(template, ParseInternal);
        }

        private static List<TemplateSegment> ParseInternal(string template)
        {
            var segments = new List<TemplateSegment>();
            int idx = 0;

            while (idx < template.Length)
            {
                int exprIdx = template.IndexOf("${", idx, StringComparison.Ordinal);
                int stmtIdx = template.IndexOf("@{", idx, StringComparison.Ordinal);
                int nextIdx;
                TemplateSegmentType nextType;

                if (exprIdx >= 0 && (stmtIdx < 0 || exprIdx < stmtIdx))
                {
                    nextIdx = exprIdx;
                    nextType = TemplateSegmentType.Expression;
                }
                else if (stmtIdx >= 0)
                {
                    nextIdx = stmtIdx;
                    nextType = TemplateSegmentType.Statement;
                }
                else
                {
                    nextIdx = -1;
                    nextType = TemplateSegmentType.Text;
                }

                if (nextIdx < 0)
                {
                    if (idx < template.Length)
                    {
                        segments.Add(new TemplateSegment(TemplateSegmentType.Text, template.Substring(idx)));
                    }
                    break;
                }

                if (nextIdx > idx)
                {
                    segments.Add(new TemplateSegment(TemplateSegmentType.Text, template.Substring(idx, nextIdx - idx)));
                }

                int start = nextIdx + 2;
                int end = template.IndexOf('}', start);
                if (end < 0)
                {
                    segments.Add(new TemplateSegment(TemplateSegmentType.Text, template.Substring(nextIdx)));
                    break;
                }

                string content = template.Substring(start, end - start);
                segments.Add(new TemplateSegment(nextType, content));
                idx = end + 1;
            }

            return segments;
        }
    }
}
