using SqlSugar;

namespace ZL.Dao.IotDevice
{
    public class iot_device_type : BaseClass
    {

        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }

        /// <summary>
        /// Desc:PLC类型(S7-300、S7-1200等)
        /// Default:
        /// Nullable:True
        /// </summary>           

        [SugarColumn(Length=18,IsNullable=true)]
        public string type { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           

        [SugarColumn(Length=64,IsNullable=true)]
        public string brand { get; set; }

    }
}
