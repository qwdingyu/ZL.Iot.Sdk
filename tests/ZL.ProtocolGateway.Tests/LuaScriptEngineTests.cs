using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Scripting;
using Xunit;
using Xunit.Abstractions;

namespace ZL.ProtocolGateway.Tests
{
    public class LuaScriptEngineTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private LuaScriptEngine _engine;

        public LuaScriptEngineTests(ITestOutputHelper output)
        {
            _output = output;
            _engine = new LuaScriptEngine();
        }

        public void Dispose()
        {
            _engine?.Dispose();
        }

        private LuaScriptContext CreateContext(Message? msg = null)
        {
            return new LuaScriptContext(
                msg ?? new Message { Topic = "test/topic" },
                (src, m) => _output.WriteLine($"[INFO/{src}] {m}"),
                (src, m) => _output.WriteLine($"[WARN/{src}] {m}"),
                (src, m) => _output.WriteLine($"[ERROR/{src}] {m}"));
        }

        // ── 基本执行 ──

        [Fact]
        public void ExecuteScript_ReturnsResult()
        {
            var result = _engine.ExecuteScript("return 42", CreateContext());
            Assert.Equal(1, result.Length);
            Assert.Equal(42.0, Convert.ToDouble(result[0]));
        }

        [Fact]
        public void ExecuteScript_MultipleReturns()
        {
            var result = _engine.ExecuteScript("return 1, 'hello', true", CreateContext());
            Assert.Equal(3, result.Length);
            Assert.Equal(1.0, Convert.ToDouble(result[0]));
            Assert.Equal("hello", result[1]);
            Assert.True((bool)result[2]);
        }

        // ── 日志 API ──

        [Fact]
        public void ExecuteScript_LogInfo_CallsCallback()
        {
            string? logged = null;
            var ctx = new LuaScriptContext(
                new Message { Topic = "test" },
                (src, m) => logged = $"[{src}]{m}",
                (_, __) => { }, (_, __) => { });

            _engine.ExecuteScript("log_info('hello world')", ctx);
            Assert.Equal("[Lua]hello world", logged);
        }

        [Fact]
        public void ExecuteScript_LogWarn_CallsCallback()
        {
            string? logged = null;
            var ctx = new LuaScriptContext(
                new Message { Topic = "test" },
                (_, __) => { },
                (src, m) => logged = $"[{src}]{m}",
                (_, __) => { });

            _engine.ExecuteScript("log_warn('watch out')", ctx);
            Assert.Equal("[Lua]watch out", logged);
        }

        [Fact]
        public void ExecuteScript_LogError_CallsCallback()
        {
            string? logged = null;
            var ctx = new LuaScriptContext(
                new Message { Topic = "test" },
                (_, __) => { }, (_, __) => { },
                (src, m) => logged = $"[{src}]{m}");

            _engine.ExecuteScript("log_error('critical')", ctx);
            Assert.Equal("[Lua]critical", logged);
        }

        // ── 消息访问 API ──

        [Fact]
        public void ExecuteScript_GetTopic_ReturnsTopic()
        {
            var msg = new Message { Topic = "sensor/temp/001" };
            var result = _engine.ExecuteScript("return get_topic()", CreateContext(msg));
            Assert.Equal("sensor/temp/001", result[0]);
        }

        [Fact]
        public void ExecuteScript_GetJson_ReturnsJsonContent()
        {
            var msg = new Message { Topic = "data" };
            msg.SetJsonContent("{\"temperature\": 25.5}");
            var result = _engine.ExecuteScript("return get_json()", CreateContext(msg));
            Assert.Equal("{\"temperature\": 25.5}", result[0]);
        }

        [Fact]
        public void ExecuteScript_GetText_ReturnsTextContent()
        {
            var msg = new Message { Topic = "data" };
            msg.SetTextContent("hello plain text");
            var result = _engine.ExecuteScript("return get_text()", CreateContext(msg));
            Assert.Equal("hello plain text", result[0]);
        }

        [Fact]
        public void ExecuteScript_GetHex_ReturnsHexContent()
        {
            var msg = new Message { Topic = "data" };
            msg.SetHexContent("DEADBEEF");
            var result = _engine.ExecuteScript("return get_hex()", CreateContext(msg));
            Assert.Equal("DEADBEEF", result[0]);
        }

        // ── Metadata API ──

