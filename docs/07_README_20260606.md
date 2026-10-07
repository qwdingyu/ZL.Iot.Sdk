# ZL.Foundation 文档索引

本目录包含 ZL.Foundation 基础设施层的完整文档。

---

## 核心文档

| 文档 | 说明 |
|---|---|
| [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md) | 项目总览与架构 — 组件一览、分层架构图、设计原则、核心类型速查 |
| [DEPENDENCY_GRAPH.md](DEPENDENCY_GRAPH.md) | 依赖关系图 — Mermaid 图表、各组件依赖详情、与 ZL.Simulator 的关系 |
| [USAGE_EXAMPLE.md](USAGE_EXAMPLE.md) | 使用示例 — 端到端示例：协议录制、透明代理、帧解析、字节缓冲区 |
| [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md) | 迁移指南 — ZL.Simulator 到 iot-sdk 的组件映射、接口一致性验证、迁移步骤 |

---

## 组件文档

| 文档 | 说明 |
|---|---|
| [ZL.Protocol.md](ZL.Protocol.md) | 协议配置模型 API — ProtocolConfig、CommandDefinition、ProtocolRecorder、ProtocolConfigLoader |
| [ZL.Probing.md](ZL.Probing.md) | 透明代理传输层 API — TransparentProxyTransport、TransparentProxyConfig、ListenMode |
| [ZL.Framing.md](ZL.Framing.md) | 帧解析基础设施 — ByteFramingOptions、FrameAssembler、传输接口、帧解码器 |
| [ZL.Shared.md](ZL.Shared.md) | 共享工具库 — StructuredLog、StringDistance |

---

## 架构图

```
ZL.Protocol ──→ 协议配置 / 录制 / 加载
ZL.Probing  ──→ 透明代理传输 / 流量录制
ZL.Framing  ──→ 帧解析 / 传输接口
ZL.Shared   ──→ 结构化日志 / 字符串工具

依赖链: ZL.Shared ← ZL.Protocol
        ZL.Framing ← ZL.Probing
```
