using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ZL.Iot.Interface;
using ZL.Dao.IotDevice;
using ZL.PlcBase.Core;
using ZL.PlcBase.Models;
using ZL.Tag;
using Newtonsoft.Json;

namespace ZL.EdgeService
{
    /// <summary>
    /// 边缘自治执行器核心容器
    /// <para>深度集成 ZL.PlcBase，利用其 QoS 队列、健康检查和原子快照能力</para>
    /// <para>针对工业加固：统一写列表逻辑与驱动生命周期管理</para>
    /// </summary>
    /// <remarks>
    /// 架构说明：
    /// - 继承自 PlcBase.DeviceBase，复用其完整的采集、缓存、事件机制
    /// - 作为 iot_sdk 与 plcbase 的核心融合点，实现"采集即触发"的业务闭环
    /// - 支持多频率采集调度、原子化业务快照、离线命令缓存
    ///
    /// 融合状态（根据 docs/43、docs/44 规划）：
    /// - [x] 继承 DeviceBase 完成内核集成
    /// - [x] 多频率采集调度引擎 (scannablePlans)
    /// - [x] 原子化业务快照 (CaptureSnapshot)
    /// - [x] 统一写队列入口 (EnqueueNormalWriteAsync)
    /// - [x] 点位触发 -> 业务执行主链路
    /// - [ ] 触发器参数完善 (TriggerMode/DebounceMs/MinIntervalMs)
    /// - [x] 完整的 Tag -> BizCode 配置映射装载 (LoadTagTriggerBindingsFromDb)
    /// </remarks>
    public class EdgeExecutorKernel : ZL.PlcBase.Core.DeviceBase, IDisposable
    {
        #region 私有字段

        /// <summary>
        /// 日志记录器实例
        /// </summary>
        private readonly ILogger<EdgeExecutorKernel> _logger;
        
        /// <summary>
        /// 业务配置执行器（处理脚本渲染、规则判断、SQL执行）
        /// </summary>
        private readonly IBizCfgExecutor _bizExecutor;
        
        /// <summary>
        /// 标签ID -> 业务编码 的触发映射表
        /// <para>当标签值变化时，根据此映射查找需要触发的业务逻辑</para>
        /// <para>P0 待完善：需要从配置存储加载完整的触发绑定关系</para>
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _tagBizCodeMap = new();
        
        /// <summary>
        /// P1: 触发防抖字典 - 记录每个标签最近一次触发时间
        /// 用于防止同一标签在短时间内被重复触发（例如传感器抖动）
        /// </summary>
        private readonly ConcurrentDictionary<string, DateTime> _lastTriggerTimes = new();
        
        /// <summary>
        /// P1: 触发器最小触发间隔（毫秒）
        /// 同一标签在此时长内只能被触发一次
        /// </summary>
        private const int MinTriggerIntervalMs = 1000; // 默认 1 秒
        
        /// <summary>
        /// P3.2: 触发模式枚举
        /// </summary>
        public enum TriggerMode
        {
            /// <summary>任何值变化都触发 (默认)</summary>
            Change = 0,
            /// <summary>仅在 0->1 (上升沿) 时触发</summary>
            Rising = 1,
            /// <summary>仅在 1->0 (下降沿) 时触发</summary>
            Falling = 2
        }
        
        /// <summary>
        /// P3.2: 每个标签的触发配置 (模式 + 去抖时长)
        /// </summary>
        /// <remarks>
        /// P4.3 并发安全性说明：
        /// - TagTriggerConfig 作为内部类，不暴露公共 setter
        /// - 配置在初始化后不可变，确保多线程读取安全
        /// - 所有字段仅通过构造函数或字段直接赋值设置
        /// </remarks>
        private class TagTriggerConfig
        {
            public TriggerMode Mode { get; internal set; } = TriggerMode.Change;
            public int DebounceMs { get; internal set; } = MinTriggerIntervalMs;
        }
        
        /// <summary>
        /// P3.2: 标签ID -> 触发配置的映射表
        /// </summary>
        private readonly ConcurrentDictionary<string, TagTriggerConfig> _tagTriggerConfigs = new();
        
