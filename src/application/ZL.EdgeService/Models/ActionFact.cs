using System;
using System.Collections.Generic;

namespace ZL.EdgeService.Models
{
    /// <summary>
    /// 动作事实模型
    /// <para>用于离线回放的标准化动作记录，替代单纯的 SQL 文本存储</para>
    /// </summary>
    /// <remarks>
    /// 设计说明：
    /// - 作为"动作事实回放协议"的核心数据结构
    /// - 支持 TraceId + ActionId + GatewayId + Version 幂等对账
    /// - 可序列化为 JSON 存储到 edge_offline_commands 表的 payload_json 字段
    ///
    /// 融合点 D（离线镜像与回放融合）的实现：
    /// - 定义标准化的动作事实结构
    /// - 支持云端对账和回放
    /// </remarks>
    public class ActionFact
    {
        /// <summary>
        /// 动作唯一标识（用于幂等）
        /// </summary>
        public string ActionId { get; set; }

        /// <summary>
        /// 追踪ID（关联到触发源）
        /// </summary>
        public string TraceId { get; set; }

        /// <summary>
        /// 网关ID（边缘节点标识）
        /// </summary>
        public string GatewayId { get; set; }

        /// <summary>
        /// 动作类型（InsertRow, UpdateRow, WriteTag, PublishEvent 等）
        /// </summary>
        public string ActionType { get; set; }

        /// <summary>
        /// 动作键（用于幂等判断，如 "order_12345_status"）
        /// </summary>
        public string ActionKey { get; set; }

        /// <summary>
        /// 动作负载数据（JSON 格式）
        /// </summary>
        public string PayloadJson { get; set; }

        /// <summary>
        /// 动作发生时间（ISO 8601 格式）
        /// </summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>
        /// 配置版本号（用于版本对账）
        /// </summary>
        public string ConfigVersion { get; set; }

        /// <summary>
        /// 业务编码（关联到 iot_exe.biz_code）
        /// </summary>
        public string BizCode { get; set; }

        /// <summary>
        /// 触发源标签ID
        /// </summary>
        public string TriggerTagId { get; set; }

        /// <summary>
        /// 工站编号
        /// </summary>
        public string StationNo { get; set; }

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// 扩展元数据（用于存储额外信息）
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// 动作类型常量定义
    /// </summary>
    public static class ActionTypes
    {
        /// <summary>
        /// 插入行动作
        /// </summary>
        public const string InsertRow = "InsertRow";

        /// <summary>
        /// 更新行动作
        /// </summary>
        public const string UpdateRow = "UpdateRow";

        /// <summary>
        /// 写标签动作
        /// </summary>
        public const string WriteTag = "WriteTag";

        /// <summary>
        /// 发布事件动作
        /// </summary>
        public const string PublishEvent = "PublishEvent";

        /// <summary>
        /// 调用存储过程动作
        /// </summary>
        public const string StoredProcedure = "StoredProcedure";

        /// <summary>
        /// HTTP 回调动作
        /// </summary>
        public const string HttpCallback = "HttpCallback";

        /// <summary>
        /// MQTT 发布动作
        /// </summary>
        public const string MqttPublish = "MqttPublish";
    }

    /// <summary>
    /// 动作事实构建器（流式 API）
    /// </summary>
    public class ActionFactBuilder
    {
        private readonly ActionFact _fact = new();

        public ActionFactBuilder()
        {
            _fact.ActionId = Guid.NewGuid().ToString("N");
            _fact.OccurredAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 设置追踪ID
        /// </summary>
        public ActionFactBuilder WithTraceId(string traceId)
        {
            _fact.TraceId = traceId;
            return this;
        }

        /// <summary>
        /// 设置网关ID
        /// </summary>
        public ActionFactBuilder WithGatewayId(string gatewayId)
        {
            _fact.GatewayId = gatewayId;
            return this;
        }

        /// <summary>
        /// 设置动作类型
        /// </summary>
        public ActionFactBuilder WithActionType(string actionType)
        {
            _fact.ActionType = actionType;
            return this;
        }

        /// <summary>
        /// 设置动作键（用于幂等判断）
        /// </summary>
        public ActionFactBuilder WithActionKey(string actionKey)
        {
            _fact.ActionKey = actionKey;
            return this;
        }

        /// <summary>
        /// 设置负载数据
        /// </summary>
        public ActionFactBuilder WithPayload(object payload)
        {
            _fact.PayloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            return this;
        }

        /// <summary>
        /// 设置负载数据（JSON 字符串）
        /// </summary>
        public ActionFactBuilder WithPayloadJson(string payloadJson)
        {
            _fact.PayloadJson = payloadJson;
            return this;
        }

        /// <summary>
        /// 设置配置版本
        /// </summary>
        public ActionFactBuilder WithConfigVersion(string version)
        {
            _fact.ConfigVersion = version;
            return this;
        }

        /// <summary>
        /// 设置业务编码
        /// </summary>
        public ActionFactBuilder WithBizCode(string bizCode)
        {
            _fact.BizCode = bizCode;
            return this;
        }

        /// <summary>
        /// 设置触发源标签
        /// </summary>
        public ActionFactBuilder WithTriggerTag(string tagId)
        {
            _fact.TriggerTagId = tagId;
            return this;
        }

        /// <summary>
        /// 设置工站编号
        /// </summary>
        public ActionFactBuilder WithStationNo(string stationNo)
        {
            _fact.StationNo = stationNo;
            return this;
        }

        /// <summary>
        /// 设置设备ID
        /// </summary>
        public ActionFactBuilder WithDeviceId(string deviceId)
        {
            _fact.DeviceId = deviceId;
            return this;
        }

        /// <summary>
        /// 添加元数据
        /// </summary>
        public ActionFactBuilder WithMetadata(string key, object value)
        {
            _fact.Metadata[key] = value;
            return this;
        }

        /// <summary>
        /// 构建动作事实对象
        /// </summary>
        public ActionFact Build()
        {
            return _fact;
        }
    }
}