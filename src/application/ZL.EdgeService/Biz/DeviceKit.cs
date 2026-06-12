using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ZL.Dao.IotDevice;
using ZL.Iot.Interface;
using ZL.PFLite.Common;
using ZL.IotHub.Core;

namespace ZL.EdgeService
{
    /// <summary>
    /// 创建、实例化、连接设备的帮助类
    /// 针对 PlcBase 架构深度优化：全面转向新一代驱动内核
    /// </summary>
    public class DeviceKit
    {
        private static readonly ConcurrentDictionary<string, Lazy<DeviceKit>> _instances = new();
        
        private Assembly MainAssembly;

        public ConcurrentDictionary<string, Type> DriverTypeDic = new ConcurrentDictionary<string, Type>();
        
        public SortedList<string, object> DeviceList = new SortedList<string, object>();

        private IotDriverService IotDriverSrv = new IotDriverService();
        private IotEdgeRelationService iotEdgeRelationSrv = new IotEdgeRelationService();
        
        public string InstanceId { get; private set; }

        public static DeviceKit GetInstance(string edgeId)
        {
            var lazy = _instances.GetOrAdd(edgeId, id => 
                new Lazy<DeviceKit>(() => new DeviceKit(id), 
                    LazyThreadSafetyMode.ExecutionAndPublication));
            return lazy.Value;
        }

        public static DeviceKit GetInstance(Assembly main_ass, string edgeId)
        {
            var lazy = _instances.GetOrAdd(edgeId, id => 
                new Lazy<DeviceKit>(() => new DeviceKit(main_ass, id), 
                    LazyThreadSafetyMode.ExecutionAndPublication));
            return lazy.Value;
        }

        public static DeviceKit GetInstance(string device_id, Assembly main_ass = null)
        {
            string edgeId = new IotEdgeRelationService().GetEdgeIdByDeviceId(device_id);
            string key = !string.IsNullOrEmpty(edgeId) ? edgeId : device_id;
            var lazy = _instances.GetOrAdd(key, id => 
                new Lazy<DeviceKit>(() => new DeviceKit(device_id, main_ass), 
                    LazyThreadSafetyMode.ExecutionAndPublication));
            return lazy.Value;
        }
        
        public static void ClearAllInstances()
        {
            _instances.Clear();
        }
        
        public static bool RemoveInstance(string instanceId)
        {
            if (_instances.TryRemove(instanceId, out var lazy))
            {
                if (lazy.IsValueCreated)
                {
                    var instance = lazy.Value;
                    instance.StopAll();
                }
                return true;
            }
            return false;
        }

        public DeviceKit(Assembly assembly, string edgeId)
        {
            InstanceId = edgeId;
            MainAssembly = assembly;
            InitConfig(edgeId);
        }

        public DeviceKit(string edgeId)
        {
            InstanceId = edgeId;
            InitConfig(edgeId);
        }

        public DeviceKit(string device_id, Assembly assembly)
        {
            InstanceId = device_id;
            MainAssembly = assembly;
            string edgeId = iotEdgeRelationSrv.GetEdgeIdByDeviceId(device_id);
            InitConfig(edgeId);
        }

        private void InitConfig(string edgeId)
        {
            try
            {
                if (!Directory.Exists(EdgeConfig.DriversDir)) Directory.CreateDirectory(EdgeConfig.DriversDir);
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace($"DeviceKit.InitConfig 异常：{ex.Message}");
            }
        }

