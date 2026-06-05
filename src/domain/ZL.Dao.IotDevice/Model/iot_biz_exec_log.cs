using SqlSugar;
using System;
using ZL.Dao.IotDevice;

namespace ZL.Model
{
    /// <summary>
    /// 业务配置执行审计日志表 SqlSugar 模型
    /// 
    /// 用途：记录 BizCfgExe 每次执行的轨迹，包括 tagId、bizCode、exe_order、
    /// 脚本快照、错误信息、耗时。用于问题追溯和性能分析。
    /// 
    /// 设计原则：
    /// - 主键为自增 ID（iot_biz_exec_log.id）
    /// - 执行结果通过 CheckValue 限制为 OK/FAIL/SKIP 三种合法值
    /// - script_snapshot/err_msg 长度限制在 2000 字符，防止注入和存储溢出
    /// - create_time 在 Insert 时由 DataExecuting AOP 自动赋值
    /// 
    /// SqlSugar CodeFirst 说明：
    /// - id 使用 IsIdentity = true 由数据库自动生成，无需应用侧设置
    /// - 索引通过 [SugarColumn(IndexGroupNameList)] 在字段上声明
    /// - ColumnDataType 对 result 字段指定为 NVARCHAR(16) 带 CHECK 约束
    /// </summary>
    public partial class iot_biz_exec_log : BaseClass, IAuditable
    {
        public iot_biz_exec_log() { }

        /// <summary>
        /// 自增主键（由数据库自动生成，Insert 时无需赋值）
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }

        /// <summary>
        /// 触发执行的标签ID（iot_tag.id）
        /// 索引：支持按标签追溯执行历史
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = false,
            IndexGroupNameList = new[] { "idx_biz_exec_tag" })]
        public string tag_id { get; set; }

        /// <summary>
        /// 业务模式代码，如 SET/GET/QTY/PLAN 等
        /// </summary>
        [SugarColumn(Length = 32, IsNullable = true)]
        public string biz_code { get; set; }

        /// <summary>
        /// 执行顺序号（iot_exe.exe_order）
        /// </summary>
        [SugarColumn(Length = 8, IsNullable = true)]
        public string exe_order { get; set; }

        /// <summary>
        /// 执行类型（S=查询/U=更新/B=批量）
        /// </summary>
        [SugarColumn(Length = 8, IsNullable = true)]
        public string exe_type { get; set; }

        /// <summary>
        /// 执行的脚本快照（脱敏后的 SQL 片段，最大 2000 字符）
        /// 截断逻辑在 IotBizExecLogService.TruncateSnapshot() 中统一处理
        /// </summary>
        [SugarColumn(Length = 2000, IsNullable = true)]
        public string script_snapshot { get; set; }

        /// <summary>
        /// 错误信息（执行失败时记录，最大 2000 字符）
        /// 正常执行时为 null，由 IotBizExecLogService.TruncateSnapshot() 统一截断
        /// </summary>
        [SugarColumn(Length = 2000, IsNullable = true)]
        public string err_msg { get; set; }

        /// <summary>
        /// 执行结果：OK（成功）/ FAIL（失败）/ SKIP（跳过）
        /// 通过 CheckValue 限制合法枚举值
        /// 索引：支持按结果状态快速筛选
        /// </summary>
        [SugarColumn(Length = 16, IsNullable = false,
            IndexGroupNameList = new[] { "idx_biz_exec_result" })]
        public string result { get; set; }

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public long? duration_ms { get; set; }

        /// <summary>
        /// 执行时间（Insert 时由 DataExecuting AOP 自动赋值为 DateTime.Now）
        /// 索引：支持按时间范围查询审计记录
        /// </summary>
        [SugarColumn(IsNullable = false,
            IndexGroupNameList = new[] { "idx_biz_exec_create" })]
        public DateTime create_time { get; set; }

        /// <summary>
        /// 设备ID（可选，便于按设备追溯执行记录）
        /// 索引：支持按设备查询执行历史
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true,
            IndexGroupNameList = new[] { "idx_biz_exec_device" })]
        public string device_id { get; set; }

        /// <summary>
        /// 事件/命令链路追踪 ID。
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true,
            IndexGroupNameList = new[] { "idx_biz_exec_trace" })]
        public string trace_id { get; set; }

        /// <summary>
        /// 触发来源：DeviceEvent / ManualReplay / TemplateAuto / Compensation 等。
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string trigger_source { get; set; }

        /// <summary>
        /// 来源事件 ID。
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string source_event_id { get; set; }

        /// <summary>
        /// 模板版本。
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string template_version { get; set; }

        /// <summary>
        /// 输入快照版本。
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string snapshot_version { get; set; }

        /// <summary>
        /// 操作人 ID。
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string operator_id { get; set; }

        /// <summary>
        /// 操作人名称。
        /// </summary>
        [SugarColumn(Length = 128, IsNullable = true)]
        public string operator_name { get; set; }

        /// <summary>
        /// 执行输入快照摘要。
        /// </summary>
        [SugarColumn(Length = 2000, IsNullable = true)]
        public string input_snapshot { get; set; }

        /// <summary>
        /// 公司ID（可选，便于按组织查询审计记录）
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string company_id { get; set; }
    }
}
