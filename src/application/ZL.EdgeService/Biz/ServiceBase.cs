using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using ZL.Dao.IotDevice;
using ZL.Iot.Interface;
using ZL.PFLite.Common;
using ZL.IotHub.Core;
using ZL.IotHub.Models;

namespace ZL.EdgeService
{
    /// <summary>
    /// Edge Service 基础服务类（已全面对接 PlcBase 架构）
    /// 职责：生命周期管理、多设备并行初始化、状态监控
    /// </summary>
    public class ServiceBase : IService
    {
        double RUN_DURATION = 30;
        DateTime StartTime;
        SortedList<string, object> _devices;
        private System.Timers.Timer _timerDeviceStatus;
        private DeviceKit deviceKit;
        private CancellationTokenSource cancelTokenSource;
        public List<Task> TaskList = new List<Task>();
        private IotDeviceService IotDeviceSrv = new IotDeviceService();
        private IotEdgeRelationService iotEdgeRelationSrv = new IotEdgeRelationService();
        string DeviceId = "";
        string EdgeId = "";
        private int StatusCheckInterval = 5000; 
        bool Debug = false;
        bool LeaderChart = false;

        public ServiceBase(Assembly main_ass, string edgeId, bool _debug = false)
        {
            Debug = _debug;
            EdgeId = edgeId;
            deviceKit = DeviceKit.GetInstance(main_ass, EdgeId);
            Init();
        }

        public ServiceBase(string device_id, Assembly main_ass, bool _debug = false)
        {
            Debug = _debug;
            if (string.IsNullOrEmpty(device_id))
            {
                LogKit.WriteAndTrace($"实例化ServiceBase时【device_id】，不能为空，请核查！");
                return;
            }
            DeviceId = device_id;
            EdgeId = iotEdgeRelationSrv.GetEdgeIdByDeviceId(DeviceId);
            deviceKit = DeviceKit.GetInstance(device_id, main_ass);
            Init();
        }

        public void Init()
        {
            try
            {
                LeaderChart = IotConfigInfo.LeaderChart;
                if (!AuthKit.CheckAppAuth())
                {
                    LogKit.WriteAndTrace($"未授权，演示模式只能使用【{RUN_DURATION}】分钟！");
                }
                else
                {
                    RUN_DURATION = 0;
                }
                StartTime = DateTime.Now;
                _devices = new SortedList<string, object>();
                
                if (string.IsNullOrEmpty(DeviceId))
                    DeviceDriverList = IotDeviceSrv.GetDeviceDriverListByEdgeId(EdgeId, Debug, -1);
                else
                    DeviceDriverList = IotDeviceSrv.GetDeviceDriverListById(DeviceId, Debug);

                if (DeviceDriverList.Count == 0)
                {
                    LogKit.WriteAndTrace("该项目未配置设备信息，或设备全部未启用！！！");
                    return;
                }
                
                deviceKit.DownAndLoadDrivers(EdgeId);

                _timerDeviceStatus = new System.Timers.Timer(StatusCheckInterval);
                _timerDeviceStatus.Elapsed += timerDeviceStatus_Elapsed;
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace("ServiceBase.Init 异常: " + ex.Message);
                throw;
            }
        }

        List<IotDeviceDriverDto> DeviceDriverList = new List<IotDeviceDriverDto>();

        public void Start()
        {
            BuildThread();
            foreach (var _task in TaskList)
            {
                _task.Start();
            }
            _timerDeviceStatus?.Start();
            LogKit.WriteAndTrace($"[ServiceBase] 服务启动完成，共计 {DeviceDriverList.Count} 个设备任务已分配。");
        }

        void BuildThread()
        {
            cancelTokenSource = new CancellationTokenSource();
            TaskList = new List<Task>();
            int DeviceCount = DeviceDriverList.Count;
            int ThreadSeed = 5; 
            int ThreadCount = (int)Math.Ceiling((double)DeviceCount / ThreadSeed);
            
            for (int i = 0; i < ThreadCount; i++)
            {
                var dtNew = DeviceDriverList.Skip(i * ThreadSeed).Take(ThreadSeed).ToList();
                if (dtNew.Count > 0)
                {
                    var t = new Task(() =>
                    {
                        InitThread(dtNew);
                    }, cancelTokenSource.Token);
                    TaskList.Add(t);
                }
            }
        }

        void InitThread(List<IotDeviceDriverDto> deviceDriverList)
        {
            foreach (var it in deviceDriverList)
            {
                InitThread(it);
            }
        }

        void InitThread(IotDeviceDriverDto it)
        {
            try
            {
                deviceKit.AddDeviceHandle(it);
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace($"[ServiceBase] InitThread 异常: {it.device_id}, {ex.Message}");
            }
        }

        private void timerDeviceStatus_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                _timerDeviceStatus.Stop();
                
                if (Debug && DateTime.Now.Second % 60 == 0)
                {
                    LogKit.WriteAndTrace($"[System] Memory: {(GC.GetTotalMemory(false) / (1024.0 * 1024.0)):F2} MB", "System");
                }

                if (RUN_DURATION > 0)
                {
                    var duration = (DateTime.Now - StartTime).TotalMinutes;
                    if (duration >= RUN_DURATION)
                    {
                        LogKit.WriteAndTrace("[System] 演示模式运行时间到，服务即将退出！");
                        Dispose();
                        Process.GetCurrentProcess().Kill();
                    }
                }

                _devices = deviceKit.DeviceList;
                if (_devices == null || _devices.Count == 0) return;

                var devices = _devices.Values.ToList();
                foreach (var device in devices)
                {
                    if (device is DeviceBase pb)
                    {
                        if (Debug && DateTime.Now.Second % 300 == 0)
                        {
                            var health = pb.GetConnectionHealthReport();
                            // 修正属性名称：IsOperational, SuccessRate
                            LogKit.WriteAndTrace($"[Health] Device: {pb.DeviceId}, Connected: {health.IsOperational}, SuccessRate: {health.SuccessRate:P1}");
                        }
                    }
                }
            }
            catch (Exception ex) 
            { 
                LogKit.WriteAndTrace($"[ServiceBase] timerDeviceStatus 异常: {ex.Message}"); 
            }
            finally
            {
                _timerDeviceStatus.Start();
            }
        }

        public void Stop()
        {
            _timerDeviceStatus?.Stop();
            deviceKit?.StopAll();
            cancelTokenSource?.Cancel();
            TaskList.Clear();
        }

        public void Dispose()
        {
            Stop();
            if (_timerDeviceStatus != null)
            {
                _timerDeviceStatus.Close();
                _timerDeviceStatus = null;
            }
            cancelTokenSource?.Dispose();
        }
    }
}