        public bool DownAndLoadDrivers(string EdgeId)
        {
            try
            {
                List<IotDriverAssemblyDto> DriverList = string.IsNullOrEmpty(InstanceId)
                    ? IotDriverSrv.GetDriverListByEdgeId(EdgeId)
                    : IotDriverSrv.GetDriverListByDeviceId(InstanceId);
                
                foreach (var it in DriverList)
                {
                    string driver_assembly_name = it.assembly_name;
                    string driver_full_class_name = it.class_full_name;
                    
                    string dllPath = DownloadDriver(driver_assembly_name);
                    if (string.IsNullOrEmpty(dllPath))
                    {
                        dllPath = GetDllPath(driver_assembly_name);
                    }

                    if (!DriverTypeDic.ContainsKey(driver_full_class_name) && !string.IsNullOrEmpty(dllPath))
                    {
                        Assembly ass = Assembly.LoadFrom(dllPath);
                        Type dvType = ass.GetType(driver_full_class_name);
                        if (dvType != null) DriverTypeDic.TryAdd(driver_full_class_name, dvType);
                    }
                }
                return DriverTypeDic.Count > 0;
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace($"下载驱动异常：{ex.Message}");
                return false;
            }
        }

        public string GetDllPath(string driver_assembly_name)
        {
            string dllFileName = driver_assembly_name + ".dll";
            string dllPath = Path.Combine(EdgeConfig.DriversDir, dllFileName);
            return File.Exists(dllPath) ? dllPath : string.Empty;
        }

