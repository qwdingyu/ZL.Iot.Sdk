using SqlSugar;
using System;
using ZL.Dao.IotDevice;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 标签运行态快照表 SqlSugar 模型
    /// 
    /// 设计原则：
    /// - 主键为 tag_id，每个标签只有一条当前快照记录（Upsert 语义）
    /// - Insert 和 Update 时 updated_at 由 DataExecuting AOP 自动赋值，无需业务代码手动设置
    /// - 使用 Upsert 语义（InsertOrUpdate），保证幂等性
    /// 
    /// 与业务表 iot_tag 的关系：
    /// - iot_tag：保留定义字段（address/data_type/group_id 等），不存储运行时值
    /// - iot_tag_snapshot：存储最新采集值（value/quality/collect_time），从 iot_tag.value 剥离
    /// - 旧系统 iot_tag.value 字段保留用于向后兼容，新写入统一走本表
    /// 
    /// SqlSugar CodeFirst 说明：
    /// - 表名默认从类名推断（iot_tag_snapshot），无需显式指定
    /// - 索引通过 [SugarColumn(IndexGroupNameList)] 在字段上声明
    /// - 所有可空字段在数据库层明确标记为 NULL，允许未采集时不赋值
    /// - device_id 上建立索引支持按设备批量查询标签快照
    /// - updated_at 上建立索引支持按时间范围清理历史快照
    /// </summary>
    public partial class iot_tag_snapshot : BaseClass, IAuditable
    {
        public iot_tag_snapshot() { }

        /// <summary>
        /// 标签ID（对应 iot_tag.id），作为主键
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, Length = 64, IsNullable = false)]
        public string tag_id { get; set; }

        /// <summary>
        /// 当前值（最新采集值）
        /// </summary>
        [SugarColumn(Length = 255, IsNullable = true)]
        public string value { get; set; }

        /// <summary>
        /// 质量码（参考 HSL 质量码体系：Good=0, Uncertain=1, Bad=2）
        /// </summary>
        [SugarColumn(Length = 8, IsNullable = true)]
        public string quality { get; set; }

        /// <summary>
        /// 采集时间戳（设备端原始采集时间）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? collect_time { get; set; }

        /// <summary>
        /// 快照更新时间（服务端写入时间）
        /// Insert 和 Update 时由 DataExecuting AOP 自动赋值为 DateTime.Now
        /// 索引：支持按时间范围清理历史快照
        /// </summary>
        [SugarColumn(IsNullable = false,
            IndexGroupNameList = new[] { "idx_tag_snapshot_updated" })]
        public DateTime updated_at { get; set; }

        /// <summary>
        /// 来源设备ID（便于按设备查询快照）
        /// 索引：支持按设备ID批量查询该设备下所有标签的快照
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true,
            IndexGroupNameList = new[] { "idx_tag_snapshot_device" })]
        public string device_id { get; set; }
    }
}
