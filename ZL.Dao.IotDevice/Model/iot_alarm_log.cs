using System;
using SqlSugar;

namespace ZL.Model
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_alarm_log
    {
        public iot_alarm_log()
        {
        }
        [SugarColumn(IsNullable = false, IsPrimaryKey = true, IsIdentity = true)]
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        public long id { get; set; }

        /// <summary>
        /// Desc:公司Id
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂Id
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
        /// Desc:设备编号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string device_no { get; set; }

        /// <summary>
        /// Desc:设备名称
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string device_name { get; set; }

        /// <summary>
        /// Desc:状态 1-报警；0-正常
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte status { get; set; }

        /// <summary>
        /// Desc:报警地址
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string start_add { get; set; }

        /// <summary>
        /// Desc:偏移位
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int offset { get; set; }

        /// <summary>
        /// Desc:报警内容
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string alarm_text { get; set; }

        /// <summary>
        /// Desc:报警类型
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string alarm_type { get; set; }

        /// <summary>
        /// Desc:当天日期
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string day { get; set; }

        /// <summary>
        /// Desc:当天报警次数
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? count { get; set; }

        /// <summary>
        /// Desc:报警开始时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? start_time { get; set; }

        /// <summary>
        /// Desc:报警结束时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? end_time { get; set; }

        /// <summary>
        /// Desc:报警时长(秒)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string duration { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
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
        public string updated_by { get; set; }

    }
}
