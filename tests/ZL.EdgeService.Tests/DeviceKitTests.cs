// DeviceKit 单元测试 - 验证配置解析逻辑
// 使用 xUnit 测试框架

using System;
using Xunit;
using ZL.Dao.IotDevice;
using ZL.Tag;
using ZL.IotHub.Hsl;

namespace ZL.EdgeService.Tests
{
    /// <summary>
    /// DeviceKit 配置解析逻辑测试
    /// </summary>
    public class DeviceKitTests
    {
        /// <summary>
        /// 测试西门子 S7 配置解析
        /// </summary>
        [Fact]
        public void TestSiemensS7Config_ParsesCorrectly()
        {
            // Arrange
            var dto = new IotDeviceDriverDto
            {
                device_id = "device-001",
                ip = "192.168.1.100",
                device_type_id = 1,  // 西门子 S7
                port = 102,
                extra_config = "{\"Rack\": 0, \"Slot\": 1}",
                debug = false
            };

            // Act
            var config = BuildDeviceConfigFromDto(dto);

            // Assert
            Assert.Equal("device-001", config.Get("DeviceId", ""));
            Assert.Equal("192.168.1.100", config.Get("DeviceIp", ""));
            Assert.Equal(102, config.Get("Port", 0));
            Assert.Equal("siemens-s7", config.Get("Protocol", ""));
            Assert.Equal(0, config.Get("Rack", -1));
            Assert.Equal(1, config.Get("Slot", -1));
        }

        /// <summary>
        /// 测试 Modbus TCP 配置解析
        /// </summary>
        [Fact]
        public void TestModbusTcpConfig_ParsesCorrectly()
        {
            // Arrange
            var dto = new IotDeviceDriverDto
            {
                device_id = "device-002",
                ip = "192.168.1.200",
                device_type_id = 3,  // Modbus TCP
                port = 502,
                extra_config = "{\"Port\": 502}",
                debug = false
            };

            // Act
            var config = BuildDeviceConfigFromDto(dto);

            // Assert
            Assert.Equal("device-002", config.Get("DeviceId", ""));
            Assert.Equal(502, config.Get("Port", 0));
            Assert.Equal("modbus-tcp", config.Get("Protocol", ""));
        }

        /// <summary>
        /// 测试汇川设备配置解析 - 从 extra_config 解析端口
        /// </summary>
        [Fact]
        public void TestInovanceConfig_ParsesPortFromExtraConfig()
        {
            // Arrange
            var dto = new IotDeviceDriverDto
            {
                device_id = "device-003",
                ip = "192.168.1.50",
                device_type_id = 4,  // 汇川
                port = 0,  // 未设置，使用 extra_config 中的端口
                extra_config = "{\"Port\": 8080}",
                debug = true
            };

            // Act
            var config = BuildDeviceConfigFromDto(dto);

            // Assert
            Assert.Equal("device-003", config.Get("DeviceId", ""));
            Assert.Equal(8080, config.Get("Port", 0));  // 从 extra_config 解析
            Assert.Equal("inovance-tcp", config.Get("Protocol", ""));
            Assert.Equal(true, config.Get("Debug", false));
        }

        /// <summary>
        /// 测试默认配置（无 extra_config）
        /// </summary>
        [Fact]
        public void TestDefaultConfig_UsesDefaultPort()
        {
            // Arrange
            var dto = new IotDeviceDriverDto
            {
                device_id = "device-004",
                ip = "192.168.1.10",
                device_type_id = 1,  // 西门子
                port = 0,  // 未设置，使用默认值
                debug = false
            };

            // Act
            var config = BuildDeviceConfigFromDto(dto);

            // Assert
            Assert.Equal("device-004", config.Get("DeviceId", ""));
            Assert.Equal(102, config.Get("Port", 0));  // 默认西门子端口
            Assert.Equal("siemens-s7", config.Get("Protocol", ""));
        }

        /// <summary>
        /// 测试所有协议类型解析
        /// </summary>
        [Theory]
        [InlineData(1, "siemens-s7")]
        [InlineData(2, "siemens-s7")]
        [InlineData(3, "modbus-tcp")]
        [InlineData(4, "inovance-tcp")]
        [InlineData(5, "mitsubishi-mc")]
        [InlineData(6, "omron-tcp")]
        [InlineData(7, "ab-plc")]
        [InlineData(99, "siemens-s7")]  // 未知类型使用默认
        public void TestProtocolResolution(int deviceTypeId, string expectedProtocol)
        {
            // Arrange
            var dto = new IotDeviceDriverDto
            {
                device_id = "device-test",
                ip = "192.168.1.1",
                device_type_id = deviceTypeId,
                port = 102
            };

            // Act
            var config = BuildDeviceConfigFromDto(dto);

            // Assert
            Assert.Equal(expectedProtocol, config.Get("Protocol", ""));
        }

