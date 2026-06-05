using SqlSugar;
using System;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_exeval
    {
        public iot_exeval()
        {
        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, Length = 36, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:标签id
        /// Default:
        /// Nullable:True
        /// </summary>           

        [SugarColumn(Length = 36, IsNullable = false)]
        public string p_id { get; set; }

        /// <summary>
        /// Desc:是否启用 0未启用，1启用
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int enable { get; set; } = 0;

        /// <summary>
        /// iot_biz_detail批量插入表时使用
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string table_name { get; set; }

        /// <summary>
        /// Desc:字段
        /// Default:
        /// Nullable:False
        /// </summary>           

        [SugarColumn(Length = 36, IsNullable = false)]
        public string val_field { get; set; }

        /// <summary>
        /// Desc:P:顺序计算;U:每次用到计算;F:首次用到计算;
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 1, IsNullable = true)]
        public string val_opu { get; set; }

        /// <summary>
        /// Desc:执行顺序
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? exe_order { get; set; }

        /// <summary>
        /// Desc:F:固定值;A:自动编号的值;Q:计算SQL的值;S:系统值;C:转换值;
        /// Default:
        /// Nullable:True
        /// </summary>           

        [SugarColumn(Length = 1, IsNullable = false)]
        public string val_mode { get; set; }

        /// <summary>
        /// Desc:S,C,F时有用
        /// S -- 0自定义，1日期时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 64, IsNullable = true)]
        public string fix_val { get; set; }

        /// <summary>
        /// Desc:以”?F?”方式引用数据集字段的值
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 2000, IsNullable = true)]
        public string sql { get; set; }

        /// <summary>
        /// 日期时间格式化字符串
        /// 
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string misc { get; set; }

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
        [SugarColumn(Length = 64, IsNullable = true)]
        public string created_by { get; set; }

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
        [SugarColumn(Length = 64, IsNullable = true)]
        public string updated_by { get; set; }
        /// <summary>
        /// 计算值
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public object val { get; set; }

    }
}
