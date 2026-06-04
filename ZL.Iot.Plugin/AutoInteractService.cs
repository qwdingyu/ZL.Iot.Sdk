using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;
using ZL.Dao.IotDevice;
using ZL.Dao.IotDevice.Dto;
using ZL.Iot.Interface;
using ZL.PFLite.Common;

namespace ZL.Iot.Plugin
{
    public class AutoInteractService : IService
    {
        static string logFile = "EdgeService";

        SortedList<string, IDev> _devices;
        private Timer _timerDeviceConn;  // 自动重连PLC
        private DeviceKit deviceKit;

        public List<Task> TaskList = new List<Task>();
        private IotDeviceService IotDeviceSrv = new IotDeviceService();
        private IotTagService IotTagSrv = new IotTagService();

        public AutoInteractService()
        {
            //直接使用本项目下的SimpleInteract类来处理 自动采集和下发工作
            deviceKit = DeviceKit.GetInstance();
        }

        public void Start()
        {
            if (!AuthKit.CheckAppAuth())
            {
                LogKit.WriteLogs($"授权检测失败，请核查APP_SN！！！", logFile);
                return;
            }
            _devices = new SortedList<string, IDev>();
            InitServerByDatabase();
            StartTimer();
        }

        public void StartTimer()
        {
            _timerDeviceConn = new Timer();
            _timerDeviceConn.Elapsed += timerDeviceConn_Elapsed;
            _timerDeviceConn.Interval = 1000;
            _timerDeviceConn.Start();
        }

        void InitServerByDatabase()
        {
            List<IotDeviceDriverDto> DeviceDriverList = IotDeviceSrv.GetDeviceDriverListByEdgeId("1");//设备交互
            int DeviceCount = DeviceDriverList.Count;
            if (DeviceCount == 0)
            {
                LogKit.WriteLogs("该项目未配置设备信息，或设备全部未启用！！！", logFile);
                return;
            }

            //统一下载所需要的驱动对应的dll，并映射为Type
            bool downOk = deviceKit.DownAndLoadDrivers();

            int ThreadSeed = DeviceCount >= C.ThreadSeed ? C.ThreadSeed : DeviceCount;
            int ThreadCount = DeviceCount / ThreadSeed;
            for (int i = 0; i < ThreadCount; i++)
            {
                var dtNew = DeviceDriverList.Skip(i * ThreadSeed).Take(ThreadSeed).ToList<IotDeviceDriverDto>();
                if (dtNew.Count > 0)
                {
                    var t = Task.Run(() => InitThread(dtNew));
                    TaskList.Add(t);
                }
            }
        }

        void InitThread(List<IotDeviceDriverDto> DeviceDriverList)
        {
            string err = string.Empty;
            foreach (var it in DeviceDriverList)
            {
                string driver_id = it.driver_id;
                string device_id = it.device_id;
                //string device_class_name = it.device_class_name;
                string driver_assembly_name = it.driver_assembly_name;
                string driver_full_class_name = it.driver_full_class_name;
                string driver_class_name = it.driver_class_name;
                string arg_str = "";
                //if (string.IsNullOrEmpty(device_class_name))
                //    err += $"驱动编号【{driver_id}】,设备编号【{device_id}】在iot_device表中的class_name未配置，请核查！{ Environment.NewLine}";
                if (string.IsNullOrEmpty(driver_full_class_name))
                    err += $"驱动编号【{driver_id}】,设备编号【{device_id}】在iot_driver表中的class_full_name未配置，请核查！{ Environment.NewLine}";
                if (!string.IsNullOrEmpty(err))
                {
                    LogKit.WriteLogs(err, logFile);
                    return;
                }
                try
                {
                    IPLCDriver dv = null;
                    dv = deviceKit.CreateDriver(it, out arg_str);
                    if (dv == null)
                    {
                        LogKit.WriteLogs($"加载驱动编号【{driver_id}】异常，返回dv为空！", logFile);
                        return;
                    }

                    if (dv.IsClosed) dv.Connect();
                    //通过Assembly反射继承了DeviceBase的具体的处理函数
                    deviceKit.AddSimpleInteractHandle(dv, it.device_id, it.company_id, it.plant_id, it.line, it.region_no, it.station_no);

                    LogKit.WriteLogs($"工位{it.station_no}、设备{it.device_name}、加载驱动编号【{driver_id}】，连接参数为{arg_str}！", logFile);
                }
                catch (Exception ex)
                {
                    LogKit.WriteLogs("创建驱动错误，错误信息:" + ex.Message);
                }
            }
        }

        private void timerDeviceConn_Elapsed(object sender, ElapsedEventArgs e)
        {
            _timerDeviceConn.Stop();
            _devices = deviceKit.DeviceList;
            if (_devices != null)
            {
                // 获取键的集合
                var key = _devices.Keys;

                foreach (var k in key)
                {
                    IDev device = _devices[k];
                    //IPAddress ip = null;
                    //bool isIp = false;
                    //if (!string.IsNullOrEmpty(device.ServerName))
                    //    isIp = IPAddress.TryParse(device.ServerName, out ip);
                    //if (device.IsClosed && isIp)
                    //    device.Connect();
                    if (device.IsClosed)
                        device.Connect();
                }
            }
            _timerDeviceConn.Start();
        }

        public void Dispose()
        {
            _devices = deviceKit.DeviceList;
            if (_timerDeviceConn != null)
            {
                _timerDeviceConn.Stop();
                _timerDeviceConn.Close();
                _timerDeviceConn = null;
            }
            if (_devices != null)
            {
                // 获取键的集合
                var key = _devices.Keys;
                foreach (var k in key)
                {
                    IDev device = _devices[k];
                    device.Stop();
                }
            }
            foreach (var it in TaskList)
            {
                it.Dispose();
            }
        }
    }
}
