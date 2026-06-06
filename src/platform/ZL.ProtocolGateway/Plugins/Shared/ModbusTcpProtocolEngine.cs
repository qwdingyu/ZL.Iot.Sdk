using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins.Shared;

/// <summary>
/// Modbus TCP 协议引擎 — 提供 Modbus TCP 帧构建、响应解析等静态工具方法。
/// <para>Input 和 Output 插件共享此模块，避免协议逻辑重复。</para>
/// </summary>
public static class ModbusTcpProtocolEngine
{
    /// <summary>Modbus 功能码: 读保持寄存器 (0x03)</summary>
    public const byte FunctionReadHoldingRegisters = 0x03;

    /// <summary>Modbus 功能码: 读线圈 (0x01)</summary>
    public const byte FunctionReadCoils = 0x01;

    /// <summary>Modbus 功能码: 写单个寄存器 (0x06)</summary>
    public const byte FunctionWriteSingleRegister = 0x06;

    /// <summary>Modbus 功能码: 写单个线圈 (0x05)</summary>
    public const byte FunctionWriteSingleCoil = 0x05;

    #region 读取请求构建

    /// <summary>
    /// 构建 Modbus TCP 读保持寄存器请求帧 (MBAP + PDU)。
    /// </summary>
    /// <param name="transactionId">事务 ID</param>
    /// <param name="unitId">从站 ID</param>
    /// <param name="startAddress">起始地址 (0-based)</param>
    /// <param name="quantity">寄存器数量 (1-125)</param>
    public static byte[] BuildReadHoldingRegistersRequest(ushort transactionId, byte unitId, ushort startAddress, ushort quantity)
    {
        if (quantity < 1 || quantity > 125)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be 1-125");

        // MBAP Header (6 bytes) + Unit ID (1 byte) + PDU (5 bytes) = 12 bytes
        var frame = new byte[12];
        int offset = 0;

        // Transaction ID (2 bytes)
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), transactionId);
        offset += 2;

        // Protocol ID (2 bytes) = 0 for Modbus TCP
        frame[offset++] = 0x00;
        frame[offset++] = 0x00;

        // Length (2 bytes) = remaining bytes after this field = 6
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), 6);
        offset += 2;

        // Unit ID (1 byte)
        frame[offset++] = unitId;

        // Function Code (1 byte)
        frame[offset++] = FunctionReadHoldingRegisters;

        // Starting Address (2 bytes, big-endian)
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), startAddress);
        offset += 2;

        // Quantity of Registers (2 bytes, big-endian)
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), quantity);

        return frame;
    }

    /// <summary>
    /// 构建 Modbus TCP 读线圈请求帧。
    /// </summary>
    public static byte[] BuildReadCoilsRequest(ushort transactionId, byte unitId, ushort startAddress, ushort quantity)
    {
        if (quantity < 1 || quantity > 2000)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be 1-2000");

        var frame = new byte[12];
        int offset = 0;

        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), transactionId);
        offset += 2;
        frame[offset++] = 0x00;
        frame[offset++] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), 6);
        offset += 2;
        frame[offset++] = unitId;
        frame[offset++] = FunctionReadCoils;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), startAddress);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), quantity);

        return frame;
    }

    #endregion

    #region 读取响应解析

    /// <summary>
    /// 解析 Modbus TCP 读保持寄存器响应。
    /// 返回 (success, registerValues[])。
    /// </summary>
    public static (bool success, ushort[] values) ParseReadHoldingRegistersResponse(byte[] frame)
    {
        if (frame == null || frame.Length < 9)
            return (false, null);

        // MBAP(6) + UnitID(1) + Function(1) + ByteCount(1) = minimum 9 bytes
        // Byte layout: [0..5]=MBAP, [6]=UnitID, [7]=FC, [8]=ByteCount, [9..]=Data
        byte functionCode = frame[7];

        // Error response: function code > 0x80
        if (functionCode > 0x80)
        {
            return (false, null);
        }

        if (functionCode != FunctionReadHoldingRegisters)
            return (false, null);

        if (frame.Length < 10)
            return (false, null);

        byte byteCount = frame[8];
        if (frame.Length < 9 + byteCount)
            return (false, null);

        int registerCount = byteCount / 2;
        ushort[] values = new ushort[registerCount];
        for (int i = 0; i < registerCount; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(9 + i * 2, 2));
        }

        return (true, values);
    }

    /// <summary>
    /// 解析 Modbus TCP 读线圈响应。
    /// 返回 (success, coilValues[])。
    /// </summary>
    public static (bool success, bool[] values) ParseReadCoilsResponse(byte[] frame, int expectedCount)
    {
        if (frame == null || frame.Length < 9)
            return (false, null);

        // MBAP(6) + UnitID(1) + Function(1) + ByteCount(1) = minimum 9 bytes
        // Byte layout: [0..5]=MBAP, [6]=UnitID, [7]=FC, [8]=ByteCount, [9..]=Data
        byte functionCode = frame[7];
        if (functionCode > 0x80 || functionCode != FunctionReadCoils)
            return (false, null);

        if (frame.Length < 10)
            return (false, null);

        byte byteCount = frame[8];
        if (frame.Length < 9 + byteCount)
            return (false, null);

        bool[] values = new bool[expectedCount];
        for (int i = 0; i < expectedCount && i < byteCount * 8; i++)
        {
            int byteIndex = 9 + (i / 8);
            int bitIndex = 7 - (i % 8);
            values[i] = (frame[byteIndex] & (1 << bitIndex)) != 0;
        }

        return (true, values);
    }

    #endregion

    #region 网络 I/O

    /// <summary>
    /// 从网络流中精确读取指定字节数。
    /// </summary>
    public static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken ct)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer, offset, length - offset, ct);
            if (read == 0)
                throw new IOException("Modbus TCP connection closed unexpectedly");
            offset += read;
        }
        return buffer;
    }

    /// <summary>
    /// 读取 Modbus TCP 响应帧（7 字节 MBAP 头部 + 可变长度主体）。
    /// </summary>
    public static async Task<byte[]> ReadResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = await ReadExactAsync(stream, 7, ct);
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        var body = await ReadExactAsync(stream, length - 1, ct);
        var frame = new byte[7 + body.Length];
        Buffer.BlockCopy(header, 0, frame, 0, 7);
        Buffer.BlockCopy(body, 0, frame, 7, body.Length);
        return frame;
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 将 "40001" 风格的 Modbus 地址转换为 0-based 起始地址。
    /// 40001-49999 → 保持寄存器 (0x0000)
    /// 30001-39999 → 输入寄存器 (0x0000)
    /// 10001-19999 → 线圈 (0x0000)
    /// 20001-29999 → 离散输入 (0x0000)
    /// 直接数字 → 保持为 0-based 地址
    /// </summary>
    public static ushort ConvertToModbusAddress(string address)
    {
        if (!ushort.TryParse(address, out ushort raw))
            throw new FormatException($"Invalid Modbus address: {address}");

        // PDU address (0-based) — user already provides 0-based
        return raw;
    }

    /// <summary>
    /// 带超时的 TCP 连接方法。
    /// </summary>
    public static async Task ConnectWithTimeoutAsync(TcpClient client, string host, int port, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        Task connectTask = client.ConnectAsync(host, port);
        Task cancellationTask = Task.Delay(Timeout.Infinite, cts.Token);
        Task completed = await Task.WhenAny(connectTask, cancellationTask);
        if (completed != connectTask)
        {
            try { client.Close(); } catch { }
            throw new OperationCanceledException($"Connection to {host}:{port} timed out after {timeoutMs}ms");
        }
        await connectTask;
    }

    #endregion
}
