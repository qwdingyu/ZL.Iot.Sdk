using System;
using System.Linq;
using ZL.PFLite.Common;

namespace ZL.Iot.Interface
{
    public class IotConfigInfo
    {
        public static readonly System.Collections.Specialized.NameValueCollection appSetting = System.Configuration.ConfigurationManager.AppSettings;
        public static int WsServerPort { get; set; } = 7070;

        public static bool Debug { get; set; } = false;
        public static string IOT_WS_SERVER { get; set; } = "IOT_WS_SERVER";
        /// <summary>
        /// 是否发送websockect消息
        /// </summary>
        public static bool CanSendWsMsg { get; set; } = false;

        public static int HeartBeatTimer { get; set; } = 1000;
        public static int CheckIpConnTimer { get; set; } = 10000;
        /// <summary>
        /// 是否开启并行读取
        /// </summary>
        public static bool UseParallel { get; set; } = false;
        /// <summary>
        /// 是否有前导符
        /// </summary>
        public static bool LeaderChart { get; set; }

        static IotConfigInfo()
        {
            try
            {
                if (appSetting.AllKeys.Contains("WsServerPort"))
                    WsServerPort = int.Parse(appSetting["WsServerPort"].ToString());

                if (appSetting.AllKeys.Contains("Debug"))
                    Debug = (appSetting["Debug"].ToString().Trim().ToUpper() == "TRUE");

                if (appSetting.AllKeys.Contains("CanSendWsMsg"))
                    CanSendWsMsg = (appSetting["CanSendWsMsg"].ToString().Trim().ToUpper() == "TRUE");

                if (appSetting.AllKeys.Contains("UseParallel"))
                    UseParallel = (appSetting["UseParallel"].ToString().Trim().ToUpper() == "TRUE");


                if (appSetting.AllKeys.Contains("LeaderChart"))
                    LeaderChart = (appSetting["LeaderChart"].ToString().ToUpper() == "TRUE");
                if (appSetting.AllKeys.Contains("HeartBeatTimer"))
                    HeartBeatTimer = int.Parse(appSetting["HeartBeatTimer"].ToString());
                if (appSetting.AllKeys.Contains("CheckIpConnTimer"))
                    CheckIpConnTimer = int.Parse(appSetting["CheckIpConnTimer"].ToString());
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace("ZL.Iot.Interface.SelfConfig 读取配置文件错误：" + ex.Message);
                throw ex;
            }
        }
    }
}
