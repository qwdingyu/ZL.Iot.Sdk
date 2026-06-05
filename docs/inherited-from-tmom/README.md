# Inherited from tmom (历史继承代码)

## 背景

iot-sdk 在拆分独立前曾是 tmom 仓库的子目录。
本目录保存了 iot-sdk 拆分后, tmom 端继续迭代的旧版本代码,
作为**代码考古和功能对比参考**。

## 为什么不直接合并到 iot-sdk?

| 原因 | 说明 |
|------|------|
| **API 演进不同** | tmom 端用 `BuildPublishOptions/PackageManifest/ScaffoldOptions`, iot-sdk 用 `GenerateJob/GenerateRequest/GenerateResult`, 类名不同 |
| **架构方向不同** | tmom 端是基于 Pipeline/Orchestrator 的流式架构; iot-sdk 是基于 Job 队列的批处理架构 |
| **测试覆盖不同** | iot-sdk 版本经过完整测试, tmom 旧版未经集成测试 |
| **NuGet 兼容性** | iot-sdk 已发布 v1.0.0 NuGet 包, 不能随便改 API 破坏外部用户 |

## 三个项目的状态对照

| 项目 | iot-sdk 位置 | tmom 位置 (历史) | 处理 |
|------|--------------|------------------|------|
| ZL.Iot.Runner.Generator | `src/application/ZL.Iot.Runner.Generator/` | `tmom/ZL.Iot.Runner.Generator/` (已删除) | ✅ 保留 iot-sdk 版本, 历史代码存档 |
| ZL.Iot.Runner.GeneratorCli | `src/app/ZL.Iot.Runner.GeneratorCli/` | `tmom/ZL.Iot.Runner.GeneratorCli/` (已删除) | ✅ 保留 iot-sdk 版本 |
| ZL.Iot.Runner.Templates | `src/application/ZL.Iot.Runner.Templates/` | `tmom/ZL.Iot.Runner.Templates/` (已删除) | ✅ 保留 iot-sdk 版本 |

## 后续决策

如需将 tmom 端的某些功能合并到 iot-sdk, 必须:
1. 先在新分支 `feat/merge-from-tmom` 上工作
2. 完整的代码审查 + 单元测试
3. 增量合并 (按文件/按类), 而不是整体替换
4. 通过 PR 评审后合入主分支

## 文件清单

```
inherited-from-tmom/
├── ZL.Iot.Runner.Generator/
│   ├── Core/Models/
│   │   ├── BuildPublishOptions.cs    # 构建/发布配置
│   │   ├── PackageManifest.cs         # 包清单
│   │   └── ScaffoldOptions.cs         # 脚手架配置
│   ├── Core/Services/
│   │   ├── BuildEngine.cs             # 构建引擎
│   │   ├── PackageBuilder.cs          # 打包器
│   │   ├── PipelineOrchestrator.cs    # 管道编排器
│   │   └── ScaffoldEngine.cs          # 脚手架引擎
│   └── ZL.Iot.Runner.Generator.csproj
├── ZL.Iot.Runner.GeneratorCli/
│   ├── Program.cs                     # 旧版 CLI 入口 (127 行)
│   └── ZL.Iot.Runner.GeneratorCli.csproj
└── console/                            # 旧版 console 模板 (5 个)
    ├── NuGet.config.scriban
    ├── Program.cs.scriban
    ├── RunnerApp.csproj.scriban
    ├── appsettings.json.scriban
    └── runner.config.json.scriban
```

**存档日期**: 2026-06-05
**原因**: iot-sdk 独立仓库后, 5 层架构迁移 + NuGet 发布 v1.0.0, 清理 tmom 端重复项目
