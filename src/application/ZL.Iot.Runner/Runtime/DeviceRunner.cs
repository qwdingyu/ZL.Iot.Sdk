// ============================================================
//  多设备协调运行器
//  管理多个 SingleDeviceRunner 实例，统一提供启动/停止/状态查询
//  设计原则：一个配置文件 → 多个设备驱动实例 → 独立并发运行
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Configuration;

namespace ZL.Iot.Runner.Runtime
{
    /// <summary>
    /// 多设备协调运行器
    /// 
    /// 职责：
    /// 1. 根据 RunnerConfig 创建并管理多个 SingleDeviceRunner 实例
    /// 2. 统一提供 Start() / Stop() / GetAllStatus() 接口
    /// 3. 并发启动所有设备驱动（互不干扰，独立运行）
    /// 4. 监控所有设备状态，支持 Ctrl+C 优雅退出
    /// 
    /// 使用方法：
    /// <code>
    /// var config = ConfigLoader.Load("runner.config.json");
    /// using var runner = new DeviceRunner(config);
    /// runner.Initialize();
    /// runner.Start();
    /// 
    /// // 主线程等待，按 Ctrl+C 优雅退出
    /// Console.CancelKeyPress += (s, e) => { e.Cancel = true; runner.Stop(); };
    /// await Task.Delay(Timeout.Infinite);
    /// </code>
    /// </summary>
    public class DeviceRunner : IDisposable
    {
        private readonly RunnerConfig _config;
        private readonly ILogger<DeviceRunner> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly Dictionary<string, SingleDeviceRunner> _deviceRunners = new();
        private readonly CancellationTokenSource _cts = new();
        private bool _isInitialized = false;
        private bool _isRunning = false;

        /// <summary>
        /// Runner 配置（包含所有设备实例）
        /// </summary>
        public RunnerConfig Config => _config;

        /// <summary>
        /// Runner 名称
        /// </summary>
        public string Name => _config.Runner?.Name ?? "ZL.Iot.Runner";

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 是否在运行中
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 设备运行器数量
        /// </summary>
        public int DeviceCount => _deviceRunners.Count;

        /// <summary>
        /// 创建多设备协调运行器
        /// </summary>
        /// <param name="config">Runner 配置（从配置文件加载）</param>
        /// <param name="loggerFactory">日志工厂</param>
        public DeviceRunner(RunnerConfig config, ILoggerFactory loggerFactory)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = loggerFactory.CreateLogger<DeviceRunner>();
        }

        /// <summary>
        /// 根据配置创建并初始化所有设备驱动实例
        /// 每个设备实例独立运行，互不干扰
        /// </summary>
        /// <exception cref="InvalidOperationException">配置验证失败时抛出</exception>
        public void Initialize()
        {
            if (_isInitialized)
            {
                _logger.LogWarning("DeviceRunner 已初始化，忽略重复初始化");
                return;
            }

            _logger.LogInformation("[{Name}] 开始初始化，共 {Count} 个设备", Name, _config.Devices?.Count ?? 0);

            // 创建设备运行器实例
            var initErrors = new List<string>();
            foreach (var profile in _config.Devices ?? new List<DeviceProfile>())
            {
                try
                {
                    var singleRunner = SingleDeviceRunner.Create(profile, _loggerFactory, _config.Runner?.DataStorage);
                    _deviceRunners[profile.Code] = singleRunner;

                    _logger.LogInformation("[{Code}] 设备运行器创建成功（协议={Protocol}, IP={Ip}:{Port}, 标签数={TagCount}）",
                        profile.Code, profile.Protocol, profile.Ip, profile.Port, profile.Tags?.Count ?? 0);
                }
                catch (Exception ex)
                {
                    var msg = $"[{profile.Code}] 设备运行器创建失败: {ex.Message}";
                    _logger.LogError(ex, msg);
                    initErrors.Add(msg);
                }
            }

            if (_deviceRunners.Count == 0)
            {
                throw new InvalidOperationException(
                    $"DeviceRunner 初始化失败：所有 {_config.Devices?.Count ?? 0} 个设备均创建失败。" +
                    string.Join("; ", initErrors));
            }

            _isInitialized = true;
            _logger.LogInformation("[{Name}] 初始化完成，共 {Count} 个设备运行器已就绪", Name, _deviceRunners.Count);
        }

        /// <summary>
        /// 启动所有设备驱动，进入采集循环
        /// </summary>
        /// <exception cref="InvalidOperationException">未调用 Initialize 时抛出</exception>
        public void Start()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("DeviceRunner 未初始化，请先调用 Initialize()");