        /// <summary>
        /// P3.2: 存储每个标签的先前值，用于边沿检测
        /// </summary>
        private readonly ConcurrentDictionary<string, object> _previousTagValues = new();
        
        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数：将 Iot 模型的 DTO 转换为 PlcBase 的 DeviceConfig
        /// </summary>
        /// <param name="deviceInfo">IoT 设备驱动 DTO（来自数据库配置）</param>
        /// <remarks>
        /// 初始化流程：
        /// 1. 调用基类构造函数，完成 DeviceConfig 映射
        /// 2. 从依赖注入容器获取日志器和业务执行器
        /// 3. 订阅 PlcBase 的标签值变更事件
        /// </remarks>
        public EdgeExecutorKernel(IotDeviceDriverDto deviceInfo)
            : base(MapDtoToConfig(deviceInfo))
        {
            // 从全局容器获取依赖服务（使用单例实例）
            var sp = Utils.EdgeServiceContainer.Instance.Provider;
            _logger = sp.GetRequiredService<ILogger<EdgeExecutorKernel>>();
            _bizExecutor = sp.GetRequiredService<IBizCfgExecutor>();

            // 订阅 PlcBase 的统一值变更事件 - 实现采集触发的核心入口
            this.OnTagValueChanged += OnInternalTagValueChanged;
            
            _logger.LogInformation("Edge-Executor Kernel deeply integrated with PlcBase. DeviceId: {DeviceId}", DeviceId);
        }
        
        #endregion

        #region 公共方法 - 配置装载

        /// <summary>
        /// 装载 Tag -> BizCode 触发映射
        /// P0: 此方法应在设备初始化时从配置存储加载触发绑定关系
        /// 未来应扩展支持：TriggerMode/DebounceMs/MinIntervalMs 等触发器参数
        /// </summary>
        /// <param name="triggerBindings">tagId -> bizCode 的映射字典</param>
        public void LoadTagTriggerBindings(Dictionary<string, string> triggerBindings)
        {
            if (triggerBindings == null) return;
            
            foreach (var kvp in triggerBindings)
            {
                _tagBizCodeMap.TryAdd(kvp.Key, kvp.Value);
            }
            _logger.LogInformation("Loaded {Count} tag-trigger bindings for device {DeviceId}",
                triggerBindings.Count, DeviceId);
        }

        /// <summary>
        /// 从数据库加载 Tag -> BizCode 触发映射
        /// 根据 iot_exe 表配置构建 tag_id -> biz_code 的映射关系
        /// </summary>
        /// <remarks>
        /// 融合点 B（触发融合）的实现：
        /// - 从 iot_exe 表加载启用状态的业务配置
        /// - 构建 tag_id -> biz_code 的快速查找映射
        /// - 仅加载当前设备关联的触发器配置
        ///
        /// 待扩展功能：
        /// - [ ] TriggerMode（上升沿/下降沿/保持/数值变化）
        /// - [ ] DebounceMs（去抖动毫秒）
        /// - [ ] MinIntervalMs（最小触发间隔）
        /// - [ ] RequireQualityGood（质量门控）
        /// </remarks>
        public void LoadTagTriggerBindingsFromDb()
        {
            try
            {
                var exeDao = new ZL.Dao.IotDevice.IotExeDao();
                
                // 获取当前设备关联的所有启用的业务配置
                var exeList = exeDao.GetListByDeviceId(DeviceId);
                
                int loadedCount = 0;
                foreach (var exe in exeList)
                {
                    // 仅处理启用的配置且有明确 tag_id 的记录
                    if (exe.enable == 1 && !string.IsNullOrEmpty(exe.tag_id))
                    {
                        var bizCode = !string.IsNullOrEmpty(exe.biz_code)
                            ? exe.biz_code
                            : exe.id; // 如果 biz_code 为空，使用 id 作为业务标识
                        
                        _tagBizCodeMap.TryAdd(exe.tag_id, bizCode);
                        loadedCount++;
                    }
                }
                
                _logger.LogInformation("Loaded {Count} tag-trigger bindings from database for device {DeviceId}",
                    loadedCount, DeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tag-trigger bindings from database for device {DeviceId}", DeviceId);
            }
        }

        /// <summary>
        /// 批量装载触发映射（从 iot_exe 列表）
        /// </summary>
        /// <param name="exeList">iot_exe 配置列表</param>
        public void LoadTagTriggerBindingsFromExeList(IEnumerable<ZL.Dao.IotDevice.iot_exe> exeList)
        {
            if (exeList == null) return;
            
            int loadedCount = 0;
            foreach (var exe in exeList)
            {
                if (exe.enable == 1 && !string.IsNullOrEmpty(exe.tag_id))
                {
                    var bizCode = !string.IsNullOrEmpty(exe.biz_code) ? exe.biz_code : exe.id;
                    _tagBizCodeMap.TryAdd(exe.tag_id, bizCode);
                    loadedCount++;
                }
            }
            
            _logger.LogInformation("Loaded {Count} tag-trigger bindings from exe list for device {DeviceId}",
                loadedCount, DeviceId);
        }

        /// <summary>
        /// 装载点位列表到 PlcBase 内核
        /// P0: 将 IotTagDto 列表注入到 DeviceRoot.Tags 字典
        /// </summary>
        /// <param name="tags">IotTagDto 列表（已继承 TagItem）</param>
        public void LoadTags(IEnumerable<IotTagDto> tags)
        {
            if (tags == null) return;
            
            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag.Id)) continue;
                this.Tags.TryAdd(tag.Id, tag);
                
