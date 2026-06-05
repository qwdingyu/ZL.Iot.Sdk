using System;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>

    public partial class iot_rank_def
    {

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }

        /// <summary>
        /// Desc:公司编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string line { get; set; }

        /// <summary>
        /// Desc:配方号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string formula_no { get; set; }

        /// <summary>
        /// Desc:档位号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string rank_no { get; set; }

        /// <summary>
        /// Desc:区域号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string region_no { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:类型(GENERAL、JOB、BOM、1Light、2Check)
        /// Default:
        /// Nullable:False
        /// </summary>           
        //public string type {get;set;}
        public string type1 { get; set; }

        /// <summary>
        /// Desc:序号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int list_order { get; set; }

        /// <summary>
        /// Desc:设置类型(R-挡位值、I-产品相关)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string set_type { get; set; }

        /// <summary>
        /// Desc:档位值
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string rank_val { get; set; }

        /// <summary>
        /// Desc:信息类型(D-流水号，M-型号，T-系列，S-基本型号，H-短代码，Z-上线时间，RD-返修目标工位)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string info_type { get; set; }

        /// <summary>
        /// Desc:信号地址
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string ctrl_add { get; set; }

        /// <summary>
        /// Desc:B:单个字节, 2S:二进制字符串, SS:多个byte(连续的), INT:整型, X:布尔类型, BS:字节数组(非连续的)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string value_type { get; set; }

        /// <summary>
        /// Desc:值长度
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int value_len { get; set; }

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
        /// Nullable:False
        /// </summary>           
        public DateTime created_at { get; set; }

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
        /// Nullable:False
        /// </summary>           
        public DateTime updated_at { get; set; }

        /// <summary>
        /// Desc:更新人
        /// Default:
        /// Nullable:True
        /// </summary>           
           [SugarColumn(Length=36,IsNullable=true)]
           public string updated_by {get;set;}

    }
}
