namespace ZL.Dao.IotDevice
{
    public class IotDriverAssemblyDto
    {
        public string id { get; set; }

        /// <summary>
        /// Desc:程序集路径
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string assembly_name { get; set; }

        /// <summary>
        /// Desc:类名
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string class_name { get; set; }

        /// <summary>
        /// Desc:类名全称
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string class_full_name { get; set; }
    }
}