        [Fact]
        public void ExecuteScript_GetSetMetadata()
        {
            var msg = new Message { Topic = "test" };
            msg.Metadata["device"] = "PLC-001";

            var result = _engine.ExecuteScript(@"
                local d = get_metadata('device')
                set_metadata('region', 'CN')
                return d, get_metadata('region')
            ", CreateContext(msg));

            Assert.Equal("PLC-001", result[0]);
            Assert.Equal("CN", result[1]);
            Assert.Equal("CN", msg.Metadata["region"]);
        }

        // ── 标签写入 API ──

        [Fact]
        public void ExecuteScript_AddWrite_AppendsToWrites()
        {
            var msg = new Message { Topic = "write" };
            _engine.ExecuteScript(@"
                add_write('DB1.DBW0', 12345, 'INT32', 'Temperature')
                add_write('DB1.DBW2', 67890, 'INT32', 'Pressure')
            ", CreateContext(msg));

            Assert.Equal(2, msg.Writes.Count);
            Assert.Equal("DB1.DBW0", msg.Writes[0].Address);
            Assert.Equal("Temperature", msg.Writes[0].Alias);
            Assert.Equal("DB1.DBW2", msg.Writes[1].Address);
        }

        // ── 状态管理 API ──

        [Fact]
        public void ExecuteScript_GetSetState()
        {
            var state = new Dictionary<string, object>();
            var ctx = new LuaScriptContext(
                new Message { Topic = "test" },
                (_, __) => { }, (_, __) => { }, (_, __) => { }, state);

            _engine.ExecuteScript("set_state('counter', 10)", ctx);
            Assert.Equal(10.0, Convert.ToDouble(state["counter"]));

            var result = _engine.ExecuteScript("return get_state('counter')", ctx);
            Assert.Equal(10.0, Convert.ToDouble(result[0]));
        }

        // ── 数学辅助 API ──

        [Fact]
        public void ExecuteScript_Clamp()
        {
            var ctx = CreateContext();
            var result = _engine.ExecuteScript(@"
                return clamp(150, 0, 100), clamp(-5, 0, 100), clamp(50, 0, 100)
            ", ctx);
            Assert.Equal(100.0, result[0]);
            Assert.Equal(0.0, result[1]);
            Assert.Equal(50.0, result[2]);
        }

        [Fact]
        public void ExecuteScript_InDeadband()
        {
            var ctx = CreateContext();
            var result = _engine.ExecuteScript(@"
                return in_deadband(10.1, 10, 0.5), in_deadband(11, 10, 0.5)
            ", ctx);
            Assert.True((bool)result[0]);
            Assert.False((bool)result[1]);
        }

        // ── 综合脚本场景 ──

        [Fact]
        public void ExecuteScript_TemperatureAlarmScenario()
        {
            var msg = new Message { Topic = "sensor/temp" };
            msg.SetJsonContent("{\"value\": 85.5, \"unit\": \"C\"}");

            _engine.ExecuteScript(@"
                local temp = 85.5
                if temp > 80 then
                    log_warn('Temperature exceeds threshold: ' .. temp)
                    add_write('M100', 1, 'BOOL', 'HighTempAlarm')
                end
                set_state('last_temp', temp)
            ", CreateContext(msg));

            Assert.Single(msg.Writes);
            Assert.Equal("M100", msg.Writes[0].Address);
            Assert.Equal("HighTempAlarm", msg.Writes[0].Alias);
        }

        [Fact]
        public void ExecuteScript_DataTransformationScenario()
        {
            var msg = new Message { Topic = "raw/data" };
            msg.SetTextContent("1024");

            var result = _engine.ExecuteScript(@"
                local raw = tonumber(get_text())
                local scaled = clamp(raw / 1000.0, 0, 10)
                add_write('DB1.DBW0', math.floor(scaled * 100), 'INT32', 'ScaledValue')
                return scaled
            ", CreateContext(msg));

            Assert.Single(msg.Writes);
            Assert.Equal("DB1.DBW0", msg.Writes[0].Address);
            Assert.Equal("ScaledValue", msg.Writes[0].Alias);
        }

        // ── 验证和错误 ──

        [Fact]
        public void ExecuteScript_NullScript_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _engine.ExecuteScript(null!, CreateContext()));
        }

        [Fact]
        public void ExecuteScript_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _engine.ExecuteScript("return 1", null!));
        }

        [Fact]
        public void Dispose_CanDispose()
        {
            var e = new LuaScriptEngine();
            e.Dispose();
            Assert.Throws<ObjectDisposedException>(() => e.ExecuteScript("return 1", CreateContext()));
        }

        // ── LuaScriptOutputPlugin ──

        [Fact]
        public async Task LuaScriptOutputPlugin_StartsWithInlineScript()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "TestLua",
                InlineScript = @"
                    local topic = get_topic()
                    log_info('Processing: ' .. topic)
                ",
                Enabled = true
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();
            Assert.Equal(PluginStatus.Running, plugin.Status);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task LuaScriptOutputPlugin_SendAsync_ExecutesScript()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "TestLua",
                InlineScript = @"
                    add_write('Q0.0', 1, 'BOOL', 'OutputBit')
                ",
                Enabled = true
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();

            var msg = new Message { Topic = "test" };
            await plugin.SendAsync(msg);

            Assert.Single(msg.Writes);
            Assert.Equal(1, plugin.MessagesProcessed);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task LuaScriptOutputPlugin_UpdateScript_HotReload()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "TestLua",
                InlineScript = "add_write('A', 1, 'INT32')",
                Enabled = true
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();

            // 热更新脚本
            plugin.UpdateScript("add_write('B', 2, 'INT32', 'NewWrite')");

            var msg = new Message { Topic = "test" };
            await plugin.SendAsync(msg);

            Assert.Single(msg.Writes);
            Assert.Equal("B", msg.Writes[0].Address);
            await plugin.StopAsync();
        }

        [Fact]
        public async Task LuaScriptOutputPlugin_UpdateScript_Null_Throws()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "TestLua",
                InlineScript = "add_write('A', 1, 'INT32')",
                Enabled = true
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();

            Assert.Throws<ArgumentNullException>(() => plugin.UpdateScript(null!));
            await plugin.StopAsync();
        }

