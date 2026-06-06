# ProtocolGateway Samples

[`samples`](src/ProtocolGateway/samples) 目录按“上手难度”和“价值感知”组织，建议按以下顺序体验。

当前 samples 分成三类：

- **最小闭环入门**：先理解统一消息、转换、路由、输出
- **核心价值展示**：直接感受工业桥接与多目标分发
- **CLI 运维体验**：感受 validate / precheck / dry-run / doctor 的配置治理价值

## 1. [`QuickStartInMemory`](src/ProtocolGateway/samples/QuickStartInMemory)

**目标：** 5 分钟内理解 [`Message`](src/ProtocolGateway/src/ProtocolGateway/Message.cs)、[`MessagePipeline`](src/ProtocolGateway/src/ProtocolGateway/Pipeline/MessagePipeline.cs) 与 [`FileOutputPlugin`](src/ProtocolGateway/src/ProtocolGateway/Plugins/Output/FileOutputPlugin.cs) 的最小闭环。

特点：

- 不需要打开端口
- 不依赖外部服务
- 直接展示"统一消息 -> Pipeline Transformer -> 路由 -> 文件落盘"

运行：

```bash
dotnet run --project src/ProtocolGateway/samples/QuickStartInMemory/QuickStartInMemory.csproj
```

**目标：** 体验更贴近现场的桥接能力：原始串口样本先通过 Pipeline Transformer 转换为 HTTP/JSON，再同时发送到 HTTP 端点与本地归档文件。

涉及核心能力：

- [`MessagePipeline.AddTransformer()`](src/ProtocolGateway/src/ProtocolGateway/Pipeline/MessagePipeline.cs:12) CSV→JSON 转换
- [`HttpOutputPlugin`](src/ProtocolGateway/src/ProtocolGateway/Plugins/Output/HttpOutputPlugin.cs:54)
- [`FileOutputPlugin`](src/ProtocolGateway/src/ProtocolGateway/Plugins/Output/FileOutputPlugin.cs:17)
- [`RouteRule`](src/ProtocolGateway/src/ProtocolGateway/Pipeline/MessagePipeline.cs:12) 扇出路由

运行：

```bash
dotnet run --project src/ProtocolGateway/samples/SerialToHttpJsonArchive/SerialToHttpJsonArchive.csproj
```

## 3. [`DemoTcpForwarding`](src/ProtocolGateway/samples/DemoTcpForwarding)

**目标：** 理解最基础的“监听 -> 收包 -> 转发”链路。

适合场景：

- 学习 [`GatewayService`](src/ProtocolGateway/src/ProtocolGateway/GatewayService.cs)
- 学习 [`TcpInputPlugin`](src/ProtocolGateway/src/ProtocolGateway/Plugins/Input/TcpInputPlugin.cs:49)
- 学习最简单的 TCP 转发模型

运行：

```bash
dotnet run --project src/ProtocolGateway/samples/DemoTcpForwarding/DemoTcpForwarding.csproj
```

## 4. [`CliDoctorWalkthrough`](src/ProtocolGateway/samples/CliDoctorWalkthrough)

**目标：** 不写一行业务代码，直接复用 [`ProtocolGateway.Cli`](src/ProtocolGateway/apps/ProtocolGateway.Cli) 体验配置驱动、运行前诊断与聚合摘要能力。

适合场景：

- 学习 [`--validate`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:52)
- 学习 [`--precheck`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:58)
- 学习 [`--dry-run`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:67)
- 学习 [`GatewayCliDoctorRunner.Run()`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:1207) 的诊断闭环

入口文档：

- [`samples/CliDoctorWalkthrough/README.md`](src/ProtocolGateway/samples/CliDoctorWalkthrough/README.md)

## 5. [`SqliteArchiveDemo`](src/ProtocolGateway/samples/SqliteArchiveDemo)

**目标：** 展示 [`SqliteOutputPlugin`](src/ProtocolGateway/src/ProtocolGateway/Plugins/Output/DatabaseOutputPlugin.cs:275) 如何把统一消息直接归档到 SQLite，体现“转发 + 留痕 + 追溯”能力。

适合场景：

- 学习 [`DatabaseOutputPlugin`](src/ProtocolGateway/src/ProtocolGateway/Plugins/Output/DatabaseOutputPlugin.cs:20) 的最小使用方式
- 体验工业数据归档与审计留痕场景
- 验证类库不只支持转发，也支持落库归档

运行：

```bash
dotnet run --project src/ProtocolGateway/samples/SqliteArchiveDemo/SqliteArchiveDemo.csproj
```

## 6. [`ModbusTcpQuickStart`](src/ProtocolGateway/samples/ModbusTcpQuickStart)

**目标：** 展示 [`ModbusTcpOutputPlugin`](src/ProtocolGateway/src/ProtocolGateway/Plugins/Industrial/ModbusTcpOutputPlugin.cs:18) 如何把统一 JSON 写请求转换为工业现场可消费的 Modbus TCP 报文。

适合场景：

- 学习 [`ModbusWriteSupport.ParseWrites()`](src/ProtocolGateway/src/ProtocolGateway/Plugins/Core/ModbusWriteSupport.cs:27) 的地址和值解析方式
- 理解 JSON -> Modbus TCP 写寄存器报文的桥接过程
- 在无真实 PLC 的情况下快速验证工业输出链路

运行：

```bash
dotnet run --project src/ProtocolGateway/samples/ModbusTcpQuickStart/ModbusTcpQuickStart.csproj
```

## 推荐体验顺序

1. 先跑 [`QuickStartInMemory`](src/ProtocolGateway/samples/QuickStartInMemory)
2. 再跑 [`SerialToHttpJsonArchive`](src/ProtocolGateway/samples/SerialToHttpJsonArchive)
3. 然后体验 [`CliDoctorWalkthrough`](src/ProtocolGateway/samples/CliDoctorWalkthrough)
4. 再跑 [`SqliteArchiveDemo`](src/ProtocolGateway/samples/SqliteArchiveDemo)
5. 再跑 [`ModbusTcpQuickStart`](src/ProtocolGateway/samples/ModbusTcpQuickStart)
6. 最后按需查看 [`DemoTcpForwarding`](src/ProtocolGateway/samples/DemoTcpForwarding)

## 当前边界

当前 samples 已覆盖：

- 最小闭环入门
- 协议转换 + 路由分发 + 双输出
- CLI 配置治理与诊断体验
- SQLite 归档与留痕演示
- Modbus TCP 工业输出演示
- 基础 TCP 转发

当前尚未覆盖：

- 暂未提供同时串联 `InputPlugin -> Converter -> ModbusTcpOutputPlugin` 的更完整工业场景样例
