using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Framing;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// TCP 输入插件配置
    /// </summary>
    public class TcpInputConfig
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 监听 IP (默认 0.0.0.0)
        /// </summary>
        public string LocalIp { get; set; } = "0.0.0.0";

        /// <summary>
        /// 监听端口
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 消息分隔符 (用于处理 TCP 粘包/拆包)
        /// 例如: "\n", "\r\n", 或者 Hex "0x03"
        /// </summary>
        public byte[] Delimiter { get; set; } = Encoding.ASCII.GetBytes("\n");

        /// <summary>
        /// 单条消息最大长度 (字节)，防止内存溢出
        /// </summary>
        public int MaxMessageSize { get; set; } = 8192;

        /// <summary>
        /// 最大并发客户端连接数 — 0 表示不限制（默认 100）
        /// <para>防止洪水攻击或客户端泄漏导致文件描述符耗尽。</para>
        /// </summary>
        public int MaxConnections { get; set; } = 100;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(LocalIp))
                errors.Add(new ConfigValidationError(nameof(LocalIp), $"LocalIp '{LocalIp}' 不是有效的 IP 地址"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            if (MaxMessageSize <= 0)
                errors.Add(new ConfigValidationError(nameof(MaxMessageSize), $"MaxMessageSize {MaxMessageSize} 必须 > 0"));
            if (Delimiter == null || Delimiter.Length == 0)
                errors.Add(new ConfigValidationError(nameof(Delimiter), $"Delimiter 不能为空"));
            return errors;
        }
    }

    /// <summary>
    /// TCP 输入插件 - 监听 TCP 端口，接收数据并处理粘包后转发
    /// 核心机制: 维护一个 List of bytes 缓冲区，不断 Append 收到的数据，并扫描 Delimiter。
    /// </summary>
    public class TcpInputPlugin : InputPluginBase
    {
        private readonly TcpInputConfig _config;
        private TcpListener _listener;
        private readonly List<Task> _clientTasks = new();

        public override string Name { get; }

        public TcpInputPlugin(TcpInputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Name = config.Name ?? $"TcpInput-{config.Port}";
            // 分帧器由每个客户端独立创建，避免多线程并发访问同一实例导致数据交错
        }

        public override string ProtocolType => "Tcp";

        protected override async Task OnStartAsync(CancellationToken ct)
        {
            try
            {
                var ip = IPAddress.Parse(_config.LocalIp);
                _listener = new TcpListener(ip, _config.Port);
                _listener.Start();
                GatewayLog.Info("TcpInput", $"Listening on {ip}:{_config.Port}");

                // 启动接受连接循环（带异常捕获，避免 fire-and-forget 静默失败）
                var acceptTask = Task.Run(AcceptLoop, ct);
                _ = acceptTask.ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception is AggregateException ae)
                    {
                        GatewayLog.Error("TcpInput", $"Accept loop failed: {ae.InnerExceptions[0]?.Message}", ae);
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                GatewayLog.Error("TcpInput", $"Failed to start TCP listener: {ex.Message}", ex);
                throw new InvalidOperationException($"Failed to start TCP listener: {ex.Message}", ex);
            }
        }

        private async Task AcceptLoop()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                try
                {
                    // P0-5 修复：检查并发连接数上限，防止洪水攻击或客户端泄漏导致文件描述符耗尽
                    int activeConnections;
                    lock (_clientTasks) activeConnections = _clientTasks.Count;
                    if (_config.MaxConnections > 0 && activeConnections >= _config.MaxConnections)
                    {
                        var rejectedClient = await _listener.AcceptTcpClientAsync();
                        GatewayLog.Warn("TcpInput", $"Max connections ({_config.MaxConnections}) reached, rejecting {rejectedClient.Client.RemoteEndPoint}");
                        rejectedClient.Close();
                        continue;
                    }

                    var client = await _listener.AcceptTcpClientAsync();
                    GatewayLog.Info("TcpInput", $"Client connected: {client.Client.RemoteEndPoint}");
                    var task = HandleClientAsync(client);
                    lock (_clientTasks) _clientTasks.Add(task);

                    // 清理已完成的客户端任务，防止 _clientTasks 列表无限增长（内存泄漏）
                    _ = task.ContinueWith(t =>
                    {
                        lock (_clientTasks) _clientTasks.Remove(t);
                    }, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("TcpInput", $"Accept error: {ex.Message}", ex);
                    await Task.Delay(1000, CancellationToken); // 避免错误风暴
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            var readBuffer = new byte[4096];
            var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            // 每个客户端使用独立分帧器，避免多线程并发访问同一实例导致数据交错
            var splitter = new DelimiterSplitter(_config.Delimiter);

            try
            {
                using var stream = client.GetStream();
                while (client.Connected && !CancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, CancellationToken);
                    if (bytesRead == 0) break; // 客户端断开

                    // 1. 将原始数据喂给分帧器
                    splitter.Append(readBuffer, 0, bytesRead);

                    // 2. 提取所有完整的帧
                    var frames = splitter.ExtractFrames();

                    // 3. 逐帧转发
                    foreach (var frame in frames)
                    {
                        var msg = new Message
                        {
                            Topic = _config.Port.ToString(),
                            ContentType = "text",
                            Metadata =
                            {
                                [GatewayMetadataKeys.Source] = remoteEndPoint,
                                ["Protocol"] = "Tcp"
                            }
                        };
                        msg.SetPayload(frame.ToArray());

                        GatewayTraceContext.EnsureTraceId(msg);
                        await InvokeMessageHandler(msg);
                    }
                }
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                GatewayLog.Info("TcpInput", $"Client receive loop canceled: {remoteEndPoint}");
            }
            catch (Exception ex)
            {
                GatewayLog.Warn("TcpInput", $"Client error: {ex.Message}", ex);
            }
            finally
            {
                client.Close();
                GatewayLog.Info("TcpInput", $"Client disconnected: {remoteEndPoint}");
            }
        }

        protected override async Task OnStopAsync()
        {
            _listener?.Stop();

            if (_clientTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(_clientTasks);
                }
                catch (Exception ex)
                {
                    GatewayLog.Warn("TcpInput", $"Client tasks completed with error: {ex.Message}", ex);
                }
            }

            GatewayLog.Info("TcpInput", $"Stopped listener on {_config.LocalIp}:{_config.Port}");
        }
    }
}
