using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ZL.Dao.IotDevice;
using ZL.PFLite.Common;

namespace ZL.Iot.Plugin
{
    /// <summary>
    /// 设备驱动管理器。动态加载驱动 DLL 并管理设备实例生命周期。
    /// </summary>
    public class DeviceKit
    {
        private static readonly Lazy<DeviceKit> _instance = new Lazy<DeviceKit>(() => new DeviceKit());
        private readonly SortedList<string, IDev> _deviceList = new SortedList<string, IDev>();
        private readonly Dictionary<string, Type> _driverTypeDic = new Dictionary<string, Type>();

        private DeviceKit() { }

        /// <summary>获取全局单例</summary>
        public static DeviceKit GetInstance() => _instance.Value;

        /// <summary>已注册的设备列表</summary>
        public SortedList<string, IDev> DeviceList => _deviceList;

        /// <summary>从数据库配置下载并加载所有驱动 DLL</summary>
        public bool DownAndLoadDrivers()
        {
            try
            {
                // 当前目录下查找驱动 DLL
                string driversDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Drivers");
                if (!Directory.Exists(driversDir)) return false;

                foreach (var dll in Directory.GetFiles(driversDir, "*.dll"))
                {
                    try
                    {
                        Assembly ass = Assembly.LoadFrom(dll);
                        foreach (var type in ass.GetExportedTypes())
                        {
                            if (!type.IsAbstract && typeof(IPLCDriver).IsAssignableFrom(type))
                            {
                                string key = type.FullName;
                                if (!_driverTypeDic.ContainsKey(key))
                                    _driverTypeDic[key] = type;
                            }
                        }
                    }
                    catch { /* skip invalid assemblies */ }
                }

                return _driverTypeDic.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>根据 DTO 配置创建一个驱动实例</summary>
        public IPLCDriver CreateDriver(IotDeviceDriverDto it, out string argStr)
        {
            argStr = it.extra_config ?? "";

            if (string.IsNullOrEmpty(it.driver_full_class_name))
                return null;

            // 先尝试在已加载类型中查找
            if (_driverTypeDic.TryGetValue(it.driver_full_class_name, out Type type))
            {
                return Activator.CreateInstance(type) as IPLCDriver;
            }

            // 尝试从指定 assembly 加载
            if (!string.IsNullOrEmpty(it.driver_assembly_name))
            {
                try
                {
                    string dllPath = it.driver_assembly_name;
                    if (!File.Exists(dllPath))
                    {
                        dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Drivers", it.driver_assembly_name);
                        if (!File.Exists(dllPath))
                            dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, it.driver_assembly_name);
                    }

                    if (File.Exists(dllPath))
                    {
                        Assembly ass = Assembly.LoadFrom(dllPath);
                        type = ass.GetType(it.driver_full_class_name);
                        if (type != null)
                        {
                            _driverTypeDic[it.driver_full_class_name] = type;
                            return Activator.CreateInstance(type) as IPLCDriver;
                        }
                    }
                }
                catch { /* load failed */ }
            }

            // 最后尝试 Type.GetType
            try
            {
                type = Type.GetType(it.driver_full_class_name);
                if (type != null)
                {
                    _driverTypeDic[it.driver_full_class_name] = type;
                    return Activator.CreateInstance(type) as IPLCDriver;
                }
            }
            catch { /* failed */ }

            return null;
        }

        /// <summary>将驱动实例包装为 IDev 并注册到设备列表</summary>
        public void AddSimpleInteractHandle(IPLCDriver dv, string device_id, string company_id, string plant_id, string line, string region_no, string station_no)
        {
            if (dv == null || _deviceList.ContainsKey(device_id)) return;

            var wrapper = new SimpleInteractWrapper(dv, device_id);
            _deviceList[device_id] = wrapper;
        }
    }
}
