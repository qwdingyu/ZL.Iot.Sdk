using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZL.Iot.Controls.Common;
using ZL.Iot.Controls.Theme;
using ZL.IotHub;
using ZL.IotHub.Core;
using ZL.IotHub.Native;
using ZL.Tag;

namespace ZL.Iot.Controls.Controls
{
    /// <summary>
    /// 设备连接面板控件（轻量版）。
    ///
    /// 职责：
    /// 1. 提供协议/IP/端口/机架/槽位/Hsl 输入界面
    /// 2. 通过 ZL.IotHub 原生 NativeProtocolRegistry 直接连接真实 PLC
    /// 3. 提供同步 ReadTag/WriteTag/ReadTagRaw 供外部 UcReadWrite 使用
    ///
    /// 注意：
    /// - 已移除仿真模式，保持控件库轻量，仅连接真实设备
    /// - Connect() 为同步调用，TCP 超时场景可能短暂阻塞 UI
    /// </summary>
    public partial class UcConnectionPanel : UserControl
    {
        private IPlcDevice? _driver;
        private bool _isConnected;

        /// <summary>连接状态变化事件</summary>
        public event EventHandler<bool>? ConnectionChanged;

        /// <summary>日志输出事件，由外部 UcConsolePanel 消费</summary>
        public event Action<string, string>? LogOutput;

        /// <summary>支持的协议列表</summary>
        public static readonly string[] SupportedProtocols = new[]
        {
            "siemens-s7",
            "modbus-tcp",
            "omron-fins",
            "mitsubishi-mc"
        };

        public string ProtocolType
        {
            get => cmbProtocol.SelectedItem?.ToString() ?? "siemens-s7";
            set
            {
                var idx = cmbProtocol.Items.IndexOf(value);
                if (idx >= 0) cmbProtocol.SelectedIndex = idx;
            }
        }

        public string DefaultPort { get; set; } = "102";
        public int Rack
        {
            get => (int)nudRack.Value;
            set => nudRack.Value = Math.Max(nudRack.Minimum, Math.Min(nudRack.Maximum, value));
        }

        public int Slot
        {
            get => (int)nudSlot.Value;
            set => nudSlot.Value = Math.Max(nudSlot.Minimum, Math.Min(nudSlot.Maximum, value));
        }

        public bool UseHsl
        {
            get => chkUseHsl.Checked;
            set => chkUseHsl.Checked = value;
        }

        public bool IsConnected => _isConnected;

        public IPlcDevice? PlcDevice => _driver;

        public UcConnectionPanel()
        {
            InitializeComponent();
            ApplyTheme();

            cmbProtocol.Items.AddRange(SupportedProtocols);
            cmbProtocol.SelectedIndex = 0;
            UpdateRackSlotVisibility();
        }

        private void ApplyTheme()
        {
            BackColor = AppTheme.BgPanel;
            btnConnect.FlatStyle = FlatStyle.Flat;
            btnConnect.BackColor = AppTheme.Accent;
            btnConnect.ForeColor = Color.White;
            btnConnect.FlatAppearance.BorderSize = 0;
        }

        private void cmbProtocol_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateRackSlotVisibility();
        }

        private void UpdateRackSlotVisibility()
        {
            bool showRackSlot = ProtocolType == "siemens-s7";
            lblRack.Visible = showRackSlot;
            nudRack.Visible = showRackSlot;
            lblSlot.Visible = showRackSlot;
            nudSlot.Visible = showRackSlot;
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (_isConnected)
            {
                Disconnect();
                return;
            }
            _ = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            SetConnectingState(true);
            try
            {
                var ip = txtIp.Text.Trim();
                if (!int.TryParse(txtPort.Text, out var port) || port <= 0)
                    port = int.Parse(DefaultPort);

                LogOutput?.Invoke("INFO", $"正在连接 {ProtocolType} @ {ip}:{port} ...");

                bool success = false;
                await Task.Run(() =>
                {
                    var result = ConnectDriver(ip, port);
                    success = result;
                });

                if (success)
                {
                    _isConnected = true;
                    UpdateUI();
                    LogOutput?.Invoke("SUCCESS", $"连接成功 ({ip}:{port})");
                    ConnectionChanged?.Invoke(this, true);
                }
                else
                {
                    LogOutput?.Invoke("ERROR", "连接失败");
                }
            }
            finally
            {
                SetConnectingState(false);
            }
        }

