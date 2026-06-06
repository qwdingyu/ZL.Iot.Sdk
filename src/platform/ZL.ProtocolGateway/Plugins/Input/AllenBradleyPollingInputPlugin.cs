using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway.Plugins
{
    /// <summary>
    /// Allen-Bradley CIP/EtherNet/IP 轮询输入配置。
    /// </summary>
    public class AllenBradleyPollingInputConfig
    {
        /// <summary>可选名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>PLC IP 地址</summary>
        public string ServerIp { get; set; } = "127.0.0.1";

        /// <summary>EtherNet/IP 端口，默认 44818</summary>
        public int Port { get; set; } = 44818;

        /// <summary>轮询间隔（毫秒），默认 1000</summary>
        public int PollIntervalMs { get; set; } = 1000;

        /// <summary>连接超时（毫秒）</summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        /// <summary>发送超时（毫秒）</summary>
        public int SendTimeoutMs { get; set; } = 3000;

        /// <summary>重连间隔（毫秒）</summary>
        public int ReconnectIntervalMs { get; set; } = 5000;

        /// <summary>要读取的标签列表</summary>
        public AllenBradleyInputTag[] Tags { get; set; } = Array.Empty<AllenBradleyInputTag>();

        /// <summary>验证配置</summary>
        public System.Collections.Generic.List<ConfigValidationError> Validate()
        {
            var errors = new System.Collections.Generic.List<ConfigValidationError>();
            if (string.IsNullOrWhiteSpace(ServerIp))
                errors.Add(new ConfigValidationError(nameof(ServerIp), "ServerIp 不能为空"));
            if (Tags == null || Tags.Length == 0)
                errors.Add(new ConfigValidationError(nameof(Tags), "至少配置一个标签"));
            return errors;
        }
    }

    /// <summary>
    /// Allen-Bradley 输入标签定义。
    /// </summary>
    public class AllenBradleyInputTag
    {
        /// <summary>PLC 标签名称（如 "MyTag", "PLC1.Local:0:I.Data0"）</summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>CIP 数据类型: REAL, DINT, BOOL, UINT, SINT, STRING（默认 REAL）</summary>
        public string DataType { get; set; } = "REAL";

        /// <summary>可选的 topic 覆盖；默认使用插件名称</summary>
        public string Topic { get; set; } = string.Empty;
    }

    /// <summary>
    /// Allen-Bradley CIP/EtherNet/IP 轮询输入插件。
    /// 通过 CIP Read 服务从 AB PLC 定期读取标签值并转换为 Message 发布。
    /// </summary>
    public class AllenBradleyPollingInputPlugin : IndustrialPollingInputBase
    {
        // CIP 协议常量
        private const byte CipServiceRead = 0x4C;
        private const byte CipClassData = 0xA0;
        private const byte CipInstanceData = 0x24;
        // EIP 封装命令
        private const ushort EncapCommandRegister = 0x0004;
        private const ushort EncapCommandSendRRData = 0x006F;

        private readonly AllenBradleyPollingInputConfig _config;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private uint _sessionHandle;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public override string Name { get; }
        public override string ProtocolType => "AllenBradley";

        public AllenBradleyPollingInputPlugin(AllenBradleyPollingInputConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ConfigValidation.ThrowIfInvalid(_config.Validate());
            Name = string.IsNullOrEmpty(config.Name)
                ? $"AB-In-{_config.ServerIp}:{_config.Port}"
                : config.Name;
            foreach (var tag in config.Tags)
            {
                RegisterPollTag(tag.TagName, _config.PollIntervalMs);
            }
        }

        #region 连接管理

        protected override async Task TryConnectAsync(CancellationToken ct)
        {
            _tcpClient = new TcpClient();
            using var connectCts = new CancellationTokenSource(_config.ConnectTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);

            var connectTask = _tcpClient.ConnectAsync(_config.ServerIp, _config.Port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(_config.ConnectTimeoutMs, linkedCts.Token));
            if (completed != connectTask)
                throw new TimeoutException($"Connect timeout after {_config.ConnectTimeoutMs}ms");
            await connectTask;

            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = _config.SendTimeoutMs;
            _stream.WriteTimeout = _config.SendTimeoutMs;

            if (!await RegisterSessionAsync())
                throw new InvalidOperationException("Allen-Bradley Register Session failed");
        }

        protected override bool HasLiveConnection()
        {
            return _tcpClient?.Connected == true && _stream != null;
        }

        protected override void CleanupConnection()
        {
            try { _stream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _stream = null;
            _tcpClient = null;
            _sessionHandle = 0;
        }

        private async Task<bool> RegisterSessionAsync()
        {
            await _sendLock.WaitAsync();
            try
            {
                if (_stream == null) return false;

                var registerRequest = new byte[]
                {
                    0x65, 0x00, // Command: Register Session
                    0x04, 0x00, // Length
                    0x00, 0x00, 0x00, 0x00, // Status
                    0x00, 0x00, 0x00, 0x00, // Session Handle
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // Context
                    0x00, 0x00, 0x00, 0x00, // Options
                    0x01, 0x00, // Protocol version
                    0x00, 0x00  // Send service time-out
                };

                await _stream!.WriteAsync(registerRequest, 0, registerRequest.Length);
                var response = new byte[28];
                var bytesRead = await ReadExactAsync(response, 24);
                if (bytesRead < 24) return false;

                var status = BitConverter.ToUInt32(response, 8);
                if (status == 0)
                {
                    _sessionHandle = BitConverter.ToUInt32(response, 4);
                    return _sessionHandle != 0;
                }
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        #endregion

        #region 轮询实现

        protected override async Task<PollResult[]> OnPollAsync(CancellationToken ct)
        {
            var results = new PollResult[_config.Tags.Length];

            for (int i = 0; i < _config.Tags.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var tag = _config.Tags[i];
                var (success, value) = await ReadTagAsync(tag.TagName, ct);
                results[i] = new PollResult(tag.TagName, success ? value : null, tag.DataType);
            }

            return results;
        }

        private async Task<(bool success, double value)> ReadTagAsync(string tagName, CancellationToken ct)
        {
            await _sendLock.WaitAsync(ct);
            try
            {
                if (_stream == null || _tcpClient?.Connected != true)
                    return (false, 0);

                var request = BuildReadRequest(tagName);
                await _stream!.WriteAsync(request, 0, request.Length, ct);

                var response = await ReadResponseAsync(ct);
                if (response == null)
                    return (false, 0);

                return ParseReadResponse(response);
            }
            catch
            {
                return (false, 0);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private byte[] BuildReadRequest(string tagName)
        {
            var tagBytes = Encoding.ASCII.GetBytes(tagName);

            // CIP Request body
            var cipBody = new MemoryStream();
            // Unconnected Data Item
            cipBody.WriteByte(0xB2);
            var dataSizePos = cipBody.Position;
            cipBody.WriteByte(0x00); // placeholder
            cipBody.WriteByte(0x00);

            // CIP Read Service
            cipBody.WriteByte(CipServiceRead);
            cipBody.WriteByte(0x00); // Request path size (in words) — Symbol segment uses extended
            cipBody.WriteByte(CipClassData); // Class
            cipBody.WriteByte(CipInstanceData); // Instance

            // Symbol path
            int pathSize = tagBytes.Length + 1;
            cipBody.WriteByte((byte)pathSize);
            cipBody.WriteByte(0x91); // Symbol segment: 8-bit ASCII
            cipBody.Write(tagBytes, 0, tagBytes.Length);
            cipBody.WriteByte(0x00); // Null terminator

            // Pad to even
            if (pathSize % 2 != 0)
                cipBody.WriteByte(0x00);

            // Number of elements
            cipBody.WriteByte(0x01);
            cipBody.WriteByte(0x00);

            // Update data size
            int dataSize = (int)cipBody.Position - 4;
            cipBody.Position = dataSizePos;
            cipBody.WriteByte((byte)(dataSize & 0xFF));
            cipBody.WriteByte((byte)((dataSize >> 8) & 0xFF));

            var cipData = cipBody.ToArray();

            // EtherNet/IP Encapsulation Header
            var encap = new MemoryStream();
            // Command: Send RR Data
            encap.WriteByte((byte)(EncapCommandSendRRData & 0xFF));
            encap.WriteByte((byte)((EncapCommandSendRRData >> 8) & 0xFF));
            // Length (bytes after this field)
            int payloadLen = cipData.Length + 8; // +8 for Interface Handle + Timeout
            encap.WriteByte((byte)(payloadLen & 0xFF));
            encap.WriteByte((byte)((payloadLen >> 8) & 0xFF));
            // Session Handle
            encap.WriteByte((byte)(_sessionHandle & 0xFF));
            encap.WriteByte((byte)((_sessionHandle >> 8) & 0xFF));
            encap.WriteByte((byte)((_sessionHandle >> 16) & 0xFF));
            encap.WriteByte((byte)((_sessionHandle >> 24) & 0xFF));
            // Status + Context + Options (20 bytes zero)
            encap.Write(new byte[20], 0, 20);
            // Item Count
            encap.WriteByte(0x01);
            encap.WriteByte(0x00);

            // CIP Data Item
            encap.WriteByte((byte)(cipData.Length & 0xFF));
            encap.WriteByte((byte)((cipData.Length >> 8) & 0xFF));
            // Type: CIP (0xB2)
            encap.WriteByte(0xB2);
            encap.WriteByte(0x00);
            encap.Write(cipData, 0, cipData.Length);

            return encap.ToArray();
        }

        private async Task<byte[]> ReadResponseAsync(CancellationToken ct)
        {
            // Read encapsulation header (24 bytes)
            var header = new byte[24];
            if (await ReadExactAsync(header, 24) < 24)
                return null;

            // Verify command response (0x6F = Send RR Data response)
            if (header[0] != 0x6F)
                return null;

            int length = (header[3] << 8) | header[4];
            if (length <= 0 || length > 65535)
                return null;

            // Read remaining data
            var body = new byte[length];
            if (await ReadExactAsync(body, length) < length)
                return null;

            return body;
        }

        private async Task<int> ReadExactAsync(byte[] buffer, int length)
        {
            int offset = 0;
            while (offset < length)
            {
                int read = await _stream!.ReadAsync(buffer, offset, length - offset);
                if (read == 0) throw new IOException("Connection closed");
                offset += read;
            }
            return offset;
        }

        private (bool success, double value) ParseReadResponse(byte[] response)
        {
            try
            {
                // Parse item count and data from response body
                // Response: Status(4) + Context(8) + Options(4) + ItemCount(2) + Items
                int offset = 18; // Skip to after Options
                if (response.Length < offset + 2) return (false, 0);

                int itemCount = response[offset++] | (response[offset++] << 8);
                if (itemCount < 1) return (false, 0);

                // First item: CIP Data
                if (offset + 2 >= response.Length) return (false, 0);
                offset += 2; // data length
                offset += 1; // type (0xB2 = CIP)
                offset += 1; // reserved

                // Skip to CIP body status
                // CIP body: Status(2) + Reserved(2) + Data
                if (offset + 4 >= response.Length) return (false, 0);
                ushort status = (ushort)(response[offset++] | (response[offset++] << 8));
                if (status != 0) return (false, 0); // CIP error
                offset += 2; // Reserved

                // Data: Length(1) + DataType(2) + Value
                if (offset + 3 >= response.Length) return (false, 0);
                int valueLen = response[offset++];
                byte encType = response[offset++]; // Encoding type
                byte encSize = response[offset++];

                // CIP data types: 0xCA=REAL(4), 0xC4=DINT(4), 0xC1=BOOL(1), 0xC2=SINT(1), 0xC3=INT(2)
                double val = 0;
                switch (encType)
                {
                    case 0xCA: // REAL
                        if (offset + 4 <= response.Length)
                            val = BitConverter.ToSingle(response, offset);
                        break;
                    case 0xC4: // DINT
                        if (offset + 4 <= response.Length)
                            val = BitConverter.ToInt32(response, offset);
                        break;
                    case 0xC3: // INT
                        if (offset + 2 <= response.Length)
                            val = (short)(response[offset] | (response[offset + 1] << 8));
                        break;
                    case 0xC2: // SINT
                        if (offset + 1 <= response.Length)
                            val = (sbyte)response[offset];
                        break;
                    case 0xC1: // BOOL
                        if (offset + 1 <= response.Length)
                            val = response[offset] != 0 ? 1 : 0;
                        break;
                    default:
                        return (false, 0);
                }

                return (true, val);
            }
            catch
            {
                return (false, 0);
            }
        }

        #endregion

        public override void Dispose()
        {
            _sendLock?.Dispose();
            base.Dispose();
        }
    }
}
