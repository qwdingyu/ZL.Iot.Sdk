using SqlSugar;
using System;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 业务模式明细步骤表
    /// 
    /// 与 iot_biz_def 配合形成"业务模板头 + 步骤明细"的两层结构。
    /// biz_code 关联到 iot_biz_def.biz_code，exe_order 表示执行顺序。
    /// 
    /// exe_type 说明：
    /// - Q: 更新SQL（Query/Update）
    /// - P: 存储过程（Procedure）
    /// - S: 查询SQL（Select）
    /// - F: 批量读取批量插入（FetchBulk）
    /// - B: 批量插入（BulkInsert）
    /// 
    /// SqlSugar 最佳实践：
    /// - 继承 BaseClass 并实现 IAuditable 接口，由 DataExecuting AOP 自动维护 created_at/updated_at
    /// - id 使用 36 位 GUID 主键（非自增）
    /// - script 字段长度为 -1 表示 TEXT/MEDIUMTEXT（适用于长 SQL 脚本）
    /// - created_by/updated_by 添加 Length 属性以匹配数据库实际宽度
    /// </summary>
    public partial class iot_biz_detail : BaseClass, IAuditable
    {
        public iot_biz_detail()
        {
        }
        /// <summary>
        /// 主键（GUID）
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, Length = 36, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// 业务模式代码（关联 iot_biz_def.biz_code）
        /// </summary>           
        [SugarColumn(Length = 64, IsNullable = false)]
        public string biz_code { get; set; }

        /// <summary>
        /// 执行次数（1单次，M多次）
        /// </summary>           
        [SugarColumn(Length = 6, IsNullable = false, DefaultValue = "1")]
        public string exe_num { get; set; }

        /// <summary>
        /// 执行序号
        /// </summary>           
        [SugarColumn(IsNullable = false)]
        public int exe_order { get; set; }

        /// <summary>
        /// 判断类型（Q:script; C:c#）
        /// </summary>           
        [SugarColumn(Length = 1, IsNullable = false)]
        public string judge_type { get; set; }

        /// <summary>
        /// 判断条件
        /// </summary>           
        [SugarColumn(Length = 128, IsNullable = false)]
        public string judge_exp { get; set; }

        /// <summary>
        /// sql类型（Q:更新SQL;P:存储过程;S:查询SQL;F:批量读取批量插入;B:批量插入）
        /// </summary>           
        [SugarColumn(Length = 1, IsNullable = false)]
        public string exe_type { get; set; }

        /// <summary>
        /// sql语句（TEXT 类型）
        /// </summary>           
        [SugarColumn(Length = -1, IsNullable = false)]
        public string script { get; set; }

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

        /// <summary>
        /// 批量插入时多少个标签为一行
        /// </summary>
        [SugarColumn(DefaultValue = "1")]
        public int row_step_num { get; set; } = 1;
    }
}
