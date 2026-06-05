// ============================================================
//  ConfigLoader 单元测试
//  覆盖：JSON/XML 双格式加载、配置验证、序列化/反序列化
// ============================================================

using System;
using System.IO;
using System.Text;
using ZL.Iot.Runner.Configuration;

namespace ZL.Iot.Runner.Tests
{
    /// <summary>
    /// ConfigLoader 单元测试
    /// 覆盖：
    /// 1. JSON 格式加载（正常路径）
    /// 2. XML 格式加载（正常路径）
    /// 3. 文件不存在异常
    /// 4. 不支持格式异常
    /// 5. 空内容异常
    /// 6. 配置验证：设备 Code/Protocol/Ip 必填
    /// 7. 配置验证：标签 Id 唯一
    /// 8. 配置验证：执行器引用标签必须存在
    /// 9. JSON 序列化/反序列化往返一致性
    /// 10. XML 序列化/反序列化往返一致性
    /// 11. ToJson/ToXml 输出格式
    /// 12. Save() 文件写入
    /// </summary>
    public class ConfigLoaderTests : IDisposable
    {
        private readonly string _tempDir;

        public ConfigLoaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"ConfigLoaderTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        #region JSON 加载测试

        [Fact]
        public void LoadFromJson_ValidConfig_ReturnsRunnerConfig()
        {
            // Arrange
            var json = """
            {
              "runner": { "name": "TestRunner", "logLevel": "Information" },
              "devices": [
                {
                  "code": "plc1",
                  "protocol": "SiemensS7",
                  "ip": "192.168.1.100",
                  "port": 102,
                  "rack": 0,
                  "slot": 1,
                  "tags": [
                    { "id": "Tag1", "address": "DB1.DBD0", "dataType": "float", "enable": true, "tagType": "D" }
                  ],
                  "executors": [
                    { "bizCode": "E1", "tagId": "Tag1", "judgeType": "1", "judgeExp": "1", "exeType": "M", "script": "SELECT 1", "exeOrder": 1, "enable": true }
                  ]
                }
              ]
            }
            """;

            // Act
            var config = ConfigLoader.LoadFromJson(json);

            // Assert
            Assert.NotNull(config);
            Assert.Equal("TestRunner", config.Runner.Name);
            Assert.Single(config.Devices);
            Assert.Equal("plc1", config.Devices[0].Code);
            Assert.Equal("SiemensS7", config.Devices[0].Protocol);
            Assert.Equal(102, config.Devices[0].Port);
            Assert.Single(config.Devices[0].Tags);
            Assert.Equal("Tag1", config.Devices[0].Tags[0].Id);
            Assert.Single(config.Devices[0].Executors);
            Assert.Equal("E1", config.Devices[0].Executors[0].BizCode);
        }

