using SqlSugar;
using System;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_biz_mode
    {
        public iot_biz_mode()
        {
        }
        /// <summary>
        /// Desc:id
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:执行序号
        /// Default:0
        /// Nullable:False
        /// </summary>           
        public int exe_order { get; set; }

        /// <summary>
        /// Desc:判断类型 Q:sql; C:c#
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string judge_type { get; set; }

        /// <summary>
        /// Desc:判断条件
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string judge_exp { get; set; }

        /// <summary>
        /// Desc:sql类型 Q:更新SQL;P:存储过程;S:查询SQL;B:批量插入;
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string sql_type { get; set; }

        /// <summary>
        /// Desc:sql语句
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string sql { get; set; }
        /// <summary>
        /// 是否启用 0未启用，1启用
        /// </summary>
        public int enable { get; set; }
        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string remark { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? created_at { get; set; }

        /// <summary>
        /// Desc:创建人
        /// Default:
        /// Nullable:True
        /// </summary>           
           [SugarColumn(Length=36,IsNullable=true)]
           public string created_by {get;set;}

        /// <summary>
        /// Desc:更新时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:
        /// Nullable:True
        /// </summary>           
           [SugarColumn(Length=36,IsNullable=true)]
           public string updated_by {get;set;}

    }
}
