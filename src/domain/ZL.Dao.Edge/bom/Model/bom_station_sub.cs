using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    ///<summary>
    ///
    ///</summary>
    public partial class bom_station_sub
    {
        public bom_station_sub()
        {


        }
        /// <summary>
        /// Desc:工位id
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:工厂id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:公司id
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
        /// Desc:型号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string model { get; set; }

        /// <summary>
        /// Desc:区域号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string region_no { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:首工位标记
        /// Default:0
        /// Nullable:True
        /// </summary>           
        public string first_station { get; set; }

        /// <summary>
        /// Desc:尾工位标记
        /// Default:0
        /// Nullable:True
        /// </summary>           
        public string last_station { get; set; }

        /// <summary>
        /// Desc:排序(取bom_station中list_order)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? list_order { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

        /// <summary>
        /// Desc:
        /// Default:CURRENT_TIMESTAMP
        /// Nullable:True
        /// </summary>           
        public DateTime? created_at { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string created_by { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string updated_by { get; set; }

        /// <summary>
        /// Desc:
        /// Default:CURRENT_TIMESTAMP
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

    }
}