        public string DownloadDriver(string driver_assembly_name)
        {
            try
            {
                string dllFileName = driver_assembly_name + ".dll";
                string targetPath = Path.Combine(EdgeConfig.DriversDir, dllFileName);
                
                if (File.Exists(targetPath)) return targetPath;

                string downloadUrl = $"{EdgeConfig.DriverRepoBaseUrl.TrimEnd('/')}/{dllFileName}";
                string tempPath = Path.Combine(EdgeConfig.TempDir, dllFileName);

                if (!Directory.Exists(EdgeConfig.TempDir))
                    Directory.CreateDirectory(EdgeConfig.TempDir);

                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EdgeConfig.DriverDownloadTimeoutMs) })
                {
                    var dllResponse = httpClient.GetAsync(downloadUrl).Result;
                    if (!dllResponse.IsSuccessStatusCode) return string.Empty;
                    var dllContent = dllResponse.Content.ReadAsByteArrayAsync().Result;
                    
                    File.WriteAllBytes(tempPath, dllContent);
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    File.Move(tempPath, targetPath);
                    
                    return targetPath;
                }
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace($"Driver download exception: {ex.Message}");
                return string.Empty;
            }
        }

        public object CreateInstance(Type type, IotDeviceDriverDto it)
        {
            if (typeof(ZL.IotHub.Core.IPlcDriver).IsAssignableFrom(type) || typeof(ZL.IotHub.Core.DeviceBase).IsAssignableFrom(type))
            {
                try
                {
                    var configCtor = type.GetConstructor(new[] { typeof(ZL.Tag.DeviceConfig) });
                    if (configCtor != null)
                    {
                        var config = BuildDeviceConfigFromDto(it);
                        return configCtor.Invoke(new object[] { config });
                    }
                    
                    var dtoCtor = type.GetConstructor(new[] { typeof(IotDeviceDriverDto) });
                    if (dtoCtor != null)
                    {
                        return dtoCtor.Invoke(new object[] { it });
                    }
                    
                    var noArgCtor = type.GetConstructor(Type.EmptyTypes);
                    if (noArgCtor != null) return noArgCtor.Invoke(null);
                }
                catch (Exception ex)
                {
                    LogKit.WriteAndTrace($"[DeviceKit] 创建驱动实例异常: {ex.Message}");
                }
            }
            return null;
        }
        
        private static ZL.Tag.DeviceConfig BuildDeviceConfigFromDto(IotDeviceDriverDto it)
        {
            var config = new ZL.Tag.DeviceConfig();
            config.Add("DeviceId", it.device_id);
            config.Add("DeviceIp", it.ip);
            
            int port = it.port > 0 ? it.port : 102;
            if (port == 102 && !string.IsNullOrEmpty(it.extra_config))
            {
                port = ParseIntFromJson(it.extra_config, "Port") ?? 102;
            }
            config.Add("Port", port);
            
            string protocolKey = ResolveProtocolFromDeviceType(it.device_type_id);
            config.Add("Protocol", protocolKey);
            
            if (!string.IsNullOrEmpty(it.extra_config))
            {
                AddJsonConfigToDeviceConfig(it.extra_config, config, port);
            }
            
            config.Add("Debug", it.debug);
            return config;
        }
        
        private static int? ParseIntFromJson(string json, string key)
        {
            try
            {
                var pattern = $"\"{key}\"\\s*:\\s*(\\d+)";
                var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int value)) return value;
            }
            catch { }
            return null;
        }
        
        private static void AddJsonConfigToDeviceConfig(string json, ZL.Tag.DeviceConfig config, int defaultPort)
        {
            try
            {
                int? rack = ParseIntFromJson(json, "Rack");
                if (rack.HasValue) config.Add("Rack", rack.Value);
                
                int? slot = ParseIntFromJson(json, "Slot");
                if (slot.HasValue) config.Add("Slot", slot.Value);
                
                int? baudRate = ParseIntFromJson(json, "BaudRate");
                if (baudRate.HasValue) config.Add("BaudRate", baudRate.Value);
                
                var parityMatch = System.Text.RegularExpressions.Regex.Match(json, "\"ParityBit\"\\s*:\\s*\"([^\"]+)\"");
                if (parityMatch.Success) config.Add("ParityBit", parityMatch.Groups[1].Value);
            }
            catch { }
        }
        
        private static string ResolveProtocolFromDeviceType(int deviceTypeId)
        {
            return deviceTypeId switch
            {
                1 => "siemens-s7",
                3 => "modbus-tcp",
                4 => "inovance-tcp",
                5 => "mitsubishi-mc",
                _ => "siemens-s7"
            };
        }

        public void AddDeviceHandle(IotDeviceDriverDto it)
        {
            try
            {
                if (DeviceList.ContainsKey(it.device_id)) return;

                string className = string.IsNullOrEmpty(it.device_class_name)
                    ? "ZL.EdgeService.EdgeExecutorKernel"
                    : it.device_class_name;

                Type type = null;
                if (!DriverTypeDic.TryGetValue(className, out type))
                {
                    Assembly bizAss = string.IsNullOrEmpty(it.device_assembly_name)
                        ? MainAssembly
                        : GetBizAssByDllPath(it.device_id, it.device_assembly_name);

                    if (bizAss != null) type = bizAss.GetType(className);
                    if (type == null) type = Type.GetType(className) ?? Assembly.GetExecutingAssembly().GetType(className);
                }

                if (type == null) return;

                object instance = CreateInstance(type, it);
                if (instance == null) return;

                DeviceList.Add(it.device_id, instance);

                // 核心修正：使用 IPlcDriver 接口进行异步连接调用
                if (instance is IPlcDriver driver)
                {
                    // 如果是 DeviceRoot 的子类，先初始化标签
                    if (instance is DeviceRoot root) root.Initialize();

                    Task.Run(async () => {
                        try {
                            await driver.ConnectAsync(CancellationToken.None);
                        } catch (Exception ex) {
                            LogKit.WriteAndTrace($"[DeviceKit] 设备 {it.device_id} 异步启动连接失败: {ex.Message}");
                        }
                    });
                    LogKit.WriteAndTrace($"[DeviceKit] 成功挂载并启动驱动: {it.device_id} ({className})");
                }
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace($"[DeviceKit] AddDeviceHandle 异常：{it.device_id}, {ex.Message}");
            }
        }

        public void StopAll()
        {
            foreach (var device in DeviceList.Values)
            {
                if (device is IDisposable d) try { d.Dispose(); } catch { }
            }
            DeviceList.Clear();
        }

        public void AddInteractHandle(IPlcDriver dv, IotDeviceDriverDto it) => AddDeviceHandle(it);

        public Assembly GetBizAssByDllPath(string device_id, string assembly_name)
        {
            string path = GetDllPath(assembly_name);
            return !string.IsNullOrEmpty(path) ? Assembly.LoadFrom(path) : null;
        }
    }
}
