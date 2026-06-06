// ============================================================
// 文件：GatewayInputManager.cs
// 描述：输入插件管理器 — 负责输入插件注册/移除/启动/停止 + 限流
// 来源：从 GatewayManager God Class 拆分
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 输入插件管理器 — 管理输入插件生命周期与全局限流。
    /// </summary>
    internal class GatewayInputManager : IDisposable
    {
        private readonly ResilientMessagePipeline _pipeline;
        private readonly List<IInputPlugin> _inputs = new();
        private TokenBucketRateLimiter? _rateLimiter;
        private readonly object _lock = new();

        /// <summary>
        /// 已注册的输入插件列表（只读）。
        /// </summary>
        public IReadOnlyList<IInputPlugin> Inputs
        {
            get
            {
                lock (_lock)
                {
                    return _inputs.AsReadOnly();
                }
            }
        }

        public GatewayInputManager(ResilientMessagePipeline pipeline)
        {
            _pipeline = pipeline;
        }

        #region 输入插件管理

        /// <summary>
        /// 添加输入插件。
        /// </summary>
        public void AddInput(IInputPlugin input)
        {
            if (input == null) return;
            lock (_lock)
            {
                if (_inputs.Contains(input))
                {
                    GatewayLog.Warn("GatewayInputManager", $"Input plugin '{input.Name}' already registered, ignoring duplicate");
                    return;
                }
                _inputs.Add(input);
            }
        }

        /// <summary>
        /// 移除输入插件（异步版本，推荐）。
        /// <para>如果插件正在运行，会先尝试优雅停止。</para>
        /// </summary>
        public async Task<bool> RemoveInputAsync(IInputPlugin input)
        {
            if (input == null) return false;

            bool removed;
            lock (_lock)
            {
                if (!_inputs.Contains(input)) return false;
                removed = _inputs.Remove(input);
            }

            if (removed && input.Status == PluginStatus.Running)
            {
                try { await input.StopAsync(); } catch { }
            }
            return removed;
        }

        /// <summary>
        /// 移除输入插件（同步版本，仅向后兼容）。
        /// <para>P0 修复：原实现 .Wait() 可能死锁，建议使用 RemoveInputAsync。</para>
        /// </summary>
        [Obsolete("Use RemoveInputAsync instead. This method may deadlock in async contexts.")]
        public bool RemoveInput(IInputPlugin input)
        {
            return RemoveInputAsync(input).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 按名称查找输入插件。
        /// </summary>
        public IInputPlugin? GetInput(string name)
        {
            lock (_lock)
            {
                return _inputs.FirstOrDefault(i => i.Name == name);
            }
        }

        #endregion

        #region 限流

        /// <summary>
        /// 设置全局消息速率限制（可选）。
        /// <para>防止 Input 侧洪水攻击导致 Pipeline 过载。</para>
        /// </summary>
        /// <param name="tokensPerSecond">每秒允许的最大消息数，0 表示取消限流</param>
        public void SetRateLimit(double tokensPerSecond)
        {
            if (tokensPerSecond <= 0)
            {
                _rateLimiter?.Dispose();
                _rateLimiter = null;
                return;
            }

            _rateLimiter?.Dispose();
            _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = (int)tokensPerSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                TokensPerPeriod = (int)tokensPerSecond,
                QueueLimit = 0 // 超限直接丢弃，不入队
            });
        }

        #endregion

        #region 启动/停止

        /// <summary>
        /// 启动所有输入插件，并将消息处理回调绑定到 Pipeline。
        /// </summary>
        public async Task StartInputsAsync(Func<string, Message, Task> processMessageAsync, CancellationToken ct)
        {
            var startTasks = new List<Task>();
            foreach (var input in _inputs)
            {
                var captureInput = input; // 闭包捕获
                startTasks.Add(input.StartAsync(async (msg) =>
                {
                    await processMessageAsync(captureInput.Name, msg);
                }, ct));
            }

            await Task.WhenAll(startTasks);
            foreach (var input in _inputs)
            {
                GatewayLog.Info("GatewayInputManager", $"Input started: {input.Name}");
            }
        }

        /// <summary>
        /// 停止所有输入插件。
        /// </summary>
        public async Task StopInputsAsync()
        {
            foreach (var input in _inputs)
            {
                try
                {
                    await input.StopAsync();
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("GatewayInputManager", $"Failed to stop input '{input.Name}': {ex.Message}", ex);
                }
            }
        }

        #endregion

        #region 限流处理

        /// <summary>
        /// 处理来自输入插件的消息（含限流检查）。
        /// </summary>
        public async Task ProcessInputMessageAsync(string inputName, Message msg)
        {
            // 限流：如果设置了速率限制，超限消息直接丢弃
            if (_rateLimiter != null)
            {
                using var lease = await _rateLimiter.AcquireAsync();
                if (!lease.IsAcquired)
                {
                    _pipeline.Metrics.RecordRateLimited();
                    GatewayLog.Warn("GatewayInputManager", $"Rate limit exceeded, dropping message from input '{inputName}'");
                    return;
                }
            }

            try
            {
                await _pipeline.ProcessAsync(msg);
            }
            catch (Exception ex)
            {
                GatewayLog.Error("GatewayInputManager", $"Message processing failed from input '{inputName}': {ex.Message}", ex);
            }
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            _rateLimiter?.Dispose();
        }

        #endregion
    }
}
