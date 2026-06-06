using System;
using System.Collections.Generic;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 配置验证结果
    /// </summary>
    public record ConfigValidationError(string PropertyName, string ErrorMessage);

    /// <summary>
    /// 配置验证扩展 — 为工业插件配置类提供统一的验证方法
    /// <para>不依赖 System.ComponentModel.DataAnnotations，兼容 netstandard2.0</para>
    /// </summary>
    public static class ConfigValidation
    {
        /// <summary>
        /// 验证 IP 地址格式
        /// </summary>
        public static bool IsValidIpAddress(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            return System.Net.IPAddress.TryParse(ip, out _);
        }

        /// <summary>
        /// 验证端口号范围 (1-65535)
        /// </summary>
        public static bool IsValidPort(int port) => port >= 1 && port <= 65535;

        /// <summary>
        /// 验证超时范围 (>= minMs)
        /// </summary>
        public static bool IsValidTimeout(int timeoutMs, int minMs = 100) => timeoutMs >= minMs;

        /// <summary>
        /// 验证重连间隔 (>= minMs)
        /// </summary>
        public static bool IsValidReconnectInterval(int ms, int minMs = 500) => ms >= minMs;

        /// <summary>
        /// 验证错误阈值 (> 0)
        /// </summary>
        public static bool IsValidErrorThreshold(int threshold) => threshold > 0;

        /// <summary>
        /// 抛出配置验证异常 — 包含所有验证错误
        /// </summary>
        public static void ThrowIfInvalid(IReadOnlyList<ConfigValidationError> errors)
        {
            if (errors == null || errors.Count == 0) return;
            var messages = new System.Text.StringBuilder();
            foreach (var e in errors)
            {
                if (messages.Length > 0) messages.Append("; ");
                messages.Append($"[{e.PropertyName}] {e.ErrorMessage}");
            }
            throw new InvalidOperationException($"Configuration validation failed: {messages}");
        }
    }
}
