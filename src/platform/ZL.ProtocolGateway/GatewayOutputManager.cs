// ============================================================
// 文件：GatewayOutputManager.cs
// 描述：输出插件管理器 — 负责输出插件注册/移除/启停/状态/事件桥接
// 来源：从 GatewayManager God Class 拆分
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 输出插件管理器 — 管理输出插件注册、生命周期、状态查询与健康事件桥接。
    /// </summary>
    internal class GatewayOutputManager
    {
        private readonly ResilientMessagePipeline _pipeline;

        // 已注册的输出插件（按名称索引），由调用方创建，由本类管理生命周期
        private readonly ConcurrentDictionary<string, IOutputPlugin> _registeredOutputs = new();
        // 跟踪每个输出插件的详细状态事件处理器引用，用于正确取消订阅
        private readonly ConcurrentDictionary<string, Action<OutputPluginStatusArgs>> _detailHandlers = new();
        // 反向索引（插件实例→名称），O(1) 取消订阅
        private readonly ConcurrentDictionary<IOutputPlugin, string> _pluginToName = new();

        /// <summary>
        /// 已注册的输出插件名称列表。
        /// </summary>
        public IReadOnlyList<string> RegisteredOutputNames => _registeredOutputs.Keys.ToList();

        /// <summary>
        /// 输出插件健康状态变更事件（桥接自各插件的 DetailedStatusChanged）。
        /// </summary>
        public event Action<OutputPluginStatusArgs>? OutputHealthChanged;

        public GatewayOutputManager(ResilientMessagePipeline pipeline)
        {
            _pipeline = pipeline;
        }

        #region 注册/移除

        /// <summary>
        /// 注册输出插件（由调用方创建插件实例，本类管理其生命周期）。
        /// 注册后不会自动加入 Pipeline，需调用 StartOutputAsync 才会注册到 Pipeline 并启动。
        /// </summary>
        public bool RegisterOutput(string name, IOutputPlugin output)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (output == null) return false;

            // 同名插件已存在时先移除
            if (_registeredOutputs.TryRemove(name, out var existing))
            {
                UnsubscribeDetailedStatus(existing);
                _pluginToName.TryRemove(existing, out _);
                _pipeline.UnregisterOutput(name);
                try { existing.Dispose(); } catch { }
            }

            _registeredOutputs[name] = output;
            _pluginToName[output] = name;
            SubscribeDetailedStatus(output, name);
            // 同步注册到 Pipeline（路由依赖），但不启动插件
            _pipeline.RegisterOutput(output);

            GatewayLog.Info("GatewayOutputManager", $"Output plugin registered: {name} ({output.ProtocolType})");
            return true;
        }

        /// <summary>
        /// 移除输出插件（停止、释放、从 Pipeline 注销）。
        /// </summary>
        public bool UnregisterOutput(string name)
        {
            if (!_registeredOutputs.TryRemove(name, out var output)) return false;

            UnsubscribeDetailedStatus(output);
            _pluginToName.TryRemove(output, out _);
            // P1 修复：先从未注册 Pipeline（阻止路由），再 Dispose。
            _pipeline.UnregisterOutput(name);
            try { output.Dispose(); } catch { }

            GatewayLog.Info("GatewayOutputManager", $"Output plugin unregistered: {name}");
            return true;
        }

        #endregion

        #region 启动/停止

        /// <summary>
        /// 启动指定输出插件（注册到 Pipeline 并调用 StartAsync）。
        /// </summary>
        public async Task<bool> StartOutputAsync(string name, CancellationToken ct = default)
        {
            if (!_registeredOutputs.TryGetValue(name, out var output)) return false;

            // 已在运行则不重复启动
            if (output.Status == PluginStatus.Running) return true;

            try
            {
                _pipeline.RegisterOutput(output);
                await output.StartAsync(ct);
                GatewayLog.Info("GatewayOutputManager", $"Output plugin started: {name}");
                return true;
            }
            catch (Exception ex)
            {
                GatewayLog.Error("GatewayOutputManager", $"Output plugin start failed: {name}: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 停止指定输出插件（从 Pipeline 注销、停止、但保留注册）。
        /// </summary>
        public async Task<bool> StopOutputAsync(string name)
        {
            // P1-5 修复：先从 Pipeline 注销（阻止新消息路由），再停止插件（排空进行中的消息），
            // 最后从注册表移除。
            if (!_registeredOutputs.TryGetValue(name, out var output)) return false;

            // 1. 先从 Pipeline 注销，阻止新消息路由到此插件
            _pipeline.UnregisterOutput(name);

            // 2. 从注册表移除（此时已无新消息会路由到此插件）
            _registeredOutputs.TryRemove(name, out _);
            UnsubscribeDetailedStatus(output);
            _pluginToName.TryRemove(output, out _);

            // 3. 停止插件（排空进行中的消息）
            try
            {
                await output.StopAsync();
                GatewayLog.Info("GatewayOutputManager", $"Output plugin stopped: {name}");
                return true;
            }
            catch (Exception ex)
            {
                GatewayLog.Warn("GatewayOutputManager", $"Output plugin stop failed: {name}: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 清除所有输出插件（停止、释放、从 Pipeline 注销）。
        /// </summary>
        public async Task ClearOutputsAsync()
        {
            var names = _registeredOutputs.Keys.ToList();
            foreach (var name in names)
            {
                await StopOutputAsync(name);
            }
            GatewayLog.Info("GatewayOutputManager", "All output plugins cleared");
        }

        #endregion

        #region 状态查询

        /// <summary>
        /// 获取所有输出插件的状态。
        /// </summary>
        public IReadOnlyList<OutputPluginStatus> GetOutputPluginStatuses()
        {
            return _registeredOutputs.Values.Select(ToOutputPluginStatus).ToList();
        }

        /// <summary>
        /// 获取指定输出插件的详细状态。
        /// </summary>
        public OutputPluginStatus? GetOutputPluginStatus(string name)
        {
            if (_registeredOutputs.TryGetValue(name, out var output))
                return ToOutputPluginStatus(output);
            return null;
        }

        /// <summary>
        /// 获取指定输出插件的基本状态。
        /// </summary>
        public PluginStatus? GetOutputStatus(string name)
        {
            if (_registeredOutputs.TryGetValue(name, out var output))
                return output.Status;
            return null;
        }

        /// <summary>
        /// 按名称获取输出插件实例（供 TestSend 等内部方法使用）。
        /// </summary>
        public bool TryGetOutput(string name, out IOutputPlugin output)
        {
            return _registeredOutputs.TryGetValue(name, out output);
        }

        private OutputPluginStatus ToOutputPluginStatus(IOutputPlugin output)
        {
            var cbState = _pipeline.GetCircuitBreakerState(output.Name);
            var status = new OutputPluginStatus
            {
                Name = output.Name,
                ProtocolType = output.ProtocolType,
                Status = output.Status.ToString(),
                StatusEnum = output.Status,
                IsRunning = output.Status == PluginStatus.Running,
                CircuitBreakerState = cbState.ToString(),
                Timestamp = DateTime.UtcNow
            };

            // 尝试从 DetailedStatusChanged 最近一次事件中获取健康详情
            if (output is Plugins.OutputPluginBase basePlugin)
            {
                status.HealthLevel = basePlugin.Status switch
                {
                    PluginStatus.Running => OutputPluginHealthLevel.Healthy,
                    PluginStatus.Degraded => OutputPluginHealthLevel.Degraded,
                    PluginStatus.Recovering => OutputPluginHealthLevel.Warning,
                    PluginStatus.Error or PluginStatus.Fatal => OutputPluginHealthLevel.Error,
                    _ => OutputPluginHealthLevel.Warning
                };
                status.LastException = basePlugin.PublicLastException;
            }

            return status;
        }

        #endregion

        #region 事件桥接

        private void SubscribeDetailedStatus(IOutputPlugin plugin, string name)
        {
            var handler = (Action<OutputPluginStatusArgs>)(args =>
            {
                if (args.PluginName == name)
                {
                    OutputHealthChanged?.Invoke(args);
                }
            });
            _detailHandlers[name] = handler;
            plugin.DetailedStatusChanged += handler;
        }

        private void UnsubscribeDetailedStatus(IOutputPlugin plugin)
        {
            if (_pluginToName.TryGetValue(plugin, out var name))
            {
                if (_detailHandlers.TryRemove(name, out var handler))
                {
                    plugin.DetailedStatusChanged -= handler;
                }
            }
        }

        #endregion
    }
}
