// ============================================================
// 文件：GatewayManager.cs
// 描述：网关管理器 — ProtocolGateway 的统一门面（Facade）
// 功能：协调各子管理器，提供网关唯一公共入口。
//       所有上层应用（PlcSimulator.UI、CLI 等）统一使用此类。
// 架构：从 God Class 拆分为 4 个子管理器：
//       - GatewayConfigManager: 配置热重载与文件监听
//       - GatewayInputManager:  输入插件生命周期 + 限流
//       - GatewayOutputManager: 输出插件生命周期 + 状态 + 事件
//       - GatewayDiagnosticsManager: 指标/死信/断路器/测试发送
// 修改日期：2026-06-10
// ============================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 网关管理器 — ProtocolGateway 的统一门面。
    /// 职责：协调各子管理器，管理网关整体生命周期（Start/Stop/Dispose）。
    /// <para>GatewayService 保留为向后兼容的薄封装，新代码应直接使用 GatewayManager。</para>
    /// </summary>
    public class GatewayManager : IDisposable, IAsyncDisposable
    {
        // ---- 子管理器 ----
        private readonly GatewayConfigManager _configManager;
        private readonly GatewayInputManager _inputManager;
        private readonly GatewayOutputManager _outputManager;
        private readonly GatewayDiagnosticsManager _diagnosticsManager;

        // ---- 核心基础设施 ----
        private readonly ResilientMessagePipeline _pipeline;

        // ---- 生命周期 ----
        private volatile int _isRunning; // 0=stopped, 1=running
        private CancellationTokenSource? _cts;

        // ---- 健康检查 ----
        private GatewayHealthCheckHttpService? _healthCheckHttp;

        // ---- 配置 ----
        private GatewayManagerOptions _options;

        /// <summary>
        /// 网关是否已启动
        /// </summary>
        public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

        /// <summary>
        /// 获取内部的流水线实例（用于访问高级功能）
        /// </summary>
        public ResilientMessagePipeline Pipeline => _pipeline;

        /// <summary>
        /// 健康检查服务 — 聚合所有输出插件的健康状态为网关整体健康状态
        /// <para>用于 K8s Liveness/Readiness Probe 和运维监控</para>
        /// </summary>
        public GatewayHealthCheckService HealthCheck { get; }

        /// <summary>
        /// HTTP 健康检查服务（仅在 EnableHealthCheckHttp = true 时非 null）。
        /// 暴露 /health、/health/live、/health/ready、/metrics 端点。
        /// </summary>
        public GatewayHealthCheckHttpService? HealthCheckHttp => _healthCheckHttp;

        /// <summary>
        /// 已注册的输出插件名称列表
        /// </summary>
        public IReadOnlyList<string> RegisteredOutputNames => _outputManager.RegisteredOutputNames;

        /// <summary>
        /// 输出插件健康状态变更事件（桥接自各插件的 DetailedStatusChanged）
        /// </summary>
        public event Action<OutputPluginStatusArgs>? OutputHealthChanged;

        /// <summary>
        /// 断路器状态变更事件（桥接自 Pipeline 的 CircuitBreakerStateChanged）
        /// </summary>
        public event Action<CircuitBreakerStateChangedArgs>? CircuitBreakerStateChanged;

        /// <summary>
        /// 新死信消息入队时触发的事件（桥接自 Pipeline 的 DeadLetterAdded）
        /// </summary>
        public event Action<DeadLetterMessage>? DeadLetterAdded
        {
            add => _pipeline.DeadLetterAdded += value;
            remove => _pipeline.DeadLetterAdded -= value;
        }

        #region 构造函数

        /// <summary>
        /// 使用默认配置创建网关管理器
        /// </summary>
        public GatewayManager()
            : this(new GatewayManagerOptions())
        {
        }

        /// <summary>
        /// 使用自定义配置创建网关管理器
        /// </summary>
        /// <param name="options">配置选项</param>
        public GatewayManager(GatewayManagerOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            // 快速失败：启动时验证配置合法性
            ConfigValidation.ThrowIfInvalid(options.Validate());

            _options = options;
            _pipeline = new ResilientMessagePipeline
            {
                QueueCapacity = options.QueueCapacity,
                SendTimeoutMs = options.SendTimeoutMs,
                MaxRetryAttempts = options.MaxRetryAttempts,
                RetryBaseDelayMs = options.RetryBaseDelayMs,
                CircuitBreakerFailureThreshold = options.CircuitBreakerFailureThreshold,
                CircuitBreakerRecoveryTimeMs = options.CircuitBreakerRecoveryTimeMs
            };

            // 创建子管理器
            _configManager = new GatewayConfigManager(_pipeline, options);
            _inputManager = new GatewayInputManager(_pipeline);
            _outputManager = new GatewayOutputManager(_pipeline);
            _diagnosticsManager = new GatewayDiagnosticsManager(
                _pipeline,
                () => IsRunning,
                name => _outputManager.TryGetOutput(name, out _),
                () => _outputManager.GetOutputPluginStatuses());

            // 死信持久化存储（默认启用）
            if (options.EnableDeadLetterPersistence)
            {
                _pipeline.DeadLetterStore = new DeadLetterStore(
                    options.DeadLetterDbPath,
                    options.DeadLetterMaxCount,
                    options.DeadLetterRetentionHours);
                GatewayLog.Info("GatewayManager",
                    $"Dead letter persistence enabled: {options.DeadLetterDbPath}, max={options.DeadLetterMaxCount}, retention={options.DeadLetterRetentionHours}h");
            }

            // 健康检查服务（延迟初始化，因为构造函数中 this 尚未完全构建）
            HealthCheck = new Lazy<GatewayHealthCheckService>(() => new GatewayHealthCheckService(this)).Value;

            // 桥接 Pipeline 事件到 GatewayManager 事件
            _pipeline.CircuitBreakerStateChanged += args => CircuitBreakerStateChanged?.Invoke(args);
            _outputManager.OutputHealthChanged += args => OutputHealthChanged?.Invoke(args);
        }

        #endregion

        #region 生命周期

        /// <summary>
        /// 启动网关服务（启动 Pipeline → 启动 Output → 启动 Input → 启动健康检查）
        /// </summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            // 原子检查：0→1 表示从 stopped 切换到 running；!=0 表示已在运行中
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            GatewayLog.Info("GatewayManager", "Protocol Gateway starting.");

            try
            {
                // 1. 启动 Pipeline（这会启动所有已注册的 Output 插件）
                await _pipeline.StartAsync(_cts.Token);
                GatewayLog.Info("GatewayManager", "Pipeline and outputs started.");

                // 2. 启动所有 Input 插件
                await _inputManager.StartInputsAsync(ProcessInputMessageAsync, _cts.Token);

                // 3. 启动 HTTP 健康检查服务（如果启用）
                if (_options.EnableHealthCheckHttp)
                {
                    try
                    {
                        var listenUrl = $"http://+:{_options.HealthCheckHttpPort}/";
                        _healthCheckHttp = new GatewayHealthCheckHttpService(this, listenUrl);
                        await _healthCheckHttp.StartAsync();
                        GatewayLog.Info("GatewayManager", $"Health check HTTP service started on {listenUrl}");
                    }
                    catch (Exception ex)
                    {
                        GatewayLog.Error("GatewayManager", $"Failed to start health check HTTP service: {ex.Message}", ex);
                        _healthCheckHttp = null;
                    }
                }

                GatewayLog.Info("GatewayManager", "Protocol Gateway started successfully.");
            }
            catch (Exception ex)
            {
                GatewayLog.Error("GatewayManager", $"Failed to start gateway: {ex.Message}", ex);
                await StopAsync();
                throw;
            }
        }

        /// <summary>
        /// 停止网关服务（先停止健康检查，再停止 Input，最后停止 Pipeline）
        /// </summary>
        public async Task StopAsync()
        {
            // 原子交换：0→0 表示已在停止状态（直接返回）；1→0 表示从 running 切换到 stopped
            if (Interlocked.Exchange(ref _isRunning, 0) == 0) return;

            GatewayLog.Info("GatewayManager", "Protocol Gateway stopping.");

            // 1. 取消令牌，通知所有后台任务停止
            _cts?.Cancel();

            // 2. 停止 HTTP 健康检查服务
            if (_healthCheckHttp != null)
            {
                try
                {
                    await _healthCheckHttp.StopAsync();
                    GatewayLog.Info("GatewayManager", "Health check HTTP service stopped");
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("GatewayManager", $"Failed to stop health check HTTP service: {ex.Message}", ex);
                }
                _healthCheckHttp = null;
            }

            // 3. 停止 Input
            await _inputManager.StopInputsAsync();

            // 4. 先清除所有输出插件
            await _outputManager.ClearOutputsAsync();

            // 5. 停止 Pipeline
            await _pipeline.StopAsync();

            _cts?.Dispose();
            _cts = null;
            GatewayLog.Info("GatewayManager", "Protocol Gateway stopped.");
        }

        /// <summary>
        /// 优雅关闭 — 在指定超时内完成 StopAsync。
        /// </summary>
        public async Task GracefulShutdownAsync(TimeSpan? timeout = null, CancellationToken ct = default)
        {
            var shutdownTimeout = timeout ?? TimeSpan.FromSeconds(30);
            using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            shutdownCts.CancelAfter(shutdownTimeout);

            var stopTask = StopAsync();
            var completed = await Task.WhenAny(stopTask, Task.Delay(shutdownTimeout, shutdownCts.Token));

            if (completed != stopTask)
            {
                GatewayLog.Warn("GatewayManager",
                    $"Graceful shutdown timed out after {shutdownTimeout.TotalSeconds}s, forcing immediate shutdown");
                throw new OperationCanceledException(
                    $"Graceful shutdown timed out after {shutdownTimeout.TotalSeconds}s");
            }

            await stopTask; // 传播 StopAsync 中的异常
            GatewayLog.Info("GatewayManager", "Graceful shutdown completed.");
        }

        #endregion

        #region 配置管理（委托给 GatewayConfigManager）

        /// <summary>
        /// 运行时重载 Pipeline 配置（无需重启网关）。
        /// </summary>
        public Task ReloadPipelineConfigAsync(GatewayManagerOptions newOptions)
            => _configManager.ReloadPipelineConfigAsync(newOptions);

        /// <summary>
        /// 监听配置文件变化，自动触发配置重载。
        /// </summary>
        public IDisposable WatchConfigurationFile(string configPath)
            => _configManager.WatchConfigurationFile(configPath);

        #endregion

        #region 输入插件管理（委托给 GatewayInputManager）

        /// <summary>
        /// 添加输入插件。
        /// </summary>
        public void AddInput(IInputPlugin input) => _inputManager.AddInput(input);

        /// <summary>
        /// 移除输入插件（异步版本，推荐）。
        /// </summary>
        public Task<bool> RemoveInputAsync(IInputPlugin input) => _inputManager.RemoveInputAsync(input);

        /// <summary>
        /// 移除输入插件（同步版本，仅向后兼容）。
        /// </summary>
        [Obsolete("Use RemoveInputAsync instead. This method may deadlock in async contexts.")]
        public bool RemoveInput(IInputPlugin input) => _inputManager.RemoveInput(input);

        /// <summary>
        /// 获取已注册的输入插件列表。
        /// </summary>
        public IReadOnlyList<IInputPlugin> Inputs => _inputManager.Inputs;

        /// <summary>
        /// 按名称查找输入插件。
        /// </summary>
        public IInputPlugin? GetInput(string name) => _inputManager.GetInput(name);

        #endregion

        #region 限流（委托给 GatewayInputManager）

        /// <summary>
        /// 设置全局消息速率限制（可选）。
        /// </summary>
        public void SetRateLimit(double tokensPerSecond) => _inputManager.SetRateLimit(tokensPerSecond);

        #endregion

        #region 输出插件管理（委托给 GatewayOutputManager）

        /// <summary>
        /// 注册输出插件。
        /// </summary>
        public bool RegisterOutput(string name, IOutputPlugin output)
            => _outputManager.RegisterOutput(name, output);

        /// <summary>
        /// 移除输出插件。
        /// </summary>
        public bool UnregisterOutput(string name) => _outputManager.UnregisterOutput(name);

        /// <summary>
        /// 启动指定输出插件。
        /// </summary>
        public Task<bool> StartOutputAsync(string name, CancellationToken ct = default)
            => _outputManager.StartOutputAsync(name, ct);

        /// <summary>
        /// 停止指定输出插件。
        /// </summary>
        public Task<bool> StopOutputAsync(string name) => _outputManager.StopOutputAsync(name);

        /// <summary>
        /// 清除所有输出插件。
        /// </summary>
        public Task ClearOutputsAsync() => _outputManager.ClearOutputsAsync();

        /// <summary>
        /// 获取所有输出插件的状态。
        /// </summary>
        public IReadOnlyList<OutputPluginStatus> GetOutputPluginStatuses()
            => _outputManager.GetOutputPluginStatuses();

        /// <summary>
        /// 获取指定输出插件的详细状态。
        /// </summary>
        public OutputPluginStatus? GetOutputPluginStatus(string name)
            => _outputManager.GetOutputPluginStatus(name);

        /// <summary>
        /// 获取指定输出插件的基本状态。
        /// </summary>
        public PluginStatus? GetOutputStatus(string name) => _outputManager.GetOutputStatus(name);

        #endregion

        #region 消息发布

        /// <summary>
        /// 向网关发布消息（直接注入 Pipeline，供进程内桥接调用）。
        /// </summary>
        public async Task PublishMessageAsync(Message message, CancellationToken ct = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (Volatile.Read(ref _isRunning) == 0) return;

            await _inputManager.ProcessInputMessageAsync("PublishMessage", message);
        }

        #endregion

        #region 测试发送（委托给 GatewayDiagnosticsManager）

        /// <summary>
        /// 测试发送消息到指定输出插件。
        /// </summary>
        public async Task<GatewayTestResult> TestSendAsync(string outputName, string? testPayload, CancellationToken ct = default)
        {
            var result = new GatewayTestResult { OutputName = outputName };

            if (!_outputManager.TryGetOutput(outputName, out var output))
            {
                result.Success = false;
                result.ErrorMessage = $"输出插件 '{outputName}' 未注册";
                return result;
            }

            return await _diagnosticsManager.TestSendAsync(output, testPayload, ct);
        }

        #endregion

        #region 指标与死信（委托给 GatewayDiagnosticsManager）

        /// <summary>
        /// 获取网关转发指标快照
        /// </summary>
        public GatewayMetricsSnapshot GetMetricsSnapshot()
            => _diagnosticsManager.GetMetricsSnapshot(RegisteredOutputNames);

        /// <summary>
        /// 获取死信队列消息
        /// </summary>
        public IReadOnlyList<DeadLetterInfo> GetDeadLetters(int limit = 100)
            => _diagnosticsManager.GetDeadLetters(limit);

        /// <summary>
        /// 清空死信队列
        /// </summary>
        public void ClearDeadLetterQueue() => _diagnosticsManager.ClearDeadLetterQueue();

        /// <summary>
        /// 将死信消息重新注入 Pipeline 进行重试
        /// </summary>
        public Task<int> RetryDeadLettersAsync(CancellationToken ct = default)
            => _diagnosticsManager.RetryDeadLettersAsync(PublishMessageAsync, ct);

        /// <summary>
        /// 重置指定输出插件的断路器
        /// </summary>
        public bool ResetCircuitBreaker(string outputName)
            => _diagnosticsManager.ResetCircuitBreaker(outputName);

        #endregion

        #region 内部方法

        private async Task ProcessInputMessageAsync(string inputName, Message msg)
        {
            await _inputManager.ProcessInputMessageAsync(inputName, msg);
        }

        #endregion

        #region IDisposable / IAsyncDisposable

        private int _disposed;

        /// <summary>
        /// 同步释放资源 — 仅设置标志并释放资源，不等待 StopAsync 完成。
        /// <para>推荐使用 DisposeAsync 以确保优雅关闭。</para>
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

            // 同步 Dispose 不等待 StopAsync 完成，避免 ThreadPool 耗尽时死锁。
            _pipeline.Dispose();
            _inputManager.Dispose();
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 异步释放资源 — 优雅停止所有输出插件并释放资源。
        /// <para>UI 层应优先调用此方法（await using 或 try/finally）。</para>
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

            try
            {
                await StopAsync();
            }
            catch (Exception ex)
            {
                GatewayLog.Warn("GatewayManager", $"Stop failed during DisposeAsync: {ex.Message}", ex);
            }
            finally
            {
                _pipeline.Dispose();
                _inputManager.Dispose();
                _cts?.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        #endregion
    }
}
