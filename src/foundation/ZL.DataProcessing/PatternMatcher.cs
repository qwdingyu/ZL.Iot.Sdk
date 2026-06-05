using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZL.DataProcessing
{
    public static class PatternMatcher
    {
        private static readonly TimeSpan UserRegexTimeout = TimeSpan.FromMilliseconds(200);

        public static Regex BuildTextRegex(string userPattern)
        {
            if (string.IsNullOrWhiteSpace(userPattern))
            {
                return new Regex("^$", RegexOptions.None, UserRegexTimeout);
            }

            // Keep placeholder blocks like "{value}" or "{VALUE}" as capture groups,
            // while escaping all other characters literally.
            // Case-insensitive because HexUtil.NormalizeHex uppercases placeholders.
            const string placeholderToken = "__ZL_CAPTURE_PLACEHOLDER__";
            string normalized = Regex.Replace(userPattern, @"\{[^}]+\}", placeholderToken, RegexOptions.IgnoreCase);
            string escaped = Regex.Escape(normalized);
            string pattern = escaped.Replace(placeholderToken, "(.+?)", StringComparison.Ordinal);
            return new Regex($"^{pattern}$", RegexOptions.IgnoreCase | RegexOptions.Compiled, UserRegexTimeout);
        }

        public static bool TryMatch(string input, Regex regex, out string[] capturedGroups)
        {
            capturedGroups = Array.Empty<string>();
            if (input == null || regex == null) return false;

            var match = regex.Match(input);
            if (!match.Success) return false;

            if (match.Groups.Count > 1)
            {
                capturedGroups = match.Groups.Cast<Group>()
                    .Skip(1)
                    .Select(g => g.Value)
                    .ToArray();
            }
            return true;
        }

        public static bool IsBinaryMatch(byte[] data, string hexPattern)
        {
            if (data == null || string.IsNullOrWhiteSpace(hexPattern)) return false;

            var tokens = ParseBinaryTokens(hexPattern);
            if (tokens.Count == 0) return false;

            return MatchBinaryIterative(tokens, data);
        }

        private static bool MatchBinaryIterative(
            System.Collections.Generic.IReadOnlyList<BinaryToken> tokens,
            byte[] data)
        {
            // Use an explicit stack to avoid deep recursion on large payloads.
            // Each state is (tokenIndex, dataIndex). We traverse all possible matches
            // produced by wildcards (** / ??[n-m]) and stop as soon as a full match is found.
            var stack = new System.Collections.Generic.Stack<(int tokenIndex, int dataIndex)>();
            var visited = new System.Collections.Generic.HashSet<(int tokenIndex, int dataIndex)>();
            stack.Push((0, 0));

            while (stack.Count > 0)
            {
                var state = stack.Pop();
                // Memoization: if this state was already explored, skip it.
                if (!visited.Add(state))
                {
                    continue;
                }

                int tokenIndex = state.tokenIndex;
                int dataIndex = state.dataIndex;

                // When all tokens are consumed, a match is valid only if input is fully consumed.
                if (tokenIndex >= tokens.Count)
                {
                    if (dataIndex == data.Length) return true;
                    continue;
                }

                var token = tokens[tokenIndex];
                switch (token.Type)
                {
                    case BinaryTokenType.Fixed:
                        if (dataIndex < data.Length && data[dataIndex] == token.Value)
                        {
                            stack.Push((tokenIndex + 1, dataIndex + 1));
                        }
                        break;
                    case BinaryTokenType.Any:
                        if (dataIndex < data.Length)
                        {
                            stack.Push((tokenIndex + 1, dataIndex + 1));
                        }
                        break;
                    case BinaryTokenType.AnyRange:
                        {
                            // ??[n-m] expands to all lengths in the range.
                            int max = Math.Min(token.MaxLength, data.Length - dataIndex);
                            for (int len = token.MinLength; len <= max; len++)
                            {
                                stack.Push((tokenIndex + 1, dataIndex + len));
                            }
                            break;
                        }
                    case BinaryTokenType.AnyMany:
                        // ** matches any length (including zero). This can expand significantly on large data,
                        // but avoids StackOverflowException by staying iterative.
                        for (int len = 0; dataIndex + len <= data.Length; len++)
                        {
                            stack.Push((tokenIndex + 1, dataIndex + len));
                        }
                        break;
                }
            }

            return false;
        }

        private static System.Collections.Generic.List<BinaryToken> ParseBinaryTokens(string hexPattern)
        {
            var tokens = new System.Collections.Generic.List<BinaryToken>();
            string[] parts = hexPattern.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in parts)
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;

                if (string.Equals(token, "??", StringComparison.Ordinal))
                {
                    tokens.Add(BinaryToken.Any());
                    continue;
                }

                if (string.Equals(token, "**", StringComparison.Ordinal))
                {
                    tokens.Add(BinaryToken.AnyMany());
                    continue;
                }

                if (TryParseAnyRange(token, out int min, out int max))
                {
                    tokens.Add(BinaryToken.AnyRange(min, max));
                    continue;
                }

                if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                {
                    return new System.Collections.Generic.List<BinaryToken>();
                }

                tokens.Add(BinaryToken.Fixed(value));
            }
            return tokens;
        }

        private static bool TryParseAnyRange(string token, out int min, out int max)
        {
            min = 0;
            max = 0;
            if (!token.StartsWith("??[", StringComparison.Ordinal) || !token.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }

            string body = token.Substring(3, token.Length - 4);
            if (string.IsNullOrWhiteSpace(body)) return false;

            int dash = body.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out min)) return false;
                max = min;
                return min >= 0;
            }

            string left = body.Substring(0, dash);
            string right = body.Substring(dash + 1);
            if (!int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out min)) return false;
            if (!int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out max)) return false;
            if (min < 0 || max < min) return false;
            return true;
        }

        private enum BinaryTokenType
        {
            Fixed,
            Any,
            AnyRange,
            AnyMany
        }

        private readonly struct BinaryToken
        {
            private BinaryToken(BinaryTokenType type, byte value, int minLength, int maxLength)
            {
                Type = type;
                Value = value;
                MinLength = minLength;
                MaxLength = maxLength;
            }

            public BinaryTokenType Type { get; }
            public byte Value { get; }
            public int MinLength { get; }
            public int MaxLength { get; }

            public static BinaryToken Fixed(byte value) => new BinaryToken(BinaryTokenType.Fixed, value, 1, 1);
            public static BinaryToken Any() => new BinaryToken(BinaryTokenType.Any, 0, 1, 1);
            public static BinaryToken AnyRange(int min, int max) => new BinaryToken(BinaryTokenType.AnyRange, 0, min, max);
            public static BinaryToken AnyMany() => new BinaryToken(BinaryTokenType.AnyMany, 0, 0, int.MaxValue);
        }
    }
}