            if (_isRunning)
            {
                _logger.LogWarning("DeviceRunner 已在运行中，忽略重复启动");
                return;
            }

            _isRunning = true;
            _logger.LogInformation("[{Name}] 启动所有设备，共 {Count} 个", Name, _deviceRunners.Count);

            foreach (var kvp in _deviceRunners)
            {
                try
                {
                    kvp.Value.Start();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Code}] 设备启动失败，继续启动其他设备", kvp.Key);
                }
            }

            _logger.LogInformation("[{Name}] 所有设备已启动，进入采集循环", Name);
        }

        /// <summary>
        /// 停止所有设备驱动
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts.Cancel();

            _logger.LogInformation("[{Name}] 停止所有设备，共 {Count} 个", Name, _deviceRunners.Count);

            foreach (var kvp in _deviceRunners)
            {
                try
                {
                    kvp.Value.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Code}] 设备停止异常", kvp.Key);
                }
            }

            _logger.LogInformation("[{Name}] 所有设备已停止", Name);
        }

        /// <summary>
        /// 阻塞式运行入口：Initialize → Start → 阻塞直到 cts 触发 → Stop。
        /// 给 Generator 生成的"瘦壳"宿主使用（Console/Service/WinForm/Web 通用）。
        /// 调用方负责创建 CancellationTokenSource 并在收到停止信号时 Cancel。
        /// </summary>
        /// <param name="cancellationToken">外部取消令牌（Ctrl+C / Service Stop / Application.Exit）</param>
        /// <exception cref="InvalidOperationException">未调用 Initialize 时抛出</exception>
        public void Run(CancellationToken cancellationToken = default)
        {
            // 确保已初始化（幂等）
            if (!_isInitialized)
            {
                Initialize();
            }

            // 确保已启动（幂等）
            if (!_isRunning)
            {
                Start();
            }

            try
            {
                // 阻塞直到外部取消（不消费 CPU）。
                // cancellationToken 来自 CancellationTokenSource 才有可等待的 WaitHandle；
                // 传入 default(CancellationToken) 时 CanBeCanceled = false，但 WaitHandle 仍可访问（永不触发）。
                cancellationToken.WaitHandle.WaitOne();
            }
            finally
            {
                Stop();
            }
        }

        /// <summary>
        /// 获取所有设备的状态摘要
        /// </summary>
        public Dictionary<string, DeviceStatus> GetAllStatus()
        {
            return _deviceRunners.ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    try { return kv.Value.GetStatus(); }
                    catch { return new DeviceStatus { Code = kv.Key, IsConnected = false, ErrorMessage = "状态查询异常" }; }
                });
        }

        /// <summary>
        /// 获取指定设备的状态
        /// </summary>
        public DeviceStatus? GetStatus(string deviceCode)
        {
            if (!_deviceRunners.TryGetValue(deviceCode, out var runner))
                return null;

            try { return runner.GetStatus(); }
            catch { return new DeviceStatus { Code = deviceCode, IsConnected = false, ErrorMessage = "状态查询异常" }; }
        }

        /// <summary>
        /// 获取运行汇总信息（用于日志/UI 显示）
        /// </summary>
        public RunnerSummary GetSummary()
        {
            var status = GetAllStatus();
            return new RunnerSummary
            {
                Name = Name,
                TotalDevices = _deviceRunners.Count,
                ConnectedDevices = status.Count(s => s.Value.IsConnected),
                TotalTags = status.Sum(s => s.Value.TagCount),
                TotalCollected = status.Sum(s => s.Value.CollectedCount),
                StartTime = _isRunning ? DateTime.Now : DateTime.MinValue,
                IsRunning = _isRunning
            };
        }

        public void Dispose()
        {
            Stop();
            foreach (var runner in _deviceRunners.Values)
            {
                (runner as IDisposable)?.Dispose();
            }
            _deviceRunners.Clear();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Runner 运行汇总信息
    /// </summary>
    public class RunnerSummary
    {
        public string Name { get; set; } = "";
        public int TotalDevices { get; set; }
        public int ConnectedDevices { get; set; }
        public int TotalTags { get; set; }
        public long TotalCollected { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsRunning { get; set; }

        public override string ToString()
        {
            var uptime = IsRunning ? $"运行 {DateTime.Now - StartTime:hh\\:mm\\:ss}" : "已停止";
            return $"[Runner: {Name}] {ConnectedDevices}/{TotalDevices} 设备已连接 | {TotalTags} 标签 | {TotalCollected} 采集 | {uptime}";
        }
    }
}