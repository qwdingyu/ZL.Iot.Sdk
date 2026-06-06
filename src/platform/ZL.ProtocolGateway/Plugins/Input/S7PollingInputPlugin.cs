using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins.Shared;

namespace ZL.ProtocolGateway.Plugins;

/// <summary>
/// S7 轮询输入插件配置
/// </summary>
public class S7PollingInputConfig
{
    /// <summary>插件名称</summary>
    public string Name { get; set; }

    /// <summary>PLC IP 地址</summary>
    public string ServerIp { get; set; }

    /// <summary>PLC 端口 (默认 102)</summary>
    public int Port { get; set; } = 102;

    /// <summary>机架号 (通常为 0)</summary>
    public byte Rack { get; set; } = 0;

    /// <summary>插槽号 (S7-1200=1, S7-300/400=2)</summary>
    public byte Slot { get; set; } = 1;

    /// <summary>连接超时 (毫秒)</summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>读取超时 (毫秒)</summary>
    public int ReadTimeoutMs { get; set; } = 3000;

    /// <summary>
    /// 要轮询的标签列表
    /// 格式: Address=PollIntervalMs, 如 "DB1.DBD0=1000,DB2.DBW10=2000"
    /// </summary>
    public S7InputTag[] Tags { get; set; } = Array.Empty<S7InputTag>();

    public System.Collections.Generic.List<ConfigValidationError> Validate()
    {
        var errors = new System.Collections.Generic.List<ConfigValidationError>();
        if (string.IsNullOrEmpty(ServerIp))
            errors.Add(new ConfigValidationError("ServerIp", "PLC IP address is required"));
        if (Port < 1 || Port > 65535)
            errors.Add(new ConfigValidationError("Port", "Port must be 1-65535"));
        if (Tags == null || Tags.Length == 0)
            errors.Add(new ConfigValidationError("Tags", "At least one tag is required"));
        return errors;
    }
}

/// <summary>
/// S7 输入标签定义
/// </summary>
public class S7InputTag
{
    /// <summary>S7 地址 (如 DB1.DBD0)</summary>
    public string Address { get; set; }

    /// <summary>数据类型: BOOL, BYTE, UINT16, INT16, UINT32, INT32, FLOAT, DOUBLE</summary>
    public string DataType { get; set; } = "FLOAT";

    /// <summary>轮询间隔 (毫秒)，默认 1000ms</summary>
    public int PollIntervalMs { get; set; } = 1000;

    public S7InputTag(string address, string dataType = "FLOAT", int pollIntervalMs = 1000)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        DataType = dataType ?? "FLOAT";
        PollIntervalMs = pollIntervalMs;
    }
}

/// <summary>
/// S7 轮询输入插件 — 周期性读取 S7 PLC 标签，值变化时生成 Message。
/// <para>复用 S7ProtocolEngine 共享协议逻辑，与 S7OutputPlugin 共享握手/帧构建/地址解析。</para>
/// </summary>
public class S7PollingInputPlugin : IndustrialPollingInputBase
{
    private readonly S7PollingInputConfig _config;
    private TcpClient _client;
    private NetworkStream _stream;
    private int _pduReference = 0x5678;
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    public override string Name { get; }
    public override string ProtocolType => "S7";

    public S7PollingInputPlugin(S7PollingInputConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        var validationErrors = config.Validate();
        if (validationErrors.Count > 0)
            throw new ArgumentException($"Invalid S7 polling input config: {string.Join(", ", validationErrors)}");

        Name = config.Name ?? $"S7Poll-{config.ServerIp}:{config.Port}";

        // 注册轮询标签
        foreach (var tag in config.Tags)
        {
            RegisterPollTag(tag.Address, tag.PollIntervalMs);
        }
    }

    /// <summary>
    /// 建立 TCP 连接 + S7 握手，然后等待直到连接断开或取消。
    /// </summary>
    protected override async Task TryConnectAsync(CancellationToken ct)
    {
        _client = new TcpClient();
        _client.SendTimeout = _config.ReadTimeoutMs;

        GatewayLog.Info(Name, $"Connecting to {_config.ServerIp}:{_config.Port}...");

        await ModbusTcpProtocolEngine.ConnectWithTimeoutAsync(
            _client, _config.ServerIp, _config.Port, _config.ConnectTimeoutMs);

        _stream = _client.GetStream();

        bool handshakeOk = await S7ProtocolEngine.PerformHandshakeAsync(
            _stream, _config.Rack, _config.Slot, _config.ConnectTimeoutMs, ct);

        if (!handshakeOk)
        {
            CleanupConnection();
            throw new IOException("S7 handshake failed");
        }

        GatewayLog.Info(Name, "S7 connection established");

        // 等待连接断开或取消
        while (!ct.IsCancellationRequested && HasLiveConnection())
        {
            await Task.Delay(1000, ct);
        }
    }

