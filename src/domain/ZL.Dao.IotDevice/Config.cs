using System;
using System.Linq;
using ZL.PFLite.Common;

namespace ZL.Dao.IotDevice
{
    class Config
    {
        public static readonly System.Collections.Specialized.NameValueCollection appSetting = System.Configuration.ConfigurationManager.AppSettings;
        public static string CompanyId { get; set; } = "default_company";
        public static string PlantId { get; set; } = "default_plant";
        public static string Line { get; set; } = "default_line";
        public static string StationNo { get; set; } = "default_station";
        public static string EdgeId { get; set; } = "default_edge";
        static string LocalDbKindStr { get; set; }
        static string LocalDbConnStr { get; set; }
        public static string LogFile { get; set; } = "ZL.Dao.IotDevice";
        static Config()
        {
            try
            {
                try
                {
                    CompanyId = appSetting["CompanyId"]?.ToString() ?? CompanyId;
                    PlantId = appSetting["PlantId"]?.ToString() ?? PlantId;
                    Line = appSetting["Line"]?.ToString() ?? Line;
                    if (appSetting.AllKeys.Contains("StationNo"))
                        StationNo = appSetting["StationNo"].ToString();
                }
                catch { }
                try
                {
                    if (appSetting.AllKeys.Contains("EdgeId"))
                        EdgeId = appSetting["EdgeId"].ToString();
                }
                catch { }
                try
                {
                    LocalDbKindStr = appSetting["LocalDbKind"].ToString();
                }
                catch { }
                try
                {
                    LocalDbConnStr = appSetting["LocalDbConn"].ToString();
                }
                catch { }
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("ZL.Dao.IotDevice 读取配置文件错误：" + ex.Message, LogFile);
                // 不再抛出异常，使用默认值
            }
        }
        public static void Log(string msg)
        {
            LogKit.WriteLogs(msg, LogFile);
        }
    }
}
