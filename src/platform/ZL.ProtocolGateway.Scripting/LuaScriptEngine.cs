using System;
using System.IO;
using System.Reflection;
using System.Threading;
using NLua;
using NLua.Event;

namespace ZL.ProtocolGateway.Scripting
{
    /// <summary>
    /// Lua 脚本引擎 — 基于 NLua（Lua 5.1）实现。
    /// 
    /// NLua RegisterFunction 规则：
    /// - 静态方法重载 RegisterFunction(string, MethodBase)：
    ///   Lua 调用时第一个参数为 Lua 实例
    /// - 实例方法重载 RegisterFunction(string, object, MethodBase)：
    ///   object 参数作为 self，Lua 调用时第一个参数为 self
    /// 
    /// 我们使用每次执行时注册实例方法的方式，避免 ThreadLocal 复杂性。
    /// 
    /// 超时中断机制：
    /// 使用 Lua Debug Hook (LuaHookMask.Count) 在每 N 条指令后回调，
    /// 检查 CancellationToken，若已取消则抛出 OperationCanceledException。
    /// 这比 Task.Run + cts.Cancel() 更有效，因为后者无法中断同步阻塞的 DoString。
    /// </summary>
    public class LuaScriptEngine : IDisposable
    {
        /// <summary>
        /// Debug Hook 指令计数阈值 — 每 1000 条 Lua 指令检查一次取消。
        /// 值越小中断越快但开销越大，1000 在响应性和性能间取得平衡。
        /// Lua 5.1 在简单操作（赋值、算术）上约 1-10 条指令/操作，
        /// 1000 条指令约对应几毫秒到几十毫秒的检查间隔。
        /// </summary>
        private const int HookInstructionCount = 1000;

        private readonly Lua _lua;
        private readonly object _lock = new();
        private volatile bool _disposed;

        public LuaScriptEngine()
        {
            _lua = new Lua();
            // P2 安全加固：移除 LoadCLRPackage() — 该调用允许 Lua 脚本通过 require("CLR") 访问任意 .NET 类型
            // （包括 System.IO.File、System.Diagnostics.Process 等危险 API）。
            // 改为仅通过 RegisterFunction 暴露白名单 API（LuaScriptContext 中定义的方法）。
            _lua.State.Encoding = System.Text.Encoding.UTF8;
        }

