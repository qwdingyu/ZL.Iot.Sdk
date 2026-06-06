using System;
using System.IO;
using System.Text.Json;
using ZL.Protocol.Models;

namespace ZL.Protocol
{
    /// <summary>
    /// 协议配置加载器——从 JSON 字符串/文件加载 ProtocolConfig。
    /// </summary>
    public static class ProtocolConfigLoader
    {
        /// <summary>
        /// 从 JSON 字符串直接解析 ProtocolConfig。
        /// </summary>
        public static ProtocolConfig? ParseJson(string json, bool applyDefaults = true)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            ProtocolConfig? configObj;
            try
            {
                configObj = JsonSerializer.Deserialize<ProtocolConfig>(json, options);
            }
            catch (JsonException)
            {
                return null;
            }

            if (configObj == null) return null;

            if (applyDefaults && string.IsNullOrEmpty(configObj.Terminator))
            {
                configObj.Terminator = "\n";
            }

            return configObj;
        }

        /// <summary>
        /// 从文件路径加载 ProtocolConfig。
        /// </summary>
        public static ProtocolConfig? LoadFromFile(string filePath, bool applyDefaults = true)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            string json;
            if (Path.IsPathRooted(filePath))
            {
                if (!File.Exists(filePath)) return null;
                json = File.ReadAllText(filePath);
            }
            else
            {
                // 相对路径：尝试从当前工作目录解析
                string diskPath = Path.Combine(Environment.CurrentDirectory, filePath);
                if (File.Exists(diskPath))
                {
                    json = File.ReadAllText(diskPath);
                }
                else
                {
                    return null;
                }
            }

            return ParseJson(json, applyDefaults);
        }

        /// <summary>
        /// 从嵌入式资源/模板包中加载 ProtocolConfig。
        /// 子类或实现者可重写此方法以支持自定义加载源。
        /// </summary>
        public static ProtocolConfig? LoadFromEmbedded(string resourceName, bool applyDefaults = true)
        {
            // 默认实现为空，由调用方通过派生类或注入实现
            return null;
        }
    }
}
