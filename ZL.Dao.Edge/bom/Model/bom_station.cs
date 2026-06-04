using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    ///<summary>
    ///
    ///</summary>
    public partial class bom_station
    {
        public bom_station()
        {


        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:公司id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string line { get; set; }

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
        /// Desc:工位名称
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string station_name { get; set; }

        /// <summary>
        /// Desc:工位类型
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string station_type { get; set; }
        /// <summary>
        /// ON上线，OFF下线，RP返修
        /// </summary>
        public string pms_type { get; set; }
        public string bom_barcodeid { get; set; }
        /// <summary>
        /// Desc:距离
        /// Default:0
        /// Nullable:True
        /// </summary>           
        public double? distance { get; set; }

        /// <summary>
        /// Desc:分线号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string subline_no { get; set; }

        /// <summary>
        /// Desc:工位长度
        /// Default:0
        /// Nullable:True
        /// </summary>           
        public double? station_len { get; set; }

        /// <summary>
        /// Desc:流转顺序
        /// Default:0
        /// Nullable:True
        /// </summary>           
        public double? goseq_no { get; set; }

        /// <summary>
        /// Desc:分段号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string sect_no { get; set; }

        /// <summary>
        /// Desc:
        /// Default:0
        /// Nullable:True
        /// </summary>           
        public double? toon_steps { get; set; }

        /// <summary>
        /// Desc:
        /// Default:0
        /// Nullable:True
        /// </summary>           
        public double? toon_time { get; set; }

        /// <summary>
        /// Desc:时间基准工位
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string time_mark { get; set; }

        /// <summary>
        /// Desc:作业指示标记
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string synhint_mark { get; set; }

        /// <summary>
        /// Desc:流转顺序
        /// Default:
        /// Nullable:True
        /// </summary>           
        public double? go_no { get; set; }

        /// <summary>
        /// Desc:等待时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string wait_time { get; set; }

        /// <summary>
        /// Desc:堵料时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string block_time { get; set; }

        public int list_order { get; set; }
        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

        /// <summary>
        /// Desc:创建人
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string created_by { get; set; }

        /// <summary>
        /// Desc:修改人
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string updated_by { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? created_at { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

    }
}
