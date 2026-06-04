using System;
using ZL.PFLite.Common;

namespace ZL.Iot.Plugin
{
    public class Config
    {
        public static readonly System.Collections.Specialized.NameValueCollection appSetting = System.Configuration.ConfigurationManager.AppSettings;
        public static string CompanyId { get; set; }
        public static string PlantId { get; set; }
        public static string Line { get; set; }
        public static string StationNo { get; set; }
        public static string EdgeId { get; set; }

        public static int HeartBeatTimer { get; set; }
        public static int CheckIpConnTimer { get; set; }
        public static string LogFile { get; set; } = "ZL.EdgeService";
        static Config()
        {
            try
            {
                try
                {
                    CompanyId = appSetting["CompanyId"].ToString();
                    PlantId = appSetting["PlantId"].ToString();
                    Line = appSetting["Line"].ToString();
                    StationNo = appSetting["StationNo"].ToString();
                }
                catch { }
                try
                {
                    HeartBeatTimer = int.Parse(appSetting["HeartBeatTimer"].ToString());
                    CheckIpConnTimer = int.Parse(appSetting["CheckIpConnTimer"].ToString());
                }
                catch { }
                try
                {
                    EdgeId = appSetting["EdgeId"].ToString();
                }
                catch { }
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("ZL.EdgeService 读取配置文件错误：" + ex.Message, LogFile);
                throw ex;
            }
        }
        //public static void Log(string msg)
        //{
        //    LogKit.WriteLogs(msg, LogFile);
        //}
    }
}