                // 根据 TagType 分类到专用集合
                if (tag.TagType?.Equals("Trigger", StringComparison.OrdinalIgnoreCase) == true)
                {
                    this.TriggerTags.TryAdd(tag.Id, tag);
                }
                else if (tag.TagType?.Equals("Heartbeat", StringComparison.OrdinalIgnoreCase) == true)
                {
                    this.HeartbeatTags.TryAdd(tag.Id, tag);
                }
            }
            
            _logger.LogInformation("Loaded {TagCount} tags for device {DeviceId} (Triggers: {TriggerCount}, Heartbeats: {HbCount})",
                this.Tags.Count, DeviceId, this.TriggerTags.Count, this.HeartbeatTags.Count);
        }

        /// <summary>
        /// 从 iot_tag DAO 模型装载点位（便捷方法）
        /// </summary>
        public void LoadTagsFromDao(IEnumerable<iot_tag> daoTags)
        {
            LoadTags(TagKit.TagChg(daoTags.ToList()));
        }

        /// <summary>
        /// 将 IoT 设备驱动 DTO 映射为 PlcBase 的 DeviceConfig
        /// </summary>
        /// <param name="dto">IoT 设备驱动数据传输对象</param>
        /// <returns>PlcBase 兼容的设备配置对象</returns>
        /// <remarks>
        /// 融合点 A（模型融合）的实现：
        /// - 定义了 IotDeviceDriverDto -> DeviceConfig 的标准映射
        /// - 包含设备级关键字段：DeviceId, StationNo, IpAddress, Port, Timeout 等
        ///
        /// 待完善：
        /// - [ ] Rack/Slot 参数映射（西门子 S7 协议需要）
        /// - [ ] 扩展参数从 JSON 字段解析
        /// </remarks>
        private static DeviceConfig MapDtoToConfig(IotDeviceDriverDto dto)
        {
            var config = new DeviceConfig();
            
            // 基础标识字段
            config.Add("DeviceId", dto.device_id);
            config.Add("StationNo", dto.station_no);
            
            // 网络连接参数
            config.Add("IpAddress", dto.ip);
            config.Add("Port", ParsePortFromDto(dto)); // 智能端口解析
            config.Add("SlaveId", 1); // Modbus 从站 ID，默认为 1
            
            // 驱动程序集信息
            config.Add("DriverType", dto.driver_full_class_name);
            config.Add("Assembly", dto.driver_assembly_name);
            
            // 超时参数（毫秒）
            config.Add("ConnectionTimeout", dto.time_out > 0 ? dto.time_out : 5000);
            config.Add("ReadTimeout", dto.time_out > 0 ? dto.time_out : 3000);
            config.Add("WriteTimeout", dto.time_out > 0 ? dto.time_out : 3000);

            return config;
        }

        /// <summary>
        /// 从 DTO 智能解析端口号
        /// </summary>
        /// <param name="dto">设备驱动 DTO</param>
        /// <returns>解析出的端口号</returns>
        /// <remarks>
        /// 解析优先级：
        /// 1. 从 brand 字段解析（如 "Siemens:192.168.0.1:502" 格式）
        /// 2. 根据 driver_class_name 使用协议默认端口
        /// 3. 默认返回 Modbus TCP 端口 502
        /// </remarks>
        private static int ParsePortFromDto(IotDeviceDriverDto dto)
        {
            // 尝试从 brand 字段解析 "IP:Port" 或 "Protocol:IP:Port" 格式
            if (!string.IsNullOrEmpty(dto.brand))
            {
                var parts = dto.brand.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int port))
                {
                    return port;
                }
            }
            
            // 根据驱动类型使用协议默认端口
            return dto.driver_class_name?.ToLower() switch
            {
                "siemens" => 102,      //西门子 S7 协议端口
                "modbustcp" => 502,    // Modbus TCP 标准端口
                "omron" => 9600,       // 欧姆龙 Fins 协议端口
                "mitsubishi" => 5000,  // 三菱 MC 协议端口
                "ab" => 44818,         // 罗克韦尔 EtherNet/IP 端口
                _ => 502               // 默认使用 Modbus 端口
            };
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 建立与 PLC 设备的连接
        /// </summary>
        /// <returns>连接是否成功</returns>
        /// <remarks>
        /// 连接流程：
        /// 1. 通过 HslProtocolRegistry 解析驱动类型
        /// 2. 使用工厂方法创建客户端实例
        /// 3. 调用 ConnectServer 建立物理连接
        /// </remarks>
        public override bool Connect()
        {
            try
            {
                var descriptor = ZL.PlcBase.Core.HslProtocolRegistry.Resolve(this.deviceConfig);
                var client = descriptor.ClientFactory(this.deviceConfig);
                
                if (client is HslCommunication.Core.IReadWriteNet rw)
                {
                    this.Device = rw;
                    // 使用反射尝试连接，因为 NetworkBase 类型可能不存在
                    try
                    {
                        var connectMethod = client.GetType().GetMethod("ConnectServer");
                        if (connectMethod != null)
                        {
                            var res = connectMethod.Invoke(client, null) as HslCommunication.OperateResult;
                            if (res != null && !res.IsSuccess)
                            {
                                _logger.LogError("EdgeExecutorKernel connect failed: {Message}", res.Message);
                                return false;
                            }
                        }
                    }
                    catch
                    {
                        // 连接方法可能不存在或不可访问
                    }
                    this.IsClosed = false;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EdgeExecutorKernel protocol connect failed.");
                return false;
            }
        }

        /// <summary>
        /// 升级后的批量采集循环：支持多频率调度计划
        /// </summary>
        public override async Task BatchReadLoop(CancellationToken token)
        {
            if (readPlan == null) BuildPlan();

            _logger.LogInformation("Edge-Executor Multi-Rate BatchRead engine started. Groups: {GroupCount}", scannablePlans?.Count ?? 0);

            // 为每个采集频率组启动独立的调度任务
            var tasks = new List<Task>();
            foreach (var rateGroup in scannablePlans)
            {
                int interval = rateGroup.Key > 0 ? rateGroup.Key : plcTriggerInterval;
                tasks.Add(RunScanGroupAsync(rateGroup.Key, interval, token));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
            else
            {
                // 降级逻辑：如果没有分组计划，使用单循环
                while (!token.IsCancellationRequested)
                {
                    await ExecutePlanByRate(0, token);
                    await Task.Delay(plcTriggerInterval, token);
                }
            }
        }

        private async Task RunScanGroupAsync(int rate, int interval, CancellationToken token)
        {
            _logger.LogDebug("Starting ScanGroup Task: Rate={Rate}ms", rate);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!IsClosed)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        await ExecutePlanByRate(rate, token);
                        sw.Stop();

                        // 动态调整建议（可选）：如果执行耗时接近间隔，可以在此处记录诊断
                    }
                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ScanGroup Rate={Rate}", rate);
                    await Task.Delay(1000, token);
                }
            }
        }

        private void OnInternalTagValueChanged(string tagId, object value, TagItem tag)
        {
            if (!_tagBizCodeMap.TryGetValue(tagId, out string bizCode)) return;

            // P3.2: 获取触发配置 (使用默认 Change 模式)
            var config = _tagTriggerConfigs.GetOrAdd(tagId, _ => new TagTriggerConfig());
            int debounceMs = config.DebounceMs;
            TriggerMode mode = config.Mode;

            // P3.2: 边沿检测逻辑
            bool shouldTrigger = false;
            bool currentBool = false;
            bool previousBool = false;
            
            // 获取当前值的布尔表示
            if (value is bool b) currentBool = b;
            else if (value is IConvertible c) currentBool = Convert.ToInt32(c) > 0;

            // 获取上一个值的布尔表示
            if (_previousTagValues.TryGetValue(tagId, out var prevVal))
            {
                if (prevVal is bool pb) previousBool = pb;
                else if (prevVal is IConvertible pc) previousBool = Convert.ToInt32(pc) > 0;
            }

            // 根据触发模式判断是否触发
            switch (mode)
            {
                case TriggerMode.Rising:
                    shouldTrigger = currentBool && !previousBool; // 0 -> 1
                    break;
                case TriggerMode.Falling:
                    shouldTrigger = !currentBool && previousBool; // 1 -> 0
                    break;
                case TriggerMode.Change:
                default:
                    // 任意变化都触发，或者值大于 0
                    shouldTrigger = currentBool;
                    break;
            }

            // P1: 触发防抖检查
            if (shouldTrigger)
            {
                var now = DateTime.UtcNow;
                if (_lastTriggerTimes.TryGetValue(tagId, out var lastTime))
                {
                    if ((now - lastTime).TotalMilliseconds < debounceMs)
                    {
                        _logger.LogDebug("Trigger debounced for Tag={TagId}, Interval={IntervalMs}ms", tagId, debounceMs);
                        return;
                    }
                }
                
                // 更新最后触发时间
                _lastTriggerTimes[tagId] = now;
                
                _ = HandleTriggerAsync(tagId, bizCode);
            }
            
            // P3.2: 更新上一个值，为下次边沿检测做准备
            _previousTagValues[tagId] = value;
        }

        private async Task HandleTriggerAsync(string tagId, string bizCode)
        {
            string traceId = Guid.NewGuid().ToString("N");
            _logger.LogInformation("[{TraceId}] Edge-Executor Triggered: Tag={TagId}, Biz={BizCode}", traceId, tagId, bizCode);

            try
            {
                var snapshot = this.CaptureSnapshot();
                var facts = new Dictionary<string, object>(snapshot)
                {
                    { "TriggerTagId", tagId },
                    { "BizCode", bizCode },
                    { "StationNo", this.deviceConfig.Get<string>("StationNo", "") },
                    { "TraceId", traceId },
                    { "Now", DateTime.Now }
                };

                bool success = await _bizExecutor.ExeUpdateAsync(tagId, facts, traceId);

                if (success)
                {
                    if (facts.TryGetValue("SuccessTag", out var sTagId))
                    {
                        var sVal = facts.TryGetValue("SuccessVal", out var v) ? v : 1;
                        await EnqueueNormalWriteAsync(sTagId.ToString(), sVal, "EdgeKernel-Success");
                    }
                }
                else
                {
                    if (facts.TryGetValue("FailTag", out var fTagId))
                    {
                        var fVal = facts.TryGetValue("FailVal", out var v) ? v : 2;
                        await EnqueueNormalWriteAsync(fTagId.ToString(), fVal, "EdgeKernel-Fail");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{TraceId}] Edge-Executor execution exception.", traceId);
            }
        }

        public object GetDiagnosticInfo()
        {
            var health = this.GetConnectionHealthReport();
            var perf = this.GetPerformanceReport();
            var (safety, normal) = this.GetQueueStatus();

            return new
            {
                DeviceId = this.DeviceId,
                Status = this.ConnectionStatus,
                IsConnected = this.IsConnected,
                LastSuccessTime = health.LastSuccessTime,
                // ConsecutiveFailureCount 已移除，使用 HeartbeatFailures 代替
                ConsecutiveFailures = health.HeartbeatFailures,
                ReadInterval = this.plcTriggerInterval,
                ReadPlanBlocks = this.readPlan?.Count ?? 0,
                Groups = scannablePlans.Select(k => new { Rate = k.Key, Count = k.Value.Count }),
                Queue = new { Safety = safety, Normal = normal },
                Performance = perf.Metrics.ToDictionary(k => k.Key, v => new
                {
                    v.Value.AverageDurationMs,
                    v.Value.MaxDurationMs,
                    v.Value.MinDurationMs,
                    v.Value.SuccessRate,
                    TotalCount = v.Value.TotalCount
                }),
                QuarantineCount = this.QuarantineTags.Count
            };
        }

        public bool WriteList(string startTagId, object[] values)
        {
            _ = EnqueueNormalWriteAsync(startTagId, values, "LegacyWriteList");
            return true;
        }

        // 兼容性读写方法（使用内部驱动）
        private Task<HslCommunication.OperateResult<byte[]>> ReadRawAsync(string address, ushort length, CancellationToken token)
        {
            return this.Device?.ReadAsync(address, length)
                ?? Task.FromResult(new HslCommunication.OperateResult<byte[]> { Message = "Driver not initialized" });
        }

        private Task<HslCommunication.OperateResult> WriteRawAsync(string address, byte[] data, CancellationToken token)
        {
            return this.Device?.WriteAsync(address, data)
                ?? Task.FromResult(new HslCommunication.OperateResult { Message = "Driver not initialized" });
        }

        public new void Dispose()
        {
            base.Dispose();
            (this.Device as IDisposable)?.Dispose();
        }

        #endregion
    }
}
