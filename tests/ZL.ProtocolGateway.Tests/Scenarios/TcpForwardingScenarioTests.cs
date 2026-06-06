using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Scenarios
{
    /// <summary>
    /// 真实转发场景集成测试 - 使用真实 Socket 通信
    /// 串行执行（GatewayLog 静态 _customWriter 不兼容并行）
    /// 
    /// 防 Flaky 设计原则：
    /// 1. 所有端口使用 GetFreeTcpPort() 动态分配，消除端口冲突
    /// 2. 用轮询等待（WaitForConditionAsync）替代固定 Task.Delay，消除时序敏感
    /// 3. 时序相关的日志计数用 InRange 范围断言替代精确相等断言
    /// 4. 每测试有总超时保护，防止 hang
    /// </summary>
    [Collection(TcpScenarioTestCollection.Name)]
    public class TcpForwardingScenarioTests : IDisposable
    {
        private TcpListener _echoServer;
        private Task _echoTask;
        private CancellationTokenSource _echoCts;
        private ConcurrentBag<string> _echoMessages = new();

        public void Dispose()
        {
            _echoCts?.Cancel();
            _echoServer?.Stop();
        }

        /// <summary>
        /// 场景 1: TCP 端口转发 (动态端口 -> Echo Server)
        /// 模拟客户端发送数据，验证 Gateway 正确转发
        /// </summary>
        [Fact(Timeout = 15_000)]
        public async Task TcpForwarding_SendsDataToTarget_EchoServerReceivesIt()
        {
            // 1. 启动 Echo Server (模拟目标服务器)
            _echoMessages = new ConcurrentBag<string>();
            var echoPort = GetFreeTcpPort();
            var inputPort = GetFreeTcpPort();
            StartEchoServer(echoPort);
            await WaitForEchoServerReady(echoPort);

            // 2. 配置 Gateway 的 Output
            var outputConfig = new TcpOutputConfig
            {
                Name = "EchoTarget",
                ServerIp = "127.0.0.1",
                Port = echoPort,
                Suffix = "\n",
                ReconnectIntervalMs = 500
            };
            var tcpOutput = new TcpOutputPlugin(outputConfig);

            // 3. 配置 Pipeline
            var pipeline = new ResilientMessagePipeline();
            pipeline.RegisterOutput(tcpOutput);
            pipeline.AddRouter(new RouteRule
            {
                Condition = msg => true,
                OutputNames = { "EchoTarget" }
            });

            await pipeline.StartAsync();

            // 4. 启动 Input (动态端口)
            var inputConfig = new TcpInputConfig
            {
                Port = inputPort,
                Delimiter = Encoding.ASCII.GetBytes("\n")
            };
            var tcpInput = new TcpInputPlugin(inputConfig);

            var gateway = new GatewayService(pipeline);
            gateway.AddInput(tcpInput);
            await gateway.StartAsync();

            // 等待 Output 成功连接到 Echo Server
            await WaitForConditionAsync(
                () => tcpOutput.Status == PluginStatus.Running,
                TimeSpan.FromSeconds(5),
                "Output did not connect to Echo Server");

            // 5. 客户端发送数据
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", inputPort);
            using var stream = client.GetStream();
            var data = Encoding.ASCII.GetBytes("Hello Gateway\n");
            await stream.WriteAsync(data, 0, data.Length);

            // 6. 轮询验证 Echo Server 收到了数据（最长等 5s）
            await WaitForConditionAsync(
                () => _echoMessages.Any(m => m.Contains("Hello Gateway")),
                TimeSpan.FromSeconds(5),
                "Echo Server did not receive expected message");

            Assert.NotEmpty(_echoMessages);
            Assert.Contains(_echoMessages, m => m.Contains("Hello Gateway"));

            await gateway.StopAsync();
        }

        /// <summary>
        /// 场景 2: 粘包处理 - 多条消息合并在一次 TCP 发送中
        /// </summary>
        [Fact(Timeout = 15_000)]
        public async Task TcpForwarding_StickyPackets_SplitsCorrectly()
        {
            _echoMessages = new ConcurrentBag<string>();
            var echoPort = GetFreeTcpPort();
            var inputPort = GetFreeTcpPort();
            StartEchoServer(echoPort);
            await WaitForEchoServerReady(echoPort);

            var pipeline = new ResilientMessagePipeline();
            var output = new TcpOutputPlugin(new TcpOutputConfig
            {
                Name = "StickyTest",
                ServerIp = "127.0.0.1",
                Port = echoPort,
                Suffix = "",
                ReconnectIntervalMs = 500
            });
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule
            {
                Condition = msg => true,
                OutputNames = { output.Name }
            });
            await pipeline.StartAsync();

            var gateway = new GatewayService(pipeline);
            gateway.AddInput(new TcpInputPlugin(new TcpInputConfig
            {
                Port = inputPort,
                Delimiter = Encoding.ASCII.GetBytes("\n")
            }));
            await gateway.StartAsync();

            // 等待 Output 成功连接到 Echo Server
            await WaitForConditionAsync(
                () => output.Status == PluginStatus.Running,
                TimeSpan.FromSeconds(5),
                "Output did not connect to Echo Server");

            // 发送粘包数据 (3 条消息合并)
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", inputPort);
            using var stream = client.GetStream();
            var stickyData = Encoding.ASCII.GetBytes("Msg1\nMsg2\nMsg3\n");
            await stream.WriteAsync(stickyData);
            await stream.FlushAsync();

            // 轮询验证所有消息都收到了（最长等 5s）
            await WaitForConditionAsync(
                () =>
                {
                    return _echoMessages.Any(m => m.Contains("Msg1"))
                        && _echoMessages.Any(m => m.Contains("Msg2"))
                        && _echoMessages.Any(m => m.Contains("Msg3"));
                },
                TimeSpan.FromSeconds(5),
                "Echo Server did not receive all 3 messages");

            Assert.True(_echoMessages.Any(m => m.Contains("Msg1")));
            Assert.True(_echoMessages.Any(m => m.Contains("Msg2")));
            Assert.True(_echoMessages.Any(m => m.Contains("Msg3")));

            await gateway.StopAsync();
        }

        /// <summary>
        /// 场景 3: 多客户端并发连接
        /// </summary>
        [Fact(Timeout = 15_000)]
        public async Task TcpForwarding_MultipleClients_AllForwarded()
        {
            _echoMessages = new ConcurrentBag<string>();
            var echoPort = GetFreeTcpPort();
            var inputPort = GetFreeTcpPort();
            StartEchoServer(echoPort);
            await WaitForEchoServerReady(echoPort);

            var pipeline = new ResilientMessagePipeline();
            var output = new TcpOutputPlugin(new TcpOutputConfig
            {
                Name = "MultiClientTest",
                ServerIp = "127.0.0.1",
                Port = echoPort,
                Suffix = "\n",
                ReconnectIntervalMs = 500
            });
            pipeline.RegisterOutput(output);
            pipeline.AddRouter(new RouteRule { Condition = msg => true, OutputNames = { output.Name } });
            await pipeline.StartAsync();

            var gateway = new GatewayService(pipeline);
            gateway.AddInput(new TcpInputPlugin(new TcpInputConfig
            {
                Port = inputPort,
                Delimiter = Encoding.ASCII.GetBytes("\n")
            }));
            await gateway.StartAsync();

            // 等待 Output 连接到 Echo Server
            await WaitForConditionAsync(
                () => output.Status == PluginStatus.Running,
                TimeSpan.FromSeconds(5),
                "Output did not connect to Echo Server");

            // 启动 3 个客户端（顺序发送，每条消息等待 100ms 确保 TCP 管道刷新）
            for (int i = 0; i < 3; i++)
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", inputPort);
                using var stream = client.GetStream();
                var data = Encoding.ASCII.GetBytes($"Client{i}\n");
                await stream.WriteAsync(data);
                await stream.FlushAsync();
                // 给 TCP 管道和 Echo Server 足够的处理时间，避免 using 立即关闭导致数据丢失
                await Task.Delay(100);
            }

            // 轮询验证收到了 3 条消息
            await WaitForConditionAsync(
                () => _echoMessages.Count(m => !string.IsNullOrWhiteSpace(m)) >= 3,
                TimeSpan.FromSeconds(5),
                $"Expected >= 3 messages, got {_echoMessages.Count}");

            var validMessages = _echoMessages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            Assert.True(validMessages.Count >= 3,
                $"Expected >= 3 messages, got {validMessages.Count}: [{string.Join(", ", validMessages)}]");

            await gateway.StopAsync();
        }

        /// <summary>
        /// 空闲连接保持测试：验证服务器保活时不会触发重连
        /// </summary>
        [Fact(Timeout = 10_000)]
        public async Task TcpOutput_IdleConnection_DoesNotReconnectWhileServerKeepsSocketOpen()
        {
            var port = GetFreeTcpPort();
            using var serverCts = new CancellationTokenSource();
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();

            var acceptedTcs = new TaskCompletionSource<TcpClient>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(serverCts.Token);
                    acceptedTcs.TrySetResult(client);
                    using var stream = client.GetStream();
                    var buffer = new byte[16];
                    while (!serverCts.Token.IsCancellationRequested)
                    {
                        var read = await stream.ReadAsync(buffer, serverCts.Token);
                        if (read <= 0) break;
                    }
                }
                catch
                {
                }
            }, serverCts.Token);

            var plugin = new TcpOutputPlugin(new TcpOutputConfig
            {
                Name = "IdleTest",
                ServerIp = "127.0.0.1",
                Port = port,
                ReconnectIntervalMs = 500
            });

            int connectedCount = 0;
            int disconnectedCount = 0;
            plugin.ConnectionChanged += (_, connected) =>
            {
                if (connected) connectedCount++;
                else disconnectedCount++;
            };

            await plugin.StartAsync();
            using var accepted = await acceptedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // 等待足够时间验证不会重连
            await Task.Delay(1500);

            Assert.Equal(1, connectedCount);
            Assert.Equal(0, disconnectedCount);
            Assert.Equal(PluginStatus.Running, plugin.Status);

            await plugin.StopAsync();
            serverCts.Cancel();
            listener.Stop();
        }

        /// <summary>
        /// 停止连接时不会有重连失败日志
        /// </summary>
        [Fact(Timeout = 10_000)]
        public async Task TcpOutput_StopWhileConnected_DoesNotEmitReconnectFailureNoise()
        {
            var port = GetFreeTcpPort();
            using var serverCts = new CancellationTokenSource();
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();

            var acceptedTcs = new TaskCompletionSource<TcpClient>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(serverCts.Token);
                    acceptedTcs.TrySetResult(client);
                    using var stream = client.GetStream();
                    var buffer = new byte[16];
                    while (!serverCts.Token.IsCancellationRequested)
                    {
                        var read = await stream.ReadAsync(buffer, serverCts.Token);
                        if (read <= 0) break;
                    }
                }
                catch
                {
                }
            }, serverCts.Token);

            var plugin = new TcpOutputPlugin(new TcpOutputConfig
            {
                Name = "StopNoiseTest",
                ServerIp = "127.0.0.1",
                Port = port,
                ReconnectIntervalMs = 500
            });

            using var writer = new StringWriter();
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.SetOutput(writer);

            try
            {
                await plugin.StartAsync();
                using var accepted = await acceptedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                // 短暂等待确保连接稳定
                await Task.Delay(200);
                await plugin.StopAsync();
                // 等待停止完成
                await Task.Delay(500);
            }
            finally
            {
                GatewayLog.ResetOutput();
                serverCts.Cancel();
                listener.Stop();
            }

            string output = writer.ToString();
            Assert.DoesNotContain("Connection failed: A task was canceled", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Retrying in", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Stop requested", output, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 延迟启动服务器：验证连接被拒时日志消噪，然后成功连接
        /// </summary>
        [Fact(Timeout = 10_000)]
        public async Task TcpOutput_DelayedServerStartup_ThrottlesConnectionRefusedNoise_AndEventuallyConnects()
        {
            var port = GetFreeTcpPort();
            var plugin = new TcpOutputPlugin(new TcpOutputConfig
            {
                Name = "DelayedStartTest",
                ServerIp = "127.0.0.1",
                Port = port,
                ReconnectIntervalMs = 500,
                LocalStartupQuietPeriodMs = 2000
            });

            using var writer = new StringWriter();
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.SetOutput(writer);

            using var serverCts = new CancellationTokenSource();
            TcpListener listener = null!;
            try
            {
                await plugin.StartAsync();
                // 等待插件开始重连（此时没有服务器在监听）
                await Task.Delay(500);

                // 启动服务器
                listener = new TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();

                var acceptTask = listener.AcceptTcpClientAsync(serverCts.Token).AsTask();
                var completed = await Task.WhenAny(acceptTask, Task.Delay(TimeSpan.FromSeconds(3), serverCts.Token));
                Assert.Same(acceptTask, completed);
                var accepted = await acceptTask;
                using (accepted)
                {
                    // 验证连接稳定
                    await Task.Delay(300);
                }

                await plugin.StopAsync();
            }
            finally
            {
                GatewayLog.ResetOutput();
                serverCts.Cancel();
                listener?.Stop();
            }

            string output = writer.ToString();
            int initialNoiseCount = CountOccurrences(output, "not listening yet");

            // 应该至少有一次 "not listening yet"（首次连接失败），但不会太多
            Assert.True(initialNoiseCount >= 1,
                $"Expected at least 1 'not listening yet', got {initialNoiseCount}. Output: {output}");

            // 在宽限期内不应该有 "still not listening" 的周期性提醒
            // 但极慢机器上可能刚好跨过宽限期边界，容忍 1 条
            int repeatedNoiseCount = CountOccurrences(output, "still not listening");
            Assert.True(repeatedNoiseCount <= 1,
                $"Expected at most 1 'still not listening', got {repeatedNoiseCount}. Output: {output}");

            Assert.Contains($"Connected to 127.0.0.1:{port}", output, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 重启后重连：验证重启消除了 ConnectionRefused 故障窗口
        /// </summary>
        [Fact(Timeout = 10_000)]
        public async Task TcpOutput_RestartAfterConnectionRefused_ResetsFailureWindow()
        {
            var port = GetFreeTcpPort();
            var plugin = new TcpOutputPlugin(new TcpOutputConfig
            {
                Name = "RestartRefusedTest",
                ServerIp = "127.0.0.1",
                Port = port,
                ReconnectIntervalMs = 500
            });

            using var writer = new StringWriter();
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.SetOutput(writer);

            try
            {
                // 第 1 次启动 → 连接被拒 → 停止
                await plugin.StartAsync();
                await Task.Delay(300);
                await plugin.StopAsync();

                // 第 2 次启动 → 连接被拒 → 停止
                await plugin.StartAsync();
                await Task.Delay(300);
                await plugin.StopAsync();
            }
            finally
            {
                GatewayLog.ResetOutput();
            }

            string output = writer.ToString();
            int initialNoiseCount = CountOccurrences(output, "not listening yet");

            // 每次启动的首次连接失败都应该输出 "not listening yet"
            // 在低负载机器上精确为 2 次；高负载机器上可能略多（第一次重试恰好在 stop 前完成）
            Assert.True(initialNoiseCount >= 2 && initialNoiseCount <= 4,
                $"Expected 2-4 'not listening yet' occurrences, got {initialNoiseCount}. Output: {output}");

            // 重启后故障窗口重置，不应有 "still not listening"（周期性提醒在 streak 5 才触发）
            int repeatedNoiseCount = CountOccurrences(output, "still not listening");
            Assert.Equal(0, repeatedNoiseCount);
        }

        /// <summary>
        /// 长时间连接被拒验证：在安静期后只打印一次 "not listening yet"，
        /// 然后定期输出 "still not listening" 提醒
        /// </summary>
        [Fact(Timeout = 15_000)]
        public async Task TcpOutput_LongRunningConnectionRefused_LogsPeriodicReminderAfterQuietPeriod()
        {
            var port = GetFreeTcpPort();
            var plugin = new TcpOutputPlugin(new TcpOutputConfig
            {
                Name = "LongRefusedTest",
                ServerIp = "127.0.0.1",
                Port = port,
                ReconnectIntervalMs = 500,
                LocalStartupQuietPeriodMs = 500
            });

            using var writer = new StringWriter();
            GatewayLog.SetMinLevel(GatewayLog.LogLevel.Info);
            GatewayLog.SetOutput(writer);

            try
            {
                await plugin.StartAsync();
                // 等待足够长时间让多次重连失败触发 "still not listening" 提醒（streak >= 5）
                // 指数退避: 500 + 1000 + 2000 + 4000 = ~7500ms 到 streak 5
                await Task.Delay(8000);
                await plugin.StopAsync();
            }
            finally
            {
                GatewayLog.ResetOutput();
            }

            string output = writer.ToString();
            int initialNoiseCount = CountOccurrences(output, "not listening yet");
            int repeatedNoiseCount = CountOccurrences(output, "still not listening");

            // 安静期内只输出一次 "not listening yet"
            Assert.True(initialNoiseCount >= 1 && initialNoiseCount <= 2,
                $"Expected 1-2 'not listening yet', got {initialNoiseCount}. Output: {output}");

            // 安静期之后应有至少 1 次 "still not listening"（指数退避中 streak >= 5 时触发）
            Assert.True(repeatedNoiseCount >= 1,
                $"Expected at least 1 periodic reminder after quiet period, output: {output}");
        }

        #region Helpers

        /// <summary>
        /// 轮询等待条件满足，避免固定时序延迟导致 flaky 测试。
        /// 每 50ms 轮询一次，超过超时抛异常。
        /// </summary>
        private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, string failureMessage)
        {
            var start = DateTime.UtcNow;
            while (!condition())
            {
                if (DateTime.UtcNow - start > timeout)
                {
                    throw new TimeoutException(failureMessage);
                }
                await Task.Delay(50);
            }
        }

        /// <summary>
        /// 轮询等待 Echo Server 开始监听（尝试 TCP 连接，连接成功即代表就绪）
        /// </summary>
        private static async Task WaitForEchoServerReady(int port, int timeoutMs = 3000)
        {
            var start = DateTime.UtcNow;
            while (true)
            {
                try
                {
                    using var probe = new TcpClient();
                    var connect = probe.ConnectAsync("127.0.0.1", port);
                    using var timeoutCts = new CancellationTokenSource(500);
                    await connect.WaitAsync(timeoutCts.Token);
                    return;
                }
                catch
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    {
                        throw new TimeoutException($"Echo Server on port {port} did not start within {timeoutMs}ms");
                    }
                    await Task.Delay(50);
                }
            }
        }

        /// <summary>
        /// 统计字符串中子串出现次数
        /// </summary>
        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        /// <summary>
        /// 获取空闲 TCP 端口，消除硬编码端口冲突。
        /// </summary>
        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>
        /// 启动 Echo Server，将收到的 ASCII 消息存入 _echoMessages（线程安全）
        /// </summary>
        private void StartEchoServer(int port)
        {
            _echoCts = new CancellationTokenSource();
            _echoServer = new TcpListener(System.Net.IPAddress.Loopback, port);
            _echoServer.Start();

            _echoTask = Task.Run(async () =>
            {
                while (!_echoCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _echoServer.AcceptTcpClientAsync();
                        _ = Task.Run(async () =>
                        {
                            using var stream = client.GetStream();
                            var buffer = new byte[1024];
                            while (client.Connected)
                            {
                                var read = await stream.ReadAsync(buffer);
                                if (read == 0) break;
                                var msg = Encoding.ASCII.GetString(buffer, 0, read).Trim();
                                _echoMessages.Add(msg);
                            }
                        }, _echoCts.Token);
                    }
                    catch { break; }
                }
            }, _echoCts.Token);
        }

        #endregion
    }
}
