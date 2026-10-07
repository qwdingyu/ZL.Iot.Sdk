# NuGet 包管理全局方案

> **状态**: 本文保留为历史包管理说明。当前强制执行的引用边界规范以 `docs/依赖引用边界规范_20260618.md` 为准。
> 如果本文与引用边界规范冲突，按引用边界规范执行。

> **版本**: v2.0 | **最后更新**: 2026-06-29 | **适用范围**: iot-sdk 及所有下游消费者项目

---

## 一、架构总览

```
┌─────────────────────────────────────────────────────────────────┐
│                    全局 NuGet 配置（唯一权威源）                   │
│                                                                  │
│  ~/.nuget/NuGet/NuGet.Config                                    │
│   └── nuget.org (https://api.nuget.org/v3/index.json)           │
│        └── ZL IoT SDK 23 个包 (v2.2.1+)                        │
│            包括: ZL.Collections ~ ZL.EdgeService, ProtocolGateway │
│                  ProtocolGateway.Scripting, ZL.Iot.Controls     │
│                                                                  │
│  ⚠️ 已废弃：local-feed (/Users/dingyuwang/.nuget/local-feed/)  │
│     当前不再使用，保留目录仅用于历史包查询，不参与正常还原。      │
└────────────────────────┬────────────────────────────────────────┘
                         │ 继承（无 <clear/>，无项目级包源覆盖）
         ┌───────────────┼───────────────┬──────────────┬──────────┐
         ▼               ▼               ▼              ▼          ▼
    iot-sdk           tmom       UseThink.Iot/api  ZL.PlcSimulator  ZL.Simulator
    (SDK 本体)       (消费者)        (消费者)        (消费者)       (消费者)
```

### 设计原则

| 原则 | 说明 |
|------|------|
| **单一配置源** | 所有包源定义在 `~/.nuget/NuGet/NuGet.Config`，项目级 config 不重复定义 |
| **nuget.org 唯一源** | 当前所有 ZL 包统一发布到 nuget.org，不再维护本地 feed |
| **CPM 精确锁定** | 消费者使用 `Directory.Packages.props` 精确指定版本号，杜绝版本漂移 |
| **GitHub Actions 发布** | 所有包通过 GitHub Actions 自动构建、打包、推送，不手动推送 |

---

## 二、配置详解

### 2.1 全局配置（核心）

**文件**: `~/.nuget/NuGet/NuGet.Config`（即 `/Users/dingyuwang/.nuget/NuGet/NuGet.Config`）

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

**关键要求**：
- 使用**绝对路径**（NuGet 不展开 `~` 符号）
- 不再配置 `local-feed`
- 不要加 `<clear/>`（与 MSBuild 用户配置合并）

### 2.2 项目级 NuGet.config

所有下游项目（tmom、UseThink.Iot/api、ZL.PlcSimulator）的 `NuGet.config` 应精简为：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <!-- 继承全局 NuGet 配置 (~/.nuget/NuGet/NuGet.Config)：
       nuget.org -->
</configuration>
```

**为什么保留文件而不是删除？**
- 标记"此项目有意继承全局配置"，避免新开发者误以为配置缺失
- 为将来某个项目需要私有源时预留扩展点
- **绝不包含** `<clear/>` 或重复的 `<packageSources>`

---

## 三、日常工作流

### 3.1 发布新 ZL 包版本

当前发布链路为 **GitHub Actions** 自动处理：

```
代码 Push → GitHub Actions → dotnet pack → 推送 NuGet.org
```

**版本号规则**：
- 所有 ZL 包统一版本号，当前固定为 `2.2.1`
- 不允许随意提升版本，由发布流程统一管理
- 版本变更必须通过 PR + CI 流程

**如需触发发布**：
```bash
# 1. 修改 Directory.Packages.props 中的版本号
# 2. 提交并 Push 到 main 分支
git add Directory.Packages.props
git commit -m "chore: 统一升级 ZL 包版本到 2.2.2"
git push origin main

# 3. GitHub Actions 自动构建并发布到 nuget.org
```

### 3.2 消费者更新到最新版本

```bash
# 方案 A: 全量同步（所有包同一版本）
cd /Users/dingyuwang/0-X/iot-sdk
zl-pipeline sync-consumers 2.2.1

# 方案 B: 独立对齐（各包取各自最新）
zl-pipeline align-versions

# 方案 C: UseThink.Iot/api 专用脚本
cd /Users/dingyuwang/0-X/UseThink.Iot/api
python3 align-zl-packages.py --dry-run   # 预览
python3 align-zl-packages.py              # 执行
```

### 3.3 消费者项目 restore 和构建

```bash
cd /path/to/consumer
dotnet restore        # 自动使用全局配置
dotnet build          # 编译
```

**如果 restore 失败**：
```bash
# 1. 清除 NuGet HTTP 缓存（nuget.org 新发版有传播延迟）
dotnet nuget locals http-cache -c

