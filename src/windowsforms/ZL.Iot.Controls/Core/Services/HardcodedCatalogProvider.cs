using System.Collections.Generic;
using ZL.Iot.Controls.Core.Interfaces;

namespace ZL.Iot.Controls.Core.Services
{
    /// <summary>
    /// 硬编码协议目录提供者（v1 阶段）
    /// 后续协议增多后替换为 JsonCatalogProvider
    ///
    /// ProtocolType = PlcSimulator.Core.ProtocolCatalog.CanonicalId，
    /// 与 ZL.IotHub.NativeProtocolRegistry 的 Key 保持一致。
    /// </summary>
    public class HardcodedCatalogProvider : ICatalogProvider
    {
        public IReadOnlyList<CatalogGroup> GetGroups()
        {
            return new List<CatalogGroup>
            {
                new()
                {
                    Key = "brand:Siemens",
                    DisplayName = "西门子 (Siemens)",
                    Items = new List<CatalogItem>
                    {
                        new() { Key = "S7-1200", DisplayName = "S7-1200", ProtocolType = "siemens-s7", DefaultPort = "102" },
                        new() { Key = "S7-1500", DisplayName = "S7-1500", ProtocolType = "siemens-s7", DefaultPort = "102" },
                    }
                },
                new()
                {
                    Key = "brand:Modbus",
                    DisplayName = "Modbus",
                    Items = new List<CatalogItem>
                    {
                        new() { Key = "Modbus TCP", DisplayName = "Modbus TCP", ProtocolType = "modbus-tcp", DefaultPort = "502" },
                    }
                },
                new()
                {
                    Key = "brand:Mitsubishi",
                    DisplayName = "三菱 (Mitsubishi)",
                    Items = new List<CatalogItem>
                    {
                        new() { Key = "Melsec MC", DisplayName = "Melsec MC 3E", ProtocolType = "melsec-mc", DefaultPort = "5000" },
                    }
                },
                new()
                {
                    Key = "brand:Omron",
                    DisplayName = "欧姆龙 (Omron)",
                    Items = new List<CatalogItem>
                    {
                        new() { Key = "Omron FINS", DisplayName = "Omron FINS UDP", ProtocolType = "omron-fins-tcp", DefaultPort = "9600" },
                    }
                },
                new()
                {
                    Key = "brand:AB",
                    DisplayName = "罗克韦尔 (Allen-Bradley)",
                    Items = new List<CatalogItem>
                    {
                        new() { Key = "AB CIP", DisplayName = "Allen-Bradley CIP", ProtocolType = "allen-bradley", DefaultPort = "44818" },
                    }
                },
                new()
                {
                    Key = "brand:Other",
                    DisplayName = "其他品牌",
                    Items = new List<CatalogItem>
                    {
                        new() { Key = "Beckhoff", DisplayName = "Beckhoff ADS", ProtocolType = "beckhoff-ads", DefaultPort = "48898" },
                        new() { Key = "Fatek", DisplayName = "Fatek FBs", ProtocolType = "fatek", DefaultPort = "500" },
                        new() { Key = "Keyence", DisplayName = "Keyence KV-Nano", ProtocolType = "keyence-mc", DefaultPort = "8501" },
                        new() { Key = "Fanuc", DisplayName = "Fanuc Robot", ProtocolType = "fanuc", DefaultPort = "8193" },
                    }
                },
                new()
                {
                    Key = "tools:Utility",
                    DisplayName = "工具箱",
                    Items = new List<CatalogItem>
                    {
                        new() { Key = "AddressCalc", DisplayName = "地址计算器", ProtocolType = "AddressCalc",
                                TabMode = CatalogTabMode.ReuseExisting, TabKey = "Tool.AddressCalc" },
                        new() { Key = "PingTool", DisplayName = "PLC Ping", ProtocolType = "PingTool",
                                TabMode = CatalogTabMode.ReuseExisting, TabKey = "Tool.PingTool" },
                    }
                },
            };
        }
    }
}