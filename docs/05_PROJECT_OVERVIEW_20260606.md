# ZL.Foundation — 项目总览与架构

## 概述

ZL.Foundation 是 iot-sdk 的基础设施层，由四个协同工作的类库组成，为 IoT 设备的协议通信、透明代理录制、二进制帧解析提供完整的底层支持。这些组件从 ZL.Simulator 项目中提取、重构而来，经过完整测试验证。

---

## 1. 组件一览

ZL.Foundation 包含以下四个类库：

| 项目 | 命名空间 | 职责 | 依赖 | 测试数 |
|---|---|---|---|---|
| **ZL.Protocol** | `ZL.Protocol`, `ZL.Protocol.Models` | 协议配置模型、录制器、加载器 | ZL.Shared | 49 |
| **ZL.Probing** | `ZL.Probing` | 透明代理传输层 | ZL.Framing | 31 |
| **ZL.Framing** | `ZL.Framing` | 帧解析基础设施、传输接口 | 无 | — |
| **ZL.Shared** | `ZL.Shared`, `ZL.Shared.Utils` | 结构化日志、字符串工具 | 无 | — |

---

## 2. 架构分层

```
┌─────────────────────────────────────────────────────┐
│                 应用层 (Application)                  │
│    IoT设备仿真 / 协议转换 / 透明代理捕获              │
└────────────────────────┬────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────┐
│              ZL.Protocol — 协议配置模型               │
│  · ProtocolConfig — 协议配置根对象                    │
│  · CommandDefinition — 命令/响应定义                  │
│  · ProtocolRecorder — 从日志学习协议                   │
│  · ProtocolConfigLoader — JSON 解析/加载              │
└────────────────────────┬────────────────────────────┘
                         │ 依赖
┌────────────────────────▼────────────────────────────┐
│              ZL.Probing — 透明代理                    │
│  · TransparentProxyTransport — 代理传输               │
│  · TransparentProxyConfig — 代理配置                  │
│  · ListenMode — TcpServer/TcpClient/Serial           │
└────────────────────────┬────────────────────────────┘
                         │ 依赖
┌────────────────────────▼────────────────────────────┐
│            ZL.Framing — 帧解析基础设施                │
│  · IByteTransport / ISessionByteTransport 等接口      │
│  · ByteFramingOptions — 帧分割配置                    │
│  · FrameAssembler — 字节流→帧组装                      │
│  · LengthFieldFrameDecoder / FixedLengthFrameDecoder  │
│  · StickyWindowBuffer — 字节缓冲区                    │
└────────────────────────┬────────────────────────────┘
                         │ 依赖
┌────────────────────────▼────────────────────────────┐
│              ZL.Shared — 共享工具                      │
│  · StructuredLog — 基于 Serilog 的日志门面             │
│  · StringDistance — Levenshtein 编辑距离               │
└─────────────────────────────────────────────────────┘
```

---

## 3. 设计原则

### 3.1 职责分离

| 层级 | 职责 | 不负责 |
|---|---|---|
| ZL.Protocol | 定义协议模型、从日志学习协议 | 网络 I/O、帧解析 |
| ZL.Probing | 透明代理流量录制/转发 | 协议语义、帧解析 |
| ZL.Framing | 字节流分割为帧、传输接口定义 | 协议语义、代理逻辑 |
| ZL.Shared | 日志、字符串工具 | 业务逻辑 |

### 3.2 接口驱动

ZL.Framing 定义了四层传输接口，形成能力继承链：

```
IByteTransport (基础: Open/Close/Send/DataReceived)
    ↑
ISessionByteTransport (+ DataReceivedSession)
    ↑
ISessionSendByteTransport (+ Send(data, sessionId))
    ↑
ISessionLifecycleTransport (+ SessionStarted/SessionEnded)
```

这样 `TransparentProxyTransport` 和 `TcpByteTransport` 等实现可以选择实现所需的最小接口集。

### 3.3 配置与实现分离

`ProtocolConfig` 是纯数据模型（POCO），不包含任何 I/O 或业务逻辑。`ProtocolConfigLoader` 负责 JSON 解析，`TransparentProxyConfig` 是透明代理的配置载体。

---

## 4. 核心设计模式

### 4.1 模板方法模式 — 协议命令

```
ProtocolConfig.Commands["CMD_KEY"]
  → CommandDefinition.CommandTemplate
  → 替换 {param} 占位符
  → 发送字节流
  → 等待响应
  → ResponseParser 解析
```

### 4.2 策略模式 — 帧分割

```
ByteFramingOptions.Strategy:
  · "Timeout" — 超时分割（文本协议）
  · "FixedLength" — 固定长度（二进制定长）
  · "LengthField" — 长度字段（二进制变长）
  · "LengthFieldWithChecksum" — 带校验和
```

