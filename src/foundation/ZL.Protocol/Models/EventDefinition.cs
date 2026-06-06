namespace ZL.Protocol.Models
{
    /// <summary>
    /// 事件定义
    /// </summary>
    public sealed class EventDefinition
    {
        /// <summary>事件 ID</summary>
        public string EventId { get; set; } = string.Empty;

        /// <summary>触发条件（如 "temperature > 100"）</summary>
        public string Trigger { get; set; } = string.Empty;

        /// <summary>事件模板（如 "ALERT: {temperature}"）</summary>
        public string Template { get; set; } = string.Empty;

        /// <summary>触发间隔（毫秒）</summary>
        public int IntervalMs { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;
    }
}
