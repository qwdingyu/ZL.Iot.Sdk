using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ZL.Iot.Interface;
using ZL.EdgeService.Models;

namespace ZL.EdgeService.Services
{
    /// <summary>
    /// 动作事实记录服务接口
    /// <para>负责将动作事实持久化到本地 SQLite，供离线回放使用</para>
    /// </summary>
    /// <remarks>
    /// 融合点 D（离线镜像与回放融合）的实现：
    /// - 将动作事实记录为标准化 JSON，而非单纯 SQL 文本
    /// - 支持 TraceId + ActionId + GatewayId + Version 幂等对账
    /// - 为云端审计和回放提供标准化协议
    /// </remarks>
    public interface IActionFactRecorder
    {
        /// <summary>
        /// 记录动作事实到本地数据库
        /// </summary>
        Task<bool> RecordAsync(ActionFact fact);

        /// <summary>
        /// 使用构建器模式记录动作事实
        /// </summary>
        Task<bool> RecordAsync(ActionFactBuilder builder);
    }

    /// <summary>
    /// 动作事实记录服务实现
    /// </summary>
    public class ActionFactRecorder : IActionFactRecorder
    {
        private readonly ILogger<ActionFactRecorder> _logger;
        private readonly ISqlExecutor _sqlExecutor;

        public ActionFactRecorder(ILogger<ActionFactRecorder> logger, ISqlExecutor sqlExecutor)
        {
            _logger = logger;
            _sqlExecutor = sqlExecutor;
        }

        /// <summary>
        /// 记录动作事实到本地数据库
        /// </summary>
        public async Task<bool> RecordAsync(ActionFact fact)
        {
            if (fact == null)
            {
                _logger.LogWarning("Cannot record null ActionFact");
                return false;
            }

            try
            {
                string payloadJson = JsonConvert.SerializeObject(fact, Formatting.None);
                string idempotentKey = $"{fact.TraceId}_{fact.ActionId}_{fact.GatewayId}";

                string sql = @"
INSERT INTO edge_offline_commands 
(id, trace_id, action_id, gateway_id, biz_code, action_type, action_key, payload_json, cmd_type, payload, config_version, occurred_at, create_time, sync_status, retry_count)
VALUES 
(@id, @trace_id, @action_id, @gateway_id, @biz_code, @action_type, @action_key, @payload_json, @cmd_type, @payload, @config_version, @occurred_at, @create_time, 0, 0)";

                var parameters = new Dictionary<string, object>
                {
                    { "id", Guid.NewGuid().ToString("N") },
                    { "trace_id", fact.TraceId ?? "" },
                    { "action_id", fact.ActionId ?? "" },
                    { "gateway_id", fact.GatewayId ?? "" },
                    { "biz_code", fact.BizCode ?? "" },
                    { "action_type", fact.ActionType ?? "" },
                    { "action_key", fact.ActionKey ?? idempotentKey },
                    { "payload_json", payloadJson },
                    { "cmd_type", fact.ActionType ?? "" },
                    { "payload", payloadJson },
                    { "config_version", fact.ConfigVersion ?? "" },
                    { "occurred_at", fact.OccurredAt.ToString("O") },
                    { "create_time", DateTime.Now.ToString("O") }
                };

                await _sqlExecutor.ExecuteNonQueryAsync(sql, parameters);

                _logger.LogInformation("Recorded ActionFact: ActionId={ActionId}, Type={ActionType}, TraceId={TraceId}",
                    fact.ActionId, fact.ActionType, fact.TraceId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record ActionFact: ActionId={ActionId}", fact.ActionId);
                return false;
            }
        }

        public async Task<bool> RecordAsync(ActionFactBuilder builder)
        {
            if (builder == null) return false;
            return await RecordAsync(builder.Build());
        }
    }

    /// <summary>
    /// 动作事实记录器扩展方法
    /// </summary>
    public static class ActionFactRecorderExtensions
    {
        /// <summary>
        /// 快速记录写标签动作
        /// </summary>
        public static async Task<bool> RecordWriteTagAsync(
            this IActionFactRecorder recorder,
            string gatewayId,
            string tagId,
            object value,
            string traceId,
            string bizCode = null)
        {
            var builder = new ActionFactBuilder()
                .WithGatewayId(gatewayId)
                .WithActionType(ActionTypes.WriteTag)
                .WithActionKey($"WriteTag_{tagId}_{DateTime.UtcNow:yyyyMMddHHmmss}")
                .WithTriggerTag(tagId)
                .WithTraceId(traceId)
                .WithPayload(new { tagId, value, timestamp = DateTime.UtcNow });

            if (!string.IsNullOrEmpty(bizCode))
                builder.WithBizCode(bizCode);

            return await recorder.RecordAsync(builder);
        }

        /// <summary>
        /// 快速记录插入行动作
        /// </summary>
        public static async Task<bool> RecordInsertRowAsync(
            this IActionFactRecorder recorder,
            string gatewayId,
            string tableName,
            object rowData,
            string traceId,
            string bizCode = null)
        {
            var builder = new ActionFactBuilder()
                .WithGatewayId(gatewayId)
                .WithActionType(ActionTypes.InsertRow)
                .WithActionKey($"InsertRow_{tableName}_{DateTime.UtcNow:yyyyMMddHHmmss}")
                .WithTraceId(traceId)
                .WithPayload(new { tableName, rowData, timestamp = DateTime.UtcNow });

            if (!string.IsNullOrEmpty(bizCode))
                builder.WithBizCode(bizCode);

            return await recorder.RecordAsync(builder);
        }

        /// <summary>
        /// 快速记录更新行动作
        /// </summary>
        public static async Task<bool> RecordUpdateRowAsync(
            this IActionFactRecorder recorder,
            string gatewayId,
            string tableName,
            string primaryKey,
            object rowData,
            string traceId,
            string bizCode = null)
        {
            var builder = new ActionFactBuilder()
                .WithGatewayId(gatewayId)
                .WithActionType(ActionTypes.UpdateRow)
                .WithActionKey($"UpdateRow_{tableName}_{primaryKey}")
                .WithTraceId(traceId)
                .WithPayload(new { tableName, primaryKey, rowData, timestamp = DateTime.UtcNow });

            if (!string.IsNullOrEmpty(bizCode))
                builder.WithBizCode(bizCode);

            return await recorder.RecordAsync(builder);
        }
    }
}
