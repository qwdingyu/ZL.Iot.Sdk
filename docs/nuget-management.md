# NuGet 包管理全局方案

> **版本**: v1.0 | **最后更新**: 2026-06-07 | **适用范围**: iot-sdk 及所有下游消费者项目

---

## 一、架构总览

```
┌─────────────────────────────────────────────────────────────────┐
│                    全局 NuGet 配置（唯一权威源）                   │
│                                                                  │
│  ~/.nuget/NuGet/NuGet.Config                                    │
│  ├── nuget.org (https://api.nuget.org/v3/index.json)            │
│  │    └── ZL IoT SDK 23 个包 (v1.1.0+)                          │
│  │        包括: ZL.Collections ~ ZL.EdgeService, ProtocolGateway │
│  │              ProtocolGateway.Scripting, ZL.PlcSimulator.*    │
│  │                                                              │
│  └── local-feed (/Users/dingyuwang/.nuget/local-feed/)          │
│       └── ZL.PlcBase.2.0.1.nupkg      (非 NuGet.org 发布的包)   │
│       └── ZL.PlcBase.Bridges.2.0.1.nupkg                       │
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
| **持久化** | 全局本地 feed 位于 `~/.nuget/local-feed/`（用户 home 目录），**不在 /tmp** |
| **NuGet.org 优先** | 能发布的包全部上 NuGet.org，本地 feed 仅作为最后手段 |
| **CPM 精确锁定** | 消费者使用 `Directory.Packages.props` 精确指定版本号，杜绝版本漂移 |
| **无冗余 feed** | 删除所有项目级 `.nuget/local-feed/`，消除多份副本的不一致 |

---

## 二、配置详解

### 2.1 全局配置（核心）

**文件**: `~/.nuget/NuGet/NuGet.Config`（即 `/Users/dingyuwang/.nuget/NuGet/NuGet.Config`）

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="local-feed" value="/Users/dingyuwang/.nuget/local-feed" />
  </packageSources>
</configuration>
```

**关键要求**：
- 使用**绝对路径**（NuGet 不展开 `~` 符号）
- `nuget.org` 必须排在 `local-feed` **之前**（优先从公网获取）
- 不要加 `<clear/>`（与 MSBuild 用户配置合并）

### 2.2 项目级 NuGet.config

所有下游项目（tmom、UseThink.Iot/api、ZL.PlcSimulator）的 `NuGet.config` 已精简为：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <!-- 继承全局 NuGet 配置 (~/.nuget/NuGet/NuGet.Config)：
       nuget.org + /Users/dingyuwang/.nuget/local-feed -->
</configuration>
```

**为什么保留文件而不是删除？**
- 标记"此项目有意继承全局配置"，避免新开发者误以为配置缺失
- 为将来某个项目需要私有源时预留扩展点
- **绝不包含** `<clear/>` 或重复的 `<packageSources>`

### 2.3 全局本地 Feed

**路径**: `/Users/dingyuwang/.nuget/local-feed/`

**当前内容**：
| 包 | 版本 | 为何不在 NuGet.org |
|---|------|-------------------|
| `ZL.PlcBase` | 2.0.1 | 第三方商业库，未发布到 NuGet.org |
| `ZL.PlcBase.Bridges` | 2.0.1 | 同上 |

**命名规则**: `{PackageId}.{Version}.nupkg`

---

## 三、日常工作流

### 3.1 发布新 ZL 包版本

```bash
# 1. 在 iot-sdk 中打包
cd /Users/dingyuwang/0-X/iot-sdk
zl-pipeline pack 1.1.0

# 2. 推送到 NuGet.org
zl-pipeline publish 1.1.0

# 3. 更新 iot-sdk 自身 CPM
#    (pack 时已自动更新 Directory.Packages.props)

# 4. 同步所有消费者
zl-pipeline sync-consumers 1.1.0
```

### 3.2 消费者更新到最新版本

```bash
# 方案 A: 全量同步（所有包同一版本）
zl-pipeline sync-consumers 1.1.0

# 方案 B: 独立对齐（各包取各自最新）
zl-pipeline align-versions

