using SqlSugar;
using System;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 标签关系表（tag_sub 字符串关系的显式替代）
    /// 
    /// 用于替代 iot_tag.tag_sub 字符串字段，将标签间的附属/依赖关系转为表驱动。
    /// 
    /// 设计背景：
    /// - iot_tag.tag_sub 是字符串字段，格式如 "tag1,tag2,tag3"，依赖解析逻辑
    /// - 本表将字符串关系显式化，支持高效查询、索引和关系维护
    /// - 旧字段 tag_sub 保留用于向后兼容，新写入统一走本表
    /// 
    /// 关系类型：
    /// - subscription：主标签触发时需要额外订阅的附属标签
    /// - writeback：写入主标签后需要回写的标签
    /// - validate：主标签值变化前需要校验的标签
    /// 
    /// SqlSugar 最佳实践：
    /// - 继承 BaseClass 并实现 IAuditable 接口，由 DataExecuting AOP 自动维护 created_at/updated_at
    /// - 使用复合主键（master_tag_id + slave_tag_id + relation_type）确保唯一性
    /// - master_tag_id 上建立索引支持按主标签快速查询所有关系
    /// - created_by/updated_by 字段添加 Length 属性以匹配数据库实际宽度
    /// </summary>
    public partial class iot_tag_relation : BaseClass, IAuditable
    {
        public iot_tag_relation()
        {
        }

        /// <summary>
        /// 主标签ID（触发源标签）
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, Length = 64, IsNullable = false,
            IndexGroupNameList = new[] { "idx_relation_master" })]
        public string master_tag_id { get; set; }

        /// <summary>
        /// 从属标签ID（被关联的标签）
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, Length = 64, IsNullable = false)]
        public string slave_tag_id { get; set; }

        /// <summary>
        /// 关系类型（subscription/writeback/validate）
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, Length = 16, IsNullable = false)]
        public string relation_type { get; set; }

        /// <summary>
        /// 是否启用（0未启用，1启用）
        /// </summary>
        [SugarColumn(DefaultValue = "1")]
        public int enable { get; set; } = 1;

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(Length = 255, IsNullable = true)]
        public string remark { get; set; }

        /// <summary>
        /// 创建时间（由 DataExecuting AOP 自动赋值）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? created_at { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string created_by { get; set; }

        /// <summary>
        /// 更新时间（由 DataExecuting AOP 自动赋值）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// 更新人
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string updated_by { get; set; }
    }
}