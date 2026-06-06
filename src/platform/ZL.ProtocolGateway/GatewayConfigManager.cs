// ============================================================
// 文件：GatewayConfigManager.cs
// 描述：网关配置管理器 — 负责 Pipeline 配置热重载与文件监听
// 来源：从 GatewayManager God Class 拆分
// ============================================================

using System;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 网关配置管理器 — 管理 Pipeline 运行时参数与配置文件监听。
    /// </summary>
    internal class GatewayConfigManager
    {
        private readonly ResilientMessagePipeline _pipeline;
        private GatewayManagerOptions _options;
        private System.Threading.Timer? _configDebounceTimer;

        public GatewayConfigManager(ResilientMessagePipeline pipeline, GatewayManagerOptions options)
        {
            _pipeline = pipeline;
            _options = options;
        }

        /// <summary>
        /// 当前配置选项（只读访问）。
        /// </summary>
        public GatewayManagerOptions Options => _options;

        /// <summary>
        /// 运行时重载 Pipeline 配置（无需重启网关）。
        /// 注意：QueueCapacity 需要重建 Channel，不支持热更新。
        /// </summary>
        public async Task ReloadPipelineConfigAsync(GatewayManagerOptions newOptions)
        {
            if (newOptions == null)
                throw new ArgumentNullException(nameof(newOptions));

            ConfigValidation.ThrowIfInvalid(newOptions.Validate());

            GatewayLog.Info("GatewayConfigManager", "Reloading pipeline configuration...");

            // 在覆盖 _options 之前捕获旧值，用于变更检测
            int oldQueueCapacity = _pipeline.QueueCapacity;

            // 更新 Pipeline 可热更新的参数
            _pipeline.SendTimeoutMs = newOptions.SendTimeoutMs;
            _pipeline.MaxRetryAttempts = newOptions.MaxRetryAttempts;
            _pipeline.RetryBaseDelayMs = newOptions.RetryBaseDelayMs;
            _pipeline.CircuitBreakerFailureThreshold = newOptions.CircuitBreakerFailureThreshold;
            _pipeline.CircuitBreakerRecoveryTimeMs = newOptions.CircuitBreakerRecoveryTimeMs;

            // 更新内部选项引用
            _options = newOptions;

            GatewayLog.Info("GatewayConfigManager",
                $"Pipeline config reloaded: SendTimeout={newOptions.SendTimeoutMs}ms, " +
                $"MaxRetry={newOptions.MaxRetryAttempts}, RetryBaseDelay={newOptions.RetryBaseDelayMs}ms, " +
                $"CBThreshold={newOptions.CircuitBreakerFailureThreshold}, CBRecovery={newOptions.CircuitBreakerRecoveryTimeMs}ms");

            // 注意：如果 QueueCapacity 发生变化，需要重建 Pipeline（当前不支持）
            if (newOptions.QueueCapacity != oldQueueCapacity)
            {
                GatewayLog.Warn("GatewayConfigManager",
                    $"QueueCapacity change detected ({oldQueueCapacity} -> {newOptions.QueueCapacity}), " +
                    "but hot-reload of QueueCapacity is not supported. Restart gateway to apply.");
            }
        }

        /// <summary>
        /// 监听配置文件变化，自动触发配置重载。
        /// 包含防抖（500ms）和并发保护，防止 FileSystemWatcher 重复触发和并发重载。
        /// </summary>
        /// <param name="configPath">配置文件路径</param>
        /// <returns>IDisposable，调用 Dispose 停止监听</returns>
        public IDisposable WatchConfigurationFile(string configPath)
        {
            if (string.IsNullOrEmpty(configPath))
                throw new ArgumentNullException(nameof(configPath));

            if (!System.IO.File.Exists(configPath))
                throw new System.IO.FileNotFoundException($"Config file not found: {configPath}");

            var watcher = new System.IO.FileSystemWatcher
            {
                Path = System.IO.Path.GetDirectoryName(configPath),
                Filter = System.IO.Path.GetFileName(configPath),
                NotifyFilter = System.IO.NotifyFilters.LastWrite
            };

            // 防抖 + 并发保护（使用 int + Interlocked 替代 volatile bool，因为 volatile 不能用于局部变量）
            int _reloadInProgress = 0;

            watcher.Changed += (sender, e) =>
            {
                // 防抖：重置 500ms 定时器，FileSystemWatcher.Changed 在单次写入时可能触发 2 次
                _configDebounceTimer?.Dispose();
                _configDebounceTimer = new System.Threading.Timer(async _ =>
                {
                    // 并发保护：同一时刻只允许一个重载任务（0→1 成功表示获取锁）
                    if (Interlocked.CompareExchange(ref _reloadInProgress, 1, 0) != 0)
                    {
                        GatewayLog.Warn("GatewayConfigManager", $"Reload already in progress, skipping change for: {configPath}");
                        return;
                    }
                    try
                    {
                        GatewayLog.Info("GatewayConfigManager", $"Config file changed: {configPath}, reloading...");
                        var newConfig = GatewayConfigurationLoader.LoadFromJson(configPath);
                        await ReloadPipelineConfigAsync(newConfig.ToManagerOptions());
                    }
                    catch (Exception ex)
                    {
                        GatewayLog.Error("GatewayConfigManager", $"Failed to reload config from {configPath}: {ex.Message}", ex);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _reloadInProgress, 0);
                    }
                }, null, 500, System.Threading.Timeout.Infinite);
            };

            watcher.EnableRaisingEvents = true;
            GatewayLog.Info("GatewayConfigManager", $"Watching config file: {configPath}");

            return new FileSystemWatcherDisposable(watcher, _configDebounceTimer);
        }

        private sealed class FileSystemWatcherDisposable : IDisposable
        {
            private readonly System.IO.FileSystemWatcher _watcher;
            private readonly System.Threading.Timer? _configDebounceTimer;

            public FileSystemWatcherDisposable(System.IO.FileSystemWatcher watcher, System.Threading.Timer? debounceTimer)
            {
                _watcher = watcher;
                _configDebounceTimer = debounceTimer;
            }

            public void Dispose()
            {
                _watcher.EnableRaisingEvents = false;
                _configDebounceTimer?.Dispose();
                _watcher.Dispose();
            }
        }
    }
}
