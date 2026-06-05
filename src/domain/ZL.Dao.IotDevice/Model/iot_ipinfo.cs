using SqlSugar;
using System;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_ipinfo
    {
        public iot_ipinfo()
        {


        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }

        /// <summary>
        /// Desc:公司id
        /// Default:-1
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36)]
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂id
        /// Default:-1
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36)]
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线号
        /// Default:-1
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36)]
        public string line { get; set; }

        /// <summary>
        /// Desc:区域号
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 10, IsNullable = true)]
        public string region_no { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 20)]
        public string station_no { get; set; }

        /// <summary>
        /// Desc:IP地址
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 50, IsNullable = true)]
        public string ip { get; set; }

        /// <summary>
        /// Desc:设备名称
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 50, IsNullable = true)]
        public string device_name { get; set; }

        /// <summary>
        /// Desc:网络状态 0正常，1异常
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public int? status { get; set; }

        /// <summary>
        /// Desc:端口号
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public int? port { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
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
        [SugarColumn(IsNullable = true)]
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// Desc:更新人
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = true)]
        public string updated_by { get; set; }

    }
}
