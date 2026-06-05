using SqlSugar;
using System;
using System.Data.SqlTypes;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_device_statistics
    {
        public iot_device_statistics()
        {


        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int id { get; set; }

        /// <summary>
        /// Desc:设备id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string device_id { get; set; }

        /// <summary>
        /// Desc:报警状态 1-报警 0-正常
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int statistics_type { get; set; }

        /// <summary>
        /// Desc:设备报警开始时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? start_time { get; set; }

        /// <summary>
        /// Desc:设备报警结束时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? end_time { get; set; }

        /// <summary>
        /// Desc:报警时长
        /// Default:
        /// Nullable:True
        /// </summary>           
        public double? duration { get; set; }

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
        /// Nullable:True
        /// </summary>           
        public DateTime? created_at { get; set; }

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
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// Desc:更新人
        /// Default:
        /// Nullable:True
        /// </summary>           
           [SugarColumn(Length=36,IsNullable=true)]
           public string updated_by {get;set;}

    }
}
