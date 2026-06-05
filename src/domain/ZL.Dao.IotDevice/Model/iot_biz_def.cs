using SqlSugar;
using System;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 业务模式定义表
    /// 
    /// 与 iot_biz_detail 配合形成"业务模板头 + 步骤明细"的两层结构。
    /// biz_code 是业务模式的唯一标识，exe_type 表示该模式的执行类型（Get/Set 等）。
    /// 
    /// SqlSugar 最佳实践：
    /// - 继承 BaseClass 并实现 IAuditable 接口，由 DataExecuting AOP 自动维护 created_at/updated_at
    /// - id 使用 36 位 GUID 主键（非自增）
    /// - biz_code 上的唯一索引应在数据库层面建立
    /// </summary>
    public partial class iot_biz_def : BaseClass, IAuditable
    {
        public iot_biz_def()
        {
        }
        /// <summary>
        /// 主键（GUID）
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, Length = 36, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// 业务模式代码（唯一标识）
        /// </summary>           
        [SugarColumn(Length = 64, IsNullable = false)]
        public string biz_code { get; set; }

        /// <summary>
        /// 业务模式名称
        /// </summary>           
        [SugarColumn(Length = 64, IsNullable = true)]
        public string biz_name { get; set; }

        /// <summary>
        /// 执行类型（Get/Set）
        /// </summary>           
        [SugarColumn(Length = 6, IsNullable = false)]
        public string exe_type { get; set; }

        /// <summary>
        /// 是否启用（0未启用，1启用）
        /// </summary>           
        [SugarColumn(DefaultValue = "0")]
        public int enable { get; set; }

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
