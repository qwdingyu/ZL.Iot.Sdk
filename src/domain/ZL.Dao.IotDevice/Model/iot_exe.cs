using System;
using System.Linq;
using System.Text;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_exe
    {
        public iot_exe()
        {
        }
        /// <summary>
        /// Desc:id
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, Length = 36, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:标签id
        /// Default:
        /// Nullable:False
        /// </summary>           
		[SugarColumn(Length = 36, IsNullable = false)]
        public string tag_id { get; set; }

        /// <summary>
        /// Desc:业务代码（支持按 biz_code 查询 Exe 配置，用于 tag_id 为空的情况）
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 64, IsNullable = true)]
        public string biz_code { get; set; }

        /// <summary>
        /// Desc:执行序号
        /// Default:0
        /// Nullable:False
        /// </summary>           
        public int exe_order { get; set; }

        /// <summary>
        /// Desc:判断类型 Q:script; C:c#
        /// Default:
        /// Nullable:True
        /// </summary>           
		[SugarColumn(Length = 1, IsNullable = false)]
        public string judge_type { get; set; }

        /// <summary>
        /// Desc:判断条件
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 128, IsNullable = true)]
        public string judge_exp { get; set; }

        /// <summary>
        /// Desc:sql类型 Q:更新SQL;P:存储过程;S:查询SQL;;F:批量读取批量插入;B:批量插入
        /// Default:
        /// Nullable:False
        /// </summary>           
		[SugarColumn(Length = 1, IsNullable = false)]
        public string exe_type { get; set; }

        /// <summary>
        /// Desc:sql语句
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = -1, IsNullable = true)]
        public string script { get; set; }
        /// <summary>
        /// 是否启用 0未启用，1启用
        /// </summary>
        [SugarColumn(DefaultValue = (("0")))]
        public int enable { get; set; }
        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 255, IsNullable = true)]
        public string remark { get; set; }

        /// <summary>
        /// 扩展配置 (JSON 格式，用于规则定义、写回配置等)
        /// </summary>
        [SugarColumn(Length = -1, IsNullable = true)]
        public string extra_config { get; set; }

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
        [SugarColumn(Length = 36, IsNullable = true)]
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
        [SugarColumn(Length = 36, IsNullable = true)]
        public string updated_by { get; set; }

    }
}
