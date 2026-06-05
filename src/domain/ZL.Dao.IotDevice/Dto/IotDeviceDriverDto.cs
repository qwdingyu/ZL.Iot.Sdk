using SqlSugar;

namespace ZL.Dao.IotDevice
{
    public class IotDeviceDriverDto
    {

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
        /// Nullable:True
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:驱动号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string driver_id { get; set; }

        public string device_id { get; set; }
        /// <summary>
        /// Desc:设备类型编号(对应iot_device_type表的id字段)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int device_type_id { get; set; }

        /// <summary>
        /// Desc:设备名
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string device_name { get; set; }

        /// <summary>
        /// Desc:类名
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string device_class_name { get; set; }


        public string device_assembly_name { get; set; }
        /// <summary>
        /// Desc:IP地址
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string ip { get; set; }

        /// <summary>
        /// Desc:超时报警时间
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int time_out { get; set; }
        /// <summary>
        /// dll文件路径及文件名：ZL.Plc.X.dll
        /// 分绝对路径和相对路径
        /// </summary>
        public string driver_assembly_name { get; set; }
        /// <summary>
        /// 驱动class全类名：ZL.Plc.X.SiemensDriver
        /// </summary>
        public string driver_full_class_name { get; set; }
        /// <summary>
        /// 驱动类名：SiemensDriver
        /// </summary>
        public string driver_class_name { get; set; }
        public string brand { get; set; }
        public int purpose { get; set; }

        /// <summary>
        /// 设备扩展配置 JSON 字符串
        /// 格式: {"Port": 102, "Rack": 0, "Slot": 1, "BaudRate": 9600}
        /// 替代已废弃的 iot_device_arg 表
        /// </summary>
        public string extra_config { get; set; }

        /// <summary>
        /// 端口号（从 extra_config 解析，兼容旧代码）
        /// </summary>
        public int port { get; set; }

        /// <summary>
        /// 是否为调试模式--打印日志
        /// </summary>
        public bool debug { get; set; }
    }
}
