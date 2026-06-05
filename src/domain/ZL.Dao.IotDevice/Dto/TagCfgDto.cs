using System.Collections.Generic;

namespace ZL.Dao.IotDevice.Dto
{

    public class TagCfgDto
    {
        public string id { get; set; }

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

        public string tag_type { get; set; }

        public int enable { get; set; } = 0;
    }

    public class TagCfgDtoList
    {
        public List<TagCfgDto> TagCfgList { get; set; } = new List<TagCfgDto>();
    }
}