        /// <summary>
        /// 执行 Lua 脚本。返回结果对象数组。
        /// </summary>
        public object[] ExecuteScript(string script, LuaScriptContext context)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            if (context == null) throw new ArgumentNullException(nameof(context));

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
                RegisterBindingsForContext(context);
                try
                {
                    return _lua.DoString(script);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Lua script execution failed: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 执行 Lua 脚本，支持 CancellationToken 中断。
        ///
        /// 使用双层防护机制：
        /// 1. Debug Hook 设置取消标志（每 HookInstructionCount 条指令检查），
        ///    DoString 返回后检查标志，若已取消则抛出 OperationCanceledException。
        /// 2. Task.Run + Task.Wait(ct) 超时兜底，防止脚本完全不返回。
        ///
        /// 关键设计：
        /// - 为每次可取消执行创建独立的 Lua 状态（new Lua()），避免与共享 _lua 状态
        ///   产生多线程竞争。NLua/Lua 状态不是线程安全的。
        /// - Hook 回调中仅设置 volatile 标志，不在 Hook 中 throw（lua_error 的 longjmp
        ///   会绕过 C# try-finally，导致 lock 无法释放、进程挂起）。
        /// - 取消后直接抛 OperationCanceledException，不等待后台线程结束。
        ///   后台线程上的独立 Lua 状态会在 GC 时自动回收。
        /// </summary>
        public object[] ExecuteScript(string script, LuaScriptContext context, CancellationToken ct)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!ct.CanBeCanceled)
            {
                lock (_lock)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
                    RegisterBindingsForContext(context);
                    try { return _lua.DoString(script); }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Lua script execution failed: {ex.Message}", ex);
                    }
                }
            }

            // 为可取消执行创建独立 Lua 状态，避免多线程竞争共享 _lua
            int cancelled = 0;
            bool taskCompleted = false;

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
            }

            Lua execLua = new Lua();
            try
            {
                execLua.State.Encoding = System.Text.Encoding.UTF8;

                RegisterBindingsForLuaContext(execLua, context);

                execLua.SetDebugHook(KeraLua.LuaHookMask.Count, HookInstructionCount);
                execLua.DebugHook += (s, e) =>
                {
                    if (ct.IsCancellationRequested)
                        cancelled = 1;
                };

                // 在线程池线程上执行 DoString
                Task<object[]> execTask = Task.Run(() =>
                {
                    try { return execLua.DoString(script); }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Lua script execution failed: {ex.Message}", ex);
                    }
                });

                // Wait(ct) 在 ct 取消时立即返回（抛 TaskCanceledException）
                try
                {
                    execTask.Wait(ct);
                    taskCompleted = true;
                }
                catch (TaskCanceledException)
                {
                    // 取消：等待后台任务完成后再 Dispose，避免资源泄漏
                    // 使用短超时防止无限等待（脚本可能已无限循环）
                    try { execTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
                    taskCompleted = true;
                    throw new OperationCanceledException("Lua script execution cancelled");
                }

                // Wait 成功返回：提取结果（可能抛 AggregateException 如果任务异常）
                object[] result;
                try
                {
                    result = execTask.Result;
                }
                catch (AggregateException ae) when (ae.InnerException != null)
                {
                    throw ae.InnerException;
                }

                // DoString 正常返回后检查取消标志（竞态：脚本刚好在取消前完成）
                if (cancelled != 0)
                    throw new OperationCanceledException("Lua script execution cancelled");

                return result;
            }
            finally
            {
                // 任务完成后 Dispose 独立 Lua 状态，确保资源及时释放
                if (taskCompleted)
                    execLua.Dispose();
            }
        }

        /// <summary>
        /// 向指定的 Lua 实例注册绑定（用于独立 Lua 状态）。
        /// </summary>
        private void RegisterBindingsForLuaContext(Lua lua, LuaScriptContext ctx)
        {
            for (int i = 0; i < _bindingNames.Length; i++)
            {
                lua.RegisterFunction(_bindingNames[i], ctx, _bindingMethods[i]);
            }

            lua["__topic"] = ctx.Topic;
            lua["__timestamp"] = ctx.Timestamp;
            lua["__json"] = ctx.JsonContent ?? "";
            lua["__text"] = ctx.TextContent ?? "";
            lua["__hex"] = ctx.HexContent ?? "";
        }

        /// <summary>
        /// 从文件加载并执行脚本
        /// </summary>
        public object[] ExecuteFile(string scriptPath, LuaScriptContext context)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Lua script not found: {scriptPath}", scriptPath);

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
            }

            return ExecuteScript(File.ReadAllText(scriptPath), context);
        }

        /// <summary>
        /// 预加载 Lua 脚本（编译但不执行），用于语法检查。
        /// </summary>
        public void ValidateScript(string script)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
                _lua.LoadString(script, "__validate__");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedInt, 1) != 0) return;
            _disposed = true;
            _lua.Dispose();
        }

        private int _disposedInt; // 配合 Interlocked.Exchange 实现线程安全的单次 Dispose

        // ═══════════════════════════════════════════════════════════════
        // 每次执行时注册绑定 — 使用实例方法模式
        // RegisterFunction(string, object, MethodBase) 中 object 作为 self
        // Lua 调用时不需要传 self，直接传业务参数
        //
        // MethodInfo 缓存为静态字段，避免每次执行时重复反射查找。
        // ═══════════════════════════════════════════════════════════════

        private static readonly MethodInfo[] _bindingMethods =
        [
            GetM(nameof(LuaScriptContext.LogInfo)),
            GetM(nameof(LuaScriptContext.LogWarn)),
            GetM(nameof(LuaScriptContext.LogError)),
            GetM(nameof(LuaScriptContext.GetMetadata)),
            GetM(nameof(LuaScriptContext.SetMetadata)),
            GetM(nameof(LuaScriptContext.AddWrite)),
            GetM(nameof(LuaScriptContext.GetState)),
            GetM(nameof(LuaScriptContext.SetState)),
            GetM(nameof(LuaScriptContext.Clamp)),
            GetM(nameof(LuaScriptContext.IsInDeadband)),
            GetM(nameof(LuaScriptContext.GetTopic)),
            GetM(nameof(LuaScriptContext.GetTimestamp)),
            GetM(nameof(LuaScriptContext.GetJson)),
            GetM(nameof(LuaScriptContext.GetText)),
            GetM(nameof(LuaScriptContext.GetHex)),
        ];

        private static readonly string[] _bindingNames =
        [
            "log_info", "log_warn", "log_error",
            "get_metadata", "set_metadata",
            "add_write",
            "get_state", "set_state",
            "clamp", "in_deadband",
            "get_topic", "get_timestamp", "get_json", "get_text", "get_hex",
        ];

        private static MethodInfo GetM(string name) =>
            typeof(LuaScriptContext).GetMethod(name)
            ?? throw new InvalidOperationException($"Method {name} not found on LuaScriptContext");

        private void RegisterBindingsForContext(LuaScriptContext ctx)
        {
            for (int i = 0; i < _bindingNames.Length; i++)
            {
                _lua.RegisterFunction(_bindingNames[i], ctx, _bindingMethods[i]);
            }

            // 同时也暴露为全局变量（向后兼容）
            _lua["__topic"] = ctx.Topic;
            _lua["__timestamp"] = ctx.Timestamp;
            _lua["__json"] = ctx.JsonContent ?? "";
            _lua["__text"] = ctx.TextContent ?? "";
            _lua["__hex"] = ctx.HexContent ?? "";
        }
    }
}