# 方案 C: UseThink.Iot/api 专用脚本
cd /Users/dingyuwang/0-X/UseThink.Iot/api
python3 align-zl-packages.py --dry-run   # 预览
python3 align-zl-packages.py              # 执行
```

### 3.3 向全局本地 feed 添加新包

```bash
# 仅当包无法发布到 NuGet.org 时执行
cp path/to/SomePackage.1.0.0.nupkg ~/.nuget/local-feed/

# 验证
dotnet nuget list source    # 确认 local-feed 存在
dotnet package search SomePackage --source local-feed  # 确认可发现
```

### 3.4 消费者项目 restore 和构建

```bash
cd /path/to/consumer
dotnet restore        # 自动使用全局配置
dotnet build          # 编译
```

**如果 restore 失败**：
```bash
# 1. 清除 NuGet HTTP 缓存（NuGet.org 新发版有传播延迟）
dotnet nuget locals http-cache -c

# 2. 重新 restore
dotnet restore
```

---

## 四、最佳实践

### 4.1 DO（推荐做法）

| # | 做法 | 理由 |
|---|------|------|
| 1 | **优先发布到 NuGet.org** | 公网源持久可靠，任何机器都能恢复 |
| 2 | **使用 CPM 精确版本** | 消费者 `Directory.Packages.props` 中写死版本号 |
| 3 | **项目级 config 不重复定义源** | 避免多份配置不一致 |
| 4 | **本地 feed 放 `~/.nuget/local-feed/`** | 持久化，不受系统重启/清理影响 |
| 5 | **CI 环境中也配置同样源** | 保持本地和 CI 行为一致 |
| 6 | **发布后立即 sync-consumers** | 防止消费者引用未发布版本 |

### 4.2 DON'T（禁止做法）

| # | 禁止 | 后果 |
|---|------|------|
| 1 | **不要把 feed 放在 /tmp** | `/tmp` 会被系统定时清理或重启丢失 |
| 2 | **不要每个项目一个 local-feed** | 多份副本版本不一致，维护成本高 |
| 3 | **不要在项目 config 中使用 `<clear/>`** | 会清除全局源，导致 ZL.PlcBase 找不到 |
| 4 | **不要混用 ProjectReference 和 PackageReference** | 同一包在构建图中只能有一种引用方式 |
| 5 | **不要跳过 CPM 直接在 .csproj 写版本** | 版本碎片化，难以追踪和更新 |
| 6 | **不要发布后忘记更新消费者** | 消费者 restore 失败（引用了不存在的版本） |

### 4.3 NuGet.org 包名安全

**现状**：`ZL.Dao.IotDevice`、`ZL.DataConvert`、`ZL.DB.Acc`、`ZL.EdgeService` 四个包名曾被第三方抢先注册了高版本号（如 `1.0.8042.25207`）。

**防御策略**：
1. **已发布 1.1.0** 绕过冲突（`1.1.0 > 1.0.xxxx` 在语义化版本比较中不一定成立，但 CPM 精确锁定不受影响）
2. **所有消费者必须使用 CPM**，精确锁定版本号，不受 NuGet 范围解析影响
3. **长期方案**：考虑统一加 `UseThink.` 前缀（如 `UseThink.Iot.Dao.IotDevice`），彻底隔离

> **重要**：即使有冲突者发布更高版本号，只要消费者 CPM 写的是 `<PackageVersion Include="ZL.Dao.IotDevice" Version="1.1.0" />`，NuGet 就会精确拉取 1.1.0，不会被更高版本劫持。

---

## 五、版本管理策略

### 5.1 发版策略：独立发版

每个 ZL 包**独立发版**，不强制所有 23 个包同步到同一版本。

| 场景 | 操作 |
|------|------|
| 仅 `ZL.Dao.IotDevice` 有 Bug 修复 | 只发布 `ZL.Dao.IotDevice` 1.1.1 |
| `ZL.Framing` + `ZL.Protocol` 有新功能 | 发布这两个包 1.1.1 |
| 全部包有大版本更新 | 发布所有 23 个包 2.0.0 |

### 5.2 消费者更新策略

| 工具 | 用途 | 命令 |
|------|------|------|
| `zl-pipeline sync-consumers X.Y.Z` | 全量同步到指定版本 | 所有包都发布了 X.Y.Z 时使用 |
| `zl-pipeline align-versions` | 各包拉到各自最新 | 日常独立发版后的消费者更新 |
| `align-zl-packages.py` | UseThink.Iot/api 专用 | 支持 NuGet.org + 本地 feed 混合源 |

### 5.3 版本查询优先级

```
align-zl-packages.py (--source auto，默认):
  1. NuGet.org index.json API  ← 优先（发布后秒级可用）
  2. ~/.nuget/local-feed/      ← 备选（NuGet.org 404 时）
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

