# ZL.Shared — 共享工具库

## 概述

ZL.Shared 是轻量级的共享工具库，为其他 ZL.* 组件提供基础支撑。当前包含两个核心模块：

1. **结构化日志基础设施**（`StructuredLog` + `LogBootstrapOptions` + `LogEventMetadata`）
2. **字符串距离计算工具**（`StringDistance` — Levenshtein 编辑距离）

虽然从 iot-sdk 的视角看 ZL.Shared 很小，但它提供了 ZL.Protocol 和 ZL.Probing 等上层组件的共同依赖基础。

---

## 1. 命名空间与类型一览

| 命名空间 | 类型 | 说明 |
|---|---|---|
| `ZL.Shared` | `StructuredLog` | 基于 Serilog 的结构化日志门面 |
| `ZL.Shared` | `LogBootstrapOptions` | 日志初始化配置 |
| `ZL.Shared` | `LogEventMetadata` | 日志事件元数据 |
| `ZL.Shared.Utils` | `StringDistance` | 字符串编辑距离计算工具 |

---

## 2. StructuredLog — 结构化日志

### 2.1 类型签名

```csharp
public static class StructuredLog
{
    public static void Initialize(LogBootstrapOptions? options = null);
    public static void Write(LogEvent payload, LogEventMetadata metadata);
    public static void Write(LogEventMetadata metadata, LogEventLevel level, 
        string messageTemplate, params object[] args);
    public static void Shutdown();
}
```

### 2.2 方法说明

| 方法 | 说明 |
|---|---|
| `Initialize(options)` | 初始化日志系统。`options` 可指定 `BaseDirectory`、`AppNameOverride`、`EnableConsole` |
| `Write(payload, metadata)` | 写入预构造的 `LogEvent`，附带元数据 |
| `Write(metadata, level, template, args)` | 便捷方法：按模板格式化消息并写入 |
| `Shutdown()` | 优雅关闭日志系统，刷新缓冲区 |

### 2.3 LogBootstrapOptions 配置

```csharp
public sealed class LogBootstrapOptions
{
    public static LogBootstrapOptions Default { get; }
    public string? BaseDirectory { get; set; }
    public string? AppNameOverride { get; set; }
    public bool EnableConsole { get; set; }
}
```

### 2.4 LogEventMetadata 元数据

```csharp
public sealed class LogEventMetadata
{
    public string Instance { get; set; }           // 默认 ""
    public string Direction { get; set; }          // 默认 "SYS"
    public string Payload { get; set; }            // 默认 ""
    public string SessionId { get; set; }          // 默认 ""
}
```

---

## 3. StringDistance — 字符串编辑距离

### 3.1 类型签名

```csharp
public static class StringDistance
{
    /// <summary>
    /// 计算两个字符串之间的 Levenshtein 编辑距离。
    /// 返回需要最少的前插入、删除、替换操作次数，将 s 转换为 t。
    /// </summary>
    public static int Levenshtein(string s, string t);

    /// <summary>
    /// 计算两个字符串的相似度，返回值范围 [0.0, 1.0]。
    /// 1.0 表示完全相同，0.0 表示完全不同。
    /// 计算公式：1.0 - distance / maxLength(s, t)
    /// </summary>
    public static double Similarity(string s, string t);
}
```

### 3.2 使用示例

```csharp
using ZL.Shared.Utils;

// 编辑距离
int distance = StringDistance.Levenshtein("kitten", "sitting");
// distance == 3

// 相似度
double sim = StringDistance.Similarity("hello", "hello");
// sim == 1.0

double sim2 = StringDistance.Similarity("VOLT", "VOLTS");
// sim2 约 0.8
```

### 3.3 应用场景

- **ProtocolRecorder** 中使用 `Similarity` 对文本模板进行相似度分组，自动合并等效命令
- 协议日志分析中匹配相似模板
- 通用字符串模糊匹配场景

---

## 4. 与其他组件的关系

```
ZL.Protocol (依赖 ZL.Shared)
  └─ ProtocolRecorder.Learn() 使用 StringDistance.Similarity
```

ZL.Shared 是整个 ZL.Foundation 栈中最底层的基础库，不依赖任何其他 ZL.* 组件。

---

## 5. 总结

| 特性 | 说明 |
|---|---|
| 定位 | 最底层共享基础设施 |
| 依赖 | 无项目依赖 |
| 被依赖 | ZL.Protocol |
| 核心功能 | 结构化日志门面、字符串编辑距离 |
| 设计原则 | 极简、无副作用、纯静态工具类 |
