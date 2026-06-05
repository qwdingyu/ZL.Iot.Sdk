using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    ///<summary>
    ///
    ///</summary>
    public partial class bom_barcode
    {
        public bom_barcode()
        {


        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public int id { get; set; }

        /// <summary>
        /// Desc:公司ID
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂ID
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线体
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string line { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:条码类型 128，RQ等
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string code_type { get; set; }

        /// <summary>
        /// Desc:条码内容
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string content { get; set; }

        /// <summary>
        /// Desc:打印变量值
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string print_vals { get; set; }

        /// <summary>
        /// Desc:条码描述
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string description { get; set; }

        /// <summary>
        /// Desc:打印类型 1:USB,2:COM;3:LPT;4:TCP;
        /// Default:1
        /// Nullable:False
        /// </summary>           
        public byte print_type { get; set; }

        /// <summary>
        /// Desc:打印参数
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string print_params { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:CURRENT_TIMESTAMP
        /// Nullable:True
        /// </summary>           
        public DateTime? created_at { get; set; }

        /// <summary>
        /// Desc:创建人
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string created_by { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:CURRENT_TIMESTAMP
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// Desc:更新人
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string updated_by { get; set; }

    }
}
