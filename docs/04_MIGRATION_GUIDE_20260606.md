# ZL.Simulator 迁移指南

本文档说明如何将 ZL.Simulator 中的可复用组件迁移到 iot-sdk 的 ZL.Foundation 栈。

---

## 1. 迁移策略总览

ZL.Simulator 和 iot-sdk 中存在多份重复代码。本次提取工作的核心目标：

1. 将 ZL.Simulator 中有价值的、可重用的内容提取到 iot-sdk 的 `src/foundation/` 下
2. 保持 iot-sdk 原有项目名称和命名不变
3. 新提取的组件以 `ZL.*` 前缀命名，形成 Foundation 栈

**核心原则：iot-sdk 为主，ZL.Simulator 为辅。提取后两个仓库的接口保持一致。**

---

## 2. 组件迁移映射表

| ZL.Simulator 原始位置 | iot-sdk 目标位置 | 说明 |
|---|---|---|
| `Simulator.Core/Protocols/ProtocolConfig.cs` | `src/foundation/ZL.Protocol/Models/ProtocolConfig.cs` | 协议配置模型（全新 ZL.Protocol 项目） |
| `Simulator.Core/Protocols/ProtocolRecorder.cs` | `src/foundation/ZL.Protocol/ProtocolRecorder.cs` | 协议录制器 |
| `Simulator.Core/Protocols/ProtocolConfigLoader.cs` | `src/foundation/ZL.Protocol/ProtocolConfigLoader.cs` | 协议加载器 |
| `Simulator.Instruments/Probing/TransparentProxyTransport.cs` | `src/foundation/ZL.Probing/TransparentProxyTransport.cs` | 透明代理传输层（全新 ZL.Probing 项目） |
| `ZL.Framing/Core/ByteFramingOptions.cs` | `src/foundation/ZL.Framing/Core/ByteFramingOptions.cs` | 帧解析选项（已有） |
| `ZL.Framing/Core/FrameAssembler.cs` | `src/foundation/ZL.Framing/Core/FrameAssembler.cs` | 帧组装器（已有） |
| `ZL.Framing/Core/IByteTransport.cs` | `src/foundation/ZL.Framing/Core/IByteTransport.cs` | 传输接口（已有） |
| `ZL.Shared/Utils/StringDistance.cs` | `src/foundation/ZL.Shared/Utils/StringDistance.cs` | 字符串距离工具 |
| `Simulator.Core/Transports/TcpByteTransport.cs` | — | 暂不迁移（属于 Simulator 核心） |
| `Simulator.Core/Transports/SerialByteTransport.cs` | — | 暂不迁移（属于 Simulator 核心） |

---

## 3. 已完成迁移

### 3.1 ZL.Protocol — 协议配置模型

- **源文件**: ZL.Simulator 的 `Simulator.Core/Protocols/`
- **目标**: `iot-sdk/src/foundation/ZL.Protocol/`
- **包含类型**:
  - `ProtocolConfig` — 协议配置根对象
  - `CommandDefinition` — 命令定义
  - `EventDefinition` — 事件定义
  - `ResponseParserDefinition` — 响应解析器
  - `ReadStrategyDefinition` — 读取策略
  - `ValidationRule` — 校验规则
  - `ConditionalResponse` — 条件响应
  - `SimulationTagConfig` — 仿真标签配置
  - `ProtocolRecorder` — 从日志学习协议
  - `ProtocolConfigLoader` — JSON 解析/加载
- **依赖**: ZL.Shared（`StringDistance.Similarity`）
- **测试**: 49 个测试全部通过

### 3.2 ZL.Probing — 透明代理传输层

- **源文件**: ZL.Simulator 的 `Simulator.Instruments/Probing/TransparentProxyTransport.cs`
- **目标**: `iot-sdk/src/foundation/ZL.Probing/`
- **包含类型**:
  - `TransparentProxyTransport` — 透明代理传输
  - `TransparentProxyConfig` — 代理配置
  - `ListenMode` 枚举 — TcpServer/TcpClient/Serial
- **依赖**: ZL.Framing
- **测试**: 31 个测试全部通过

### 3.3 ZL.Framing — 帧解析基础设施（已存在）

- **源文件**: ZL.Simulator 的 `ZL.Framing/`
- **状态**: 在 iot-sdk 中已存在，无需额外迁移
- **一致性验证**: iot-sdk 与 ZL.Simulator 的 `ByteFramingOptions` 和 `FrameAssembler` **完全一致**

### 3.4 ZL.Shared — 共享工具库（已存在）

- **源文件**: ZL.Simulator 的 `ZL.Shared/`
- **状态**: 在 iot-sdk 中已存在，补充了 `StringDistance.cs`
- **新增类型**: `StringDistance`（Levenshtein 编辑距离）

---

## 4. 接口一致性验证

以下接口在两个仓库中的签名**完全一致**：

| 接口 | ZL.Simulator 位置 | iot-sdk 位置 |
|---|---|---|
| `IByteTransport` | `Simulator.Core/Transports/IByteTransport.cs` | `ZL.Framing/Core/IByteTransport.cs` |
| `ISessionByteTransport` | `Simulator.Core/Transports/ISessionByteTransport.cs` | `ZL.Framing/Core/ISessionByteTransport.cs` |
| `ISessionSendByteTransport` | `Simulator.Core/Transports/ISessionSendByteTransport.cs` | `ZL.Framing/Core/ISessionSendByteTransport.cs` |
| `ISessionLifecycleTransport` | `Simulator.Core/Transports/ISessionLifecycleTransport.cs` | `ZL.Framing/Core/ISessionLifecycleTransport.cs` |

