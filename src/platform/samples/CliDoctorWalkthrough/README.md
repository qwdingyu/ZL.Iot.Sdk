# CLI Doctor Walkthrough

这个 sample 不新增代码工程，而是直接复用 [`ProtocolGateway.Cli`](src/ProtocolGateway/apps/ProtocolGateway.Cli) 现有能力，让使用者快速体验：

- [`--validate`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:52)
- [`--precheck`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:58)
- [`--dry-run`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:67)
- `--doctor`

它适合用来感受这个类库在“真正启动前就能发现配置问题、输出诊断摘要、降低现场试错成本”方面的价值。

## 推荐体验顺序

### 1. 先做纯校验

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --scenario text-forward \
  --payload "D100=42" \
  --validate
```

你会看到配置标准化和字段合法性校验结果。

### 2. 再做运行前预检

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --config src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/text-forward-file.json \
  --precheck
```

你会看到：

- 输入/输出路径检查
- 文件输出目录检查
- 风险项与阻断项分层

### 3. 查看最终生效配置

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --config src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/json-transform-file.json \
  --dry-run
```

这个步骤能让使用者理解“配置驱动”是如何落到统一运行模型上的。

### 4. 最后执行 doctor 聚合诊断

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --config src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/serial-to-http.json \
  --doctor
```

你会看到聚合后的：

- validate 结果
- precheck 结果
- 摘要统计
- 建议动作

## 这个 sample 想传达什么

如果只看 [`DemoTcpForwarding`](src/ProtocolGateway/samples/DemoTcpForwarding)，容易把这个项目理解成“普通 TCP 转发器”。

而这个 walkthrough 想强调的是：

1. [`ProtocolGateway.Cli`](src/ProtocolGateway/apps/ProtocolGateway.Cli) 不只是启动器，还是配置治理与诊断入口
2. [`GatewayCliDoctorRunner.Run()`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:1207) 能把 validate + precheck 聚合成稳定的诊断报告
3. [`GatewayCliPrecheckRunner.Run()`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:793) 能在真正运行前先暴露高频风险
4. 这类能力更贴近工业现场交付，而不只是 demo 代码展示

## 配套样例文件

- [`serial-to-http.json`](src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/serial-to-http.json)
- [`json-transform-file.json`](src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/json-transform-file.json)
- [`text-forward-file.json`](src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/text-forward-file.json)
