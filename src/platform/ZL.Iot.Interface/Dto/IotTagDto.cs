using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using ZL.Tag;

namespace ZL.Iot.Interface
{
    public class TagCfgDtoList
    {
        public List<IotTagDto> TagCfgList { get; set; } = new List<IotTagDto>();
    }

    /// <summary>
    /// Iot 系统的点位定义 DTO
    /// 现已通过继承 TagItem 实现与 PlcBase 的模型统一
    /// </summary>
    public class IotTagDto : TagItem
    {
        [JsonProperty("id")]
        public new string Id { get => base.Id; set => base.Id = value; }

        [JsonProperty("tag_name")]
        public string tag_name { get => base.Description; set => base.Description = value; }

        [JsonProperty("group_id")]
        public string group_id { get; set; }

        [JsonProperty("data_type")]
        public new string DataType { get => base.DataTypeCode; set => base.DataTypeCode = value; }

        [JsonProperty("data_size")]
        public int data_size { get => base.Length; set => base.Length = value; }

        [JsonProperty("address")]
        public new string Address { get => base.Address; set => base.Address = value; }
        
        [JsonProperty("tag_type")]
        public new string TagType { get => base.TagType; set => base.TagType = value; }

        [JsonProperty("set_type")]
        public string set_type { get; set; }

        [JsonProperty("preset")]
        public string preset { get; set; }

        [JsonProperty("info_type")]
        public new string InfoType { get => base.InfoType; set => base.InfoType = value; }

        [JsonProperty("data_source")]
        public new string DataSource { get => base.DataSource; set => base.DataSource = value; }

        public float su { get; set; }
        public float sl { get; set; }
        public float sv { get; set; }

        public int archive { get; set; }

        /// <summary>
        /// 采样周期 (ms)
        /// </summary>
        [JsonProperty("cycle")]
        public int cycle { get => base.ScanRate; set => base.ScanRate = value; }

        [JsonProperty("enable")]
        public int enable_int 
        { 
            get => base.Enable ? 1 : 0; 
            set => base.Enable = value > 0; 
        }
    }
}
