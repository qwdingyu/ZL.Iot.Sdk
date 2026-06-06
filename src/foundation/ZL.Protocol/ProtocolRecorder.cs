using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZL.Protocol.Models;
using ZL.Shared.Utils;

namespace ZL.Protocol
{
    /// <summary>
    /// 协议录制器——从 JSONL 日志中自动分析并生成 ProtocolConfig。
    /// 支持校验位推断、参数识别、枚举识别、模板合并。
    /// </summary>
    public sealed class ProtocolRecorder : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// 录制选项
        /// </summary>
        public sealed class RecordingOptions
        {
            /// <summary>协议名称</summary>
            public string ProtocolName { get; set; } = "RecordedProtocol";

            /// <summary>帧模式：Text / Hex / Binary</summary>
            public string FrameMode { get; set; } = "Text";

            /// <summary>帧终止符</summary>
            public string Terminator { get; set; } = "\n";

            /// <summary>模板相似度阈值（0.0-1.0，高于此值视为同一模板）</summary>
            public double SimilarityThreshold { get; set; } = 0.8;

            /// <summary>是否自动合并相似模板</summary>
            public bool AutoMergeTemplates { get; set; } = true;

            /// <summary>是否推断参数</summary>
            public bool InferParameters { get; set; } = true;

            /// <summary>是否推断校验位</summary>
            public bool InferChecksums { get; set; } = true;
        }

        /// <summary>
        /// 从 JSONL 日志行流中分析并生成协议配置。
        /// </summary>
        public ProtocolConfig Analyze(IEnumerable<string> logLines, RecordingOptions options)
        {
            ThrowIfDisposed();

            var config = new ProtocolConfig
            {
                ProtocolName = options.ProtocolName,
                FrameMode = options.FrameMode,
                Terminator = options.Terminator
            };

            var sessions = ParseLogLines(logLines);
            var commands = new List<IntermediateCommand>();

            foreach (var session in sessions)
            {
                var sessionCommands = ExtractCommandPairs(session, options);
                commands.AddRange(sessionCommands);
            }

            var processedCommands = PostProcessCommands(commands, options);

            foreach (var innerCmd in processedCommands)
            {
                var cmd = innerCmd.Definition;
                string baseName = GenerateCommandName(cmd);
                string name = baseName;
                int counter = 1;
                while (config.Commands.ContainsKey(name))
                {
                    name = $"{baseName}_{counter++}";
                }
                config.Commands[name] = cmd;
            }

            return config;
        }

        #region Private - Parse & Extract

