using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.Framing;

namespace ZL.Probing
{
    /// <summary>
    /// 透明代理传输层，用于侦听和录制网络流量。
    /// 支持"只侦听"和"侦听+转发"两种模式。
    /// </summary>
    public sealed class TransparentProxyTransport : IByteTransport, ISessionByteTransport, ISessionSendByteTransport, ISessionLifecycleTransport
    {
        private readonly TransparentProxyConfig _config;
        private readonly IByteTransport _targetTransport;
        private readonly Encoding _encoding;
        private readonly FrameAssembler _framer;
        private readonly ConcurrentDictionary<string, SessionState> _sessions = new ConcurrentDictionary<string, SessionState>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, StreamWriter> _sessionLogWriters = new ConcurrentDictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);
        private StreamWriter _mainLogWriter;
        private int _sessionCounter;
        private CancellationTokenSource? _cts;
        private bool _isOpen;
        private bool _targetEventsSubscribed;

        /// <summary>资源名称</summary>
        public string ResourceName => $"Proxy:{(_config.SourcePort?.ToString() ?? _config.SourcePortName ?? "unknown")}->{_config.TargetHost}:{(_config.TargetPort?.ToString() ?? _config.TargetPortName ?? "unknown")}";

        /// <summary>是否已打开</summary>
        public bool IsOpen => _isOpen;

        /// <summary>帧超时毫秒数</summary>
        public int FrameTimeoutMs { get; }

        /// <summary>字节帧选项</summary>
        public ByteFramingOptions Framing { get; }

        /// <summary>数据到达事件</summary>
        public event Action<byte[]>? DataReceived;

        /// <summary>带会话 ID 的数据到达事件</summary>
        public event Action<byte[], string>? DataReceivedSession;

        /// <summary>帧状态变化事件</summary>
        public event Action<FrameStatus>? FrameStatusChanged;

        /// <summary>会话开始事件</summary>
        public event Action<string>? SessionStarted;

        /// <summary>会话结束事件</summary>
        public event Action<string>? SessionEnded;

        /// <summary>
        /// 创建透明代理传输。
        /// </summary>
        /// <param name="config">代理配置</param>
        /// <param name="targetTransport">目标传输（转发目标）</param>
        public TransparentProxyTransport(TransparentProxyConfig config, IByteTransport targetTransport)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _targetTransport = targetTransport ?? throw new ArgumentNullException(nameof(targetTransport));
            _encoding = Encoding.GetEncoding(config.EncodingName ?? "UTF-8");
            FrameTimeoutMs = config.FrameTimeoutMs <= 0 ? 30 : config.FrameTimeoutMs;
            Framing = config.ByteFraming ?? new ByteFramingOptions();
            _framer = new FrameAssembler(Framing, FrameTimeoutMs, OnFrameReady);

