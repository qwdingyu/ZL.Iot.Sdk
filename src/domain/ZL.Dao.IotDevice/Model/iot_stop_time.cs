using System;
using System.Linq;
using System.Text;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_stop_time
    {
        public iot_stop_time()
        {
        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }

        /// <summary>
        /// Desc:公司Id
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36)]
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂Id
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36)]
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36)]
        public string line { get; set; }

        /// <summary>
        /// Desc:区域号
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 16, IsNullable = true)]
        public string region_no { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 32)]
        public string station_no { get; set; }

        /// <summary>
        /// Desc:设备编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 20)]
        public string device_id { get; set; }

        /// <summary>
        /// Desc:设备名称
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 20, IsNullable = true)]
        public string device_name { get; set; }

        /// <summary>
        /// Desc:iot_tag表id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string tag_id { get; set; }
        /// <summary>
        /// Desc:状态 1-停线；0-正常
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte status { get; set; }

        /// <summary>
        /// Desc:当天日期
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 20, IsNullable = true)]
        public string day { get; set; }

        /// <summary>
        /// Desc:当天停线次数
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public int? count { get; set; }

        /// <summary>
        /// Desc:停线开始时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public DateTime? start_time { get; set; }

        /// <summary>
        /// Desc:停线结束时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public DateTime? end_time { get; set; }

        /// <summary>
        /// Desc:停线时长(秒)
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 32, IsNullable = true)]
        public string duration { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 50, IsNullable = true)]
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
        [SugarColumn(Length = 16, IsNullable = true)]
        public string created_by { get; set; }

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
        [SugarColumn(Length = 16, IsNullable = true)]
        public string updated_by { get; set; }

    }
}
