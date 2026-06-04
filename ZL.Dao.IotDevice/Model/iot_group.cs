using SqlSugar;

namespace ZL.Dao.IotDevice
{
    public class iot_group : BaseClass
    {

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           

        [SugarColumn(IsPrimaryKey = true, Length=36, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:设备号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length=36, IsNullable = true)]
        public string device_id { get; set; }

        /// <summary>
        /// Desc:组名
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length=64, IsNullable = false)]
        public string group_name { get; set; }

        /// <summary>
        /// Desc:轮询周期
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int update_rate { get; set; }

        /// <summary>
        /// Desc:激活属性(是否轮询)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public sbyte is_active { get; set; }

        /// <summary>
        /// Desc:是否监听事件
        /// Default:
        /// Nullable:False
        /// </summary>           
        public sbyte is_moniter { get; set; }
    }
}