        /// <summary>
        /// 测试完整 extra_config 解析（包含多个参数）
        /// </summary>
        [Fact]
        public void TestFullExtraConfig_ParsesAllParameters()
        {
            // Arrange
            var dto = new IotDeviceDriverDto
            {
                device_id = "device-005",
                ip = "192.168.1.100",
                device_type_id = 1,
                port = 102,
                extra_config = "{\"Rack\": 0, \"Slot\": 1, \"BaudRate\": 9600, \"DataBits\": 8, \"StopBits\": 1, \"ParityBit\": \"N\", \"DataFormat\": \"ABCD\"}",
                debug = false
            };

            // Act
            var config = BuildDeviceConfigFromDto(dto);

            // Assert
            Assert.Equal(0, config.Get("Rack", -1));
            Assert.Equal(1, config.Get("Slot", -1));
            Assert.Equal(9600, config.Get("BaudRate", 0));
            Assert.Equal(8, config.Get("DataBits", 0));
            Assert.Equal(1, config.Get("StopBits", 0));
            Assert.Equal("N", config.Get("ParityBit", ""));
            Assert.Equal("ABCD", config.Get("DataFormat", ""));
        }

        /// <summary>
        /// 测试 HslUnifiedDriver 构造
        /// </summary>
        [Fact]
        public void TestHslUnifiedDriver_CanBeConstructed()
        {
            // Arrange
            var dto = new IotDeviceDriverDto
            {
                device_id = "device-006",
                ip = "192.168.1.100",
                device_type_id = 1,
                port = 102,
                extra_config = "{\"Rack\": 0, \"Slot\": 1}",
                debug = false
            };

            // Act
            var config = BuildDeviceConfigFromDto(dto);
            
            // 尝试构造 HslUnifiedDriver - 这会验证配置是否有效
            var exception = Record.Exception(() => new HslUnifiedDriver(config));

            // Assert
            Assert.Null(exception);  // 如果有异常会记录下来
        }

        #region 辅助方法（复制 DeviceKit 中的逻辑）

        /// <summary>
        /// 从 DTO 构建配置
        /// </summary>
        private static DeviceConfig BuildDeviceConfigFromDto(IotDeviceDriverDto it)
        {
            var config = new DeviceConfig();
            config.Add("DeviceId", it.device_id);
            
            // IP 地址
            config.Add("DeviceIp", it.ip);
            
            // 端口号 - 优先使用 DTO 的 port 字段，其次从 extra_config 解析
            int port = it.port > 0 ? it.port : 102;
            if (port == 102 && !string.IsNullOrEmpty(it.extra_config))
            {
                port = ParseIntFromJson(it.extra_config, "Port") ?? 102;
            }
            config.Add("Port", port);
            
            // 协议类型
            string protocolKey = ResolveProtocolFromDeviceType(it.device_type_id);
            config.Add("Protocol", protocolKey);
            
            // 从 extra_config 解析其他参数
            if (!string.IsNullOrEmpty(it.extra_config))
            {
                AddJsonConfigToDeviceConfig(it.extra_config, config, port);
            }
            
            // 调试标志
            config.Add("Debug", it.debug);
            
            return config;
        }

        private static int? ParseIntFromJson(string json, string key)
        {
            try
            {
                var pattern = $"\"{key}\"\\s*:\\s*(\\d+)";
                var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
                {
                    return value;
                }
            }
            catch { }
            return null;
        }

        private static void AddJsonConfigToDeviceConfig(string json, DeviceConfig config, int defaultPort)
        {
            try
            {
                int? rack = ParseIntFromJson(json, "Rack");
                if (rack.HasValue) config.Add("Rack", rack.Value);
                
                int? slot = ParseIntFromJson(json, "Slot");
                if (slot.HasValue) config.Add("Slot", slot.Value);
                
                int? baudRate = ParseIntFromJson(json, "BaudRate");
                if (baudRate.HasValue) config.Add("BaudRate", baudRate.Value);
                
                int? dataBits = ParseIntFromJson(json, "DataBits");
                if (dataBits.HasValue) config.Add("DataBits", dataBits.Value);
                
                int? stopBits = ParseIntFromJson(json, "StopBits");
                if (stopBits.HasValue) config.Add("StopBits", stopBits.Value);
                
                var parityMatch = System.Text.RegularExpressions.Regex.Match(json, "\"ParityBit\"\\s*:\\s*\"([^\"]+)\"");
                if (parityMatch.Success) config.Add("ParityBit", parityMatch.Groups[1].Value);
                
                var dataFormatMatch = System.Text.RegularExpressions.Regex.Match(json, "\"DataFormat\"\\s*:\\s*\"([^\"]+)\"");
                if (dataFormatMatch.Success) config.Add("DataFormat", dataFormatMatch.Groups[1].Value);
                
                var serverNameMatch = System.Text.RegularExpressions.Regex.Match(json, "\"ServerName\"\\s*:\\s*\"([^\"]+)\"");
                if (serverNameMatch.Success) config.Add("ServerName", serverNameMatch.Groups[1].Value);
            }
            catch { }
        }

        private static string ResolveProtocolFromDeviceType(int deviceTypeId)
        {
            return deviceTypeId switch
            {
                1 => "siemens-s7",
                2 => "siemens-s7",
                3 => "modbus-tcp",
                4 => "inovance-tcp",
                5 => "mitsubishi-mc",
                6 => "omron-tcp",
                7 => "ab-plc",
                _ => "siemens-s7"
            };
        }

        #endregion
    }
}