### 4.3 观察者模式 — 事件驱动

```
TransparentProxyTransport:
  DataReceived + DataReceivedSession — 数据到达
  SessionStarted / SessionEnded — 会话生命周期
  FrameStatusChanged — 帧状态变化

IByteTransport:
  DataReceived — 数据到达
  FrameStatusChanged — 帧状态
```

### 4.4 工厂模式 — 协议录制

```
ProtocolRecorder.Analyze(logLines, options)
  → 解析 JSONL 日志
  → TX/RX 配对
  → 模板分组（Similarity 阈值）
  → 参数推断
  → 生成 ProtocolConfig
```

---

## 5. 关键类型速查

### ProtocolConfig（协议配置根对象）

```csharp
public sealed class ProtocolConfig
{
    public string ProtocolName { get; set; }               // 协议名称
    public string FrameMode { get; set; }                   // "Text" / "Hex"
    public string? CheckSum { get; set; }                   // 校验和算法
    public bool AutoAppendCheckSum { get; set; }            // 自动附加校验和
    public string Terminator { get; set; }                  // 文本终止符
    public int DefaultTimeoutMs { get; set; }               // 默认超时
    public int InterCommandWaitMs { get; set; }             // 命令间隔
    public Dictionary<string, CommandDefinition> Commands { get; set; }
    public ReadStrategyDefinition? ReadStrategy { get; set; }
    public List<EventDefinition>? Events { get; set; }
    public Dictionary<string, SimulationTagConfig>? SimulationTags { get; set; }
}
```

### CommandDefinition（命令定义）

```csharp
public sealed class CommandDefinition
{
    public string CommandTemplate { get; set; }             // "VOLT {v}"
    public ResponseParserDefinition? Parser { get; set; }
    public string? ResponseTemplate { get; set; }          // "{STATE:voltage}"
    public string? MatchPattern { get; set; }              // 正则匹配
    public ValidationRule? Validation { get; set; }        // 校验规则
    public ConditionalResponse? ConditionalResponse { get; set; }
    public List<string>? DependsOn { get; set; }           // 依赖命令
    public string? SetStateKey { get; set; }               // 状态键
    public List<string>? StateReset { get; set; }          // 状态重置
}
```

### TransparentProxyTransport（透明代理）

```csharp
public sealed class TransparentProxyTransport
    : IByteTransport, ISessionByteTransport,
      ISessionSendByteTransport, ISessionLifecycleTransport, IDisposable
{
    public TransparentProxyTransport(TransparentProxyConfig config, IByteTransport targetTransport);
    
    public string ResourceName { get; }
    public bool IsOpen { get; }
    
    public event Action<byte[]?>? DataReceived;
    public event Action<byte[]?, string>? DataReceivedSession;
    public event Action<FrameStatus>? FrameStatusChanged;
    public event Action<string>? SessionStarted;
    public event Action<string>? SessionEnded;
    
    public void Open();
    public void Close();
    public void Dispose();
    public void Send(byte[]? data);
    public void Send(byte[]? data, string sessionId);
}
```

### FrameAssembler（帧组装器）

```csharp
public sealed class FrameAssembler : IDisposable
{
    public FrameAssembler(ByteFramingOptions options, int timeoutMs, 
        Action<byte[], FrameAssembleMode> onFrame);
    
    public void Append(byte[] data);
    public void Stop();
    public void Dispose();
}
```

---

## 6. 测试覆盖

ZL.Foundation 通过两个独立的 xUnit 测试项目保证质量：

| 测试项目 | 测试数 | 覆盖范围 |
|---|---|---|
| ZL.Protocol.Tests | 49 | 模型验证、JSON 解析、文件加载、协议录制、帧模式配对 |
| ZL.Probing.Tests | 31 | 配置默认值、三种监听模式、Send 语义、SnifferOnly、事件注册 |

**总计：80 个测试，0 失败，0 跳过。**

---

## 7. 目标读者

- **IoT 设备仿真开发者**: 使用 `ProtocolConfig` + `CommandDefinition` 定义设备协议
- **协议逆向工程师**: 使用 `ProtocolRecorder` 从抓包日志学习协议
- **网络工程师**: 使用 `TransparentProxyTransport` 录制/转发设备流量
- **二进制协议开发者**: 使用 `FrameAssembler` + `ByteFramingOptions` 解析自定义协议

---

## 8. 总结

ZL.Foundation 是 iot-sdk 的基础设施层，提供从协议建模到帧解析的完整能力栈。四个组件职责清晰、依赖方向单一、测试覆盖完整，为上层 IoT 应用提供了稳定可靠的底层支撑。
