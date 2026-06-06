# ZL.Protocol — 协议配置模型与录制器

## 概述

ZL.Protocol 是 IoT 设备的协议配置模型库，提供完整的命令/响应定义、事件定义、仿真标签、协议录制和 JSON 加载能力。从 ZL.Simulator 的 `Simulator.Core.Protocols` 提取并重构而来。

**命名空间**: `ZL.Protocol`（根命名空间）、`ZL.Protocol.Models`（数据模型命名空间）

**依赖**: ZL.Shared（`StringDistance.Similarity`）

**测试覆盖**: 49 个测试，0 失败

---

## 1. 类型一览

### 数据模型（`ZL.Protocol.Models`）

| 类型 | 说明 |
|---|---|
| `ProtocolConfig` | 协议配置根对象 |
| `CommandDefinition` | 命令定义（模板、解析器、校验、状态管理） |
| `EventDefinition` | 事件定义（触发、模板、间隔） |
| `ResponseParserDefinition` | 响应解析器定义 |
| `ReadStrategyDefinition` | 读取策略定义 |
| `ValidationRule` | 校验规则 |
| `ConditionalResponse` | 条件响应 |
| `SimulationTagConfig` | 仿真标签配置（Fixed/Ramp/Sine/Step） |

### 工具类（`ZL.Protocol`）

| 类型 | 说明 |
|---|---|
| `ProtocolRecorder` | 从 JSONL 日志学习协议 |
| `ProtocolConfigLoader` | JSON 解析/文件加载 |

---

## 2. ProtocolConfig — 协议配置根对象

```csharp
public sealed class ProtocolConfig
{
    public string ProtocolName { get; set; }                 // 默认 ""
    public string FrameMode { get; set; }                     // 默认 "Text"
    public string? CheckSum { get; set; }                    // 校验和算法
    public bool AutoAppendCheckSum { get; set; }             // 默认 false
    public string Terminator { get; set; }                   // 默认 "\n"
    public int DefaultTimeoutMs { get; set; }                // 默认 2000
    public int InterCommandWaitMs { get; set; }              // 默认 50
    public string AsyncEventDispatchMode { get; set; }       // 默认 "Broadcast"
    public int JitterMs { get; set; }                       // 默认 0
    public int SimDelayMs { get; set; }                     // 默认 0
    public double SimPacketLossRate { get; set; }           // 默认 0.0
    public double ValueJitter { get; set; }                 // 默认 0.0
    public double ValueNoise { get; set; }                  // 默认 0.0
    public bool IsProtected { get; set; }                   // 默认 false
    public Dictionary<string, CommandDefinition> Commands { get; set; }  // 默认 new Dictionary
    public ReadStrategyDefinition? ReadStrategy { get; set; }  // 默认 null
    public string? UnknownResponse { get; set; }             // 默认 null
    public List<EventDefinition>? Events { get; set; }       // 默认 null
    public Dictionary<string, SimulationTagConfig>? SimulationTags { get; set; }  // 默认 null
}
```

### 2.1 属性说明

| 属性 | 用途 | 示例 |
|---|---|---|
| `ProtocolName` | 协议标识 | `"SCPI_5225A"` |
| `FrameMode` | 帧模式 | `"Text"` / `"Hex"` |
| `Terminator` | 文本终止符 | `"\n"` / `"\r\n"` |
| `CheckSum` | 校验和算法 | `"CRC16"` / `"XOR"` |
| `AutoAppendCheckSum` | 自动附加校验和 | `true` |
| `DefaultTimeoutMs` | 命令超时 | `2000` |
| `JitterMs` | 响应延迟抖动 | `50` |
| `ValueJitter` | 数值抖动比例 | `0.05`（5%） |
| `SimPacketLossRate` | 模拟丢包率 | `0.01`（1%） |

---

## 3. CommandDefinition — 命令定义

