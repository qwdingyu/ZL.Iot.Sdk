// ============================================================
//  单设备生命周期管理器
//  每个 SingleDeviceRunner 实例对应配置文件中的一个设备实例
//  负责：连接初始化 → 采集循环启动 → 触发回调 → 优雅停止
// ============================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZL.Biz.Execute.Biz;
using ZL.Biz.Execute.Conditions;
using ZL.Iot.Interface;
using ZL.Iot.Runner.Configuration;
using ZL.IotHub.Core;
using ZL.IotHub.Hsl;
using ZL.Tag;

namespace ZL.Iot.Runner.Runtime
{
    /// <summary>
    /// 单设备生命周期管理器
    ///
    /// 实现 ITagValueProvider，为 TriggerExecutor 提供标签读取和反馈写回能力
    ///
    /// 职责：
    /// 1. 使用 HslDriverFactory.Create(config) 创建 DeviceRoot 驱动实例
    /// 2. 加载标签列表到 driver.Tags
    /// 3. 初始化连接（Initialize）+ 启动后台采集（StartBackgroundTasks）
    /// 4. 订阅 OnTriggerDataChanged 事件，传递给 TriggerExecutor
    /// 5. 提供状态查询（GetStatus）和优雅停止（Stop）
    /// 
    /// 使用 HslUnifiedDriver 而非直接继承西门子驱动，确保：
    /// - 一行代码创建，支持 15+ PLC 协议
    /// - 更换协议无需修改代码，只需改配置文件
    /// </summary>
    public class SingleDeviceRunner : ITagValueProvider, IDisposable
    {
        private readonly string _deviceCode;
        private readonly DeviceRoot _driver;
        private TriggerExecutor _executor;  // 非 readonly：Create 工厂方法先构造再赋值
        private HistoryStoragePipeline? _historyStorage;
        private readonly ILogger<SingleDeviceRunner> _logger;
        private CancellationTokenSource? _cts;
        private bool _isRunning = false;
        private int _collectedCount = 0;

        // 缓存设备元数据（用于状态显示）
        // 原因：HslUnifiedDriver 没有公开 Port 属性，反射会失败被 catch 吞掉返回 0
        // 改为缓存 profile 原始值，确保状态信息准确
        private readonly string _cachedProtocol;
        private readonly string _cachedIp;
        private readonly int _cachedPort;

        /// <summary>
        /// 设备编码（唯一标识）
        /// </summary>
        public string DeviceCode => _deviceCode;

        /// <summary>
        /// 设备是否在运行中
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 创建单设备运行器
        /// </summary>
        /// <param name="deviceCode">设备编码（唯一标识）</param>
        /// <param name="driver">DeviceRoot 驱动实例（HslUnifiedDriver）</param>
        /// <param name="executor">触发执行器</param>
        /// <param name="logger">日志记录器</param>
        public SingleDeviceRunner(
            string deviceCode,
            DeviceRoot driver,
            TriggerExecutor executor,
            ILogger<SingleDeviceRunner> logger,
            string protocol = "",
            string ip = "",
            int port = 0,
            HistoryStoragePipeline? historyStorage = null)
        {
            _deviceCode = deviceCode ?? throw new ArgumentNullException(nameof(deviceCode));
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _historyStorage = historyStorage;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 缓存设备元数据（用于状态显示，不依赖 driver 反射）
            _cachedProtocol = protocol;
            _cachedIp = ip;
            _cachedPort = port;

            // 订阅触发事件：当 HslUnifiedDriver.TriggerDataChanged 触发时，传递给执行器
            if (_driver is HslUnifiedDriver unified)
            {
                unified.TriggerDataChanged += OnTagTriggered;
            }
            else
            {
                // 兼容其他 DeviceRoot 子类（罕见情况）
                _logger.LogWarning("[{Code}] 当前驱动类型不支持事件订阅（{Type}），触发回调可能无法工作",
                    _deviceCode, _driver.GetType().Name);
            }
        }

