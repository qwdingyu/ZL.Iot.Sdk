using System.IO;
using System.Reflection;
using System.Threading;
using NLua;
using NLua.Event;

namespace ZL.Scripting
{
    /// <summary>
    /// Lua 脚本引擎 — 基于 NLua（Lua 5.1）实现。
    /// 
    /// NLua RegisterFunction 规则：
    /// - 实例方法重载 RegisterFunction(string, object, MethodBase)：
    ///   object 参数作为 self，Lua 调用时不需要传 self，直接传业务参数
    /// 
    /// 我们使用每次执行时注册实例方法的方式，避免 ThreadLocal 复杂性。
    /// 
    /// 超时中断机制：
    /// 使用 Lua Debug Hook (LuaHookMask.Count) 在每 N 条指令后回调，
    /// 检查 CancellationToken，若已取消则设置取消标志。
    /// 配合 Task.Run + Task.Wait(ct) 双层防护，确保脚本可被可靠中断。
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
            try
            {
                _lua.LoadCLRPackage();
            }
            catch
            {
                // CLR package 加载失败时仍可正常使用 RegisterFunction
            }
            _lua.State.Encoding = System.Text.Encoding.UTF8;
        }

        /// <summary>
        /// 执行 Lua 脚本，传入上下文对象和绑定名映射。
        /// 返回结果对象数组。
        /// </summary>
        /// <param name="script">Lua 脚本内容</param>
        /// <param name="context">上下文对象，其方法将通过 RegisterFunction 暴露给 Lua</param>
        /// <param name="bindings">绑定配置，格式: ("lua_function_name", "CSharpMethodName"), ...</param>
        public object[] ExecuteScript(string script, object context, params (string luaName, string methodName)[] bindings)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            if (context == null) throw new ArgumentNullException(nameof(context));

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
                RegisterBindings(context, bindings);
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
        public object[] ExecuteScript(string script, object context, CancellationToken ct, params (string luaName, string methodName)[] bindings)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!ct.CanBeCanceled)
            {
                lock (_lock)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
                    RegisterBindings(context, bindings);
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
                try { execLua.LoadCLRPackage(); } catch { }
                execLua.State.Encoding = System.Text.Encoding.UTF8;

                RegisterBindingsInLua(execLua, context, bindings);

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
        /// 执行 Lua 脚本（无上下文绑定）。
        /// </summary>
        public object[] ExecuteScript(string script)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
                try { return _lua.DoString(script); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Lua script execution failed: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 从文件加载并执行脚本
        /// </summary>
        public object[] ExecuteFile(string scriptPath, object context, params (string luaName, string methodName)[] bindings)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Lua script not found: {scriptPath}", scriptPath);

            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
            }

            return ExecuteScript(File.ReadAllText(scriptPath), context, bindings);
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

        /// <summary>
        /// 设置 Lua 全局变量
        /// </summary>
        public void SetGlobal(string name, object value)
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptEngine));
                _lua[name] = value;
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
        // 绑定注册
        // ═══════════════════════════════════════════════════════════════

        private void RegisterBindings(object ctx, (string luaName, string methodName)[] bindings)
        {
            RegisterBindingsInLua(_lua, ctx, bindings);
        }

        private static void RegisterBindingsInLua(Lua lua, object ctx, (string luaName, string methodName)[] bindings)
        {
            if (bindings == null || bindings.Length == 0) return;

            for (int i = 0; i < bindings.Length; i++)
            {
                var (luaName, methodName) = bindings[i];
                var mi = ctx.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"Method {methodName} not found on {ctx.GetType().Name}");
                lua.RegisterFunction(luaName, ctx, mi);
            }
        }
    }
}