        private bool ConnectDriver(string ip, int port)
        {
            try
            {
                var config = new DeviceConfig();
                config.SetParam("Protocol", ResolveProtocolKey(ProtocolType));
                config.SetParam("IPAddress", ip);
                config.SetParam("Port", port);
                if (ProtocolType == "siemens-s7")
                {
                    config.SetParam("Rack", Rack);
                    config.SetParam("Slot", Slot);
                }
                if (UseHsl)
                    config.SetParam("UseHsl", true);

                var desc = NativeProtocolRegistry.Resolve(config);
                _driver = (IPlcDevice)desc.ClientFactory(config);
                var result = _driver.ConnectServer();
                if (!result.IsSuccess)
                {
                    CleanupDriver();
                    LogOutput?.Invoke("ERROR", result.Message ?? "连接失败");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                CleanupDriver();
                LogOutput?.Invoke("ERROR", $"连接异常: {ex.Message}");
                return false;
            }
        }

        private string ResolveProtocolKey(string protocolType)
        {
            if (UseHsl)
            {
                return protocolType.StartsWith("hsl-", StringComparison.OrdinalIgnoreCase)
                    ? protocolType
                    : "hsl-" + protocolType;
            }

            var clean = protocolType;
            if (clean.StartsWith("hsl-", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(4);
            if (clean.StartsWith("native-", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(7);
            return clean;
        }

        private void Disconnect()
        {
            CleanupDriver();
            _isConnected = false;
            UpdateUI();
            LogOutput?.Invoke("INFO", "已断开连接");
            ConnectionChanged?.Invoke(this, false);
        }

        private void CleanupDriver()
        {
            if (_driver != null)
            {
                try { _driver.ConnectClose(); } catch { }
                try { if (_driver is IDisposable d) d.Dispose(); } catch { }
            }
            _driver = null;
        }

        private void SetConnectingState(bool connecting)
        {
            btnConnect.Enabled = !connecting;
            btnConnect.Text = connecting ? "连接中..." : (_isConnected ? "断开" : "连接");
            btnConnect.BackColor = connecting ? AppTheme.AccentDis : AppTheme.Accent;
            txtIp.Enabled = !connecting && !_isConnected;
            txtPort.Enabled = !connecting && !_isConnected;
            cmbProtocol.Enabled = !connecting && !_isConnected;
            nudRack.Enabled = !connecting && !_isConnected;
            nudSlot.Enabled = !connecting && !_isConnected;
            chkUseHsl.Enabled = !connecting && !_isConnected;
        }

        private void UpdateUI()
        {
            btnConnect.Text = _isConnected ? "断开" : "连接";
            btnConnect.BackColor = _isConnected ? Color.FromArgb(0xCC, 0x00, 0x00) : AppTheme.Accent;
            lampStatus.BackColor = _isConnected ? AppTheme.LampGreen : AppTheme.LampRed;
            txtIp.Enabled = !_isConnected;
            txtPort.Enabled = !_isConnected;
            cmbProtocol.Enabled = !_isConnected;
            nudRack.Enabled = !_isConnected;
            nudSlot.Enabled = !_isConnected;
            chkUseHsl.Enabled = !_isConnected;
        }

        public object? ReadTag(string address, string dataType)
        {
            if (!_isConnected || _driver == null)
            {
                LogOutput?.Invoke("ERROR", "未连接，无法读取");
                return null;
            }
            try
            {
                var value = ReadInternal(address, dataType);
                LogOutput?.Invoke("SUCCESS", $"读 {address}({dataType}) = {value}");
                return value;
            }
            catch (Exception ex)
            {
                LogOutput?.Invoke("ERROR", $"读取失败: {ex.Message}");
                return null;
            }
        }

        public string WriteTag(string address, string dataType, object value)
        {
            if (!_isConnected || _driver == null)
            {
                LogOutput?.Invoke("ERROR", "未连接，无法写入");
                return "未连接";
            }
            try
            {
                WriteInternal(address, dataType, value);
                LogOutput?.Invoke("SUCCESS", $"写 {address}({dataType}) = {value}");
                return "OK";
            }
            catch (Exception ex)
            {
                LogOutput?.Invoke("ERROR", $"写入失败: {ex.Message}");
                return ex.Message;
            }
        }

        public byte[]? ReadTagRaw(string address, string dataType)
        {
            if (_driver == null) return null;
            try
            {
                int byteLen = dataType.ToLowerInvariant() switch
                {
                    "bool" or "bit" => 1,
                    "byte" => 1,
                    "short" or "int16" or "ushort" or "uint16" or "word" => 2,
                    "int" or "int32" or "dint" or "uint" or "uint32" or "udint" or "dword" or "float" or "real" => 4,
                    "long" or "int64" or "ulong" or "uint64" or "double" or "float64" => 8,
                    _ => 4,
                };
                var result = _driver.ReadBytes(address, (ushort)byteLen);
                return result.IsSuccess ? result.Content : null;
            }
            catch { return null; }
        }

        private object? ReadInternal(string address, string dataType)
        {
            var t = dataType.ToLowerInvariant();
            object? raw = t switch
            {
                "bool" or "bit" => _driver!.ReadBool(address).Content,
                "byte" => _driver!.ReadBytes(address, 1).Content,
                "short" or "int16" => _driver!.ReadInt16(address).Content,
                "ushort" or "uint16" or "word" => _driver!.ReadUInt16(address).Content,
                "int" or "int32" or "dint" => _driver!.ReadInt32(address).Content,
                "uint" or "uint32" or "udint" or "dword" => _driver!.ReadUInt32(address).Content,
                "long" or "int64" => _driver!.ReadInt64(address).Content,
                "ulong" or "uint64" => _driver!.ReadUInt64(address).Content,
                "float" or "real" => _driver!.ReadFloat(address).Content,
                "double" or "float64" => _driver!.ReadDouble(address).Content,
                _ => _driver!.ReadBytes(address, 4).Content,
            };

            if (raw == null) return null;
            if (raw is bool || raw is short || raw is ushort || raw is int || raw is uint || raw is long || raw is ulong || raw is float || raw is double || raw is string)
                return raw;

            if (raw is byte[] bytes)
            {
                if (t == "bool" || t == "bit")
                {
                    var bitIndex = 0;
                    var parts = address.Split(new[] { '.', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && int.TryParse(parts[parts.Length - 1], out var idx)) bitIndex = idx;
                    if (bitIndex < 0) bitIndex = 0;
                    var byteIndex = bitIndex / 8;
                    var bitOffset = bitIndex % 8;
                    if (byteIndex < bytes.Length)
                        return ((bytes[byteIndex] >> bitOffset) & 1) != 0;
                    return bytes.Length > 0 && bytes[0] != 0;
                }

                byte[] copy = bytes;
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(copy);

                return t switch
                {
                    "byte" when copy.Length >= 1 => copy[0],
                    "short" or "int16" when copy.Length >= 2 => BitConverter.ToInt16(copy, copy.Length - 2),
                    "ushort" or "uint16" or "word" when copy.Length >= 2 => BitConverter.ToUInt16(copy, copy.Length - 2),
                    "int" or "int32" or "dint" when copy.Length >= 4 => BitConverter.ToInt32(copy, copy.Length - 4),
                    "uint" or "uint32" or "udint" or "dword" when copy.Length >= 4 => BitConverter.ToUInt32(copy, copy.Length - 4),
                    "long" or "int64" when copy.Length >= 8 => BitConverter.ToInt64(copy, copy.Length - 8),
                    "ulong" or "uint64" when copy.Length >= 8 => BitConverter.ToUInt64(copy, copy.Length - 8),
                    "float" or "real" when copy.Length >= 4 => BitConverter.ToSingle(copy, copy.Length - 4),
                    "double" or "float64" when copy.Length >= 8 => BitConverter.ToDouble(copy, copy.Length - 8),
                    _ => bytes,
                };
            }

            return raw;
        }

        private void WriteInternal(string address, string dataType, object value)
        {
            if (_driver == null) throw new InvalidOperationException("设备未连接");
            var t = dataType.ToLowerInvariant();
            switch (t)
            {
                case "bool":
                case "bit":
                    _driver.Write(address, Convert.ToBoolean(value));
                    break;
                case "byte":
                    _driver.Write(address, new[] { Convert.ToByte(value) });
                    break;
                case "short":
                case "int16": _driver.Write(address, Convert.ToInt16(value)); break;
                case "ushort":
                case "uint16":
                case "word": _driver.Write(address, Convert.ToUInt16(value)); break;
                case "int":
                case "int32":
                case "dint": _driver.Write(address, Convert.ToInt32(value)); break;
                case "uint":
                case "uint32":
                case "udint":
                case "dword": _driver.Write(address, Convert.ToUInt32(value)); break;
                case "long":
                case "int64": _driver.Write(address, Convert.ToInt64(value)); break;
                case "ulong":
                case "uint64": _driver.Write(address, Convert.ToUInt64(value)); break;
                case "real":
                case "float": _driver.Write(address, Convert.ToSingle(value)); break;
                case "float64":
                case "double": _driver.Write(address, Convert.ToDouble(value)); break;
                default: _driver.Write(address, value.ToString() ?? ""); break;
            }
        }

        public string GetIP() => txtIp.Text.Trim();
        public string GetPort() => txtPort.Text.Trim();
    }
}
