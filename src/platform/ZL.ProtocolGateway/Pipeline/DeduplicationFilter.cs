// ============================================================
// 文件：DeduplicationFilter.cs
// 描述：消息去重过滤器 — 基于滑动窗口防止重复消息
// 功能：在 Pipeline Filter 阶段拦截重复消息，避免下游系统处理重复数据
//       使用 SHA256 指纹 + 时间窗口实现，支持自动过期清理
// 修改日期：2026-06-07
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 消息去重过滤器 — 基于滑动窗口防止重复消息。
    /// 在 Pipeline 中注册为 Filter，拦截重复的 MQTT 重传、HTTP 重试、Bridge 重复事件等。
    /// </summary>
    public class DeduplicationFilter : IDisposable
    {
        /// <summary>
        /// 滑动窗口：最近窗口时间内见过的消息指纹。
        /// </summary>
        private readonly ConcurrentDictionary<string, DateTime> _seenMessages = new();
        private readonly TimeSpan _window;
        private Timer? _cleanupTimer;

        /// <summary>
        /// 创建去重过滤器。
        /// </summary>
        /// <param name="window">去重时间窗口，默认 5 分钟</param>
        /// <param name="cleanupInterval">过期条目清理间隔，默认 1 分钟</param>
        public DeduplicationFilter(TimeSpan? window = null, TimeSpan? cleanupInterval = null)
        {
            _window = window ?? TimeSpan.FromMinutes(5);
            var cleanup = cleanupInterval ?? TimeSpan.FromMinutes(1);

            _cleanupTimer = new Timer(CleanupExpired, null, cleanup, cleanup);
        }

        /// <summary>
        /// 作为 Pipeline Filter 使用：返回 true 表示通过（非重复），false 表示拦截（重复）。
        /// </summary>
        public async Task<bool> FilterAsync(Message msg)
        {
            var key = ComputeDedupKey(msg);
            if (_seenMessages.TryGetValue(key, out _))
            {
                return false; // 重复消息，过滤
            }
            _seenMessages.TryAdd(key, DateTime.UtcNow);
            return true;
        }

        /// <summary>
        /// 当前窗口内的去重条目数量。
        /// </summary>
        public int SeenCount => _seenMessages.Count;

        /// <summary>
        /// 清除所有去重记录（用于测试或手动重置）。
        /// </summary>
        public void Reset()
        {
            _seenMessages.Clear();
        }

        private string ComputeDedupKey(Message msg)
        {
            // 策略：Topic + Payload 前 64 字节哈希 + 1 秒时间窗口
            // 这样同一秒内的相同 Topic+Payload 被视为重复
            var hashInput = msg.Topic + "|" +
                (msg.Payload?.Length > 64
                    ? Convert.ToBase64String(msg.Payload, 0, 64)
                    : Convert.ToBase64String(msg.Payload ?? Array.Empty<byte>())) + "|" +
                msg.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss");

            return Convert.ToBase64String(
                    SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(hashInput)))
                .AsSpan(0, 16).ToString();
        }

        private void CleanupExpired(object? state)
        {
            var cutoff = DateTime.UtcNow - _window;
            foreach (var kv in _seenMessages)
            {
                if (kv.Value < cutoff)
                {
                    _seenMessages.TryRemove(kv.Key, out _);
                }
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}
