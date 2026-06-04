using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;

namespace ZL.Biz.Execute
{

    public class BizSupportDto
    {
        /// <summary>
        /// exe模式可支持的类型
        /// </summary>
        public static string[] ExeTypeSupport = new string[] { "GET", "GETS", "GETBULK", "SET", "SETS" };
        public static string[] BizModeSupport = new string[] { "BG", "BGI", "BS", "BSI" };
    }

    /// <summary>
    /// 批量插入辅助类
    /// </summary>
    public class BulkInfo
    {
        //{"TableName":"pms_qty","Split":"Y"}
        public string TableName { get; set; }
        /// <summary>
        /// 是否为分区表插入模式
        /// 由于sqlsugar .net freamwork 版本不支持 分区表，所以暂时没有使用
        /// </summary>
        public string Split { get; set; }

        [JsonIgnore]
        public DataTable dbDataTable { get; set; } = new DataTable();
        [JsonIgnore]
        public string[] ColumsName { get; set; }

        /// <summary>
        /// 批量执行时的原始数据字典列表
        /// </summary>
        [JsonIgnore]
        public List<ConcurrentDictionary<string, object>> SelectDicList { get; set; } = new List<ConcurrentDictionary<string, object>>();
    }
}
