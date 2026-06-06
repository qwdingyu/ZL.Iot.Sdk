using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// Modbus TCP 输出插件配置
    /// </summary>
    public class ModbusTcpOutputConfig
    {
        public string Name { get; set; }
        public string ServerIp { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;
        public byte UnitId { get; set; } = 1;
        public int TimeoutMs { get; set; } = 3000;

        public List<ConfigValidationError> Validate()
        {
            var errors = new List<ConfigValidationError>();
            if (!ConfigValidation.IsValidIpAddress(ServerIp))
                errors.Add(new ConfigValidationError(nameof(ServerIp), $"ServerIp '{ServerIp}' 不是有效的 IP 地址"));
            if (!ConfigValidation.IsValidPort(Port))
                errors.Add(new ConfigValidationError(nameof(Port), $"Port {Port} 必须在 1-65535 范围内"));
            if (!ConfigValidation.IsValidTimeout(TimeoutMs))
                errors.Add(new ConfigValidationError(nameof(TimeoutMs), $"TimeoutMs {TimeoutMs} 必须 >= 100"));
            return errors;
        }
    }

    /// <summary>
    /// Modbus TCP 输出插件 — 使用基类 IndustrialOutputPluginBase 的统一连接管理
    /// <para>覆盖 TryConnectAsync/HasLiveConnection/CleanupConnection 实现协议特定逻辑</para>
    /// </summary>
    public class ModbusTcpOutputPlugin : IndustrialOutputPluginBase
    {
        private readonly ModbusTcpOutputConfig _config;

        /// <summary>持久化 TCP 连接，避免每次写入都新建连接导致性能差和 PLC 连接数超限</summary>
        private TcpClient? _client;
        private NetworkStream? _stream;

        /// <summary>异步串行化发送，防止多线程并发 Send 时竞争同一 NetworkStream</summary>
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        /// <summary>Modbus TCP 事务 ID（每个请求递增）</summary>
        private ushort _transactionId;

        public ModbusTcpOutputPlugin(ModbusTcpOutputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Name => string.IsNullOrWhiteSpace(_config.Name) ? $"ModbusTcp-{_config.ServerIp}:{_config.Port}" : _config.Name;
        public override string ProtocolType => "ModbusTcp";

        /// <summary>Modbus TCP 协议特定的错误分类</summary>
        protected override (string errorCode, string userMessage, string advice) ClassifyError(
            OutputPluginHealthLevel level, string message)
        {
            return level switch
            {
                OutputPluginHealthLevel.Healthy => (GatewayErrorCodes.None, "Modbus TCP connected", "No action needed"),
                OutputPluginHealthLevel.Warning => (GatewayErrorCodes.ConnectionFailed, "Modbus TCP connection retrying", "Check device network connection"),
                OutputPluginHealthLevel.Error => (GatewayErrorCodes.ConnectionFailed, "Modbus TCP connection failed", "Check device IP, port, and network settings"),
                OutputPluginHealthLevel.Fatal => (GatewayErrorCodes.ConfigurationInvalid, "Modbus TCP configuration error", "Check output plugin configuration"),
                _ => (GatewayErrorCodes.InternalException, "Modbus TCP unknown error", "Check logs for details")
            };
        }

        /// <summary>
        /// 建立 TCP 连接并等待直到连接断开或取消
        /// <para>基类 ConnectionLoopAsync 调用此方法，返回后自动调用 CleanupConnection</para>
        /// </summary>
        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            // 建立 TCP 连接
            _client = new TcpClient();
            try
            {
                using var connectCts = new CancellationTokenSource(_config.TimeoutMs);
                await ConnectWithTimeoutAsync(_client, _config.ServerIp, _config.Port, connectCts.Token);
                _client.ReceiveTimeout = _config.TimeoutMs;
                _client.SendTimeout = _config.TimeoutMs;
                _stream = _client.GetStream();
            }
            catch
            {
                DisposeConnection();
                throw;
            }

            // 等待连接断开或取消请求
            while (!ct.IsCancellationRequested && HasLiveConnection())
            {
                await Task.Delay(1000, ct);
            }
        }

        /// <summary>检查 TCP 连接是否仍然有效</summary>
        protected override bool HasLiveConnection()
        {
            return _client != null && _client.Connected;
        }

        /// <summary>同步清理 TCP 连接资源</summary>
        protected override void CleanupConnection()
        {
            DisposeConnection();
        }

        /// <summary>
        /// 复用持久连接发送 Modbus 写请求
        /// <para>通过 _sendLock 串行化，确保同一时刻只有一个发送操作</para>
        /// </summary>
        protected async override Task OnSendAsync(Message message, CancellationToken cancellationToken)
        {
            if (message == null) return;

            // 解析写入操作列表
            List<ModbusWriteOperation> writes;
            if (message.Writes != null && message.Writes.Count > 0)
            {
                // 优先使用协议无关的 TagWrite 列表（解决 Bridge→Modbus "暗契约"问题）
                writes = new List<ModbusWriteOperation>();
                foreach (var tw in message.Writes)
                {
                    if (ModbusWriteSupport.TryParseAddress(tw.Address, tw.Value?.ToString() ?? "", _config.UnitId, out var op))
                    {
                        writes.Add(op);
                    }
                }
            }
            else
            {
                writes = ModbusWriteSupport.ParseWrites(message, _config.UnitId);
            }

            if (writes.Count == 0) return;

            await _sendLock.WaitAsync();
            try
            {
                // 连接断开时 HasLiveConnection 返回 false，基类会自动重连；
                // 此处直接尝试发送，失败后 DisposeConnection 会触发下次 TryConnectAsync
                if (!HasLiveConnection())
                {
                    throw new InvalidOperationException("Modbus TCP connection is not alive");
                }

                var stream = _stream!;
                foreach (var write in writes)
                {
                    using var cts = new CancellationTokenSource(_config.TimeoutMs);
                    var request = ModbusWriteSupport.BuildTcpWriteRequest(write, unchecked(++_transactionId));
                    await stream.WriteAsync(request, 0, request.Length, cts.Token);
                    await stream.FlushAsync(cts.Token);

                    var response = await ReadResponseAsync(stream, cts.Token);
                    ModbusWriteSupport.ValidateTcpWriteResponse(response, write);
                }
            }
            catch (Exception ex)
            {
                // 发送失败后关闭已损坏的连接，基类连接循环会检测到并自动重连
                DisposeConnection();
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>释放 SemaphoreSlim 资源</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sendLock?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>安全关闭并释放 TCP 连接资源（私有工具方法）</summary>
        private void DisposeConnection()
        {
            try { _stream?.Close(); } catch { }
            _stream = null;
            try { _client?.Close(); _client?.Dispose(); } catch { }
            _client = null;
        }

        /// <summary>读取 Modbus TCP 响应帧（7 字节头部 + 可变长度主体）</summary>
        private static async Task<byte[]> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var header = await ReadExactAsync(stream, 7, cancellationToken);
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
            var body = await ReadExactAsync(stream, length - 1, cancellationToken);
            var frame = new byte[7 + body.Length];
            Buffer.BlockCopy(header, 0, frame, 0, 7);
            Buffer.BlockCopy(body, 0, frame, 7, body.Length);
            return frame;
        }

        /// <summary>从网络流中精确读取指定字节数</summary>
        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset, cancellationToken);
                if (read == 0)
                    throw new InvalidOperationException("Modbus TCP connection closed unexpectedly");
                offset += read;
            }
            return buffer;
        }

        /// <summary>带超时的 TCP 连接方法（避免孤儿连接）</summary>
        private static async Task ConnectWithTimeoutAsync(TcpClient client, string host, int port, CancellationToken cancellationToken)
        {
            Task connectTask = client.ConnectAsync(host, port);
            Task cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
            Task completed = await Task.WhenAny(connectTask, cancellationTask);
            if (completed != connectTask)
            {
                try { client.Close(); } catch { }
                throw new OperationCanceledException(cancellationToken);
            }
            await connectTask;
        }
    }
}
