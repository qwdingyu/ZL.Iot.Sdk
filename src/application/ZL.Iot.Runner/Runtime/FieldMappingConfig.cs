// ============================================================
//  FieldMapping 配置模型 — Runner 侧复刻
//  与 UseThink.Iot 的 FieldMappingConfig 同构，但无外部依赖
//  按轴展开逻辑内联实现，不依赖 PerAxisExpandEngine
// ============================================================

using System.Text.Json;

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// FieldMapping 配置 — 定义触发标签后的采集规则
/// </summary>
public class FieldMappingConfig
{
    /// <summary>目标表名</summary>
    public string TableName { get; set; } = "";

    /// <summary>写入模式：Insert / Upsert</summary>
    public string SaveMode { get; set; } = "Insert";

    /// <summary>Upsert 时的业务主键</summary>
    public string? BusinessPrimaryKey { get; set; }

    /// <summary>采集完成回写的触发标签</summary>
    public string? FeedbackTag { get; set; }

    /// <summary>多轴展开配置</summary>
    public PerAxisConfig? PerAxis { get; set; }

    /// <summary>字段映射规则列表</summary>
    public List<FieldMappingRule> Columns { get; set; } = new();

    /// <summary>从 JSON 反序列化</summary>
    public static FieldMappingConfig? FromJson(string json)
    {
        return JsonSerializer.Deserialize<FieldMappingConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}

/// <summary>
/// 多轴展开配置
/// </summary>
public class PerAxisConfig
{
    public bool Enabled { get; set; }
    public AxisDiscoveryConfig? AxisDiscovery { get; set; }
    public Dictionary<string, string>? Transform { get; set; }
}

/// <summary>
/// 轴发现策略
/// </summary>
public class AxisDiscoveryConfig
{
    public string Mode { get; set; } = "explicit";
    public List<string>? Axes { get; set; }
    public int? Start { get; set; }
    public int? End { get; set; }
    public int Step { get; set; } = 1;
    public string? Format { get; set; }
    public List<AxisDiscoveryRule>? Rules { get; set; }
}

/// <summary>
/// 轴发现规则
/// </summary>
public class AxisDiscoveryRule
{
    public string Pattern { get; set; } = "";
    public string Extract { get; set; } = "number";
}

/// <summary>
/// 字段映射规则
/// </summary>
public class FieldMappingRule
{
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "TagValue";
    public string? SourceTag { get; set; }
    public string? SourceField { get; set; }
    public bool PerAxis { get; set; }
    public string DataType { get; set; } = "Int32";
    public double ScaleFactor { get; set; } = 1.0;
    public Dictionary<string, string>? Transform { get; set; }
    public bool IsPrimaryKey { get; set; }
    public string? DefaultValue { get; set; }
}