        /// <summary>
        /// 创建设备驱动：使用 HslDriverFactory.Create(config) 创建 HslUnifiedDriver
        /// </summary>
        public static SingleDeviceRunner Create(
            DeviceProfile profile,
            ILoggerFactory loggerFactory,
            DataStorageOptions? storage = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var logger = loggerFactory.CreateLogger<SingleDeviceRunner>();

            // 1. 将 Profile 转换为 DeviceConfig（HslDriverFactory.Create 的参数格式）
            var deviceConfig = ProfileToDeviceConfig(profile);

            // 2. 创建驱动实例（一行代码，支持 15+ 协议）
            var driver = HslDriverFactory.Create(deviceConfig);

            // 3. 加载标签列表到 driver.Tags
            var tagDict = ProfileTagsToDict(profile.Tags);
            foreach (var kvp in tagDict)
            {
                driver.Tags.TryAdd(kvp.Key, kvp.Value);
            }

            // 4. 先创建 Runner（使用空执行器占位，后面替换为带 SQL/Provider 的执行器）
            var placeholderExecutor = new TriggerExecutor(
                profile.Executors,
                loggerFactory.CreateLogger<TriggerExecutor>());
            var runner = new SingleDeviceRunner(profile.Code, driver, placeholderExecutor, logger,
                protocol: profile.Protocol, ip: profile.Ip, port: profile.Port);

            // 5. 创建执行器（传入 runner 自身作为 ITagValueProvider）
            var executorLogger = loggerFactory.CreateLogger<TriggerExecutor>();
            var sqlExecutor = CreateSqlExecutor(storage, loggerFactory, logger);
            var historyLogger = loggerFactory.CreateLogger<HistoryStoragePipeline>();
            var historyStorage = storage?.History?.Enabled == true && sqlExecutor != null
                ? new HistoryStoragePipeline(profile.Code, storage.History, sqlExecutor, historyLogger)
                : null;
            var executor = sqlExecutor != null && storage != null
                ? new TriggerExecutor(
                    profile.Executors,
                    storage,
                    new ScribanScriptEngine(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<ScribanScriptEngine>.Instance),
                    new RulesEngineAdapter(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<RulesEngineAdapter>.Instance),
                    sqlExecutor,
                    executorLogger,
                    tagValueProvider: runner)
                : new TriggerExecutor(profile.Executors, executorLogger,
                    tagValueProvider: runner);
            runner._executor = executor;
            runner._historyStorage = historyStorage;

            return runner;
        }

        /// <summary>
        /// 根据 Runner DataStorage 创建 SQL 执行器；当前生产闭环优先支持 SQLite。
        /// </summary>
        private static ISqlExecutor? CreateSqlExecutor(
            DataStorageOptions? storage,
            ILoggerFactory loggerFactory,
            ILogger<SingleDeviceRunner> logger)
        {
            if (storage == null || string.Equals(storage.Type, "None", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Runner DataStorage 未启用，FieldMapping/SQL 将写入 JSONL 降级文件");
                return null;
            }

            if (!string.Equals(storage.Type, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Runner DataStorage 类型 {Type} 暂未接入，FieldMapping/SQL 将写入 JSONL 降级文件", storage.Type);
                return null;
            }

            var dbPath = ResolveSqlitePath(storage.ConnectionString);
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? AppContext.BaseDirectory);

            logger.LogInformation("Runner DataStorage 已启用 SQLite: {Path}", dbPath);
            return new SqliteExecutor(loggerFactory.CreateLogger<SqliteExecutor>(), dbPath);
        }

        /// <summary>
        /// 兼容完整连接串和裸路径，统一提取 SQLite 文件路径。
        /// </summary>
        private static string ResolveSqlitePath(string? connectionString)
        {
            var raw = string.IsNullOrWhiteSpace(connectionString)
                ? "./data/iot_runner.db"
                : connectionString.Trim();

            const string dataSourcePrefix = "Data Source=";
            if (raw.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[dataSourcePrefix.Length..].Split(';', 2)[0].Trim();
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = "./data/iot_runner.db";
            }

            return Path.GetFullPath(raw, AppContext.BaseDirectory);
        }

        /// <summary>
        /// 启动设备驱动
        /// 1. 调用 driver.Initialize() 初始化连接
        /// 2. 调用 driver.StartBackgroundTasks() 启动后台采集循环
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                _logger.LogWarning("[{Code}] 设备已在运行中，忽略重复启动", _deviceCode);
                return;
            }

            _cts = new CancellationTokenSource();
            _isRunning = true;