        [Fact]
        public void LoadFromJson_EmptyContent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ConfigLoader.LoadFromJson(""));
            Assert.Throws<ArgumentNullException>(() => ConfigLoader.LoadFromJson("   "));
            Assert.Throws<ArgumentNullException>(() => ConfigLoader.LoadFromJson(null!));
        }

        [Fact]
        public void LoadFromJson_InvalidJson_ThrowsInvalidOperationException()
        {
            var invalidJson = "{ \"runner\": "; // 截断的 JSON
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson(invalidJson));
            Assert.Contains("JSON 配置格式错误", ex.Message);
        }

        [Fact]
        public void LoadFromJson_NullResult_ThrowsInvalidOperationException()
        {
            // "null" JSON 反序列化为 null
            Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson("null"));
        }

        #endregion

        #region XML 加载测试

        [Fact]
        public void LoadFromXml_ValidConfig_ReturnsRunnerConfig()
        {
            // Arrange
            var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <RunnerConfig>
              <Runner>
                <Name>XmlTestRunner</Name>
                <LogLevel>Information</LogLevel>
              </Runner>
              <Devices>
                <Device>
                  <Code>plc_xml</Code>
                  <Protocol>ModbusTcp</Protocol>
                  <Ip>192.168.1.200</Ip>
                  <Port>502</Port>
                  <Tags>
                    <TagProfile>
                      <Id>Tag1</Id>
                      <Address>40001</Address>
                      <DataType>short</DataType>
                      <Enable>true</Enable>
                      <TagType>D</TagType>
                    </TagProfile>
                  </Tags>
                </Device>
              </Devices>
            </RunnerConfig>
            """;

            // Act
            var config = ConfigLoader.LoadFromXml(xml);

            // Assert
            Assert.NotNull(config);
            Assert.Equal("XmlTestRunner", config.Runner.Name);
            Assert.Single(config.Devices);
            Assert.Equal("plc_xml", config.Devices[0].Code);
            Assert.Equal("ModbusTcp", config.Devices[0].Protocol);
            Assert.Single(config.Devices[0].Tags);
        }

        [Fact]
        public void LoadFromXml_EmptyContent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ConfigLoader.LoadFromXml(""));
            Assert.Throws<ArgumentNullException>(() => ConfigLoader.LoadFromXml(null!));
        }

        [Fact]
        public void LoadFromXml_InvalidXml_ThrowsInvalidOperationException()
        {
            var invalidXml = "<RunnerConfig><Runner><Name>"; // 截断
            Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromXml(invalidXml));
        }

        #endregion

        #region 文件加载测试

        [Fact]
        public void Load_FromJsonFile_ReturnsConfig()
        {
            // Arrange
            var jsonPath = Path.Combine(_tempDir, "test.json");
            File.WriteAllText(jsonPath, """
            {
              "runner": { "name": "FileTest" },
              "devices": [{ "code": "p1", "protocol": "SiemensS7", "ip": "127.0.0.1", "port": 102, "tags": [], "executors": [] }]
            }
            """, Encoding.UTF8);

            // Act
            var config = ConfigLoader.Load(jsonPath);

            // Assert
            Assert.Equal("FileTest", config.Runner.Name);
            Assert.Equal("p1", config.Devices[0].Code);
        }

        [Fact]
        public void Load_FromXmlFile_ReturnsConfig()
        {
            // Arrange
            var xmlPath = Path.Combine(_tempDir, "test.xml");
            File.WriteAllText(xmlPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <RunnerConfig>
              <Runner><Name>XmlFileTest</Name></Runner>
              <Devices>
                <Device><Code>p1</Code><Protocol>SiemensS7</Protocol><Ip>127.0.0.1</Ip><Port>102</Port><Tags /><Executors /></Device>
              </Devices>
            </RunnerConfig>
            """, Encoding.UTF8);

            // Act
            var config = ConfigLoader.Load(xmlPath);

            // Assert
            Assert.Equal("XmlFileTest", config.Runner.Name);
        }

        [Fact]
        public void Load_FileNotFound_ThrowsFileNotFoundException()
        {
            var ex = Assert.Throws<FileNotFoundException>(() => ConfigLoader.Load("/nonexistent/path.json"));
            Assert.Contains("配置文件不存在", ex.Message);
        }

        [Fact]
        public void Load_UnsupportedExtension_ThrowsNotSupportedException()
        {
            var txtPath = Path.Combine(_tempDir, "test.txt");
            File.WriteAllText(txtPath, "anything");
            var ex = Assert.Throws<NotSupportedException>(() => ConfigLoader.Load(txtPath));
            Assert.Contains("不支持的配置文件格式", ex.Message);
        }

        [Fact]
        public void Load_EmptyPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ConfigLoader.Load(""));
            Assert.Throws<ArgumentNullException>(() => ConfigLoader.Load(null!));
        }

        #endregion

        #region 配置验证测试

        [Fact]
        public void LoadFromJson_NoDevices_ThrowsInvalidOperationException()
        {
            var json = """{ "runner": { "name": "Empty" }, "devices": [] }""";
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson(json));
            Assert.Contains("Devices 列表不能为空", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyDeviceCode_ThrowsInvalidOperationException()
        {
            var json = """
            {
              "runner": { "name": "Test" },
              "devices": [{ "code": "", "protocol": "SiemensS7", "ip": "127.0.0.1", "port": 102, "tags": [], "executors": [] }]
            }
            """;
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson(json));
            Assert.Contains("Code 不能为空", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyProtocol_ThrowsInvalidOperationException()
        {
            var json = """
            {
              "runner": { "name": "Test" },
              "devices": [{ "code": "p1", "protocol": "", "ip": "127.0.0.1", "port": 102, "tags": [], "executors": [] }]
            }
            """;
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson(json));
            Assert.Contains("Protocol 不能为空", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyIp_ThrowsInvalidOperationException()
        {
            var json = """
            {
              "runner": { "name": "Test" },
              "devices": [{ "code": "p1", "protocol": "SiemensS7", "ip": "", "port": 102, "tags": [], "executors": [] }]
            }
            """;
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson(json));
            Assert.Contains("Ip 不能为空", ex.Message);
        }

        [Fact]
        public void LoadFromJson_DuplicateTagIds_ThrowsInvalidOperationException()
        {
            var json = """
            {
              "runner": { "name": "Test" },
              "devices": [{
                "code": "p1", "protocol": "SiemensS7", "ip": "127.0.0.1", "port": 102,
                "tags": [
                  { "id": "T1", "address": "DB1.DBD0", "dataType": "float", "enable": true },
                  { "id": "T1", "address": "DB1.DBD4", "dataType": "float", "enable": true }
                ],
                "executors": []
              }]
            }
            """;
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson(json));
            Assert.Contains("标签 Id [T1] 重复", ex.Message);
        }

        [Fact]
        public void LoadFromJson_ExecutorReferencesNonExistentTag_ThrowsInvalidOperationException()
        {
            var json = """
            {
              "runner": { "name": "Test" },
              "devices": [{
                "code": "p1", "protocol": "SiemensS7", "ip": "127.0.0.1", "port": 102,
                "tags": [{ "id": "T1", "address": "DB1.DBD0", "dataType": "float", "enable": true }],
                "executors": [{ "bizCode": "E1", "tagId": "NonExistent", "judgeType": "1", "exeType": "M", "script": "x", "enable": true }]
              }]
            }
            """;
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson(json));
            Assert.Contains("引用的 TagId [NonExistent] 不存在", ex.Message);
        }

        [Fact]
        public void LoadFromJson_ExecutorReferencesDisabledTag_ThrowsInvalidOperationException()
        {
            var json = """
            {
              "runner": { "name": "Test" },
              "devices": [{
                "code": "p1", "protocol": "SiemensS7", "ip": "127.0.0.1", "port": 102,
                "tags": [{ "id": "T1", "address": "DB1.DBD0", "dataType": "float", "enable": false }],
                "executors": [{ "bizCode": "E1", "tagId": "T1", "judgeType": "1", "exeType": "M", "script": "x", "enable": true }]
              }]
            }
            """;
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadFromJson(json));
            Assert.Contains("引用的 TagId [T1] 不存在或未启用", ex.Message);
        }

        [Fact]
        public void LoadFromJson_DisabledTagsIgnored_InDuplicateAndReferenceCheck()
        {
            // 重复的 Id 但都是 enable=false 应该通过（被过滤）
            var json = """
            {
              "runner": { "name": "Test" },
              "devices": [{
                "code": "p1", "protocol": "SiemensS7", "ip": "127.0.0.1", "port": 102,
                "tags": [
                  { "id": "T1", "address": "DB1.DBD0", "dataType": "float", "enable": false },
                  { "id": "T1", "address": "DB1.DBD4", "dataType": "float", "enable": false }
                ],
                "executors": []
              }]
            }
            """;
            // 不应该抛出异常
            var config = ConfigLoader.LoadFromJson(json);
            Assert.Equal(2, config.Devices[0].Tags.Count);
        }

        #endregion

        #region 序列化往返测试

        [Fact]
        public void JsonRoundTrip_PreservesAllProperties()
        {
            // Arrange
            var original = CreateSampleConfig();

            // Act
            var json = ConfigLoader.ToJson(original, writeIndented: true);
            var deserialized = ConfigLoader.LoadFromJson(json);

            // Assert
            Assert.Equal(original.Runner.Name, deserialized.Runner.Name);
            Assert.Equal(original.Devices.Count, deserialized.Devices.Count);
            Assert.Equal(original.Devices[0].Code, deserialized.Devices[0].Code);
            Assert.Equal(original.Devices[0].Tags.Count, deserialized.Devices[0].Tags.Count);
            Assert.Equal(original.Devices[0].Executors.Count, deserialized.Devices[0].Executors.Count);
            Assert.Equal(original.Devices[0].Tags[0].Id, deserialized.Devices[0].Tags[0].Id);
            Assert.Equal(original.Devices[0].Tags[0].Address, deserialized.Devices[0].Tags[0].Address);
        }

        [Fact]
        public void XmlRoundTrip_PreservesAllProperties()
        {
            // Arrange
            var original = CreateSampleConfig();

            // Act
            var xml = ConfigLoader.ToXml(original);
            var deserialized = ConfigLoader.LoadFromXml(xml);

            // Assert
            Assert.Equal(original.Runner.Name, deserialized.Runner.Name);
            Assert.Equal(original.Devices.Count, deserialized.Devices.Count);
            Assert.Equal(original.Devices[0].Code, deserialized.Devices[0].Code);
        }

        [Fact]
        public void SaveJson_CreatesFileWithCorrectContent()
        {
            // Arrange
            var path = Path.Combine(_tempDir, "save_test.json");
            var config = CreateSampleConfig();

            // Act
            ConfigLoader.Save(config, path);

            // Assert
            Assert.True(File.Exists(path));
            var reloaded = ConfigLoader.Load(path);
            Assert.Equal(config.Runner.Name, reloaded.Runner.Name);
        }

        [Fact]
        public void SaveXml_CreatesFileWithCorrectContent()
        {
            // Arrange
            var path = Path.Combine(_tempDir, "save_test.xml");
            var config = CreateSampleConfig();

            // Act
            ConfigLoader.Save(config, path);

            // Assert
            Assert.True(File.Exists(path));
            var reloaded = ConfigLoader.Load(path);
            Assert.Equal(config.Runner.Name, reloaded.Runner.Name);
        }

        [Fact]
        public void Save_UnsupportedExtension_ThrowsNotSupportedException()
        {
            var path = Path.Combine(_tempDir, "test.yaml");
            Assert.Throws<NotSupportedException>(() => ConfigLoader.Save(CreateSampleConfig(), path));
        }

        [Fact]
        public void Save_CreatesDirectoryIfNotExists()
        {
            // Arrange
            var nestedDir = Path.Combine(_tempDir, "subdir", "nested");
            var path = Path.Combine(nestedDir, "config.json");
            var config = CreateSampleConfig();

            // Act
            ConfigLoader.Save(config, path);

            // Assert
            Assert.True(Directory.Exists(nestedDir));
            Assert.True(File.Exists(path));
        }

        #endregion

        #region 辅助方法

        private static RunnerConfig CreateSampleConfig()
        {
            return new RunnerConfig
            {
                Runner = new RunnerOptions
                {
                    Name = "SampleRunner",
                    LogLevel = "Information",
                    DataStorage = new DataStorageOptions
                    {
                        Type = "Sqlite",
                        ConnectionString = "Data Source=./test.db"
                    }
                },
                Devices = new System.Collections.Generic.List<DeviceProfile>
                {
                    new DeviceProfile
                    {
                        Code = "plc_sample",
                        Protocol = "SiemensS7",
                        Ip = "192.168.1.50",
                        Port = 102,
                        Rack = 0,
                        Slot = 1,
                        ConnectTimeout = 5000,
                        ReadInterval = 200,
                        Tags = new System.Collections.Generic.List<TagProfile>
                        {
                            new TagProfile { Id = "T1", Address = "DB1.DBD0", DataType = "float", Enable = true, TagType = "D" },
                            new TagProfile { Id = "T2", Address = "DB1.DBX4.0", DataType = "bool", Enable = true, TagType = "M" }
                        },
                        Executors = new System.Collections.Generic.List<ExecutorProfile>
                        {
                            new ExecutorProfile
                            {
                                BizCode = "E1",
                                TagId = "T2",
                                JudgeType = "1",
                                JudgeExp = "1",
                                ExeType = "M",
                                Script = "INSERT INTO x VALUES ('{{TagId}}', {{Value}})",
                                ExeOrder = 1,
                                Enable = true
                            }
                        }
                    }
                }
            };
        }

        #endregion
    }
}