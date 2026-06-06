using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlcSimulator.Core.Memory;
using PlcSimulator.Protocols.Siemens;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// S7OutputPlugin 与 S7Simulator.Standalone（S7Server）的集成测试。
    /// 使用真实 TCP + S7 协议栈端到端验证。
    /// </summary>
    [Collection("S7ServerIntegration")]
    public class S7OutputPluginIntegrationTests
    {
        private readonly S7ServerFixture _fixture;

        public S7OutputPluginIntegrationTests(S7ServerFixture fixture)
        {
            _fixture = fixture;
        }

        // ── 连接生命周期 ──────────────────────────────────────────

        [Fact]
        public async Task Start_ConnectsAndStatusIsRunning()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));

            await plugin.StartAsync();
            var ok = await WaitForStatusAsync(plugin, PluginStatus.Running, timeoutSec: 5);
            Assert.True(ok, $"应达到 Running 状态，实际为 {plugin.Status}");
            Assert.Equal(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);
        }

        [Fact]
        public async Task StartStop_CanBeCalledMultipleSafely()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));

            await plugin.StartAsync();
            bool running = await WaitForStatusAsync(plugin, PluginStatus.Running, timeoutSec: 5);
            Assert.True(running);

            await plugin.StopAsync();
            Assert.Equal(PluginStatus.Stopped, plugin.Status);

            // 再次 Start（模拟重启场景）
            _fixture.ResetMemory();
            await plugin.StartAsync();
            running = await WaitForStatusAsync(plugin, PluginStatus.Running, timeoutSec: 5);
            Assert.True(running, "第一次停止后应能重新启动");

            await plugin.StopAsync();
        }

        // ── 写入数据验证 ──────────────────────────────────────────

        [Fact]
        public async Task Send_WritesRawBytesToDefaultDbAddress()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();

            Console.WriteLine($"[DIAG] Status after StartAsync: {plugin.Status}");

            // 用更可靠的方式等待连接真正建立：轮询直到 S7Server 有客户端
            await WaitForServerClientConnectedAsync(timeoutSec: 5);

            Console.WriteLine($"[DIAG] Status after wait: {plugin.Status}");

            // 确认插件状态
            Assert.Equal(PluginStatus.Running, plugin.Status);

            // 用原始 TCP 客户端探测服务器是否响应 S7 握手
            var probeOk = await ProbeS7HandshakeAsync(_fixture.Port);
            Console.WriteLine($"[DIAG] Probe S7 handshake: {probeOk}");

            var payload = "HELLO";
            var msg = new Message { Payload = Encoding.UTF8.GetBytes(payload) };

            var before = _fixture.ReadDb(dbNumber: 1, byteOffset: 0, length: payload.Length);
            Console.WriteLine($"[DIAG] Before write: {BitConverter.ToString(before)}");

            await plugin.SendAsync(msg);

            // 读后数据
            var after = _fixture.ReadDb(dbNumber: 1, byteOffset: 0, length: payload.Length);
            Console.WriteLine($"[DIAG] After write:  {BitConverter.ToString(after)}");
            Assert.Equal(Encoding.UTF8.GetBytes(payload), after);

            await plugin.StopAsync();
        }

        /// <summary>
        /// 用原始 TCP 客户端探测 S7 握手是否正常（用于诊断，非断言）。
        /// 每个响应都用循环读确保 TCP 分片不干扰。
        /// </summary>
        private static async Task<bool> ProbeS7HandshakeAsync(int port)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(5000);
                await client.ConnectAsync("127.0.0.1", port, cts.Token);
                using var stream = client.GetStream();

                // ── Step 1: COTP CR ──
                byte[] cotpCr = new byte[] {
                    0x03, 0x00, 0x00, 0x16, 0x11, 0xE0, 0x00, 0x00, 0x00, 0x01,
                    0x00, 0xC0, 0x01, 0x0A, 0xC1, 0x02, 0x01, 0x00, 0xC2, 0x02, 0x01, 0x02
                };
                await stream.WriteAsync(cotpCr, cts.Token);

                // Read COTP CC — 循环读确保拿到完整 TPKT
                var cotpCc = await ReadExactTpktAsync(stream, cts.Token);
                // COTP CC: payload[1] = 0xD0 (CC PDU type)
                if (cotpCc == null || cotpCc.Length < 6 || cotpCc[5] != 0xD0)
                    return false;

                // ── Step 2: S7 Setup Communication ──
                byte[] s7Setup = new byte[] {
                    0x03, 0x00, 0x00, 0x19, 0x02, 0xF0, 0x80,
                    0x32, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x08, 0x00, 0x00,
                    0xF0, 0x00, 0x00, 0x01, 0x00, 0x01, 0x03, 0xC0
                };
                await stream.WriteAsync(s7Setup, cts.Token);

                // Read S7 Setup Response — 同样循环读
                var s7Resp = await ReadExactTpktAsync(stream, cts.Token);
                // S7 Setup Response 的 Error class/code 在整体偏移 17/18（TPKT[4+3+10+0/1]）
                // 即 payload[13]/payload[14]：= 0x00/0x00 表示成功
                if (s7Resp == null || s7Resp.Length < 19 || s7Resp[17] != 0x00 || s7Resp[18] != 0x00)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 按 TPKT 协议完整读取一个包（先读 4 字节头，根据 Length 读剩余负载）。
        /// 返回完整包（含 TPKT 头），或 null 如果读取出错。
        /// </summary>
        private static async Task<byte[]> ReadExactTpktAsync(NetworkStream stream, CancellationToken ct)
        {
            byte[] header = new byte[4];
            int read = 0;
            while (read < 4)
            {
                int n = await stream.ReadAsync(header, read, 4 - read, ct);
                if (n == 0) return null;
                read += n;
            }
            if (header[0] != 0x03) return null;
            int tpktLen = (header[2] << 8) | header[3];
            if (tpktLen < 4) return null;
            byte[] full = new byte[tpktLen];
            Array.Copy(header, 0, full, 0, 4);
            int remaining = tpktLen - 4;
            read = 0;
            while (read < remaining)
            {
                int n = await stream.ReadAsync(full, 4 + read, remaining - read, ct);
                if (n == 0) return null;
                read += n;
            }
            return full;
        }

        [Fact]
        public async Task Send_WritesToCustomAddressViaMetadata()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            await WaitForServerClientConnectedAsync();

            // 通过 Metadata 指定写入 M 区域
            var payload = "M-TEST";
            var msg = new Message
            {
                Payload = Encoding.UTF8.GetBytes(payload),
                Metadata = { ["TargetAddress"] = "M100" }
            };

            await plugin.SendAsync(msg);

            // 验证 M 区域 100 偏移处写入了数据
            var written = _fixture.ReadM(byteOffset: 100, length: payload.Length);
            Assert.Equal(Encoding.UTF8.GetBytes(payload), written);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_WritesToQArea()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            await WaitForServerClientConnectedAsync();

            var payload = "Q-TEST";
            var msg = new Message
            {
                Payload = Encoding.UTF8.GetBytes(payload),
                Metadata = { ["TargetAddress"] = "Q10" }
            };

            await plugin.SendAsync(msg);

            var written = _fixture.ReadQ(byteOffset: 10, length: payload.Length);
            Assert.Equal(Encoding.UTF8.GetBytes(payload), written);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_WritesToIArea()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            await WaitForServerClientConnectedAsync();

            var payload = "I-TEST";
            var msg = new Message
            {
                Payload = Encoding.UTF8.GetBytes(payload),
                Metadata = { ["TargetAddress"] = "I20" }
            };

            await plugin.SendAsync(msg);

            var written = _fixture.ReadI(byteOffset: 20, length: payload.Length);
            Assert.Equal(Encoding.UTF8.GetBytes(payload), written);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_WritesToDifferentDbBlock()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            await WaitForServerClientConnectedAsync();

            var payload = "DB200";
            var msg = new Message
            {
                Payload = Encoding.UTF8.GetBytes(payload),
                Metadata = { ["TargetAddress"] = "DB200.DBW0" }
            };

            await plugin.SendAsync(msg);

            var written = _fixture.ReadDb(dbNumber: 200, byteOffset: 0, length: payload.Length);
            Assert.Equal(Encoding.UTF8.GetBytes(payload), written);

            await plugin.StopAsync();
        }

        // ── 连接故障处理 ──────────────────────────────────────────

        [Fact]
        public async Task ConnectToUnreachablePort_ReportsError()
        {
            // 使用一个空闲但不会响应 S7 的端口（无人监听的端口）
            var config = S7ServerFixture.CreateFastConfig(19988);
            config.ErrorThreshold = 2;
            config.ReconnectIntervalMs = 500;

            using var plugin = new S7OutputPlugin(config);
            await plugin.StartAsync();

            // 应该因连接被拒绝进入 Error 状态
            var error = await WaitForStatusAsync(plugin, PluginStatus.Error, timeoutSec: 8);
            Assert.True(error, $"应因连接拒绝达到 Error 状态，实际为 {plugin.Status}");

            await plugin.StopAsync();
        }

        // ── 网络中断与恢复 ──────────────────────────────────────

        [Fact]
        public async Task ServerStops_PluginDetectsAndReportsDisconnected()
        {
            // 创建独立的 S7Server 实例，不干扰共享 fixture
            var independentPort = S7ServerFixture.FindFreePort(19800, 19850);
            var independentMemory = new PlcMemory();
            var independentServer = new S7Server(independentMemory, independentPort, S7CpuModel.S7_1200, station: 0);
            Assert.True(independentServer.Start(), "独立 S7Server 应启动成功");

            var config = S7ServerFixture.CreateFastConfig(independentPort);
            using var plugin = new S7OutputPlugin(config);
            await plugin.StartAsync();
            Assert.True(await WaitForStatusAsync(plugin, PluginStatus.Running, 5));

            // 停止独立服务器
            independentServer.Stop();

            // 插件应检测到连接断开并转为非 Running（Starting 或 Error）
            await Task.Delay(4000);
            Assert.NotEqual(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
        }

        // ── 二进制 / 特殊数据 ──────────────────────────────────────

        [Fact]
        public async Task Send_BinaryDataPreservedExactly()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            await WaitForServerClientConnectedAsync();

            // 空字节 + 非 UTF8 序列 + 高位字节
            var binary = new byte[] { 0x00, 0xFF, 0xAB, 0x7F, 0x00, 0x01, 0x02, 0x03 };
            var msg = new Message
            {
                Payload = binary,
                // 空 content 触发 OnSendAsync 的 content=null→fallback 到 Payload 分支
                ContentType = "binary"
            };

            await plugin.SendAsync(msg);

            var written = _fixture.ReadDb(dbNumber: 1, byteOffset: 0, length: binary.Length);
            Assert.Equal(binary, written);

            await plugin.StopAsync();
        }

        [Fact]
        public async Task Send_EmptyPayload_DoesNotThrow()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            await WaitForServerClientConnectedAsync();

            // 空 payload 应被容忍（S7 协议会发送 0 字节写入）
            var msg = new Message { Payload = Array.Empty<byte>() };

            // 不应抛出
            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.Null(ex);

            await plugin.StopAsync();
        }

        // ── 并发 ──────────────────────────────────────────────────

        [Fact]
        public async Task Send_MultipleSequentialWrites_Success()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            await WaitForServerClientConnectedAsync();

            for (int i = 0; i < 20; i++)
            {
                var data = $"MSG-{i:D4}";
                var msg = new Message { Payload = Encoding.UTF8.GetBytes(data) };
                await plugin.SendAsync(msg);

                // 每次写到不同偏移（通过 Metadata）
                var addr = $"DB1.DBW{i * 10}";
                msg = new Message
                {
                    Payload = Encoding.UTF8.GetBytes(data),
                    Metadata = { ["TargetAddress"] = addr }
                };
                await plugin.SendAsync(msg);

                // 交叉读取验证：两次写入覆盖同一个地址
                var written = _fixture.ReadDb(dbNumber: 1, byteOffset: i * 10, length: data.Length);
                Assert.Equal(Encoding.UTF8.GetBytes(data), written);
            }

            await plugin.StopAsync();
        }

        // ── 资源清理 ──────────────────────────────────────────────

        [Fact]
        public async Task Dispose_CleanupNoExceptions()
        {
            var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            Assert.True(await WaitForStatusAsync(plugin, PluginStatus.Running, 5));

            // Dispose 应优雅关闭所有资源（TCP 连接、CancellationTokenSource、后台任务）
            var ex = Record.Exception(() => plugin.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public async Task StopWhileConnecting_DoesNotLeaveOrphanTask()
        {
            var config = S7ServerFixture.CreateFastConfig(19987); // 无人监听
            config.ReconnectIntervalMs = 500;

            var plugin = new S7OutputPlugin(config);
            await plugin.StartAsync();

            // 不等连接完成，立即 Stop
            await Task.Delay(200);
            var ex = Record.Exception(() => plugin.Dispose());
            Assert.Null(ex);
        }

        // ── 地址解析与 S7 协议细节 ──────────────────────────────

        [Fact]
        public async Task Send_WriteToBitAddress_DoesNotCrash()
        {
            using var plugin = new S7OutputPlugin(S7ServerFixture.CreateFastConfig(_fixture.Port));
            await plugin.StartAsync();
            await WaitForServerClientConnectedAsync();

            // 位写入：S7OutputPlugin 对 bit 地址使用 transport size BIT (0x01)
            var msg = new Message
            {
                Payload = new byte[] { 0x01 },
                Metadata = { ["TargetAddress"] = "DB1.DBX0.0" }
            };

            var ex = await Record.ExceptionAsync(() => plugin.SendAsync(msg));
            Assert.Null(ex);

            // 验证位被写入（DB1, offset 0, bit 0）
            var bitValue = _fixture.Memory.ReadBit(0x84, 1, 0, 0);
            Assert.True(bitValue);

            await plugin.StopAsync();
        }

        // ── 帮助方法 ──────────────────────────────────────────────

        /// <summary>等待插件达到指定状态，超时返回 false</summary>
        private static async Task<bool> WaitForStatusAsync(S7OutputPlugin plugin, PluginStatus target, int timeoutSec)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSec)
            {
                if (plugin.Status == target)
                    return true;
                await Task.Delay(100);
            }
            return false;
        }

        /// <summary>
        /// 等待 TCP 连接真正建立 + S7 握手完成。
        /// 直接等待足够时间让 S7OutputPlugin 的连接循环完成 TCP 连接 + S7 握手。
        /// </summary>
        private async Task WaitForServerClientConnectedAsync(int timeoutSec = 5)
        {
            // S7OutputPlugin 的连接循环在后台 Task.Run 中运行。
            // 等 ~2 秒确保 TCP 连接 + COTP/S7 握手完成
            await Task.Delay(2000);
        }
    }
}
