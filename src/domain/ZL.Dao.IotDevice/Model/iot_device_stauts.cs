using SqlSugar;
using System;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_device_stauts
    {
        public iot_device_stauts()
        {

        }
        /// <summary>
        /// Desc:设备号(一个PLC为一个设备)
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public int id { get; set; }

        /// <summary>
        /// Desc:公司编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = false)]
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = false)]
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 32, IsNullable = false)]
        public string line { get; set; }

        /// <summary>
        /// Desc:区域号
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = true)]
        public string region_no { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = false)]
        public string station_no { get; set; }
        [SugarColumn(Length = 36, IsNullable = false)]
        public string device_id { get; set; }
        [SugarColumn(Length = 64, IsNullable = false)]
        public string device_name { get; set; }
        
        /// <summary>
        /// Desc:设备状态码
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 18, IsNullable = true)]
        public string mac_code { get; set; }

        /// <summary>
        /// Desc:设备状态
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 18, IsNullable = true)]
        public string mac_stauts { get; set; }
        /// <summary>
        /// Desc:设备状态码
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 18, IsNullable = true)]
        public string conn_code { get; set; }

        /// <summary>
        /// Desc:设备状态
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 18, IsNullable = true)]
        public string conn_stauts { get; set; }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 255, IsNullable = true)]
        public string remark { get; set; }
        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public DateTime? created_at { get; set; }

        /// <summary>
        /// Desc:创建人
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = true)]
        public string created_by { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// Desc:更新人
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = true)]
        public string updated_by { get; set; }

    }
}
