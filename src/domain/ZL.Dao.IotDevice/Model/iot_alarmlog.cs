using System;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_alarmlog
    {
        public iot_alarmlog()
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
        /// Desc:统计id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? statistics_id { get; set; }

        /// <summary>
        /// Desc:设备id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string device_id { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:iot_tag表id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string tag_id { get; set; }

        /// <summary>
        /// Desc:状态0正常，1报警
        /// Default:0
        /// Nullable:True
        /// </summary>           
        public int? status { get; set; }

        /// <summary>
        /// Desc:报警地址
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string address { get; set; }

        /// <summary>
        /// Desc:偏移量
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? offset { get; set; }

        /// <summary>
        /// Desc:报警文本
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string alarm_text { get; set; }

        /// <summary>
        /// Desc:1- 报警 2- 自动运行 3- 手动运行(待机)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string alarm_type { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string day { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? recno { get; set; }

        /// <summary>
        /// Desc:开始时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? start_time { get; set; }

        /// <summary>
        /// Desc:结束时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? end_time { get; set; }

        /// <summary>
        /// Desc:时长(秒)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public double? duration { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime created_at { get; set; }

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
        public DateTime updated_at { get; set; }

        /// <summary>
        /// Desc:更新人
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = true)]
        public string updated_by { get; set; }

    }
}