```csharp
public sealed class CommandDefinition
{
    public string CommandTemplate { get; set; }             // "VOLT {v}"
    public int WaitAfterMs { get; set; }                    // 默认 0
    public ResponseParserDefinition? Parser { get; set; }
    public ReadStrategyDefinition? ReadStrategy { get; set; }
    public string? MatchPattern { get; set; }              // 正则匹配
    public string? ResponseTemplate { get; set; }          // "{STATE:voltage}"
    public bool? AutoAppendCheckSum { get; set; }
    public string? CheckSum { get; set; }
    public string? SetStateKey { get; set; }               // 状态键
    public int? SetStateGroupIndex { get; set; }
    public List<string>? StateReset { get; set; }          // 状态重置列表
    public bool IsFavorite { get; set; }                   // 默认 false
    public List<string>? DependsOn { get; set; }           // 依赖命令
    public ValidationRule? Validation { get; set; }
    public string? ValidationErrorResponse { get; set; }
    public ConditionalResponse? ConditionalResponse { get; set; }
    public List<string>? Triggers { get; set; }
    public string? ResponseExpression { get; set; }
    public bool? UiCanRead { get; set; }
    public bool? UiCanWrite { get; set; }
}
```

### 3.1 状态管理

```json
{
  "CommandTemplate": "VOLT {v}",
  "SetStateKey": "voltage"
}
```
设置状态变量后，响应中可引用：`"ResponseTemplate": "{STATE:voltage}"`

```json
{
  "CommandTemplate": "*RST",
  "StateReset": ["voltage", "current"]
}
```
重置命令指定要清理的状态变量列表。

### 3.2 校验规则

```json
{
  "Validation": {
    "Type": "float",
    "Min": 0.0,
    "Max": 30.0
  },
  "ValidationErrorResponse": "ERR:RANGE"
}
```

校验规则类型：
- `"int"` / `"float"` — 数值范围校验（`Min`/`Max`）
- `"enum"` — 枚举值校验（`EnumValues`）
- `"string"` — 正则模式校验（`Pattern`）

### 3.3 条件响应

```json
{
  "ConditionalResponse": {
    "Condition": "{v} > 20",
    "IfTrue": "WARN:HIGH_VOLTAGE",
    "IfFalse": "OK"
  }
}
```

### 3.4 依赖与触发

```json
{
  "DependsOn": ["PWR_ON"],  // 先执行 PWR_ON
  "Triggers": ["EVENT_POWER"]  // 触发事件
}
```

---

## 4. 其他模型类型

### 4.1 EventDefinition

```csharp
public sealed class EventDefinition
{
    public string EventId { get; set; }       // 默认 ""
    public string Trigger { get; set; }       // 默认 ""
    public string Template { get; set; }      // 默认 ""
    public int IntervalMs { get; set; }       // 默认 0
    public bool Enabled { get; set; }         // 默认 true
}
```

### 4.2 ResponseParserDefinition

```csharp
public sealed class ResponseParserDefinition
{
    public string Type { get; set; }           // 默认 "None"
    public string? Pattern { get; set; }       // 正则模式
    public int Index { get; set; }             // 捕获组索引
    public string TargetType { get; set; }     // 默认 "String"
}
```

### 4.3 ReadStrategyDefinition

```csharp
public sealed class ReadStrategyDefinition
{
    public string Type { get; set; }           // 默认 "Terminator"
    public string? Terminator { get; set; }    // 终止符
    public int Length { get; set; }            // 固定长度
}
```

### 4.4 ValidationRule

```csharp
public sealed class ValidationRule
{
    public double? Min { get; set; }
    public double? Max { get; set; }
    public List<string>? EnumValues { get; set; }
    public string? Pattern { get; set; }
    public string? Type { get; set; }
}
```

### 4.5 ConditionalResponse

```csharp
public sealed class ConditionalResponse
{
    public string? Condition { get; set; }
    public string? IfTrue { get; set; }
    public string? IfFalse { get; set; }
    public string? Default { get; set; }
}
```

### 4.6 SimulationTagConfig

```csharp
public sealed class SimulationTagConfig
{
    public string Mode { get; set; }                    // 默认 "Fixed"
    public double InitialValue { get; set; }            // 默认 0
    public double? FixedValue { get; set; }             // 默认 null
    public double Min { get; set; }                     // 默认 0
    public double Max { get; set; }                     // 默认 0
    public double RampRate { get; set; }               // 默认 0
    public double Frequency { get; set; }              // 默认 0.1
    public double Amplitude { get; set; }              // 默认 10.0
    public double Offset { get; set; }                 // 默认 50.0
    public int StepDurationMs { get; set; }            // 默认 5000
    public List<double>? StepValues { get; set; }
    public string Format { get; set; }                 // 默认 "0.##"
    public string? Unit { get; set; }
    public int UpdateIntervalMs { get; set; }          // 默认 0
    public bool Enabled { get; set; }                  // 默认 true
}
```

