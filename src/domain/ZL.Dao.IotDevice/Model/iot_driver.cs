using SqlSugar;

namespace ZL.Dao.IotDevice
{
    public class iot_driver : BaseClass
    {
        [SugarColumn(IsPrimaryKey = true, Length=36, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:程序集路径
        /// Default:
        /// Nullable:False
        /// </summary>           
    	[SugarColumn(Length=255, IsNullable = false)]
        public string assembly_name { get; set; }

        /// <summary>
        /// Desc:类名
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length=128, IsNullable = false)]
        public string class_name { get; set; }

        /// <summary>
        /// Desc:类名全称
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length=128, IsNullable = false)]
        public string class_full_name { get; set; }

        /// <summary>
        /// Desc:描述
        /// Default:
        /// Nullable:False
        /// </summary>           

        [SugarColumn(Length=-1, IsNullable = false)]
        public string description { get; set; }


    }
}
