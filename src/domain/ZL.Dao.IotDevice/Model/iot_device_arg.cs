using SqlSugar;

namespace ZL.Dao.IotDevice
{
    public class iot_device_arg : BaseClass
    {
        [SugarColumn(IsPrimaryKey = true, Length=36, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:类型编号(对应iot_device_type的id字段)
        /// Default:
        /// Nullable:False
        /// </summary>                   
		[SugarColumn(Length=36, IsNullable = false)]
        public string device_type_id { get; set; }

        /// <summary>
        /// Desc:属性名
        /// Default:
        /// Nullable:False
        /// </summary>           
		[SugarColumn(Length=36, IsNullable = false)]
        public string pro_name { get; set; }

        /// <summary>
        /// Desc:属性值
        /// Default:
        /// Nullable:False
        /// </summary>           
		[SugarColumn(Length=64, IsNullable = false)]
        public string pro_value { get; set; }

        /// <summary>
        /// Desc:描述
        /// Default:
        /// Nullable:False
        /// </summary>           
		[SugarColumn(Length=-1)]
        public string description { get; set; }
    }
}