---

## 5. ProtocolRecorder — 协议录制器

```csharp
public sealed class ProtocolRecorder : IDisposable
{
    public ProtocolConfig Analyze(IEnumerable<string> logLines, RecordingOptions options);
    public void Dispose();
}

public sealed class ProtocolRecorder.RecordingOptions
{
    public string ProtocolName { get; set; }           // 默认 "RecordedProtocol"
    public string FrameMode { get; set; }              // 默认 "Text"
    public string Terminator { get; set; }             // 默认 "\n"
    public double SimilarityThreshold { get; set; }    // 默认 0.8
    public bool AutoMergeTemplates { get; set; }       // 默认 true
    public bool InferParameters { get; set; }          // 默认 true
    public bool InferChecksums { get; set; }           // 默认 true
}
```

### 5.1 录制原理

1. **解析 JSONL 日志** — 每行格式：`{"Timestamp":"...","Direction":"TX|RX","SessionId":"...","Payload":"..."}`
2. **TX/RX 配对** — 将发送和响应按时间顺序配对
3. **模板分组** — 使用 `StringDistance.Similarity` 对相似模板分组（阈值默认 0.8）
4. **参数推断** — 从模板中提取 `{param}` 占位符
5. **校验和推断** — 检测并推断校验和算法
6. **生成 ProtocolConfig** — 输出完整的协议配置

### 5.2 输入日志格式

```jsonl
{"Timestamp":"2024-01-01T10:00:00Z","Direction":"TX","SessionId":"sess1","Payload":"VOLT 5"}
{"Timestamp":"2024-01-01T10:00:00.010Z","Direction":"RX","SessionId":"sess1","Payload":"5.000"}
{"Timestamp":"2024-01-01T10:00:01Z","Direction":"TX","SessionId":"sess1","Payload":"VOLT 10"}
{"Timestamp":"2024-01-01T10:00:01.010Z","Direction":"RX","SessionId":"sess1","Payload":"10.000"}
```

---

## 6. ProtocolConfigLoader — 协议加载器

```csharp
public static class ProtocolConfigLoader
{
    public static ProtocolConfig? ParseJson(string json, bool applyDefaults = true);
    public static ProtocolConfig? LoadFromFile(string filePath, bool applyDefaults = true);
    public static ProtocolConfig? LoadFromEmbedded(string resourceName, bool applyDefaults = true);
}
```

### 6.1 方法说明

| 方法 | 说明 |
|---|---|
| `ParseJson(json, applyDefaults)` | 从 JSON 字符串解析。`applyDefaults=true` 时自动填充默认值 |
| `LoadFromFile(filePath, applyDefaults)` | 从文件路径加载，支持绝对路径和相对路径（相对于 `Environment.CurrentDirectory`） |
| `LoadFromEmbedded(resourceName, applyDefaults)` | 从嵌入式资源加载，默认实现返回 null，由派生类或注入实现 |

### 6.2 JSON 示例

```json
{
  "ProtocolName": "MyDevice",
  "FrameMode": "Text",
  "Terminator": "\n",
  "DefaultTimeoutMs": 2000,
  "Commands": {
    "VOL": {
      "CommandTemplate": "VOLT {v}",
      "ResponseTemplate": "{STATE:voltage}",
      "Parser": {
        "Type": "Regex",
        "Pattern": "VOLT\\s+([\\d.]+)",
        "Index": 1,
        "TargetType": "Double"
      }
    },
    "IDN": {
      "CommandTemplate": "*IDN?"
    }
  }
}
```

---

## 7. 测试覆盖

**测试项目**: `tests/ZL.Protocol.Tests/`（49 个测试）

| 测试文件 | 测试数 | 覆盖范围 |
|---|---|---|
| `ProtocolConfigModelTests.cs` | 12 | ProtocolConfig、CommandDefinition、EventDefinition、SimulationTagConfig |
| `ProtocolConfigLoaderTests.cs` | 16 | ParseJson、LoadFromFile、命令/事件/仿真标签/校验/行为控制反序列化 |
| `ProtocolRecorderTests.cs` | 21 | 日志分析、配对、模板合并、帧模式、异常处理 |
