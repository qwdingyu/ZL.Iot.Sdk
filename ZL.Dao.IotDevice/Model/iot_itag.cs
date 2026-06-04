using System;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    public class iot_itag : BaseClass
    {
        /// <summary>
        /// Desc:编号
        /// Default:
        /// Nullable:False
        /// </summary>        
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:PLC编号(对应iot_device表的id字段)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string device_id { get; set; }

        /// <summary>
        /// Desc:组号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string group_id { get; set; }

        /// <summary>
        /// Desc:名称
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string tag_name { get; set; }

        /// <summary>
        /// Desc:数据类型(Bool-1、byte-3、Short-4、Float-8、Str-11)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int data_type { get; set; }

        /// <summary>
        /// Desc:数据长度
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int data_size { get; set; }

        /// <summary>
        /// Desc:地址	Bool-DB111,DBX1.0	Byte-DB111,DBB2(长度维护在data_size)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string address { get; set; }

        /// <summary>
        /// Desc:PLC,MES
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string data_source { get; set; }
        public string pid { get; set; }
        public int exe_order { get; set; }
        public string tag_type { get; set; }
        public string set_type { get; set; }
        public string preset { get; set; }
        public string info_type { get; set; }

        /// <summary>
        /// Desc:是否启用
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int is_active { get; set; }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string default_value { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string description { get; set; }

        /// <summary>
        /// Desc:标准上限
        /// Default:
        /// Nullable:True
        /// </summary>           
        public float su { get; set; }

        /// <summary>
        /// Desc:标准下限
        /// Default:
        /// Nullable:True
        /// </summary>           
        public float sl { get; set; }

        /// <summary>
        /// Desc:标准值
        /// Default:
        /// Nullable:True
        /// </summary>           
        public float sv { get; set; }

        /// <summary>
        /// Desc:单位
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string unit { get; set; }

        /// <summary>
        /// Desc:当前值
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string value { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime updated_at { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string tag_sub { get; set; }

    }
}
