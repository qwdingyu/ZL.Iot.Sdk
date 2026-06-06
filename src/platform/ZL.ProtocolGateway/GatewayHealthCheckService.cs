using System;
using System.Collections.Generic;
using System.Linq;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 网关健康状态 — 用于 K8s Liveness/Readiness Probe 和运维监控
    /// <para>Healthy: 所有输出插件正常运行</para>
    /// <para>Degraded: 部分输出插件降级/重连中，但至少有一个 Healthy</para>
    /// <para>Unhealthy: 所有输出插件错误或无插件运行</para>
    /// </summary>
    public enum GatewayHealthStatus
    {
        /// <summary>所有输出插件正常运行</summary>
        Healthy,
        /// <summary>部分输出插件降级/重连中，网关仍在工作</summary>
        Degraded,
        /// <summary>所有输出插件错误或无插件运行，网关不可用</summary>
        Unhealthy
    }

    /// <summary>
    /// 单个输出插件的健康检查结果
    /// </summary>
    public record OutputPluginHealthResult(
        string PluginName,
        string ProtocolType,
        PluginStatus Status,
        OutputPluginHealthLevel HealthLevel,
        string Message,
        string ErrorCode,
        string Advice,
        Exception? LastException,
        int ConsecutiveFailures,
        DateTime Timestamp
    );

    /// <summary>
    /// 网关聚合健康检查结果 — 包含整体状态和各插件详情
    /// </summary>
    public record GatewayHealthResult(
        GatewayHealthStatus Status,
        string Description,
        int TotalPlugins,
        int HealthyCount,
        int DegradedCount,
        int UnhealthyCount,
        IReadOnlyList<OutputPluginHealthResult> PluginResults,
        DateTime Timestamp
    )
    {
        /// <summary>
        /// 快速判断是否就绪接收流量（至少有一个 Healthy 输出插件）
        /// </summary>
        public bool IsReady => Status != GatewayHealthStatus.Unhealthy;

        /// <summary>
        /// 快速判断进程是否存活（网关服务本身未崩溃）
        /// </summary>
        public bool IsLive => Status != GatewayHealthStatus.Unhealthy || TotalPlugins == 0;
    }

    /// <summary>
    /// 健康检查服务 — 聚合所有输出插件的健康状态为网关整体健康状态
    /// <para>用法: var result = healthCheck.Check();</para>
    /// <para>K8s Liveness: result.IsLive → 进程存活</para>
    /// <para>K8s Readiness: result.IsReady → 就绪接收流量</para>
    /// </summary>
    public class GatewayHealthCheckService
    {
        private readonly GatewayManager _manager;

        public GatewayHealthCheckService(GatewayManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>
        /// 执行健康检查 — 聚合所有注册输出插件的健康状态
        /// </summary>
        public GatewayHealthResult Check()
        {
            var pluginStatuses = _manager.GetOutputPluginStatuses();
            var now = DateTime.UtcNow;

            if (pluginStatuses.Count == 0)
            {
                return new GatewayHealthResult(
                    GatewayHealthStatus.Unhealthy,
                    "No output plugins registered",
                    0, 0, 0, 0,
                    Array.Empty<OutputPluginHealthResult>(),
                    now
                );
            }

            var results = pluginStatuses.Select(ToHealthResult).ToList();

            var healthyCount = results.Count(r => r.HealthLevel == OutputPluginHealthLevel.Healthy);
            var degradedCount = results.Count(r => r.HealthLevel == OutputPluginHealthLevel.Warning || r.HealthLevel == OutputPluginHealthLevel.Degraded);
            var unhealthyCount = results.Count(r => r.HealthLevel == OutputPluginHealthLevel.Error || r.HealthLevel == OutputPluginHealthLevel.Fatal);

            // 聚合策略：All Healthy → Healthy; Any Unhealthy + No Healthy → Unhealthy; else Degraded
            var overallStatus = DetermineOverallStatus(healthyCount, degradedCount, unhealthyCount, results.Count);
            var description = BuildDescription(overallStatus, healthyCount, degradedCount, unhealthyCount, results.Count);

            return new GatewayHealthResult(
                overallStatus,
                description,
                results.Count,
                healthyCount,
                degradedCount,
                unhealthyCount,
                results,
                now
            );
        }

        private static GatewayHealthStatus DetermineOverallStatus(int healthy, int degraded, int unhealthy, int total)
        {
            if (healthy == total) return GatewayHealthStatus.Healthy;
            if (healthy == 0 && unhealthy > 0) return GatewayHealthStatus.Unhealthy;
            return GatewayHealthStatus.Degraded;
        }

        private static string BuildDescription(GatewayHealthStatus status, int healthy, int degraded, int unhealthy, int total)
        {
            return status switch
            {
                GatewayHealthStatus.Healthy => $"All {total} output plugins healthy",
                GatewayHealthStatus.Degraded => $"{healthy} healthy, {degraded} degraded, {unhealthy} unhealthy out of {total} plugins",
                GatewayHealthStatus.Unhealthy => $"All {total} output plugins unhealthy (0 healthy)",
                _ => $"Unknown status: {total} plugins"
            };
        }

        private static OutputPluginHealthResult ToHealthResult(OutputPluginStatus status)
        {
            return new OutputPluginHealthResult(
                status.Name,
                status.ProtocolType,
                status.StatusEnum,
                status.HealthLevel,
                status.Message ?? string.Empty,
                status.ErrorCode ?? GatewayErrorCodes.None,
                status.Advice ?? string.Empty,
                status.LastException,
                status.ConsecutiveFailures,
                status.Timestamp ?? DateTime.UtcNow
            );
        }
    }
}