        [Fact]
        public async Task LuaScriptOutputPlugin_UpdateScript_SyntaxError_Throws()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "TestLua",
                InlineScript = "add_write('A', 1, 'INT32')",
                Enabled = true
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();

            Assert.Throws<NLua.Exceptions.LuaScriptException>(() => plugin.UpdateScript("function bad("));
            await plugin.StopAsync();
        }

        [Fact]
        public async Task LuaScriptOutputPlugin_SyntaxError_BecomesFatal()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "BadLua",
                InlineScript = "if then end",
                Enabled = true
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();
            // OutputPluginBase.StartAsync 捕获异常并设 Status=Fatal，不向上抛出
            Assert.Equal(PluginStatus.Fatal, plugin.Status);
        }

        [Fact]
        public async Task LuaScriptOutputPlugin_Disabled_BecomesFatal()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "DisabledLua",
                InlineScript = "return 1",
                Enabled = false
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();
            // OutputPluginBase.StartAsync 捕获异常并设 Status=Fatal，不向上抛出
            Assert.Equal(PluginStatus.Fatal, plugin.Status);
        }

        [Fact]
        public async Task LuaScriptOutputPlugin_GetStats_ReturnsData()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "StatsLua",
                InlineScript = "log_info('ok')",
                Enabled = true
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();
            await plugin.SendAsync(new Message { Topic = "test" });

            var stats = plugin.GetStats();
            Assert.Equal(1, stats["messages_processed"]);
            Assert.Equal(0, stats["script_errors"]);
            Assert.Equal(true, stats["enabled"]);

            await plugin.StopAsync();
        }

        // ── 跨消息状态持久化（核心场景）──

        [Fact]
        public async Task LuaScriptOutputPlugin_StatePersistsAcrossMessages()
        {
            var config = new LuaScriptOutputConfig
            {
                Name = "StateTest",
                InlineScript = @"
                    local count = get_state('count') or 0
                    count = count + 1
                    set_state('count', count)
                    add_write('C', count, 'INT32', 'Counter')
                ",
                Enabled = true
            };

            using var plugin = new LuaScriptOutputPlugin(config);
            await plugin.StartAsync();

            // 第一条消息：count 应为 1
            var msg1 = new Message { Topic = "test" };
            await plugin.SendAsync(msg1);
            Assert.Single(msg1.Writes);
            Assert.Equal(1.0, Convert.ToDouble(msg1.Writes[0].Value));

            // 第二条消息：count 应为 2（状态跨消息持久化）
            var msg2 = new Message { Topic = "test" };
            await plugin.SendAsync(msg2);
            Assert.Single(msg2.Writes);
            Assert.Equal(2.0, Convert.ToDouble(msg2.Writes[0].Value));

            // 第三条消息：count 应为 3
            var msg3 = new Message { Topic = "test" };
            await plugin.SendAsync(msg3);
            Assert.Single(msg3.Writes);
            Assert.Equal(3.0, Convert.ToDouble(msg3.Writes[0].Value));

            await plugin.StopAsync();
        }

        // ── ExecuteFile 测试 ──

        [Fact]
        public void ExecuteFile_ExecutesScriptFromFile()
        {
            var tempFile = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempFile, "return 99");
                var result = _engine.ExecuteFile(tempFile, CreateContext());
                Assert.Equal(1, result.Length);
                Assert.Equal(99.0, Convert.ToDouble(result[0]));
            }
            finally
            {
                System.IO.File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExecuteFile_NotFound_Throws()
        {
            Assert.Throws<System.IO.FileNotFoundException>(() => _engine.ExecuteFile("/nonexistent/script.lua", CreateContext()));
        }

        // ── ReloadScript 测试 ──

        [Fact]
        public async Task LuaScriptOutputPlugin_ReloadScript_FromFile()
        {
            var tempFile = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempFile, "add_write('A', 1, 'INT32')");

                var config = new LuaScriptOutputConfig
                {
                    Name = "ReloadTest",
                    ScriptPath = tempFile,
                    Enabled = true
                };

                using var plugin = new LuaScriptOutputPlugin(config);
                await plugin.StartAsync();

                // 第一次执行
                var msg1 = new Message { Topic = "test" };
                await plugin.SendAsync(msg1);
                Assert.Equal("A", msg1.Writes[0].Address);

                // 修改文件内容
                System.IO.File.WriteAllText(tempFile, "add_write('B', 2, 'INT32', 'Reloaded')");
                plugin.ReloadScript();

                // 第二次执行应使用新脚本
                var msg2 = new Message { Topic = "test" };
                await plugin.SendAsync(msg2);
                Assert.Equal("B", msg2.Writes[0].Address);
                Assert.Equal("Reloaded", msg2.Writes[0].Alias);

                await plugin.StopAsync();
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                    System.IO.File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task LuaScriptOutputPlugin_ReloadScript_SyntaxError_Throws()
        {
            var tempFile = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempFile, "add_write('A', 1, 'INT32')");

                var config = new LuaScriptOutputConfig
                {
                    Name = "ReloadErrorTest",
                    ScriptPath = tempFile,
                    Enabled = true
                };

                using var plugin = new LuaScriptOutputPlugin(config);
                await plugin.StartAsync();

                // 写入有语法错误的脚本
                System.IO.File.WriteAllText(tempFile, "function foo(");
                Assert.Throws<NLua.Exceptions.LuaScriptException>(() => plugin.ReloadScript());

                await plugin.StopAsync();
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                    System.IO.File.Delete(tempFile);
            }
        }

        // ── ValidateScript 测试 ──

        [Fact]
        public void ValidateScript_ValidScript_DoesNotThrow()
        {
            _engine.ValidateScript("return 42");
        }

        [Fact]
        public void ValidateScript_SyntaxError_Throws()
        {
            Assert.Throws<NLua.Exceptions.LuaScriptException>(() => _engine.ValidateScript("function foo("));
        }

        // ── CancellationToken 中断测试 ──

        [Fact]
        public void ExecuteScript_CancellationToken_InfinteLoop_Cancels()
        {
            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var ctx = CreateContext();

            // 无限循环脚本 — 应被 Debug Hook 中断
            var script = "local i = 0 while true do i = i + 1 end";

            Assert.Throws<OperationCanceledException>(() => _engine.ExecuteScript(script, ctx, cts.Token));
        }

        [Fact]
        public void ExecuteScript_CancellationToken_None_Succeeds()
        {
            var ctx = CreateContext();
            var script = "return 42";

            var result = _engine.ExecuteScript(script, ctx, System.Threading.CancellationToken.None);
            // Lua returns numbers as double; assert by converting to a common type
            Assert.Equal(42.0, Convert.ToDouble(result[0]));
        }

        [Fact]
        public void ExecuteScript_CancellationToken_AlreadyCancelled_Throws()
        {
            var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();
            var ctx = CreateContext();

            var script = "local i = 0 for i=1,100000 do end return i";

            Assert.Throws<OperationCanceledException>(() => _engine.ExecuteScript(script, ctx, cts.Token));
        }
    }
}