        private List<List<LogEvent>> ParseLogLines(IEnumerable<string> lines)
        {
            var sessions = new Dictionary<string, List<LogEvent>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    string sessionId = root.GetProperty("SessionId").GetString() ?? "default";
                    string direction = root.GetProperty("Direction").GetString() ?? "TX";
                    string hex = root.GetProperty("Hex").GetString() ?? string.Empty;
                    string text = root.GetProperty("Text").GetString() ?? string.Empty;
                    long timestamp = root.GetProperty("Timestamp").GetInt64();

                    if (!sessions.TryGetValue(sessionId, out var events))
                    {
                        events = new List<LogEvent>();
                        sessions[sessionId] = events;
                    }

                    events.Add(new LogEvent
                    {
                        Timestamp = timestamp,
                        Direction = direction,
                        Text = text,
                        Data = HexToBytes(hex),
                        SessionId = sessionId
                    });
                }
                catch
                {
                    // 忽略错误行
                }
            }
            return sessions.Values.ToList();
        }

        private List<IntermediateCommand> ExtractCommandPairs(List<LogEvent> events, RecordingOptions options)
        {
            var result = new List<IntermediateCommand>();
            var sorted = events.OrderBy(e => e.Timestamp).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var ev = sorted[i];
                if (ev.Direction == "TX")
                {
                    var cmd = new IntermediateCommand
                    {
                        RawRequest = ev.Data,
                        RequestText = CleanTerminator(ev.Text, options.Terminator),
                        Definition = new CommandDefinition()
                    };
                    cmd.Definition.CommandTemplate = cmd.RequestText;

                    for (int j = i + 1; j < Math.Min(i + 5, sorted.Count); j++)
                    {
                        var next = sorted[j];
                        if (next.Direction == "RX")
                        {
                            cmd.RawResponse = next.Data;
                            cmd.ResponseText = CleanTerminator(next.Text, options.Terminator);
                            cmd.Definition.ResponseTemplate = cmd.ResponseText;
                            cmd.Definition.WaitAfterMs = (int)(next.Timestamp - ev.Timestamp);
                            i = j;
                            break;
                        }
                        if (next.Direction == "TX") break;
                    }

                    if (!string.IsNullOrEmpty(cmd.Definition.CommandTemplate))
                    {
                        result.Add(cmd);
                    }
                }
            }
            return result;
        }

        #endregion

        #region Private - Post Process & Inference

        private List<IntermediateCommand> PostProcessCommands(List<IntermediateCommand> raw, RecordingOptions options)
        {
            if (raw.Count == 0) return raw;

            var result = new List<IntermediateCommand>();
            var groups = new List<List<IntermediateCommand>>();

            foreach (var cmd in raw)
            {
                bool matchedGroup = false;
                if (options.AutoMergeTemplates)
                {
                    foreach (var group in groups)
                    {
                        bool isHexFrame = options.FrameMode.Equals("Hex", StringComparison.OrdinalIgnoreCase) ||
                            options.FrameMode.Equals("Binary", StringComparison.OrdinalIgnoreCase);
                        double sim = isHexFrame
                            ? ByteSimilarity(group[0].RawRequest, cmd.RawRequest)
                            : StringDistance.Similarity(group[0].Definition.CommandTemplate, cmd.Definition.CommandTemplate);

                        if (sim >= options.SimilarityThreshold)
                        {
                            group.Add(cmd);
                            matchedGroup = true;
                            break;
                        }
                    }
                }

                if (!matchedGroup)
                {
                    groups.Add(new List<IntermediateCommand> { cmd });
                }
            }

            foreach (var group in groups)
            {
                if (group.Count == 1)
                {
                    result.Add(group[0]);
                    continue;
                }
                var merged = InferAdvancedTemplate(group, options);
                result.Add(merged);
            }

            return result;
        }

        private IntermediateCommand InferAdvancedTemplate(List<IntermediateCommand> group, RecordingOptions options)
        {
            var representative = group[0];
            if (!options.InferParameters) return representative;

            // 1. 识别校验位
            bool isHexFrame = options.FrameMode.Equals("Hex", StringComparison.OrdinalIgnoreCase) ||
                options.FrameMode.Equals("Binary", StringComparison.OrdinalIgnoreCase);
            if (options.InferChecksums && isHexFrame)
            {
                string? detectedCrc = DetectCheckSum(group);
                if (detectedCrc != null)
                {
                    representative.Definition.CheckSum = detectedCrc;
                    representative.Definition.AutoAppendCheckSum = true;
                    representative.Definition.CommandTemplate = StripCheckSum(representative.RawRequest, detectedCrc);
                }
            }

            // 2. 识别数字参数
            var requests = group.Select(g => g.Definition.CommandTemplate).ToList();
            string bestTemplate = requests[0];

            var numRegex = new Regex(@"\d+(\.\d+)?");
            var matches = numRegex.Matches(bestTemplate);
            foreach (Match m in matches)
            {
                string pattern = bestTemplate.Substring(0, m.Index) + @"\d+(\.\d+)?" + bestTemplate.Substring(m.Index + m.Length);
                var r = new Regex("^" + Regex.Escape(pattern).Replace(@"\d+(\.\d+)?", @"\d+(\.\d+)?") + "$");
                if (requests.All(req => r.IsMatch(req)))
                {
                    bestTemplate = numRegex.Replace(bestTemplate, "{Value}", 1, m.Index);
                }
            }

            // 3. 识别枚举
            if (group.Count > 1)
            {
                var diffs = ExtractDifferences(requests);
                if (diffs.IsEnumCandidate)
                {
                    string enumPart = "{" + string.Join("|", diffs.DistinctValues.Take(8)) + "}";
                    bestTemplate = ReplaceAt(bestTemplate, diffs.StartIndex, diffs.Length, enumPart);
                }
            }

            representative.Definition.CommandTemplate = bestTemplate;
            representative.Definition.WaitAfterMs = (int)group.Average(g => g.Definition.WaitAfterMs);

            return representative;
        }

        private string? DetectCheckSum(List<IntermediateCommand> group)
        {
            var protocols = new[] { "CRC16", "Modbus", "Sum8", "Xor8" };
            foreach (var p in protocols)
            {
                bool matchAll = true;
                foreach (var item in group)
                {
                    if (!VerifyChecksum(item.RawRequest, p))
                    {
                        matchAll = false;
                        break;
                    }
                }
                if (matchAll) return p;
            }
            return null;
        }

        private bool VerifyChecksum(byte[] data, string type)
        {
            if (data.Length < 3) return false;
            switch (type)
            {
                case "CRC16":
                case "Modbus":
                    if (data.Length < 4) return false;
                    ushort expected = CalculateModbusCrc(data, 0, data.Length - 2);
                    ushort actual = (ushort)(data[data.Length - 2] | (data[data.Length - 1] << 8));
                    return expected == actual;
                case "Sum8":
                    byte sum = 0;
                    for (int i = 0; i < data.Length - 1; i++) sum += data[i];
                    return sum == data[data.Length - 1];
                case "Xor8":
                    byte xor = 0;
                    for (int i = 0; i < data.Length - 1; i++) xor ^= data[i];
                    return xor == data[data.Length - 1];
            }
            return false;
        }

        private ushort CalculateModbusCrc(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0) crc = (ushort)((crc >> 1) ^ 0xA001);
                    else crc >>= 1;
                }
            }
            return crc;
        }

        private string StripCheckSum(byte[] data, string type)
        {
            int len = (type == "CRC16" || type == "Modbus") ? 2 : 1;
            var sub = new byte[data.Length - len];
            Array.Copy(data, sub, sub.Length);
            return BytesToHex(sub);
        }

        private DiffResult ExtractDifferences(List<string> inputs)
        {
            if (inputs == null || inputs.Count < 2) return new DiffResult();

            string first = inputs[0];
            string prefix = GetCommonPrefix(inputs);
            var remainings = inputs.Select(s => s.Substring(prefix.Length)).ToList();
            string suffix = GetCommonSuffix(remainings);

            int diffLen = first.Length - prefix.Length - suffix.Length;
            if (diffLen > 0)
            {
                var values = inputs.Select(s => s.Substring(prefix.Length, s.Length - prefix.Length - suffix.Length)).Distinct().ToList();
                return new DiffResult
                {
                    StartIndex = prefix.Length,
                    Length = diffLen,
                    DistinctValues = values,
                    IsEnumCandidate = values.Count > 1 && values.Count <= 16 && values.All(v => v.Length == values[0].Length && v.Length < 32)
                };
            }
            return new DiffResult();
        }

        private string GetCommonPrefix(List<string> ss)
        {
            if (ss.Count == 0) return "";
            string first = ss[0];
            for (int i = 0; i < first.Length; i++)
            {
                char c = first[i];
                if (ss.Any(s => i >= s.Length || s[i] != c)) return first.Substring(0, i);
            }
            return first;
        }

        private string GetCommonSuffix(List<string> ss)
        {
            if (ss.Count == 0) return "";
            string first = ss[0];
            for (int i = 0; i < first.Length; i++)
            {
                char c = first[first.Length - 1 - i];
                if (ss.Any(s => i >= s.Length || s[s.Length - 1 - i] != c)) return first.Substring(first.Length - i);
            }
            return first;
        }

        private string ReplaceAt(string input, int index, int length, string replacement)
        {
            return input.Remove(index, length).Insert(index, replacement);
        }

        #endregion

        #region Private - Helpers

        private double ByteSimilarity(byte[] a, byte[] b)
        {
            if (a.Length == 0 || b.Length == 0) return a.Length == b.Length ? 1.0 : 0.0;
            int matches = 0;
            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++) if (a[i] == b[i]) matches++;
            return (double)matches / Math.Max(a.Length, b.Length);
        }

        private string GenerateCommandName(CommandDefinition cmd)
        {
            string tpl = cmd.CommandTemplate.Trim();
            var parts = tpl.Split(new[] { ':', ' ', '?', '{', '}' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return $"{parts[0]}_{parts[1]}".Replace("*", "IDN");
            if (parts.Length == 1) return parts[0].Replace("*", "IDN");
            return "CMD";
        }

        private string CleanTerminator(string text, string terminator)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.EndsWith(terminator)) return text.Substring(0, text.Length - terminator.Length);
            return text.Trim();
        }

        private byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            if (hex.Length % 2 != 0) return Array.Empty<byte>();
            byte[] raw = new byte[hex.Length / 2];
            for (int i = 0; i < raw.Length; i++) raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return raw;
        }

        private string BytesToHex(byte[] data) => BitConverter.ToString(data).Replace("-", " ");

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProtocolRecorder));
        }

        #endregion

        #region Internal Types

        private sealed class LogEvent
        {
            public long Timestamp { get; set; }
            public string Direction { get; set; } = string.Empty;
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public string Text { get; set; } = string.Empty;
            public string SessionId { get; set; } = string.Empty;
        }

        private sealed class IntermediateCommand
        {
            public byte[] RawRequest { get; set; } = Array.Empty<byte>();
            public string RequestText { get; set; } = string.Empty;
            public byte[] RawResponse { get; set; } = Array.Empty<byte>();
            public string ResponseText { get; set; } = string.Empty;
            public CommandDefinition Definition { get; set; } = new CommandDefinition();
        }

        private sealed class DiffResult
        {
            public int StartIndex { get; set; }
            public int Length { get; set; }
            public List<string> DistinctValues { get; set; } = new List<string>();
            public bool IsEnumCandidate { get; set; }
        }

        #endregion
    }
}