            try
            {
                // 初始化连接（此方法会建立与 PLC 的 TCP 连接）
                _driver.Initialize();
                _logger.LogInformation("[{Code}] 设备驱动初始化完成（协议={Protocol}, IP={Ip}:{Port}）",
                    _deviceCode, GetProtocol(), GetIp(), GetPort());

                // 启动后台采集任务（按 ScanRate 轮询标签值）
                _driver.StartBackgroundTasks();
                _logger.LogInformation("[{Code}] 设备进入采集循环（标签数={TagCount}）",
                    _deviceCode, _driver.Tags.Count);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _logger.LogError(ex, "[{Code}] 设备启动失败", _deviceCode);
                throw;
            }
        }

        /// <summary>
        /// 停止设备驱动
        /// 1. 取消后台采集任务
        /// 2. 释放驱动资源（TCP 连接等）
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                _historyStorage?.Dispose();
                _historyStorage = null;
                return;
            }

            _cts?.Cancel();
            _isRunning = false;

            try
            {
                // 释放驱动资源（HslUnifiedDriver 实现了 IDisposable）
                (_driver as IDisposable)?.Dispose();
                _historyStorage?.Dispose();
                _historyStorage = null;
                _logger.LogInformation("[{Code}] 设备驱动已停止", _deviceCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Code}] 设备停止异常", _deviceCode);
            }
        }

        /// <summary>
        /// 获取设备当前状态
        /// </summary>
        public DeviceStatus GetStatus()
        {
            var isConnected = _driver switch
            {
                DeviceBase db => db.IsConnected,
                _ => _isRunning && !_driver.Tags.IsEmpty
            };

            return new DeviceStatus
            {
                Code = _deviceCode,
                IsConnected = isConnected,
                Protocol = GetProtocol(),
                Ip = GetIp(),
                Port = GetPort(),
                TagCount = _driver.Tags.Count,
                CollectedCount = Interlocked.Exchange(ref _collectedCount, 0), // 重置计数
                LastCollectTime = DateTime.Now,
                ErrorMessage = null
            };
        }

        // ============================================================
        //  ITagValueProvider 实现
        //  供 TriggerExecutor F 分支使用
        // ============================================================

        /// <summary>
        /// 读取标签当前值（优先从驱动缓存，fallback 实时读取）
        /// </summary>
        public object? ReadTag(string tagId)
        {
            try
            {
                // 从驱动缓存读取（TagItem.CurrentValue）
                if (_driver.Tags.TryGetValue(tagId, out var tagItem))
                {
                    if (!string.IsNullOrEmpty(tagItem.CurrentValue))
                        return tagItem.CurrentValue;
                }

                // fallback：通过 dynamic 调用 Read 方法
                dynamic d = _driver;
                return d.Read(tagId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Code}] 读取标签失败: {TagId}", _deviceCode, tagId);
                return null;
            }
        }

        /// <summary>
        /// 写入标签值到 PLC（FieldMapping 反馈写回使用）
        /// </summary>
        public bool WriteTag(string tagId, object value)
        {
            try
            {
                dynamic d = _driver;
                d.Write(tagId, value);
                _logger.LogInformation("[{Code}] 写入标签成功: {TagId}={Value}", _deviceCode, tagId, value);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Code}] 写入标签失败: {TagId}", _deviceCode, tagId);
                return false;
            }
        }

        /// <summary>
        /// 当标签值变化时触发（DeviceRoot.OnTriggerDataChanged 回调）
        /// 将触发事件传递给 TriggerExecutor 进行条件判断和执行
        /// </summary>
        private void OnTagTriggered(string tagId, object value, TagItem tag)
        {
            Interlocked.Increment(ref _collectedCount);
            _logger.LogDebug("[{Code}] 标签触发 | Tag={TagId}, Value={Value}, Type={TagType}",
                _deviceCode, tagId, value, tag?.TagType);

            try
            {
                _historyStorage?.TryEnqueue(tagId, value, tag);
                _executor.OnTagChanged(tagId, value, quality: 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Code}] 触发执行异常 | Tag={TagId}", _deviceCode, tagId);
            }
        }

        /// <summary>
        /// 将 DeviceProfile 转换为 HslDriverFactory.Create 需要的 DeviceConfig 格式
        /// </summary>
        private static DeviceConfig ProfileToDeviceConfig(DeviceProfile profile)
        {
            var config = new DeviceConfig();

            // 必需参数
            config.SetParam("DeviceId", profile.Code);
            config.SetParam("DeviceIp", profile.Ip);
            config.SetParam("Protocol", profile.Protocol);
            config.SetParam("Port", profile.Port);

            // 可选参数（S7 专用）
            if (profile.Rack > 0 || profile.Slot > 0)
            {
                config.SetParam("Rack", profile.Rack);
                config.SetParam("Slot", profile.Slot);
            }

            // 连接参数
            config.SetParam("ConnectTimeOut", profile.ConnectTimeout > 0 ? profile.ConnectTimeout : 5000);
            config.SetParam("ReadInterval", profile.ReadInterval > 0 ? profile.ReadInterval : 200);

            return config;
        }

        /// <summary>
        /// 将 TagProfile 列表转换为 ConcurrentDictionary<string, TagItem>
        /// </summary>
        private static System.Collections.Concurrent.ConcurrentDictionary<string, TagItem> ProfileTagsToDict(
            System.Collections.Generic.List<TagProfile> tags)
        {
            var dict = new System.Collections.Concurrent.ConcurrentDictionary<string, TagItem>();

            foreach (var t in tags ?? new System.Collections.Generic.List<TagProfile>())
            {
                if (!t.Enable) continue;
                if (string.IsNullOrWhiteSpace(t.Id)) continue;

                dict[t.Id] = new TagItem
                {
                    Id = t.Id,
                    Description = t.Description,
                    Address = t.Address,
                    DataTypeCode = t.DataType,
                    Length = t.Length,
                    StringEncoding = t.StringEncoding,
                    Enable = t.Enable,
                    TagType = t.TagType,
                    Deadband = t.Deadband,
                    ScanRate = t.ScanRate
                };
            }

            return dict;
        }

        private string GetProtocol()
        {
            // 优先使用缓存（profile.Protocol），避免依赖 driver 反射
            if (!string.IsNullOrEmpty(_cachedProtocol)) return _cachedProtocol;
            if (_driver == null) return "Unknown";

            // 兜底：HslUnifiedDriver 暴露 ProtocolKey / ProtocolDisplayName
            try
            {
                dynamic d = _driver;
                return (d.ProtocolKey?.ToString())
                       ?? (d.ProtocolDisplayName?.ToString())
                       ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetIp()
        {
            // 优先使用缓存（profile.Ip）
            if (!string.IsNullOrEmpty(_cachedIp)) return _cachedIp;
            if (_driver == null) return "Unknown";

            // 兜底：DeviceRoot.DeviceIp（基类）
            if (!string.IsNullOrEmpty(_driver.DeviceIp)) return _driver.DeviceIp;
            return "Unknown";
        }

        private int GetPort()
        {
            // 优先使用缓存（profile.Port）
            if (_cachedPort > 0) return _cachedPort;
            return 0;
        }

        public void Dispose()
        {
            Stop();

            // 取消事件订阅，避免触发器回调到已释放的对象（防止内存泄漏）
            if (_driver is HslUnifiedDriver unified)
            {
                unified.TriggerDataChanged -= OnTagTriggered;
            }

            _cts?.Dispose();
        }
    }

    /// <summary>
    /// 设备状态信息
    /// </summary>
    public class DeviceStatus
    {
        /// <summary>设备编码</summary>
        public string Code { get; set; } = "";

        /// <summary>是否已连接</summary>
        public bool IsConnected { get; set; }

        /// <summary>协议类型</summary>
        public string Protocol { get; set; } = "";

        /// <summary>IP 地址</summary>
        public string Ip { get; set; } = "";

        /// <summary>端口</summary>
        public int Port { get; set; }

        /// <summary>标签数量</summary>
        public int TagCount { get; set; }

        /// <summary>采集计数（自上次查询以来）</summary>
        public long CollectedCount { get; set; }

        /// <summary>最后采集时间</summary>
        public DateTime LastCollectTime { get; set; }

        /// <summary>错误信息（如果有）</summary>
        public string? ErrorMessage { get; set; }

        public override string ToString()
        {
            return $"[{Code}] {(IsConnected ? "●" : "○")} {Protocol}@{Ip}:{Port} | Tags={TagCount} | Collected={CollectedCount}";
        }
    }
}