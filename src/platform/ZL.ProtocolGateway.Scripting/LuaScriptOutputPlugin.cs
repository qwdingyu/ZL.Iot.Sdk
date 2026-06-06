using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;

namespace ZL.ProtocolGateway.Scripting
{
    /// <summary>
    /// Lua 脚本输出插件配置
    /// </summary>
    public class LuaScriptOutputConfig
    {
        /// <summary>
        /// 脚本文件路径（.lua 文件）
        /// </summary>
        public string ScriptPath { get; set; } = string.Empty;

        /// <summary>
        /// 内联脚本内容（如果设置，优先于 ScriptPath）
        /// </summary>
        public string? InlineScript { get; set; }

        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; } = "LuaScript";

        /// <summary>
        /// 是否启用脚本
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 脚本超时（毫秒），默认 5000ms
        /// </summary>
        public int TimeoutMs { get; set; } = 5000;
    }

    /// <summary>
    /// Lua 脚本输出插件 — 将消息传递给 Lua 脚本进行处理。
    /// 脚本可以：
    /// - 读取/修改消息内容
    /// - 添加标签写入
    /// - 执行条件逻辑（报警、过滤、转换）
    /// - 维护跨消息的状态
    /// </summary>
    public class LuaScriptOutputPlugin : OutputPluginBase
    {
        private readonly LuaScriptOutputConfig _config;
        private volatile LuaScriptEngine? _engine;
        private volatile string _scriptContent;
        private readonly Dictionary<string, object> _state = new();
        private readonly object _stateLock = new();
        private int _messagesProcessed;
        private int _scriptErrors;

        public LuaScriptOutputPlugin(LuaScriptOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _scriptContent = config.InlineScript ?? string.Empty;
        }

        public override string Name => _config.Name;
        public override string ProtocolType => "lua-script";

        /// <summary>
        /// 已处理消息数
        /// </summary>
        public int MessagesProcessed => Volatile.Read(ref _messagesProcessed);

        /// <summary>
        /// 脚本错误数
        /// </summary>
        public int ScriptErrors => Volatile.Read(ref _scriptErrors);

        protected override Task OnStartAsync(CancellationToken ct)
        {
            if (!_config.Enabled)
            {
                throw new InvalidOperationException("Lua script plugin is disabled");
            }

            // 加载脚本
            if (!string.IsNullOrEmpty(_config.InlineScript))
            {
                _scriptContent = _config.InlineScript;
            }
            else if (!string.IsNullOrEmpty(_config.ScriptPath))
            {
                if (!System.IO.File.Exists(_config.ScriptPath))
                {
                    throw new System.IO.FileNotFoundException($"Script file not found: {_config.ScriptPath}");
                }
                _scriptContent = System.IO.File.ReadAllText(_config.ScriptPath);
            }
            else
            {
                throw new InvalidOperationException("No script provided (set ScriptPath or InlineScript)");
            }

            // 验证脚本语法（编译但不执行）
            try
            {
                using var testEngine = new LuaScriptEngine();
                testEngine.ValidateScript(_scriptContent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Script syntax error: {ex.Message}", ex);
            }

            _engine = new LuaScriptEngine();
            return Task.CompletedTask;
        }

        protected override async Task OnSendAsync(Message message, CancellationToken ct)
        {
            // 读取 volatile 字段的本地快照，避免在锁内捕获字段引用
            // 防止 OnStopAsync 在等待锁时将 _engine 置为 null
            var engine = _engine;
            if (engine == null || message == null)
                return;

            var script = _scriptContent;
            var stateSnapshot = CloneState();

            try
            {
                var ctx = CreateContext(message, stateSnapshot);

                // 合并超时取消与外部取消 — Debug Hook 在 Lua VM 内部每 1000 条指令检查取消，
                // 比 Task.Run + Task.WhenAny 更精确（从 VM 内部中断而非等待线程空闲）
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMilliseconds(_config.TimeoutMs));

                engine.ExecuteScript(script, ctx, cts.Token);
                Interlocked.Increment(ref _messagesProcessed);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _scriptErrors);
                GatewayLog.Warn(Name, $"Script execution cancelled or timeout after {_config.TimeoutMs}ms");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _scriptErrors);
                GatewayLog.Warn(Name, $"Script execution error: {ex.Message}");
                throw;
            }
            finally
            {
                // 无论成功/失败/超时，都将脚本修改的状态合并回持久化存储
                MergeStateBack(stateSnapshot);
            }
        }

        protected override Task OnStopAsync()
        {
            _engine?.Dispose();
            _engine = null;
            return Task.CompletedTask;
        }

        private LuaScriptContext CreateContext(Message message, Dictionary<string, object> state)
        {
            return new LuaScriptContext(
                message,
                (src, msg) => GatewayLog.Info($"{Name}/{src}", msg),
                (src, msg) => GatewayLog.Warn($"{Name}/{src}", msg),
                (src, msg) => GatewayLog.Error($"{Name}/{src}", msg),
                state);
        }

        private Dictionary<string, object> CloneState()
        {
            lock (_stateLock)
            {
                return new Dictionary<string, object>(_state);
            }
        }

        /// <summary>
        /// 将脚本执行后的状态副本合并回持久化 _state。
        /// 脚本通过 set_state 修改的是副本，必须手动合并回来。
        /// </summary>
        private void MergeStateBack(Dictionary<string, object> snapshot)
        {
            lock (_stateLock)
            {
                foreach (var kvp in snapshot)
                {
                    _state[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// 更新脚本内容（热更新）
        /// </summary>
        public void UpdateScript(string newScript)
        {
            if (newScript == null) throw new ArgumentNullException(nameof(newScript));
            _engine?.ValidateScript(newScript);
            _scriptContent = newScript;
        }

        /// <summary>
        /// 从文件重新加载脚本
        /// </summary>
        public void ReloadScript()
        {
            if (string.IsNullOrEmpty(_config.ScriptPath) || !System.IO.File.Exists(_config.ScriptPath))
                return;

            var newScript = System.IO.File.ReadAllText(_config.ScriptPath);
            UpdateScript(newScript);
            GatewayLog.Info(Name, "Script reloaded from file");
        }

        /// <summary>
        /// 获取插件统计信息
        /// </summary>
        public Dictionary<string, object> GetStats()
        {
            return new Dictionary<string, object>
            {
                ["messages_processed"] = MessagesProcessed,
                ["script_errors"] = ScriptErrors,
                ["script_length"] = _scriptContent.Length,
                ["enabled"] = _config.Enabled,
                ["timeout_ms"] = _config.TimeoutMs
            };
        }
    }
}
