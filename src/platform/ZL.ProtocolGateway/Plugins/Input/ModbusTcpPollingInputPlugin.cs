using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins.Shared;

namespace ZL.ProtocolGateway.Plugins;

/// <summary>
/// Modbus TCP 轮询输入插件配置
/// </summary>
public class ModbusTcpPollingInputConfig
{
    /// <summary>插件名称</summary>
    public string Name { get; set; }

    /// <summary>设备 IP 地址</summary>
    public string ServerIp { get; set; }

    /// <summary>设备端口 (默认 502)</summary>
    public int Port { get; set; } = 502;

    /// <summary>从站 ID (默认 1)</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>超时 (毫秒)</summary>
    public int TimeoutMs { get; set; } = 3000;

    /// <summary>要轮询的寄存器列表</summary>
    public ModbusInputRegister[] Registers { get; set; } = Array.Empty<ModbusInputRegister>();

    public System.Collections.Generic.List<ConfigValidationError> Validate()
    {
        var errors = new System.Collections.Generic.List<ConfigValidationError>();
        if (string.IsNullOrEmpty(ServerIp))
            errors.Add(new ConfigValidationError("ServerIp", "Device IP address is required"));
        if (Port < 1 || Port > 65535)
            errors.Add(new ConfigValidationError("Port", "Port must be 1-65535"));
        if (Registers == null || Registers.Length == 0)
            errors.Add(new ConfigValidationError("Registers", "At least one register is required"));
        return errors;
    }
}

/// <summary>
/// Modbus 输入寄存器定义
/// </summary>
public class ModbusInputRegister
{
    /// <summary>寄存器起始地址 (0-based PDU 地址)</summary>
    public ushort StartAddress { get; set; }

    /// <summary>寄存器数量 (1-125)</summary>
    public ushort Quantity { get; set; } = 1;

    /// <summary>轮询间隔 (毫秒)，默认 1000ms</summary>
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>是否为线圈 (false=保持寄存器)</summary>
    public bool IsCoil { get; set; } = false;

    /// <summary>标签名称 (用于标识)</summary>
    public string TagName { get; set; }

    public ModbusInputRegister(ushort startAddress, ushort quantity = 1, int pollIntervalMs = 1000, bool isCoil = false, string tagName = null)
    {
        StartAddress = startAddress;
        Quantity = quantity;
        PollIntervalMs = pollIntervalMs;
        IsCoil = isCoil;
        TagName = tagName ?? $"MB-{(isCoil ? "C" : "R")}{startAddress}";
    }
}

/// <summary>
/// Modbus TCP 轮询输入插件 — 周期性读取 Modbus 寄存器，值变化时生成 Message。
/// <para>复用 ModbusTcpProtocolEngine 共享协议逻辑，与 ModbusTcpOutputPlugin 共享帧构建/解析。</para>
/// </summary>
public class ModbusTcpPollingInputPlugin : IndustrialPollingInputBase
{
    private readonly ModbusTcpPollingInputConfig _config;
    private TcpClient _client;
    private NetworkStream _stream;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private ushort _transactionId;

    public override string Name { get; }
    public override string ProtocolType => "ModbusTcp";

    public ModbusTcpPollingInputPlugin(ModbusTcpPollingInputConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        var validationErrors = config.Validate();
        if (validationErrors.Count > 0)
            throw new ArgumentException($"Invalid Modbus TCP polling input config: {string.Join(", ", validationErrors)}");

        Name = config.Name ?? $"ModbusTcpPoll-{config.ServerIp}:{config.Port}";

        // 为每个寄存器注册轮询标签
        foreach (var reg in config.Registers)
        {
            string key = MakeRegisterKey(reg);
            RegisterPollTag(key, reg.PollIntervalMs);
        }
    }

    /// <summary>
    /// 建立 TCP 连接并等待直到断开或取消。
    /// </summary>
    protected override async Task TryConnectAsync(CancellationToken ct)
    {
        _client = new TcpClient();
        try
        {
            await ModbusTcpProtocolEngine.ConnectWithTimeoutAsync(
                _client, _config.ServerIp, _config.Port, _config.TimeoutMs);
            _client.ReceiveTimeout = _config.TimeoutMs;
            _client.SendTimeout = _config.TimeoutMs;
            _stream = _client.GetStream();
        }
        catch
        {
            CleanupConnection();
            throw;
        }

        GatewayLog.Info(Name, $"Modbus TCP connected to {_config.ServerIp}:{_config.Port}");

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
        try { _stream?.Close(); } catch { }
        _stream = null;
        try { _client?.Close(); _client?.Dispose(); } catch { }
        _client = null;
    }

    /// <summary>
    /// 执行一次轮询：读取所有注册的寄存器。
    /// 按 (StartAddress, IsCoil) 分组合并连续读取，减少往返次数。
    /// </summary>
    protected override async Task<PollResult[]> OnPollAsync(CancellationToken ct)
    {
        await _pollLock.WaitAsync(ct);
        try
        {
            if (!HasLiveConnection())
                throw new IOException("Modbus TCP connection is not alive");

            var allResults = new System.Collections.Generic.List<PollResult>();

            foreach (var reg in _config.Registers)
            {
                ushort txId = unchecked(++_transactionId);

                if (reg.IsCoil)
                {
                    var request = ModbusTcpProtocolEngine.BuildReadCoilsRequest(
                        txId, _config.UnitId, reg.StartAddress, reg.Quantity);
                    await _stream.WriteAsync(request, 0, request.Length, ct);

                    var response = await ModbusTcpProtocolEngine.ReadResponseAsync(_stream, ct);
                    var (success, coilValues) = ModbusTcpProtocolEngine.ParseReadCoilsResponse(response, reg.Quantity);

                    if (success && coilValues != null)
                    {
                        for (int i = 0; i < reg.Quantity; i++)
                        {
                            string addr = MakeRegisterKey(reg, i);
                            allResults.Add(new PollResult(addr, coilValues[i], "BOOL"));
                        }
                    }
                }
                else
                {
                    var request = ModbusTcpProtocolEngine.BuildReadHoldingRegistersRequest(
                        txId, _config.UnitId, reg.StartAddress, reg.Quantity);
                    await _stream.WriteAsync(request, 0, request.Length, ct);

                    var response = await ModbusTcpProtocolEngine.ReadResponseAsync(_stream, ct);
                    var (success, registerValues) = ModbusTcpProtocolEngine.ParseReadHoldingRegistersResponse(response);

                    if (success && registerValues != null)
                    {
                        for (int i = 0; i < registerValues.Length; i++)
                        {
                            string addr = MakeRegisterKey(reg, i);
                            allResults.Add(new PollResult(addr, registerValues[i], "UINT16"));
                        }
                    }
                }
            }

            return allResults.ToArray();
        }
        finally
        {
            _pollLock.Release();
        }
    }

    /// <summary>生成寄存器唯一键</summary>
    private static string MakeRegisterKey(ModbusInputRegister reg, int index = 0)
    {
        return $"{(reg.IsCoil ? "C" : "R")}{reg.StartAddress + index}";
    }

    /// <summary>释放 SemaphoreSlim 资源</summary>
    protected override Task OnStopCoreAsync()
    {
        _pollLock?.Dispose();
        return Task.CompletedTask;
    }
}