    /// <summary>检查 TCP 连接是否仍然有效</summary>
    protected override bool HasLiveConnection()
    {
        if (_client?.Client == null || !_client.Connected || _stream == null)
            return false;

        try
        {
            if (_client.Client.Poll(0, SelectMode.SelectRead) && _client.Client.Available == 0)
            {
                return false; // 远程已关闭
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>同步清理 TCP 连接资源</summary>
    protected override void CleanupConnection()
    {
        try { _stream?.Close(); } catch { }
        _stream = null;
        try { _client?.Close(); _client?.Dispose(); } catch { }
        _client = null;
    }

    /// <summary>
    /// 执行一次轮询：读取所有注册的标签。
    /// 使用批量读取（Multi Read）减少往返次数。
    /// </summary>
    protected override async Task<PollResult[]> OnPollAsync(CancellationToken ct)
    {
        await _pollLock.WaitAsync(ct);
        try
        {
            if (!HasLiveConnection())
                throw new IOException("S7 connection is not alive");

            // 构建批量读取请求
            var tags = new S7ReadTag[_config.Tags.Length];
            for (int i = 0; i < _config.Tags.Length; i++)
            {
                tags[i] = CreateReadTagFromConfig(_config.Tags[i]);
            }

            ushort pduRef = (ushort)Interlocked.Increment(ref _pduReference);
            var request = S7ProtocolEngine.BuildMultiReadRequest(tags, pduRef);

            await _stream.WriteAsync(request, 0, request.Length, ct);

            var response = await S7ProtocolEngine.ReadTpktPacketAsync(
                _stream, _config.ReadTimeoutMs, ct);

            var results = S7ProtocolEngine.ParseMultiReadResponse(response, tags.Length);
            var pollResults = new PollResult[results.Length];

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].success && results[i].data != null)
                {
                    object value = DecodeValue(results[i].data, _config.Tags[i].DataType);
                    pollResults[i] = new PollResult(_config.Tags[i].Address, value, _config.Tags[i].DataType);
                }
                else
                {
                    pollResults[i] = new PollResult(_config.Tags[i].Address, null, _config.Tags[i].DataType);
                }
            }

            return pollResults;
        }
        finally
        {
            _pollLock.Release();
        }
    }

    /// <summary>
    /// 根据配置创建 S7ReadTag。
    /// </summary>
    private static S7ReadTag CreateReadTagFromConfig(S7InputTag tag)
    {
        return tag.DataType.ToUpperInvariant() switch
        {
            "BOOL" => new S7ReadTag(tag.Address, 0x01, 1),   // BIT
            "BYTE" or "UINT8" or "INT8" => new S7ReadTag(tag.Address, 0x02, 1),
            "UINT16" or "INT16" or "WORD" => new S7ReadTag(tag.Address, 0x03, 2),
            "UINT32" or "INT32" or "DWORD" => new S7ReadTag(tag.Address, 0x04, 4),
            "FLOAT" or "REAL" => new S7ReadTag(tag.Address, 0x05, 4),
            "DOUBLE" or "LREAL" => new S7ReadTag(tag.Address, 0x04, 8),  // DWORD transport, 8 bytes
            _ => new S7ReadTag(tag.Address, 0x04, 4)  // 默认 DWORD, 4 bytes
        };
    }

    /// <summary>
    /// 将原始字节解码为对应数据类型的值。
    /// </summary>
    private static object DecodeValue(byte[] data, string dataType)
    {
        return dataType.ToUpperInvariant() switch
        {
            "BOOL" => data.Length > 0 && (data[0] & 0x01) != 0,
            "BYTE" => data.Length > 0 ? (object)data[0] : (byte)0,
            "UINT8" => data.Length > 0 ? (object)data[0] : (byte)0,
            "INT8" => data.Length > 0 ? (object)(sbyte)data[0] : (sbyte)0,
            "UINT16" or "WORD" => data.Length >= 2 ? (object)BinaryPrimitives.ReadUInt16BigEndian(data) : (ushort)0,
            "INT16" or "SHORT" => data.Length >= 2 ? (object)BinaryPrimitives.ReadInt16BigEndian(data) : (short)0,
            "UINT32" or "DWORD" => data.Length >= 4 ? (object)BinaryPrimitives.ReadUInt32BigEndian(data) : (uint)0,
            "INT32" or "INT" => data.Length >= 4 ? (object)BinaryPrimitives.ReadInt32BigEndian(data) : (int)0,
            "FLOAT" or "REAL" => data.Length >= 4 ? (object)BinaryPrimitives.ReadSingleBigEndian(data) : 0f,
            "DOUBLE" or "LREAL" => data.Length >= 8 ? (object)BinaryPrimitives.ReadDoubleBigEndian(data) : 0.0,
            _ => data  // 无法识别的类型，返回原始字节
        };
    }

    /// <summary>释放 SemaphoreSlim 资源</summary>
    protected override Task OnStopCoreAsync()
    {
        _pollLock?.Dispose();
        return Task.CompletedTask;
    }
}
