using System;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    public class iot_tag : BaseClass
    {
        /// <summary>
        /// Desc:编号
        /// Default:
        /// Nullable:False
        /// </summary>        
        [SugarColumn(IsPrimaryKey = true, Length = 64, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:PLC编号(对应iot_device表的id字段)
        /// Default:
        /// Nullable:False
        /// </summary>           
		[SugarColumn(Length = 64, IsNullable = false)]
        public string device_id { get; set; }

        /// <summary>
        /// Desc:组号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 64, IsNullable = true)]
        public string group_id { get; set; }

        /// <summary>
        /// Desc:名称
        /// Default:
        /// Nullable:False
        /// </summary>           
		[SugarColumn(Length = 64, IsNullable = false)]
        public string tag_name { get; set; }

        /// <summary>
        /// Desc:数据类型(Bool-1、byte-3、Short-4、Float-8、Str-11)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string data_type { get; set; }

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
		[SugarColumn(Length = 64)]
        public string address { get; set; }

        /// <summary>
        /// Desc:PLC,MES
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 36, IsNullable = true)]
        public string data_source { get; set; }
        [SugarColumn(Length = 36, IsNullable = true)]
        public string tag_type { get; set; }
        [SugarColumn(Length = 36, IsNullable = true)]
        public string pid { get; set; }
        [SugarColumn(IsNullable = true)]
        public int list_order { get; set; }
        [SugarColumn(IsNullable = true)]
        public int  exe_order { get; set; }
        [SugarColumn(Length = 64, IsNullable = true)]
        public string set_type { get; set; }
        [SugarColumn(Length = 64, IsNullable = true)]
        public string preset { get; set; }
        [SugarColumn(Length = 64, IsNullable = true)]
        public string info_type { get; set; }
        /// <summary>
        /// 业务模式执行类别 E:iot_exe定义执行；B:iot_biz_def定义执行
        /// 只有在监控标签才需要配置
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string biz_mode { get; set; }
        [SugarColumn(Length = 64, IsNullable = true)]
        public string biz_code { get; set; }
        /// <summary>
        /// Desc:步骤-仿真器使用
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public int step { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public int pstep { get; set; }

        /// <summary>
        /// Desc:是否启用
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int is_active { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public int archive { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 255, IsNullable = true)]
        public string default_value { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 255, IsNullable = true)]
        public string description { get; set; }

        /// <summary>
        /// Desc:标准上限
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float su { get; set; }

        /// <summary>
        /// Desc:标准下限
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float sl { get; set; }

        /// <summary>
        /// Desc:标准值
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float sv { get; set; }

        /// <summary>
        /// Desc:单位
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 6, IsNullable = true)]
        public string unit { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public int cycle { get; set; }

        /// <summary>
        /// Desc:当前值
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 255, IsNullable = true)]
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
        [SugarColumn(Length = 255, IsNullable = true)]
        public string remark { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length = 64, IsNullable = true)]
        public string tag_sub { get; set; }

    }
}