**结论**: iot-sdk 中的传输接口可以直接被 ZL.Simulator 引用，无需适配层。

---

## 5. 命名空间差异与迁移

### 5.1 关键差异

| 组件 | ZL.Simulator 命名空间 | iot-sdk 命名空间 |
|---|---|---|
| ProtocolConfig | `Simulator.Core.Protocols` | `ZL.Protocol.Models` |
| ProtocolRecorder | `Simulator.Core.Protocols` | `ZL.Protocol` |
| TransparentProxyTransport | `Simulator.Instruments.Probing` | `ZL.Probing` |
| ByteFramingOptions | `ZL.Framing` | `ZL.Framing`（一致） |
| FrameAssembler | `ZL.Framing` | `ZL.Framing`（一致） |
| StringDistance | `ZL.Shared.Utils` | `ZL.Shared.Utils`（一致） |

### 5.2 迁移建议

ZL.Simulator 中可以逐步将 `using Simulator.Core.Protocols;` 替换为 `using ZL.Protocol.Models;`，最终删除本地副本。

---

## 6. ZL.Simulator 使用 iot-sdk Foundation 的步骤

如果要将 ZL.Simulator 改为引用 iot-sdk 的 Foundation 组件：

### 步骤 1: 添加项目引用

在 ZL.Simulator 的 `.csproj` 中添加：

```xml
<ProjectReference Include="../iot-sdk/src/foundation/ZL.Protocol/ZL.Protocol.csproj" />
<ProjectReference Include="../iot-sdk/src/foundation/ZL.Probing/ZL.Probing.csproj" />
<ProjectReference Include="../iot-sdk/src/foundation/ZL.Framing/ZL.Framing.csproj" />
<ProjectReference Include="../iot-sdk/src/foundation/ZL.Shared/ZL.Shared.csproj" />
```

### 步骤 2: 更新 using 指令

```diff
- using Simulator.Core.Protocols;
+ using ZL.Protocol.Models;
+ using ZL.Protocol;

- using Simulator.Instruments.Probing;
+ using ZL.Probing;
```

### 步骤 3: 删除本地副本

确认引用正确后，删除 ZL.Simulator 中的重复文件：

```bash
# 可以保留但标记为 deprecated，或直接删除
rm Simulator.Core/Protocols/ProtocolConfig.cs
rm Simulator.Core/Protocols/ProtocolRecorder.cs
rm Simulator.Instruments/Probing/TransparentProxyTransport.cs
```

### 步骤 4: 编译验证

```bash
dotnet build ZL.Simulator.sln
```

---

## 7. 其他项目迁移建议

### 7.1 iot-sdk 中的其他项目

如果 iot-sdk 的其他项目也需要使用 Foundation 组件：

```xml
<!-- 在需要的 .csproj 中添加 -->
<ProjectReference Include="../foundation/ZL.Protocol/ZL.Protocol.csproj" />
<ProjectReference Include="../foundation/ZL.Probing/ZL.Probing.csproj" />
```

### 7.2 渐进式迁移

不建议一次性替换所有引用。推荐策略：

1. **第一阶段**: 两个仓库各自保留副本，并行运行（当前状态）
2. **第二阶段**: 选择 1-2 个 ZL.Simulator 的使用点，改为引用 iot-sdk 的 Foundation
3. **第三阶段**: 验证无回归后，删除 ZL.Simulator 中的副本

---

## 8. 常见问题

### Q1: 迁移后如果 iot-sdk 的 Foundation 有 bug 怎么办？

**A**: 当前两个仓库的副本是一致的。如果发现 bug，先在 iot-sdk 中修复，测试通过后同步到 ZL.Simulator。

### Q2: ProtocolConfig 的字段在两个仓库中有差异怎么办？

**A**: iot-sdk 的 `ZL.Protocol.Models.ProtocolConfig` 包含更多高级字段（`ReadStrategy`、`Events`、`SimulationTags` 等）。迁移时应以 iot-sdk 版本为准，ZL.Simulator 的副本应同步这些字段。

### Q3: TransparentProxyTransport 在两个仓库中是否完全一致？

**A**: 接口签名完全一致（实现了相同的 4 个传输接口）。`ByteFramingOptions` 和 `FrameAssembler` 也完全一致。可以安全替换。

### Q4: 需要保留 ZL.Simulator 的副本吗？

**A**: 建议保留一段时间作为过渡。可以在副本文件顶部添加 `// DEPRECATED: 使用 ZL.Protocol / ZL.Probing 代替` 注释，标记为已弃用。

---

## 9. 总结

| 迁移项 | 状态 | 备注 |
|---|---|---|
| ZL.Protocol | 已完成 | 49 个测试通过 |
| ZL.Probing | 已完成 | 31 个测试通过 |
| ZL.Framing | 已有，无需迁移 | 接口一致 |
| ZL.Shared | 已有，已补充 StringDistance | 一致 |
| ZL.Simulator 引用 iot-sdk | 待实施 | 按 4 步迁移 |
| 删除 ZL.Simulator 副本 | 可选 | 建议标记 deprecated 后删除 |