# 2. 重新 restore
dotnet restore
```

---

## 四、最佳实践

### 4.1 DO（推荐做法）

| # | 做法 | 理由 |
|---|------|------|
| 1 | **通过 GitHub Actions 发布到 nuget.org** | 统一发布流程，可追溯，不依赖本地环境 |
| 2 | **使用 CPM 精确版本** | 消费者 `Directory.Packages.props` 中写死版本号 |
| 3 | **项目级 config 不重复定义源** | 避免多份配置不一致 |
| 4 | **CI 环境中也配置同样源** | 保持本地和 CI 行为一致 |
| 5 | **发布后立即 sync-consumers** | 防止消费者引用未发布版本 |

### 4.2 DON'T（禁止做法）

| # | 禁止 | 后果 |
|---|------|------|
| 1 | **不要手动 dotnet nuget push** | 绕过 CI，缺少审计，容易产生版本漂移 |
| 2 | **不要使用 local-feed** | 已废弃，不再维护，容易产生版本不一致 |
| 3 | **不要把 feed 放在 /tmp** | `/tmp` 会被系统定时清理或重启丢失 |
| 4 | **不要每个项目一个 local-feed** | 多份副本版本不一致，维护成本高 |
| 5 | **不要在项目 config 中使用 `<clear/>`** | 会清除全局源，导致包找不到 |
| 6 | **不要混用 ProjectReference 和 PackageReference** | 同一包在构建图中只能有一种引用方式 |
| 7 | **不要跳过 CPM 直接在 .csproj 写版本** | 版本碎片化，难以追踪和更新 |
| 8 | **不要发布后忘记更新消费者** | 消费者 restore 失败（引用了不存在的版本） |

### 4.3 NuGet.org 包名安全

**现状**：`ZL.Dao.IotDevice`、`ZL.DataConvert`、`ZL.DB.Acc`、`ZL.EdgeService` 四个包名曾被第三方抢先注册了高版本号（如 `1.0.8042.25207`）。

**防御策略**：
1. **已发布 2.2.1** 绕过冲突（CPM 精确锁定不受影响）
2. **所有消费者必须使用 CPM**，精确锁定版本号，不受 NuGet 范围解析影响
3. **长期方案**：考虑统一加 `UseThink.` 前缀（如 `UseThink.Iot.Dao.IotDevice`），彻底隔离

> **重要**：即使有冲突者发布更高版本号，只要消费者 CPM 写的是 `<PackageVersion Include="ZL.Dao.IotDevice" Version="2.2.1" />`，NuGet 就会精确拉取 2.2.1，不会被更高版本劫持。

---

## 五、版本管理策略

### 5.1 发版策略：统一版本

所有 ZL 包**统一版本号**，同步发布。

| 场景 | 操作 |
|------|------|
| 修复 Bug | 统一升级所有包版本，如 `2.2.1` → `2.2.2` |
| 新功能 | 统一升级所有包版本，如 `2.2.1` → `2.3.0` |
| 大版本更新 | 统一升级所有包版本，如 `2.2.1` → `3.0.0` |

**版本号由 GitHub Actions 流水线统一管理**，禁止手动随意提升版本。

### 5.2 消费者更新策略

| 工具 | 用途 | 命令 |
|------|------|------|
| `zl-pipeline sync-consumers X.Y.Z` | 全量同步到指定版本 | 所有包都发布了 X.Y.Z 时使用 |
| `zl-pipeline align-versions` | 各包拉到各自最新 | 日常独立发版后的消费者更新 |
| `align-zl-packages.py` | UseThink.Iot/api 专用 | 支持 NuGet.org 源 |

### 5.3 版本查询优先级

```
align-zl-packages.py (--source auto，默认):
  1. NuGet.org index.json API  ← 唯一源
```

---

## 六、故障排查

### 6.1 restore 失败：找不到包

```bash
# 诊断步骤
dotnet nuget list source                    # 确认源配置
dotnet package search ZL.Dao.IotDevice     # 确认包在源中存在
dotnet nuget locals http-cache -c          # 清除 HTTP 缓存
dotnet restore                              # 重试
```

### 6.2 restore 拉到了错误的版本号

```bash
# 检查 CPM
cat Directory.Packages.props | grep ZL.

# 如果 CPM 版本正确但拉错，清缓存
dotnet nuget locals global-packages -c     # ⚠️ 清除全局包缓存（较慢）
dotnet restore
```

### 6.3 NuGet.org 新发版后消费者找不到

NuGet.org 有**传播延迟**（通常 1-5 分钟）：
- `index.json` API：推送后**立即可用**
- `dotnet package search` 搜索索引：可能延迟 1-5 分钟
- 第三方工具（NuGet 浏览器等）：可能延迟更长

```bash
# 验证包是否已可用（最可靠）
curl -s "https://api.nuget.org/v3-flatcontainer/zl.dao.iotdevice/index.json" | python3 -c "import sys,json; print('2.2.1 OK' if '2.2.1' in json.load(sys.stdin)['versions'] else 'NOT YET')"

# 如果已可用但 restore 失败，清缓存
dotnet nuget locals http-cache -c
dotnet restore
```

### 6.4 编译时报"传递性包冲突"

当项目同时引用了 NuGet 包和源码（ProjectReference）版本的同一依赖时：

```xml
<!-- 在被引用项目的 .csproj 中添加 -->
<ItemGroup>
  <PackageReference Include="冲突的包名" PrivateAssets="All" />
