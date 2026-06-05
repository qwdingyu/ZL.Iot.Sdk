// ============================================================
//  统一配置加载器 - 自动识别 JSON/XML 格式
//  使用 System.Text.Json + XmlSerializer（铁律：ORM 优先）
// ============================================================

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZL.Iot.Runner.Configuration
{
    /// <summary>
    /// 统一的配置加载入口，自动识别 JSON/XML 格式并反序列化为 RunnerConfig
    /// 
    /// 使用方法：
    /// <code>
    /// var config = ConfigLoader.Load("runner.config.json");
    /// // 或
    /// var config = ConfigLoader.Load("runner.config.xml");
    /// </code>
    /// </summary>
    public static class ConfigLoader
    {
        // =====================================================
        //  JSON 序列化选项（统一命名策略，避免配置字段大小写问题）
        // =====================================================
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// 从文件路径自动识别格式加载 RunnerConfig
        /// </summary>
        /// <param name="configPath">配置文件路径，支持 .json 和 .xml</param>
        /// <returns>解析后的 RunnerConfig 实例</returns>
        /// <exception cref="FileNotFoundException">配置文件不存在</exception>
        /// <exception cref="NotSupportedException">不支持的文件格式</exception>
        public static RunnerConfig Load(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
                throw new ArgumentNullException(nameof(configPath));

            if (!File.Exists(configPath))
                throw new FileNotFoundException($"配置文件不存在: {configPath}，请检查路径是否正确", configPath);

            var extension = Path.GetExtension(configPath).ToLowerInvariant();
            var content = File.ReadAllText(configPath, Encoding.UTF8);

            return extension switch
            {
                ".json" => LoadFromJson(content),
                ".xml" => LoadFromXml(content),
                _ => throw new NotSupportedException(
                    $"不支持的配置文件格式: {extension}，仅支持 .json 和 .xml。建议使用 .json 格式")
            };
        }

        /// <summary>
        /// 从 JSON 字符串加载配置
        /// </summary>
        /// <param name="json">JSON 字符串内容</param>
        public static RunnerConfig LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentNullException(nameof(json), "JSON 配置内容不能为空");

            try
            {
                var config = JsonSerializer.Deserialize<RunnerConfig>(json, JsonOptions);
                if (config == null)
                    throw new InvalidOperationException("JSON 配置解析失败，返回 null（可能格式错误）");

                ValidateConfig(config);
                return config;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"JSON 配置格式错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 从 XML 字符串加载配置（兼容老项目格式）
        /// XML 格式与老项目 ZL.DeviceLib 的 LINE1_OP10_PlcTag.xml 兼容
        /// </summary>
        /// <param name="xml">XML 字符串内容</param>
        public static RunnerConfig LoadFromXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new ArgumentNullException(nameof(xml), "XML 配置内容不能为空");

            try
            {
                // 使用 XmlSerializer 进行反序列化
                var serializer = new System.Xml.Serialization.XmlSerializer(typeof(RunnerConfig));
                using var reader = new StringReader(xml);
                var config = serializer.Deserialize(reader) as RunnerConfig;

                if (config == null)
                    throw new InvalidOperationException("XML 配置解析失败，返回 null（可能格式错误）");

                ValidateConfig(config);
                return config;
            }
            catch (InvalidOperationException ex) when (ex.InnerException != null)
            {
                throw new InvalidOperationException($"XML 配置格式错误: {ex.InnerException.Message}", ex);
            }
        }

        /// <summary>
        /// 将 RunnerConfig 序列化为 JSON 字符串
        /// </summary>
        /// <param name="config">配置对象</param>
        /// <param name="writeIndented">是否格式化输出（便于阅读）</param>
        public static string ToJson(RunnerConfig config, bool writeIndented = true)
        {
            var options = new JsonSerializerOptions(JsonOptions)
            {
                WriteIndented = writeIndented
            };
            return JsonSerializer.Serialize(config, options);
        }

        /// <summary>
        /// 将 RunnerConfig 序列化为 XML 字符串
        /// </summary>
        public static string ToXml(RunnerConfig config)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(RunnerConfig));
            using var writer = new StringWriter();
            var namespaces = new System.Xml.Serialization.XmlSerializerNamespaces();
            namespaces.Add("", ""); // 移除默认命名空间声明
            serializer.Serialize(writer, config, namespaces);
            return writer.ToString();
        }

        /// <summary>
        /// 保存配置到文件（根据文件扩展名自动选择格式）
        /// </summary>
        public static void Save(RunnerConfig config, string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var content = extension switch
            {
                ".json" => ToJson(config, writeIndented: true),
                ".xml" => ToXml(config),
                _ => throw new NotSupportedException($"不支持的格式: {extension}，仅支持 .json 和 .xml")
            };

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, content, Encoding.UTF8);
        }

        /// <summary>
        /// 配置验证：检查必填字段和基本约束
        /// </summary>
        private static void ValidateConfig(RunnerConfig config)
        {
            if (config.Devices == null || config.Devices.Count == 0)
                throw new InvalidOperationException("配置验证失败：Devices 列表不能为空，至少需要配置一个设备");

            for (int i = 0; i < config.Devices.Count; i++)
            {
                var device = config.Devices[i];
                if (string.IsNullOrWhiteSpace(device.Code))
                    throw new InvalidOperationException($"配置验证失败：第 {i + 1} 个设备的 Code 不能为空");
                if (string.IsNullOrWhiteSpace(device.Protocol))
                    throw new InvalidOperationException($"配置验证失败：设备 [{device.Code}] 的 Protocol 不能为空");
                if (string.IsNullOrWhiteSpace(device.Ip))
                    throw new InvalidOperationException($"配置验证失败：设备 [{device.Code}] 的 Ip 不能为空");

                // 检查标签 Id 是否有重复
                var tagIds = new HashSet<string>();
                foreach (var tag in device.Tags)
                {
                    if (!tag.Enable) continue;
                    if (string.IsNullOrWhiteSpace(tag.Id))
                        throw new InvalidOperationException($"配置验证失败：设备 [{device.Code}] 中存在 Id 为空的标签");
                    if (!tagIds.Add(tag.Id))
                        throw new InvalidOperationException($"配置验证失败：设备 [{device.Code}] 中标签 Id [{tag.Id}] 重复");
                }

                // 检查执行器引用的 TagId 是否存在
                foreach (var exe in device.Executors.Where(e => e.Enable))
                {
                    if (!device.Tags.Any(t => t.Enable && t.Id == exe.TagId))
                        throw new InvalidOperationException(
                            $"配置验证失败：设备 [{device.Code}] 执行器 [{exe.BizCode}] 引用的 TagId [{exe.TagId}] 不存在或未启用");
                }
            }
        }
    }
}