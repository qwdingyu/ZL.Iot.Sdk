using System;
using SqlSugar;

namespace ZL.Model
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_station
    {
        public iot_station()
        {


        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>        
        /// 
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
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
        /// Desc:alarm状态
        /// Default:
        /// Nullable:False
        /// </summary>           
        public short status_alarm { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
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
        public string created_by { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:
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