</ItemGroup>
```

`PrivateAssets="All"` 阻止该引用向外传播，消除冲突。

---

## 七、项目级配置清单

### 7.1 所有项目 NuGet 配置状态

| 项目 | NuGet.config | 包源 | CPM | 备注 |
|------|-------------|------|-----|------|
| **iot-sdk** | 无（继承全局） | 全局 | ✅ `Directory.Packages.props` | SDK 本体 |
| **tmom** | ✅（仅注释） | 全局 | ✅ `Directory.Packages.props` | 14 个项目，引用 4 个 ZL 包 |
| **UseThink.Iot/api** | ✅（仅注释） | 全局 | ✅ `Directory.Packages.props` | 14 个项目，引用 5 个 ZL 包 + ProtocolGateway |
| **ZL.PlcSimulator** | ✅（仅注释） | 全局 | ❌ 无 CPM（包自带版本） | 引用 ZL.Watchdog |
| **ZL.Simulator** | 无（继承全局） | 全局 | ❌ 无 CPM（包自带版本） | 引用 ZL.Framing/Protocol/Probing |

### 7.2 关键文件位置

| 文件 | 路径 | 作用 |
|------|------|------|
| 全局 NuGet 配置 | `~/.nuget/NuGet/NuGet.Config` | 定义所有包源 |
| iot-sdk CPM | `iot-sdk/Directory.Packages.props` | SDK 内部统一版本 |
| iot-sdk pipeline | `iot-sdk/pipeline.json` | 定义项目列表和消费者 |
| tmom CPM | `tmom/Directory.Packages.props` | tmom 统一版本 |
| UseThink.Iot CPM | `UseThink.Iot/api/Directory.Packages.props` | UseThink 统一版本 |
| 发布脚本 | `deploy/tools/ZL.Pipeline.Cli/zl-pipeline.py` | sync-consumers / align-versions |
| UseThink 对齐脚本 | `UseThink.Iot/api/align-zl-packages.py` | 版本对齐 |

---

## 八、新消费者接入指南

当有新项目需要引用 ZL IoT SDK 包时：

### Step 1: 确认 NuGet.config

```bash
# 在项目根目录创建 NuGet.config（如果不存在）
cat > NuGet.config << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <!-- 继承全局 NuGet 配置 -->
</configuration>
EOF
```

### Step 2: 启用 CPM

在项目根目录创建 `Directory.Packages.props`：

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- 在此添加需要的 ZL 包及版本号 -->
    <PackageVersion Include="ZL.Collections" Version="2.2.1" />
    <PackageVersion Include="ZL.Framing" Version="2.2.1" />
    <!-- ... -->
  </ItemGroup>
</Project>
```

### Step 3: 注册到 pipeline.json

```json
{
  "consumers": [
    {
      "name": "新项目名称",
      "path": "/Users/dingyuwang/0-X/新项目绝对路径",
      "cpmFile": "Directory.Packages.props",
      "buildTarget": "主项目/主项目.csproj",
      "autoCommit": false
    }
  ]
}
```

### Step 4: 验证

```bash
cd /path/to/new-project
dotnet restore    # 确认全部成功
dotnet build      # 确认编译通过
```

---

## 九、与 zl-pipeline 工具的集成

| zl-pipeline 命令 | 与全局 NuGet 方案的关系 |
|-----------------|----------------------|
| `zl-pipeline pack X.Y.Z` | 本地打包/验证，不负责 nuget.org 推送 |
| `zl-pipeline publish X.Y.Z` | 本地 dry-run 验证用；实际 nuget.org 推送由 GitHub Actions 完成 |
| `zl-pipeline sync-consumers X.Y.Z` | 更新 pipeline.json 中所有消费者的 CPM 到 X.Y.Z |
| `zl-pipeline align-versions` | 查询 NuGet.org，将每个消费者 CPM 中各包拉到**各自最新** |
| `zl-pipeline verify` | 验证所有消费者 restore + build |

> **注意**：当前所有 NuGet.org 发布统一由 `.github/workflows/publish.yml` 处理。`zl-pipeline publish` 仅保留本地打包和验证能力，不再直接推送 nuget.org。

---

## 十、版本历史

| 日期 | 变更 |
|------|------|
| 2026-06-29 | 废弃 local-feed，所有发布改为 GitHub Actions + nuget.org |
| 2026-06-07 | 初始方案：统一全局 NuGet 配置，删除项目级本地 feed，ZL 包全部发布到 NuGet.org 1.1.0 |
| 2026-06-07 | 发现并修复 NuGet.org 包名冲突（4 个包被第三方抢占），发布 1.1.0 绕过 |
| 2026-06-07 | ProtocolGateway 从 UseThink.Iot 本地 feed 1.0.1 迁移到 NuGet.org 1.1.0 |
| 2026-06-07 | ZL.PlcSimulator 和 ZL.Simulator 从 ProjectReference 迁移到 NuGet 1.1.0 |
