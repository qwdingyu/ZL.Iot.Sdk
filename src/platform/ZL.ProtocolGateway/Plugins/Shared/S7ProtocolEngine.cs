using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins.Shared;

/// <summary>
/// S7 协议引擎 — 提供 S7 帧构建、地址解析、TPKT 读取等静态工具方法。
/// <para>Input 和 Output 插件共享此模块，避免协议逻辑重复。</para>
/// </summary>
public static class S7ProtocolEngine
{
    #region S7 地址解析

    /// <summary>
    /// S7 内存区域标识符
    /// </summary>
    public const byte AreaMarkerDB = 0x84;
    public const byte AreaMarkerMerker = 0x83;
    public const byte AreaMarkerInput = 0x81;
    public const byte AreaMarkerOutput = 0x82;

    /// <summary>
    /// 解析 S7 地址字符串，提取 DB 号、区域码、字节偏移和位偏移。
    /// 支持格式: DB1.DBW0, DB1.DBB10, DB1.DBX0.0, M0, I0.0, Q0
    /// </summary>
    public static (int dbNumber, byte area, int byteOffset, int bitOffset, bool isBit) ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return (1, AreaMarkerDB, 0, 0, false);

        address = address.Trim();

        // DBx.DBW0 / DBx.DBB0 / DBx.DBX0.0
        if (address.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
        {
            return ParseS7DbAddress(address);
        }

        // Mx / Mx.x (Merker)
        if (address.StartsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            return ParseS7AreaAddress(address.Substring(1), AreaMarkerMerker);
        }

        // Ix.x (Input)
        if (address.StartsWith("I", StringComparison.OrdinalIgnoreCase))
        {
            return ParseS7AreaAddress(address.Substring(1), AreaMarkerInput);
        }

        // Qx / Qx.x (Output)
        if (address.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
        {
            return ParseS7AreaAddress(address.Substring(1), AreaMarkerOutput);
        }

        // 默认: DB1.DBW0
        return (1, AreaMarkerDB, 0, 0, false);
    }

    private static (int dbNumber, byte area, int byteOffset, int bitOffset, bool isBit) ParseS7DbAddress(string address)
    {
        // 查找 DB 号结束位置
        int dbEnd = 2;
        while (dbEnd < address.Length && char.IsDigit(address[dbEnd]))
            dbEnd++;

        if (dbEnd <= 2 || !int.TryParse(address.Substring(2, dbEnd - 2), out int dbNumber))
            return (1, AreaMarkerDB, 0, 0, false);

        if (dbEnd >= address.Length)
            return (dbNumber, AreaMarkerDB, 0, 0, false);

        // 解析数据类型和偏移量
        string rest = address.Substring(dbEnd);
        if (rest.StartsWith(".", StringComparison.Ordinal))
            rest = rest.Substring(1);

        if (rest.StartsWith("DBX", StringComparison.OrdinalIgnoreCase))
        {
            // 位访问: DBX<byte>.<bit>
            string[] parts = rest.Substring(3).Split('.');
            if (parts.Length == 2 && int.TryParse(parts[0], out int byteOff) && int.TryParse(parts[1], out int bitOff))
                return (dbNumber, AreaMarkerDB, byteOff, bitOff, true);
        }
        else if (rest.StartsWith("DBW", StringComparison.OrdinalIgnoreCase))
        {
            // 字访问
            if (int.TryParse(rest.Substring(3), out int wordOff))
                return (dbNumber, AreaMarkerDB, wordOff, 0, false);
        }
        else if (rest.StartsWith("DBD", StringComparison.OrdinalIgnoreCase))
        {
            // 双字访问
            if (int.TryParse(rest.Substring(3), out int dwordOff))
                return (dbNumber, AreaMarkerDB, dwordOff, 0, false);
        }
        else if (rest.StartsWith("DBB", StringComparison.OrdinalIgnoreCase))
        {
            // 字节访问
            if (int.TryParse(rest.Substring(3), out int byteOff))
                return (dbNumber, AreaMarkerDB, byteOff, 0, false);
        }

        return (dbNumber, AreaMarkerDB, 0, 0, false);
    }

    private static (int dbNumber, byte area, int byteOffset, int bitOffset, bool isBit) ParseS7AreaAddress(string rest, byte area)
    {
        if (string.IsNullOrEmpty(rest))
            return (0, area, 0, 0, false);

        // 位访问: x.x
        if (rest.Contains('.'))
        {
            string[] parts = rest.Split('.');
            if (parts.Length == 2 && int.TryParse(parts[0], out int byteOff) && int.TryParse(parts[1], out int bitOff))
                return (0, area, byteOff, bitOff, true);
        }

        // 字节/字访问
        if (int.TryParse(rest, out int offset))
            return (0, area, offset, 0, false);

        return (0, area, 0, 0, false);
    }

    #endregion

    #region S7 读取请求构建

    /// <summary>
    /// 构建 S7 Read Variable 请求帧。
    /// </summary>
    /// <param name="address">S7 地址 (如 DB1.DBD0)</param>
    /// <param name="transportSize">传输大小: 0x01=BIT, 0x02=BYTE, 0x03=WORD, 0x04=DWORD, 0x05=REAL</param>
    /// <param name="length">读取字节数</param>
    /// <param name="pduReference">PDU 引用号</param>
    public static byte[] BuildReadRequest(string address, byte transportSize, int length, ushort pduReference)
    {
        var (dbNumber, area, byteOffset, bitOffset, isBit) = ParseAddress(address);

        // S7 Payload = Header(10) + Parameter(14) + Data(4)
        var s7Payload = new byte[10 + 14 + 4];
        int offset = 0;

        // S7 Header (10 bytes)
        s7Payload[offset++] = 0x32; // Protocol ID
        s7Payload[offset++] = 0x01; // ROSCTR: Job
        s7Payload[offset++] = 0x00; // Redundancy ID (high)
        s7Payload[offset++] = 0x00; // Redundancy ID (low)
        s7Payload[offset++] = (byte)(pduReference >> 8);
        s7Payload[offset++] = (byte)(pduReference & 0xFF);
        s7Payload[offset++] = 0x00; // Parameters length (high)
        s7Payload[offset++] = 0x0E; // Parameters length (low) - 14 bytes
        s7Payload[offset++] = 0x00; // Data length (high)
        s7Payload[offset++] = 0x04; // Data length (low) - 4 bytes

        // Parameter: Read Variable (14 bytes)
        s7Payload[offset++] = 0x04; // Function: Read Var
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Item count (high)
        s7Payload[offset++] = 0x01; // Item count (low)

        // Item specification (12 bytes for S7ANY)
        s7Payload[offset++] = 0x12; // Syntax ID: S7ANY
        s7Payload[offset++] = transportSize; // Transport size
        s7Payload[offset++] = (byte)(length >> 8); // Length (high)
        s7Payload[offset++] = (byte)(length & 0xFF); // Length (low)
        s7Payload[offset++] = (byte)(dbNumber >> 8); // DB number (high)
        s7Payload[offset++] = (byte)(dbNumber & 0xFF); // DB number (low)
        s7Payload[offset++] = area; // Area
        int fullAddress = (byteOffset << 3) | (isBit ? bitOffset : 0);
        s7Payload[offset++] = (byte)((fullAddress >> 16) & 0xFF);
        s7Payload[offset++] = (byte)((fullAddress >> 8) & 0xFF);
        s7Payload[offset++] = (byte)(fullAddress & 0xFF);

        // Data (4 bytes): return code(0) + transport size + length
        s7Payload[offset++] = 0x00; // Return code: reserved
        s7Payload[offset++] = transportSize; // Transport size
        s7Payload[offset++] = (byte)(length >> 8); // Length (high)
        s7Payload[offset++] = (byte)(length & 0xFF); // Length (low)

        return WrapTpktCotpDt(s7Payload);
    }

    /// <summary>
    /// 构建 S7 批量读取请求（多地址合并为一个 PDU）。
    /// </summary>
    public static byte[] BuildMultiReadRequest(S7ReadTag[] tags, ushort pduReference)
    {
        int itemCount = tags.Length;
        int paramLen = 4 + (12 * itemCount); // Function(1) + Reserved(1) + Count(2) + Items
        int dataLen = 4 * itemCount; // 每个 item 4 字节
        int payloadLen = 10 + paramLen + dataLen;

        var s7Payload = new byte[payloadLen];
        int offset = 0;

        // S7 Header (10 bytes)
        s7Payload[offset++] = 0x32;
        s7Payload[offset++] = 0x01;
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = (byte)(pduReference >> 8);
        s7Payload[offset++] = (byte)(pduReference & 0xFF);
        s7Payload[offset++] = (byte)(paramLen >> 8);
        s7Payload[offset++] = (byte)(paramLen & 0xFF);
        s7Payload[offset++] = (byte)(dataLen >> 8);
        s7Payload[offset++] = (byte)(dataLen & 0xFF);

        // Parameter
        s7Payload[offset++] = 0x04; // Read Var
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = (byte)(itemCount >> 8);
        s7Payload[offset++] = (byte)(itemCount & 0xFF);

        // Item specifications
        foreach (var tag in tags)
        {
            var (dbNumber, area, byteOffset_t, bitOffset_t, isBit) = ParseAddress(tag.Address);
            s7Payload[offset++] = 0x12; // S7ANY
            s7Payload[offset++] = tag.TransportSize;
            s7Payload[offset++] = (byte)(tag.Length >> 8);
            s7Payload[offset++] = (byte)(tag.Length & 0xFF);
            s7Payload[offset++] = (byte)(dbNumber >> 8);
            s7Payload[offset++] = (byte)(dbNumber & 0xFF);
            s7Payload[offset++] = area;
            int fullAddr = (byteOffset_t << 3) | (isBit ? bitOffset_t : 0);
            s7Payload[offset++] = (byte)((fullAddr >> 16) & 0xFF);
            s7Payload[offset++] = (byte)((fullAddr >> 8) & 0xFF);
            s7Payload[offset++] = (byte)(fullAddr & 0xFF);
        }

        // Data
        for (int i = 0; i < itemCount; i++)
        {
            s7Payload[offset++] = 0x00;
            s7Payload[offset++] = tags[i].TransportSize;
            s7Payload[offset++] = (byte)(tags[i].Length >> 8);
            s7Payload[offset++] = (byte)(tags[i].Length & 0xFF);
        }

        return WrapTpktCotpDt(s7Payload);
    }

    #endregion

    #region S7 读取响应解析

    /// <summary>
    /// 解析 S7 Read Variable 响应，提取数据。
    /// 返回 (success, dataBytes)。dataBytes 为 null 表示读取失败。
    /// </summary>
    public static (bool success, byte[] data) ParseReadResponse(byte[] response)
    {
        if (response == null || response.Length < 25)
            return (false, null);

        // TPKT(4) + COTP DT(3) + S7 Header(10) + Parameter(4) + Data(≥4)
        // response[7] = 0x32 (Protocol ID)
        // response[8] = 0x03 (ROSCTR: Ack_Data)
        // response[9] = error_class, response[10] = error_code
        if (response[7] != 0x32 || response[8] != 0x03)
            return (false, null);

        byte errorClass = response[9];
        byte errorCode = response[10];

        // Parameter: [17] = 0x04 (Read Var), [18..19] = item count
        byte function = response[17];
        if (function != 0x04)
            return (false, null);

        // Data section starts at offset 20
        // Data format: return_code(1) + transport_size(1) + length(2) + data
        byte returnCode = response[21];
        if (returnCode != 0xFF)
        {
            // 0xFF = success, 0x00 = error
            return (false, null);
        }

        int dataLen = (response[23] << 8) | response[24];
        if (response.Length < 25 + dataLen)
            return (false, null);

        byte[] data = new byte[dataLen];
        Array.Copy(response, 25, data, 0, dataLen);
        return (true, data);
    }

    /// <summary>
    /// 解析 S7 批量读取响应。返回每个 tag 的 (success, data) 对。
    /// </summary>
    public static (bool success, byte[] data)[] ParseMultiReadResponse(byte[] response, int expectedItemCount)
    {
        var results = new (bool success, byte[] data)[expectedItemCount];

        if (response == null || response.Length < 25)
        {
            Array.Fill(results, (false, null));
            return results;
        }

        // 检查基本响应头
        if (response[7] != 0x32 || response[8] != 0x03)
        {
            Array.Fill(results, (false, null));
            return results;
        }

        // 解析每个 item 的数据
        int offset = 20; // Data section start

        // Parameter section: [17]=0x04, [18..19]=item count
        int paramItemCount = (response[18] << 8) | response[19];
        int itemCount = Math.Min(paramItemCount, expectedItemCount);

        // Skip to data section: Header(10) + Parameter(4 + 12*itemCount)
        int dataStart = 10 + 4 + (12 * paramItemCount);
        offset = dataStart;

        for (int i = 0; i < itemCount && offset + 4 <= response.Length; i++)
        {
            byte returnCode = response[offset++];
            offset++; // transport size
            int dataLen = (response[offset] << 8) | response[offset + 1];
            offset += 2;

            if (returnCode == 0xFF && offset + dataLen <= response.Length)
            {
                byte[] data = new byte[dataLen];
                Array.Copy(response, offset, data, 0, dataLen);
                results[i] = (true, data);
            }
            else
            {
                results[i] = (false, null);
            }
            offset += dataLen;
        }

        return results;
    }

    #endregion

    #region S7 握手

    /// <summary>
    /// 构建 COTP Connection Request 帧
    /// </summary>
    public static byte[] BuildCotpConnectionRequest()
    {
        // COTP Connection Request (CR)
        // TPKT(4) + COTP CR(15)
        var cotpCr = new byte[15];
        int offset = 0;
        cotpCr[offset++] = 0x05; // Length
        cotpCr[offset++] = 0xE0; // PDU type: CR
        cotpCr[offset++] = 0x00; // Dst ref high
        cotpCr[offset++] = 0x01; // Dst ref low
        cotpCr[offset++] = 0x00; // Src ref high
        cotpCr[offset++] = 0x01; // Src ref low
        cotpCr[offset++] = 0x00; // Class + options
        cotpCr[offset++] = 0xC0; // PDU size revision
        cotpCr[offset++] = 0x01; // High
        cotpCr[offset++] = 0x00; // Options
        cotpCr[offset++] = 0x02; // Length
        cotpCr[offset++] = 0xC1; // PDU size reference
        cotpCr[offset++] = 0x01; // High
        cotpCr[offset++] = 0x04; // Low (4096 bytes)

        var totalLength = 4 + cotpCr.Length;
        var data = new byte[totalLength];
        data[0] = 0x03;
        data[1] = 0x00;
        data[2] = (byte)(totalLength >> 8);
        data[3] = (byte)(totalLength & 0xFF);
        Array.Copy(cotpCr, 0, data, 4, cotpCr.Length);
        return data;
    }

    /// <summary>
    /// 构建 S7 Setup Communication 帧
    /// </summary>
    public static byte[] BuildS7SetupCommunication(byte rack, byte slot)
    {
        // S7 Communication Setup Request
        var s7Payload = new byte[10 + 8 + 24];
        int offset = 0;

        // S7 Header (10 bytes)
        s7Payload[offset++] = 0x32; // Protocol ID
        s7Payload[offset++] = 0x01; // ROSCTR: Job
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = 0x00; // PDU ref high
        s7Payload[offset++] = 0x01; // PDU ref low
        s7Payload[offset++] = 0x00; // Parameters length high
        s7Payload[offset++] = 0x08; // Parameters length low
        s7Payload[offset++] = 0x00; // Data length high
        s7Payload[offset++] = 0x18; // Data length low (24 bytes)

        // Parameter (8 bytes)
        s7Payload[offset++] = 0x00; // Function: Setup Comm
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x01; // Version: 1
        s7Payload[offset++] = 0x00; // Src TSAP high
        s7Payload[offset++] = 0x01; // Src TSAP low
        s7Payload[offset++] = 0x00; // Dst TSAP high
        s7Payload[offset++] = 0x01; // Dst TSAP low

        // Data (24 bytes)
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = rack;  // Dst Rack
        s7Payload[offset++] = slot;  // Dst Slot
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x01; // PDU max high
        s7Payload[offset++] = 0x00; // PDU max low (256 bytes)
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved
        s7Payload[offset++] = 0x00; // Reserved

        return WrapTpktCotpDt(s7Payload);
    }

    /// <summary>
    /// 执行 S7 握手（COTP + S7 Setup Communication）
    /// </summary>
    public static async Task<bool> PerformHandshakeAsync(NetworkStream stream, byte rack, byte slot,
        int timeoutMs, CancellationToken ct)
    {
        try
        {
            // 1. COTP Connection Request
            var cotpCr = BuildCotpConnectionRequest();
            await stream.WriteAsync(cotpCr, 0, cotpCr.Length, ct);

            var cotpCc = await ReadTpktPacketAsync(stream, timeoutMs, ct);
            if (cotpCc == null || cotpCc.Length < 11 || cotpCc[0] != 0x03 || cotpCc[5] != 0xD0)
                return false;

            // 2. S7 Setup Communication
            var s7Setup = BuildS7SetupCommunication(rack, slot);
            await stream.WriteAsync(s7Setup, 0, s7Setup.Length, ct);

            var s7Response = await ReadTpktPacketAsync(stream, timeoutMs, ct);
            if (s7Response == null || s7Response.Length < 25)
                return false;

            // Validate: [7]=0x32, [8]=0x03 (Ack_Data), error_class=0, error_code=0
            if (s7Response[7] != 0x32 || s7Response[8] != 0x03)
                return false;

            if (s7Response[9] != 0x00 || s7Response[10] != 0x00)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region TPKT 传输层

    /// <summary>
    /// 将 S7 载荷包装为 TPKT + COTP DT 帧
    /// </summary>
    public static byte[] WrapTpktCotpDt(byte[] s7Payload)
    {
        var cotpDt = new byte[] { 0x02, 0xF0, 0x80 };
        int totalLength = 4 + cotpDt.Length + s7Payload.Length;
        var data = new byte[totalLength];
        data[0] = 0x03;
        data[1] = 0x00;
        data[2] = (byte)(totalLength >> 8);
        data[3] = (byte)(totalLength & 0xFF);
        Array.Copy(cotpDt, 0, data, 4, cotpDt.Length);
        Array.Copy(s7Payload, 0, data, 4 + cotpDt.Length, s7Payload.Length);
        return data;
    }

    /// <summary>
    /// 读取一个完整的 TPKT 协议包
    /// </summary>
    public static async Task<byte[]> ReadTpktPacketAsync(NetworkStream stream, int timeoutMs, CancellationToken ct)
    {
        var tpktHeader = new byte[4];
        int headerRead = 0;
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            while (headerRead < 4)
            {
                int read = await stream.ReadAsync(tpktHeader, headerRead, 4 - headerRead, linkedCts.Token);
                if (read == 0) throw new IOException("TPKT read: connection closed (EOF)");
                headerRead += read;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Read TPKT header timeout after {timeoutMs}ms");
        }

        int tpktLen = (tpktHeader[2] << 8) | tpktHeader[3];
        if (tpktLen < 4 || tpktLen > 65535)
            throw new IOException($"Invalid TPKT length: {tpktLen}");

        int payloadLen = tpktLen - 4;
        if (payloadLen <= 0)
            return tpktHeader;

        byte[] payload = new byte[tpktLen];
        Array.Copy(tpktHeader, 0, payload, 0, 4);

        int totalRead = 0;
        try
        {
            while (totalRead < payloadLen)
            {
                int read = await stream.ReadAsync(payload, 4 + totalRead, payloadLen - totalRead, linkedCts.Token);
                if (read == 0) throw new IOException("TPKT payload read: connection closed (EOF)");
                totalRead += read;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Read TPKT payload timeout after {timeoutMs}ms");
        }

        return payload;
    }

    #endregion

    #region S7 写入请求构建

    /// <summary>
    /// 构建 S7 Write Variable 请求帧
    /// </summary>
    public static byte[] BuildWriteRequest(string address, byte[] value, ushort pduReference)
    {
        var (dbNumber, area, byteOffset, bitOffset, isBit) = ParseAddress(address);

        var s7Payload = new byte[10 + 14 + 4 + value.Length];
        int offset = 0;

        // S7 Header (10 bytes)
        s7Payload[offset++] = 0x32;
        s7Payload[offset++] = 0x01;
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = (byte)(pduReference >> 8);
        s7Payload[offset++] = (byte)(pduReference & 0xFF);
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = 0x0E;
        s7Payload[offset++] = (byte)((4 + value.Length) >> 8);
        s7Payload[offset++] = (byte)((4 + value.Length) & 0xFF);

        // Parameter (14 bytes)
        s7Payload[offset++] = 0x05; // Write Var
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = 0x01;

        s7Payload[offset++] = 0x12; // S7ANY
        s7Payload[offset++] = (byte)(isBit ? 0x01 : 0x0A); // Transport size: BIT or BYTE
        s7Payload[offset++] = (byte)(value.Length >> 8);
        s7Payload[offset++] = (byte)(value.Length & 0xFF);
        s7Payload[offset++] = (byte)(dbNumber >> 8);
        s7Payload[offset++] = (byte)(dbNumber & 0xFF);
        s7Payload[offset++] = area;
        int fullAddress = (byteOffset << 3) | (isBit ? bitOffset : 0);
        s7Payload[offset++] = (byte)((fullAddress >> 16) & 0xFF);
        s7Payload[offset++] = (byte)((fullAddress >> 8) & 0xFF);
        s7Payload[offset++] = (byte)(fullAddress & 0xFF);

        // Data
        s7Payload[offset++] = 0x00;
        s7Payload[offset++] = (byte)(isBit ? 0x03 : 0x04);
        s7Payload[offset++] = (byte)(value.Length >> 8);
        s7Payload[offset++] = (byte)(value.Length & 0xFF);
        Array.Copy(value, 0, s7Payload, offset, value.Length);

        return WrapTpktCotpDt(s7Payload);
    }

    #endregion
}

/// <summary>
/// S7 读取标签定义
/// </summary>
public class S7ReadTag
{
    /// <summary>S7 地址 (如 DB1.DBD0)</summary>
    public string Address { get; set; }
    /// <summary>传输大小 (0x01=BIT, 0x02=BYTE, 0x03=WORD, 0x04=DWORD, 0x05=REAL)</summary>
    public byte TransportSize { get; set; }
    /// <summary>读取字节数</summary>
    public int Length { get; set; }

    public S7ReadTag(string address, byte transportSize, int length)
    {
        Address = address;
        TransportSize = transportSize;
        Length = length;
    }
}
