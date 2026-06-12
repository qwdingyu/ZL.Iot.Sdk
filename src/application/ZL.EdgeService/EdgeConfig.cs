using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZL.Iot.Interface;
using ZL.PFLite.Common;
using ZL.IotHub.Core;

namespace ZL.EdgeService
{
    public class EdgeConfig
    {
        public static readonly System.Collections.Specialized.NameValueCollection appSetting = System.Configuration.ConfigurationManager.AppSettings;
        /// <summary>
        /// 全局存储所有的工位和设备类之间的对应关系
        /// </summary>
        public static ConcurrentDictionary<string, IPlcDriver> StationPlcDic = new ConcurrentDictionary<string, IPlcDriver>();
        /// <summary>
        /// 本地存放Driver的目录
        /// </summary>
        public static string DriversDir = Path.Combine(System.Environment.CurrentDirectory, "Drivers");
        /// <summary>
        /// 本地下载临时目录
        /// </summary>
        public static string TempDir = Path.Combine(System.Environment.CurrentDirectory, "Temp");
        
        /// <summary>
        /// 驱动仓库基础 URL (P1: 驱动动态下发配置)
        /// 格式: https://driver-repo.tmom.com/drivers/{assembly_name}.dll
        /// </summary>
        public static string DriverRepoBaseUrl { get; set; } = "https://driver-repo.tmom.com/drivers";
        
        /// <summary>
        /// 驱动下载超时时间（毫秒）
        /// </summary>
        public static int DriverDownloadTimeoutMs { get; set; } = 30000;
        public static string LogFile { get; set; } = "ZL.EdgeService";
        /// <summary>
        /// 设备的刷新频率 EdgeConfig.DevUpdateRate
        /// </summary>
        public static int DevUpdateRate { get; set; } = 100;
        /// <summary>
        /// 驱动dll 对应的类别
        /// </summary>
        public static Dictionary<string, string> DriverDllTypeDic = new Dictionary<string, string>() {
            { "ZL.Plc.X.dll", "Hsl" },
            { "ZL.Plc.S7Net.dll", "S7Net" },
            { "ZL.Plc.Iot.dll", "IotClient" },
            { "ZL.Opc.dll", "Opc" },
        };
        static EdgeConfig()
        {
            try
            {

            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace("ZL.EdgeService 读取配置文件错误：" + ex.Message, LogFile);
                throw ex;
            }
        }
        /// <summary>
        /// 添加工位和设备类之间的对应关系
        /// </summary>
        /// <param name="stationNo"></param>
        /// <param name="d"></param>
        public static void AddStationPlcDic(string stationNo, IPlcDriver d)
        {
            if (StationPlcDic.ContainsKey(stationNo))
                StationPlcDic[stationNo] = d;
            else
                StationPlcDic.TryAdd(stationNo, d);
        }
        /// <summary>
        /// 根据工位获取设备信息
        /// </summary>
        /// <param name="stationNo"></param>
        /// <returns></returns>
        public static IPlcDriver GetPlcByStation(string stationNo)
        {
            IPlcDriver d = null;
            if (StationPlcDic.ContainsKey(stationNo))
                d = StationPlcDic[stationNo];
            return d;
        }
        public static string GetDriverTypeByDllName(string dllName)
        {
            string d = "";
            if (DriverDllTypeDic.ContainsKey(dllName))
                d = DriverDllTypeDic[dllName];
            return d;
        }
    }
}
