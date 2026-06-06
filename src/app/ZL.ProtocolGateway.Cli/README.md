# ProtocolGateway.Cli

无头协议转换体验宿主，复用 `GatewayService + MessagePipeline + converters/plugins`，当前既可做场景演示，也可承担启动前校验与预检入口。

- **交互式菜单**：选择常见协议转换场景并填写参数
- **JSON 配置文件**：快速复现实验与演示
- **validate**：只做配置标准化与合法性校验
- **precheck**：输出运行前阻断项、风险项与基础环境检查
- **dry-run / save-config**：先预览最终配置，再决定是否真正启动无头网关

## 快速开始

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj
```

列出场景：

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- --list-scenarios
```

直接跑串口文本 -> HTTP JSON 模拟：

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --scenario serial-to-http \
  --payload "SN123,10.5,20.3,15.7" \
  --station-no ST-01 \
  --target-url https://example.com/api/upload
```

通过 JSON 文件启动：

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --config src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/serial-to-http.json
```

只做预览，不实际启动：

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --config src/ProtocolGateway/apps/ProtocolGateway.Cli/examples/json-transform-file.json \
  --dry-run
```

导出标准化配置：

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --scenario serial-to-http \
  --payload "SN123,10.5,20.3" \
  --station-no ST-01 \
  --target-url https://example.com/api/upload \
  --save-config ./gateway-export.json \
  --dry-run
```

仅做配置校验：

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --scenario text-forward \
  --payload "D100=42" \
  --validate
```

执行运行前预检：

```bash
dotnet run --project src/ProtocolGateway/apps/ProtocolGateway.Cli/ProtocolGateway.Cli.csproj -- \
  --scenario serial-to-http \
  --payload "SN123,10.5,20.3" \
  --station-no ST-01 \
  --target-url https://example.com/api/upload \
  --precheck
```

## 支持的体验场景

1. **serial-to-http**：串口文本 -> HTTP JSON
2. **json-transform**：JSON 字段映射 -> JSON
3. **text-forward**：文本透传 -> 控制台/文件
4. **serial-to-http-file**：串口文本 -> HTTP JSON -> 文件归档
5. **json-transform-file**：JSON 字段映射 -> 文件归档
6. **text-forward-file**：文本透传 -> 文件归档

## 参数优先级

1. 先加载 `--config` 指定的 JSON
2. 再应用 CLI 覆盖参数（如 `--payload`、`--target-url`、`--mapping`、`--output-file`）
3. 若使用 `--save-config`，导出的是**最终生效配置**
4. 若使用 `--validate`，仅做配置合法性校验，不执行预检与启动
5. 若使用 `--precheck`，执行运行前检查并输出报告，不启动 `GatewayService`
6. 若同时指定 `--dry-run`，只预览，不真正启动 `GatewayService`

## 运行模式说明

### [`--validate`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:52)

适用场景：

- CI 中校验样例配置是否合法
- 调整 CLI 参数后先确认标准化结果可通过校验
- 在正式联调前先排除 `scenario` / `contentType` / `source.mode` / `output.mode` 等字段错误

当前覆盖的基础检查包括：

- 场景 ID 是否存在
- `source.mode` / `output.mode` 是否受支持
- `contentType` 是否受支持
- inline / file 模式的必要字段是否完整
- 场景特定参数是否齐全

### [`--precheck`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:58)

适用场景：

- 启动前检查输入文件是否存在
- 检查文件输出目录是否存在或将被自动创建
- 检查 `serial-to-http` 场景的目标 URL 是否是合法 HTTP/HTTPS 地址
- 在真实启动前输出阻断项与 warning，减少现场试错成本

当前预检报告分为：

- `INFO`：已满足的前置条件
- `WARN`：非阻断风险，如输出目录当前不存在但运行时可尝试创建
- `ERROR`：阻断项，如输入文件缺失、目标 URL 非法

### [`--dry-run`](src/ProtocolGateway/apps/ProtocolGateway.Cli/GatewayCliApp.cs:67)

适用场景：

- 预览最终生效 JSON 配置
- 保存标准化配置并复核内容
- 在不触发真实输出的前提下展示链路摘要

## JSON 配置结构

```json
{
  "schemaVersion": "protocol-gateway-cli/v1",
  "scenario": "serial-to-http",
  "source": {
    "mode": "inline",
    "protocol": "Serial",
    "contentType": "text",
    "topic": "serial/input",
    "payload": "SN123,10.5,20.3"
  },
  "output": {
    "mode": "console"
  },
  "serialToHttp": {
    "stationNo": "ST-01",
    "targetUrl": "https://example.com/api/upload"
  }
}
```

建议优先保留 `schemaVersion`，后续 CLI 增量升级时可据此做兼容迁移。
