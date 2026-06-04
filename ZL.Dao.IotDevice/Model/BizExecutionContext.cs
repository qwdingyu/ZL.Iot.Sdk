using System;
using System.Collections.Generic;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// IoT 模板执行上下文。
    /// 用于把触发来源、设备事件、模板版本、快照版本、操作人和 traceId
    /// 统一带入 BizCfgExe 审计日志，支撑定位、回放和失败补偿。
    /// </summary>
    public class BizExecutionContext
    {
        public string TriggerSource { get; set; }
        public string SourceEventId { get; set; }
        public string DeviceId { get; set; }
        public string TagId { get; set; }
        public string SnapshotVersion { get; set; }
        public string TemplateVersion { get; set; }
        public string OperatorId { get; set; }
        public string OperatorName { get; set; }
        public string TraceId { get; set; }
        public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>();

        public static BizExecutionContext Create(string triggerSource, string deviceId, string tagId, string traceId = null)
        {
            return new BizExecutionContext
            {
                TriggerSource = triggerSource,
                DeviceId = deviceId,
                TagId = tagId,
                TraceId = string.IsNullOrWhiteSpace(traceId) ? Guid.NewGuid().ToString("N") : traceId
            };
        }
    }
}