            EnsureMainLogWriter();
            StartLogWriterTask();
        }

        /// <inheritdoc/>
        public void Open()
        {
            if (_isOpen) return;
            _cts = new CancellationTokenSource();
            _targetTransport.Open();
            EnsureMainLogWriter();
            SubscribeTargetTransportEvents();

            if (_config.ListenMode == ListenMode.TcpServer)
            {
                _ = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
            }

            _isOpen = true;
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (!_isOpen) return;
            _logQueue.CompleteAdding();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            foreach (var sessionId in _sessions.Keys)
            {
                CleanupSession(sessionId);
            }

            _mainLogWriter?.Close();
            _mainLogWriter = null;

            foreach (var writer in _sessionLogWriters.Values)
            {
                writer?.Close();
            }
            _sessionLogWriters.Clear();

            UnsubscribeTargetTransportEvents();
            _targetTransport.Close();
            _framer.Stop();
            _isOpen = false;
        }

        /// <inheritdoc/>
        public void Send(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            LogPacket(data, PacketDirection.ClientToProxy, "proxy");

            if (!_config.SnifferOnly && _targetTransport != null)
            {
                _targetTransport.Send(data);
            }
        }

        /// <inheritdoc/>
        public void Send(byte[] data, string sessionId)
        {
            if (data == null || data.Length == 0) return;
            if (string.IsNullOrEmpty(sessionId)) return;

            LogPacket(data, PacketDirection.ClientToProxy, sessionId);

            if (!_config.SnifferOnly && _targetTransport is ISessionSendByteTransport targetSession)
            {
                targetSession.Send(data, sessionId);
            }
            else if (!_config.SnifferOnly)
            {
                _targetTransport.Send(data);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Close();
            _framer.Dispose();
        }

        #region Private - Listen & Proxy Loop

        private async Task ListenLoopAsync(CancellationToken token)
        {
            var listener = new TcpListener(IPAddress.Any, _config.SourcePort ?? 0);
            listener.Start();
            var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;

            WriteLog($"Transparent proxy listening on port {actualPort}");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(token);
                    string sessionId = $"ProxySession#{Interlocked.Increment(ref _sessionCounter)}";
                    var framer = new FrameAssembler(Framing, FrameTimeoutMs, (frame, mode) =>
                        OnProxyFrameReady(sessionId, frame, mode));

                    var state = new SessionState(sessionId, client, framer);
                    _sessions[sessionId] = state;

                    if (!string.IsNullOrEmpty(_config.SessionLogDir))
                    {
                        var sessionLogPath = Path.Combine(_config.SessionLogDir, $"{sessionId}.jsonl");
                        var sessionLogWriter = new StreamWriter(sessionLogPath, false, new UTF8Encoding(false))
                        {
                            AutoFlush = true
                        };
                        _sessionLogWriters[sessionId] = sessionLogWriter;
                    }

                    SessionStarted?.Invoke(sessionId);
                    WriteLog($"Session started: {sessionId}");

                    _ = Task.Run(() => ProxyReadLoopAsync(state, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(100, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            listener.Stop();
        }

        private async Task ProxyReadLoopAsync(SessionState state, CancellationToken token)
        {
            var stream = state.Client.GetStream();
            var buffer = new byte[4096];

            while (!token.IsCancellationRequested && state.Client.Connected)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                }
                catch
                {
                    break;
                }

                if (read <= 0) break;

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);

                LogPacket(chunk, PacketDirection.ClientToProxy, state.SessionId);

                if (!_config.SnifferOnly)
                {
                    _targetTransport.Send(chunk);
                }

                state.Framer.Append(chunk);
            }

            CleanupSession(state.SessionId);
        }

        #endregion

        #region Private - Target Data Handlers

        private void OnTargetDataReceived(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            LogPacket(data, PacketDirection.TargetToProxy, "target");

            if (!_config.SnifferOnly)
            {
                foreach (var session in _sessions.Values)
                {
                    TrySendToClient(session.Client, data);
                }
            }

            _framer.Append(data);
        }

        private void OnTargetDataReceivedSession(byte[] data, string sessionId)
        {
            if (data == null || data.Length == 0) return;

            LogPacket(data, PacketDirection.TargetToProxy, sessionId);

            if (!_config.SnifferOnly && _sessions.TryGetValue(sessionId, out var state))
            {
                TrySendToClient(state.Client, data);
            }

            DataReceivedSession?.Invoke(data, sessionId);
            DataReceived?.Invoke(data);
        }

        private void OnTargetSessionStarted(string sessionId)
        {
            WriteLog($"Target session started: {sessionId}");
        }

        private void OnTargetSessionEnded(string sessionId)
        {
            WriteLog($"Target session ended: {sessionId}");
            SessionEnded?.Invoke(sessionId);
        }

        #endregion

        #region Private - Frame Callbacks

        private void OnProxyFrameReady(string sessionId, byte[] frame, FrameAssembleMode mode)
        {
            if (mode == FrameAssembleMode.Recovery) return;
            DataReceivedSession?.Invoke(frame, sessionId);
            DataReceived?.Invoke(frame);
            FrameStatusChanged?.Invoke(new FrameStatus(ResourceName, MapSplitMode(mode), frame.Length, string.Empty));
        }

        private void OnFrameReady(byte[] frame, FrameAssembleMode mode)
        {
            if (mode == FrameAssembleMode.Recovery) return;
            DataReceived?.Invoke(frame);
            FrameStatusChanged?.Invoke(new FrameStatus(ResourceName, MapSplitMode(mode), frame.Length, string.Empty));
        }

        #endregion

        #region Private - Session Cleanup

        private void CleanupSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            if (_sessions.TryRemove(sessionId, out var state))
            {
                try
                {
                    state.Client.Close();
                }
                catch
                {
                    // Ignore close errors.
                }
                state.Framer.Stop();
                state.Framer.Dispose();
                SessionEnded?.Invoke(sessionId);

                if (_sessionLogWriters.TryRemove(sessionId, out var writer))
                {
                    writer?.Close();
                }

                WriteLog($"Session ended: {sessionId}");
            }
        }

        #endregion

        #region Private - Logging

        private readonly BlockingCollection<PacketLog> _logQueue = new BlockingCollection<PacketLog>(1000);

        private void StartLogWriterTask()
        {
            Task.Run(() =>
            {
                foreach (var packet in _logQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(packet);
                        _mainLogWriter?.WriteLine(json);

                        if (_sessionLogWriters.TryGetValue(packet.ExtraSessionId, out var sessionWriter))
                        {
                            sessionWriter.WriteLine(json);
                        }
                    }
                    catch { /* Ignore logging failures in background */ }
                }
            });
        }

        private void LogPacket(byte[] data, PacketDirection direction, string sessionId)
        {
            try
            {
                string hex = BitConverter.ToString(data).Replace("-", " ").ToUpperInvariant();
                string text = _encoding.GetString(data);
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var packet = new PacketLog
                {
                    Timestamp = timestamp,
                    Direction = direction == PacketDirection.ClientToProxy ? "TX" : "RX",
                    SessionId = sessionId,
                    Length = data.Length,
                    Hex = hex,
                    Text = SanitizeText(text),
                    ExtraSessionId = sessionId
                };

                _logQueue.TryAdd(packet);
            }
            catch
            {
                // Ignore logging errors.
            }
        }

        private static string SanitizeText(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                {
                    sb.Append($"\\x{(int)c:X2}");
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static void TrySendToClient(TcpClient client, byte[] data)
        {
            if (client == null || !client.Connected) return;
            try
            {
                client.GetStream().Write(data, 0, data.Length);
            }
            catch
            {
                // Ignore send errors.
            }
        }

        private static FrameSplitMode MapSplitMode(FrameAssembleMode mode)
        {
            return mode switch
            {
                FrameAssembleMode.Decoded => FrameSplitMode.Length,
                FrameAssembleMode.Timeout => FrameSplitMode.Timeout,
                FrameAssembleMode.Chunk => FrameSplitMode.Chunk,
                _ => FrameSplitMode.Timeout
            };
        }

        private void WriteLog(string message)
        {
            try
            {
                var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TransparentProxy] {message}";
                Console.WriteLine(logLine);
            }
            catch
            {
                // Ignore logging errors.
            }
        }

        #endregion

        #region Private - Target Transport Event Subscription

        private void SubscribeTargetTransportEvents()
        {
            if (_targetEventsSubscribed) return;

            if (_targetTransport is ISessionByteTransport targetSession)
            {
                targetSession.DataReceivedSession += OnTargetDataReceivedSession;
            }
            else
            {
                _targetTransport.DataReceived += OnTargetDataReceived;
            }

            if (_targetTransport is ISessionLifecycleTransport targetLifecycle)
            {
                targetLifecycle.SessionStarted += OnTargetSessionStarted;
                targetLifecycle.SessionEnded += OnTargetSessionEnded;
            }

            _targetEventsSubscribed = true;
        }

        private void UnsubscribeTargetTransportEvents()
        {
            if (!_targetEventsSubscribed) return;

            if (_targetTransport is ISessionByteTransport targetSession)
            {
                targetSession.DataReceivedSession -= OnTargetDataReceivedSession;
            }
            else
            {
                _targetTransport.DataReceived -= OnTargetDataReceived;
            }

            if (_targetTransport is ISessionLifecycleTransport targetLifecycle)
            {
                targetLifecycle.SessionStarted -= OnTargetSessionStarted;
                targetLifecycle.SessionEnded -= OnTargetSessionEnded;
            }

            _targetEventsSubscribed = false;
        }

        #endregion

        #region Private - Log Writer Management

        private void EnsureMainLogWriter()
        {
            if (_mainLogWriter != null || string.IsNullOrEmpty(_config.LogFile)) return;

            var dir = Path.GetDirectoryName(_config.LogFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _mainLogWriter = new StreamWriter(_config.LogFile, false, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        #endregion

        #region Internal Types

        private enum PacketDirection
        {
            ClientToProxy,
            TargetToProxy
        }

        private sealed class PacketLog
        {
            public long Timestamp { get; set; }
            public string Direction { get; set; } = string.Empty;
            public string SessionId { get; set; } = string.Empty;
            public int Length { get; set; }
            public string Hex { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonIgnore]
            public string ExtraSessionId { get; set; } = string.Empty;
        }

        private sealed class SessionState
        {
            public SessionState(string sessionId, TcpClient client, FrameAssembler framer)
            {
                SessionId = sessionId;
                Client = client;
                Framer = framer;
            }

            public string SessionId { get; }
            public TcpClient Client { get; }
            public FrameAssembler Framer { get; }
        }

        #endregion
    }

    /// <summary>
    /// 透明代理配置。
    /// </summary>
    public sealed class TransparentProxyConfig
    {
        /// <summary>监听模式</summary>
        public ListenMode ListenMode { get; set; } = ListenMode.TcpServer;

        /// <summary>本地监听端口（TCP 服务器模式）</summary>
        public int? SourcePort { get; set; }

        /// <summary>本地监听串口名（Serial 监听模式）</summary>
        public string? SourcePortName { get; set; }

        /// <summary>目标主机（TCP 客户端模式）</summary>
        public string? TargetHost { get; set; } = "localhost";

        /// <summary>目标端口（TCP 客户端模式）</summary>
        public int? TargetPort { get; set; }

        /// <summary>目标串口名（Serial 代理模式）</summary>
        public string? TargetPortName { get; set; }

        /// <summary>只侦听模式，不转发数据</summary>
        public bool SnifferOnly { get; set; }

        /// <summary>日志文件路径</summary>
        public string? LogFile { get; set; }

        /// <summary>会话日志目录</summary>
        public string? SessionLogDir { get; set; }

        /// <summary>编码名称</summary>
        public string? EncodingName { get; set; } = "UTF-8";

        /// <summary>帧超时毫秒数</summary>
        public int FrameTimeoutMs { get; set; } = 30;

        /// <summary>字节帧选项</summary>
        public ByteFramingOptions? ByteFraming { get; set; }
    }

    /// <summary>
    /// 监听模式枚举。
    /// </summary>
    public enum ListenMode
    {
        /// <summary>TCP 服务器模式：监听端口，接受客户端连接</summary>
        TcpServer,

        /// <summary>TCP 客户端模式：连接到目标服务器</summary>
        TcpClient,

        /// <summary>Serial 监听模式：监听串口</summary>
        Serial
    }
}
