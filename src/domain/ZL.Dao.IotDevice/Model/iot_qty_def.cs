using System;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///质量数据定义表
    ///</summary>
    public partial class iot_qty_def
    {
        public iot_qty_def()
        {


        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public string id { get; set; }


        /// <summary>
        /// Desc:PLC编号(对应iot_device表的id字段)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string device_id { get; set; }

        /// <summary>
        /// Desc:对应iot_tag表id字段
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string tag_id { get; set; }
        /// <summary>
        /// 需要同iot_biz_detail.exe_order 保持一致
        /// </summary>
        public int exe_order { get; set; }
        /// <summary>
        /// Desc:地址
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public int plc_add { get; set; }

        /// <summary>
        /// Desc:数据文件名
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string val_field { get; set; }

        /// <summary>
        /// Desc:数据描述
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string val_des { get; set; }

        /// <summary>
        /// Desc:数据类型(R-浮点型(4个字节)、I-整型(2个字节))
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string data_type { get; set; }

        /// <summary>
        /// Desc:是否判断非空(0-检查，1-不检查)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte null_check { get; set; }

        /// <summary>
        /// Desc:版本起
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string valid_start { get; set; }

        /// <summary>
        /// Desc:版本止
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string valid_end { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string remark { get; set; }

        /// <summary>
        /// Desc:质量数据类型：N 拧紧 Y 压装  C 测量 S 试漏 T图片
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string qty_type { get; set; }

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
        [SugarColumn(Length = 36, IsNullable = true)]
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
        [SugarColumn(Length = 36, IsNullable = true)]
        public string updated_by { get; set; }

    }
}
