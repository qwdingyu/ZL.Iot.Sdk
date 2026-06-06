using System;
using System.Collections.Generic;
using System.Linq;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 路由规则 — 从已删除的 MessagePipeline 迁移至此，避免重复定义。
    /// </summary>
    public class RouteRule
    {
        public string Name { get; set; }
        public Func<Message, bool> Condition { get; set; }
        public List<string> OutputNames { get; set; } = new List<string>();
        public bool ContinueMatching { get; set; } = true;
        public int Priority { get; set; } = 100;

        public bool Matches(Message message)
        {
            return Condition?.Invoke(message) ?? false;
        }
    }

    /// <summary>
    /// 消息路由器 — 根据路由规则将消息匹配到目标输出插件集合。
    /// 支持按优先级排序、多规则匹配、短路匹配。
    /// 使用 volatile 快照模式：Route() 读路径无锁，写操作（Add/Remove/Update/Clear）通过写锁发布新快照。
    /// </summary>
    public class MessageRouter
    {
        // volatile 快照：Route() 直接读取，无需 lock，消除高频读路径的 contention
        private volatile RouteRule[] _rulesSnapshot = Array.Empty<RouteRule>();
        private readonly object _writeLock = new();

        /// <summary>
        /// 添加路由规则，自动按 Priority 排序。
        /// 写操作通过 _writeLock 保护，完成后发布新快照。
        /// </summary>
        public void AddRule(RouteRule rule)
        {
            lock (_writeLock)
            {
                var list = new List<RouteRule>(_rulesSnapshot);
                // 仅对已命名规则执行同名去重；匿名规则（Name 为 null/空）允许共存
                if (!string.IsNullOrEmpty(rule.Name))
                    list.RemoveAll(r => r.Name == rule.Name);
                list.Add(rule);
                list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                _rulesSnapshot = list.ToArray();
            }
        }

        /// <summary>
        /// 根据路由规则匹配消息，返回命中的输出插件名称集合。
        /// 无锁读路径：直接遍历 volatile 快照数组。
        /// </summary>
        public HashSet<string> Route(Message message)
        {
            var matchedOutputs = new HashSet<string>();
            var rules = _rulesSnapshot; // volatile read

            foreach (var rule in rules)
            {
                if (rule.Matches(message))
                {
                    foreach (var outputName in rule.OutputNames)
                    {
                        matchedOutputs.Add(outputName);
                    }

                    if (!rule.ContinueMatching) break;
                }
            }

            return matchedOutputs;
        }

        /// <summary>
        /// 获取当前规则数量（只读）。
        /// </summary>
        public int RuleCount => _rulesSnapshot.Length;

        /// <summary>
        /// 按 Name 删除路由规则，返回是否找到并删除。
        /// </summary>
        public bool RemoveRule(string name)
        {
            lock (_writeLock)
            {
                var list = new List<RouteRule>(_rulesSnapshot);
                var rule = list.FirstOrDefault(r => r.Name == name);
                if (rule == null) return false;
                list.Remove(rule);
                _rulesSnapshot = list.ToArray();
                return true;
            }
        }

        /// <summary>
        /// 按 Name 查找并替换规则（复制 updated 的所有属性到现有规则），返回是否找到。
        /// </summary>
        public bool UpdateRule(RouteRule updated)
        {
            lock (_writeLock)
            {
                var list = new List<RouteRule>(_rulesSnapshot);
                var existing = list.FirstOrDefault(r => r.Name == updated.Name);
                if (existing == null) return false;

                existing.Condition = updated.Condition;
                existing.OutputNames.Clear();
                existing.OutputNames.AddRange(updated.OutputNames);
                existing.ContinueMatching = updated.ContinueMatching;
                existing.Priority = updated.Priority;

                list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                _rulesSnapshot = list.ToArray();
                return true;
            }
        }

        /// <summary>
        /// 返回规则快照（无锁：返回 volatile 数组的防御性副本，调用方无法篡改内部状态）。
        /// </summary>
        public IReadOnlyList<RouteRule> GetRules()
        {
            return (IReadOnlyList<RouteRule>)new List<RouteRule>(_rulesSnapshot);
        }

        /// <summary>
        /// 清空所有规则。
        /// </summary>
        public void ClearRules()
        {
            lock (_writeLock)
            {
                _rulesSnapshot = Array.Empty<RouteRule>();
            }
        }
    }
}
