using System;
using System.Collections.Generic;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 消息意图 — 明确声明 Message 在 Pipeline 中的语义角色，消除 OutputPlugin 的暗契约判断。
    /// </summary>
    public enum MessageIntent
    {
        /// <summary>
        /// 通用协议转发 — Payload 携带原始数据（默认意图，Input→Pipeline 路径）
        /// </summary>
        Forward,

        /// <summary>
        /// PLC 标签写入 — Writes 列表携带结构化 TagWrite（Bridge→Pipeline 路径）
        /// </summary>
        TagWrite,

        /// <summary>
        /// PLC 标签读取请求 — Writes 为空，OutputPlugin 应执行读操作并返回结果
        /// </summary>
        TagRead,

        /// <summary>
        /// 脚本触发 — 用于 LuaScriptOutputPlugin 等脚本引擎的触发消息
        /// </summary>
        ScriptTrigger
    }

    /// <summary>
    /// 协议无关的标签写入请求 — 作为 Bridge 和 OutputPlugin 之间的公共契约。
    /// 避免了各插件隐式定义不同文本格式的"暗契约"问题。
    /// </summary>
    public record TagWrite(
        string Address,      // 目标地址，如 "DB1.DBW0" 或 "40001" 或 "ns=2;s=MyVar"
        object Value,        // 强类型值：bool, short, int, float, double 等
        string DataType,     // "BOOL", "INT16", "UINT16", "INT32", "UINT32", "FLOAT", "DOUBLE", "BYTE", "STRING"
        string? Alias,       // 用户友好名称（可选）
        DateTime Timestamp   // 值变更时间
    );

    /// <summary>
    /// 标签写入操作的结果反馈 — 由 OutputPlugin 在执行写入后填充。
    /// 使 PlcSimulator UI 能够获知每条写入操作的成功/失败状态。
    /// </summary>
    public class TagWriteResult
    {
        public string Address { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Latency { get; set; }
    }

    /// <summary>
    /// 统一消息格式 - 协议转发的核心数据结构
    /// 支持在 Serial/MQTT/TCP/HTTP 等协议间转换
    /// </summary>
    public class Message
    {
        /// <summary>
        /// 消息唯一 ID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 主题/路由键（用于消息路由）
        /// 例如：MQTT Topic, HTTP Path, Serial Port 名称
        /// </summary>
        public string Topic { get; set; }

        /// <summary>
        /// 消息意图 — 明确声明 Message 在 Pipeline 中的语义角色。
        /// OutputPlugin 根据此属性决定处理策略（转发 Payload 或执行 TagWrite）。
        /// 默认 Forward（Input→Pipeline 路径）；Bridge 注入时设为 TagWrite。
        /// </summary>
        public MessageIntent Intent { get; set; } = MessageIntent.Forward;

        /// <summary>
        /// 原始数据载荷
        /// </summary>
        public byte[] Payload { get; internal set; }

        /// <summary>
        /// 内容类型：json, text, hex, binary
        /// </summary>
        public string ContentType { get; set; } = "binary";

        /// <summary>
        /// 消息创建时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 元数据（协议相关的附加信息）
        /// 例如：源协议类型、设备 ID、量具编号等
        /// </summary>
        public Dictionary<string, string> Metadata { get; internal set; } = new Dictionary<string, string>();

        /// <summary>
        /// 标签写入列表 — 协议无关的中间表示。
        /// 当此列表非空时，OutputPlugin 应优先使用此结构化数据而非解析 Payload 文本。
        /// 这解决了 Bridge→OutputPlugin 消息格式不匹配的"暗契约"问题。
        /// </summary>
        public List<TagWrite> Writes { get; set; } = new List<TagWrite>();

        /// <summary>
        /// 写入操作结果列表 — 由 OutputPlugin 在执行写入后填充。
        /// 使 PlcSimulator UI 能够获知每条写入操作的成功/失败状态。
        /// </summary>
        public List<TagWriteResult> WriteResults { get; set; } = new List<TagWriteResult>();

        /// <summary>
        /// 链路追踪 ID（便捷属性，实际存储在 Metadata["TraceId"] 中）
        /// </summary>
        public string TraceId
        {
            get => Metadata.TryGetValue(GatewayMetadataKeys.TraceId, out var v) ? v : string.Empty;
            set => Metadata[GatewayMetadataKeys.TraceId] = value ?? string.Empty;
        }

        /// <summary>
        /// 获取 JSON 格式的 Payload
        /// </summary>
        public string GetJsonContent()
        {
            if (Payload == null || Payload.Length == 0) return null;
            if (ContentType != "json")
                throw new InvalidOperationException($"Content type is '{ContentType}', expected 'json'");
            return System.Text.Encoding.UTF8.GetString(Payload);
        }

        /// <summary>
        /// 获取文本格式的 Payload
        /// </summary>
        public string GetTextContent()
        {
            if (Payload == null || Payload.Length == 0) return null;
            return System.Text.Encoding.UTF8.GetString(Payload);
        }

        /// <summary>
        /// 获取十六进制格式的 Payload
        /// </summary>
        public string GetHexContent()
        {
            if (Payload == null || Payload.Length == 0) return null;
            return BitConverter.ToString(Payload).Replace("-", "");
        }

        /// <summary>
        /// 直接设置原始字节载荷（供 Input 插件使用）。
        /// </summary>
        public void SetPayload(byte[] payload)
        {
            Payload = payload;
        }

        /// <summary>
        /// 设置 JSON 内容
        /// </summary>
        public void SetJsonContent(string json)
        {
            Payload = System.Text.Encoding.UTF8.GetBytes(json);
            ContentType = "json";
        }

        /// <summary>
        /// 设置文本内容
        /// </summary>
        public void SetTextContent(string text)
        {
            Payload = System.Text.Encoding.UTF8.GetBytes(text);
            ContentType = "text";
        }

        /// <summary>
        /// 设置十六进制内容
        /// </summary>
        public void SetHexContent(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            if (hex.Length % 2 != 0)
                throw new ArgumentException("Hex string must have even length");

            Payload = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                Payload[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            ContentType = "hex";
        }

        /// <summary>
        /// 创建消息副本
        /// </summary>
        public Message Clone()
        {
            return new Message
            {
                Id = this.Id,
                Topic = this.Topic,
                Intent = this.Intent,
                Payload = this.Payload != null ? (byte[])this.Payload.Clone() : null,
                ContentType = this.ContentType,
                Timestamp = this.Timestamp,
                Metadata = new Dictionary<string, string>(this.Metadata),
                Writes = this.Writes != null ? new List<TagWrite>(this.Writes) : new List<TagWrite>(),
                WriteResults = this.WriteResults != null ? new List<TagWriteResult>(this.WriteResults) : new List<TagWriteResult>()
            };
        }

        public override string ToString()
        {
            return $"Message[Id={Id}, Topic={Topic}, Intent={Intent}, Payload={Payload?.Length ?? 0} bytes, Type={ContentType}]";
        }
    }
}