### 6.2 restore 失败：ZL.PlcBase 找不到

```bash
# 确认全局本地 feed 存在
ls ~/.nuget/local-feed/ZL.PlcBase*

# 确认全局配置引用了该路径
cat ~/.nuget/NuGet/NuGet.Config

# 如果 feed 被误删，从备份恢复
cp /path/to/backup/*.nupkg ~/.nuget/local-feed/
```

### 6.3 restore 拉到了错误的版本号

```bash
# 检查 CPM
cat Directory.Packages.props | grep ZL.

# 如果 CPM 版本正确但拉错，清缓存
dotnet nuget locals global-packages -c     # ⚠️ 清除全局包缓存（较慢）
dotnet restore
```

### 6.4 NuGet.org 新发版后消费者找不到

NuGet.org 有**传播延迟**（通常 1-5 分钟）：
- `index.json` API：推送后**立即可用**
- `dotnet package search` 搜索索引：可能延迟 1-5 分钟
- 第三方工具（NuGet 浏览器等）：可能延迟更长

```bash
# 验证包是否已可用（最可靠）
curl -s "https://api.nuget.org/v3-flatcontainer/zl.dao.iotdevice/index.json" | python3 -c "import sys,json; print('1.1.0 OK' if '1.1.0' in json.load(sys.stdin)['versions'] else 'NOT YET')"

# 如果已可用但 restore 失败，清缓存
dotnet nuget locals http-cache -c
dotnet restore
```

### 6.5 编译时报"传递性包冲突"

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
| 全局本地 feed | `~/.nuget/local-feed/` | ZL.PlcBase 等非公网包 |
| iot-sdk CPM | `iot-sdk/Directory.Packages.props` | SDK 内部统一版本 |
| iot-sdk pipeline | `iot-sdk/pipeline.json` | 定义项目列表和消费者 |
| tmom CPM | `tmom/Directory.Packages.props` | tmom 统一版本 |
| UseThink.Iot CPM | `UseThink.Iot/api/Directory.Packages.props` | UseThink 统一版本 |
| 发布脚本 | `deploy/tools/ZL.Pipeline.Cli/zl-pipeline.py` | pack/publish/sync/align |
| UseThink 对齐脚本 | `UseThink.Iot/api/align-zl-packages.py` | 双源版本对齐 |

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
    <PackageVersion Include="ZL.Collections" Version="1.1.0" />
    <PackageVersion Include="ZL.Framing" Version="1.1.0" />
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
| `zl-pipeline pack X.Y.Z` | 打包前更新 iot-sdk CPM 到 X.Y.Z |
| `zl-pipeline publish X.Y.Z` | 推送 artifacts/*.nupkg 到 NuGet.org |
| `zl-pipeline sync-consumers X.Y.Z` | 更新 pipeline.json 中所有消费者的 CPM 到 X.Y.Z |
| `zl-pipeline align-versions` | 查询 NuGet.org，将每个消费者 CPM 中各包拉到**各自最新** |
| `zl-pipeline verify` | 验证所有消费者 restore + build |

---

## 十、版本历史

| 日期 | 变更 |
|------|------|
| 2026-06-07 | 初始方案：统一全局 NuGet 配置，删除项目级本地 feed，ZL 包全部发布到 NuGet.org 1.1.0 |
| 2026-06-07 | 发现并修复 NuGet.org 包名冲突（4 个包被第三方抢占），发布 1.1.0 绕过 |
| 2026-06-07 | ProtocolGateway 从 UseThink.Iot 本地 feed 1.0.1 迁移到 NuGet.org 1.1.0 |
| 2026-06-07 | ZL.PlcSimulator 和 ZL.Simulator 从 ProjectReference 迁移到 NuGet 1.1.0 |
