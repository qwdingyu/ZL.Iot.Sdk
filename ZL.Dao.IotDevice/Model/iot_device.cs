using SqlSugar;

namespace ZL.Dao.IotDevice
{
    public class iot_device : BaseClass
    {
        [SugarColumn(IsPrimaryKey = true, Length = 36, IsNullable = false)]
        public string id { get; set; }

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
        [SugarColumn(Length = 36, IsNullable = false)]
        public string line { get; set; }

        /// <summary>
        /// Desc:区域号
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = true)]
        public string region_no { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:True
        /// </summary>           

        [SugarColumn(Length = 36, IsNullable = false)]
        public string station_no { get; set; }

        /// <summary>
        /// Desc:驱动号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = false)]
        public string driver_id { get; set; }

        /// <summary>
        /// Desc:设备名
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = false)]
        public string device_name { get; set; }

        /// <summary>
        /// Desc:是否启用
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte is_active { get; set; }

        /// <summary>
        /// Desc:类名
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 128, IsNullable = true)]
        public string class_name { get; set; }

        /// <summary>
        /// Desc:class_name对应的程序集路径
        /// Default:
        /// Nullable:true
        /// </summary>           
        [SugarColumn(Length = 128, IsNullable = true)]
        public string assembly_name { get; set; }
        /// <summary>
        /// Desc:IP地址
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 15, IsNullable = false)]
        public string address { get; set; }


        [SugarColumn(Length = 15, IsNullable = true)]
        public string test_ip { get; set; }

        /// <summary>
        /// Desc:设备类型编号(对应iot_device_type表的id字段)
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsNullable = false)]
        public int device_type_id { get; set; }

        /// <summary>
        /// Desc:超时报警时间
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int time_out { get; set; }

        /// <summary>
        /// Desc:0：报警采集，1：交互
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int purpose { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 255, IsNullable = true)]
        public string remark { get; set; }
        /// <summary>
        /// 是否为调试模式--打印日志
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public bool debug { get; set; }

    }
}
