using SqlSugar;
using System;
using ZL.Dao.IotDevice;

namespace ZL.Model
{
    /// <summary>
    /// 设备运行态表 SqlSugar 模型
    /// 
    /// 设计原则：
    /// - 主键为 device_id，每个设备只有一条运行态记录
    /// - 所有时间字段在数据库层可空（NULL 表示未触发过该事件）
    /// - 连接状态变化通过 PlcNotificationHub 事件驱动更新，非定时轮询
    /// - updated_at 在 Insert/Update 时由 DataExecuting AOP 自动赋值
    /// 
    /// SqlSugar CodeFirst 说明：
    /// - 表名默认从类名推断（iot_device_runtime），无需显式指定
    /// - 索引通过 [SugarColumn(IndexGroupNameList)] 在真实字段上声明
    /// - bool/int 类型使用 ColumnDataType 确保 SQL Server 创建为 NOT NULL 带 DEFAULT
    /// 
    /// 与业务表 iot_device 的关系：
    /// - iot_device：设备定义信息（名称/IP/驱动类型等静态配置）
    /// - iot_device_runtime：运行时状态（在线状态、采集计数、异常信息等动态数据）
    /// - 两表通过 device_id 关联，查询时使用 JOIN 而非复合大表
    /// </summary>
    public partial class iot_device_runtime : BaseClass, IAuditable
    {
        public iot_device_runtime() { }

        /// <summary>
        /// 设备ID（对应 iot_device.id），作为主键
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, Length = 64, IsNullable = false)]
        public string device_id { get; set; }

        /// <summary>
        /// 是否在线（Ping + 连接均正常时为 true）
        /// </summary>
        [SugarColumn(IsNullable = false, ColumnDataType = "BIT", DefaultValue = "0")]
        public bool is_online { get; set; }

        /// <summary>
        /// 连接是否建立（物理连接已建立，不等于业务在线）
        /// </summary>
        [SugarColumn(IsNullable = false, ColumnDataType = "BIT", DefaultValue = "0")]
        public bool is_connected { get; set; }

        /// <summary>
        /// Ping 是否成功（网络可达）
        /// </summary>
        [SugarColumn(IsNullable = false, ColumnDataType = "BIT", DefaultValue = "0")]
        public bool is_ping_ok { get; set; }

        /// <summary>
        /// 最后连接成功时间（首次连接成功时记录，后续连接恢复时更新）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? last_connect_time { get; set; }

        /// <summary>
        /// 最后 Ping 时间（每次 Ping 操作后更新）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? last_ping_time { get; set; }

        /// <summary>
        /// 最后采集成功时间（每次成功采集一轮标签后更新）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? last_collect_time { get; set; }

        /// <summary>
        /// 最后采集异常时间（发生异常时记录，正常后清空）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? last_error_time { get; set; }

        /// <summary>
        /// 最后异常消息（截断到 1000 字符，null 表示无异常）
        /// </summary>
        [SugarColumn(Length = 1000, IsNullable = true)]
        public string last_error_msg { get; set; }

        /// <summary>
        /// 连续失败次数（超过阈值触发告警，正常采集后重置为 0）
        /// </summary>
        [SugarColumn(IsNullable = false, ColumnDataType = "INT", DefaultValue = "0")]
        public int fail_count { get; set; }

        /// <summary>
        /// 运行时状态描述（Running / Degraded / Quarantined / Offline / Connecting）
        /// 由 IotDeviceRuntimeService.UpdateConnectionStatus() 在状态变更时更新
        /// </summary>
        [SugarColumn(Length = 32, IsNullable = true)]
        public string runtime_status { get; set; }

        /// <summary>
        /// 快照更新时间（服务端写入时间）
        /// Insert 和 Update 时由 DataExecuting AOP 自动赋值为 DateTime.Now
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime updated_at { get; set; }

        /// <summary>
        /// 公司ID（便于按组织查询设备运行态）
        /// 索引：支持按公司ID快速筛选设备
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true,
            IndexGroupNameList = new[] { "idx_device_runtime_company" })]
        public string company_id { get; set; }
    }
}
