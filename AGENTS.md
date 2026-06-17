# AGENTS.md — iot-sdk 项目开发铁律

> **版本**: 1.1
> **日期**: 2026-06-17
> **变更**: 新增依赖管理规则（§依赖管理铁律）

---

## 核心原则

1. **每一次代码变更必须提交到代码仓库。** commit 信息使用中文。
2. **所有代码修改必须同步文档。** 修改了什么、为什么修改、影响范围。
3. **全局视野，顶层思路。** 不能为了修一个 Bug 打补丁式地改代码，必须理解整个模块的设计后再动手。
4. **瑞士军刀，不要瑞士军刀组。** 不要做大而全的系统。功能要小而精，聚焦核心场景。
5. **改动前必须先理解全局和代码。** 不要只看局部。确保所有环节形成闭环。核心功能必须有单元测试。
6. **所有问题、规范、规则、流程要文档化。**
7. **不要假大空。** 一切站在落地的使用场景角度考虑问题。
8. **核心功能优先。** 确保核心功能快速实现和上线。
9. **技术选型要遵循行业最佳实践。** 不要重复造轮子。
10. **核心代码必须加中文注释。**
11. **ORM 优先，严禁裸 SQL。**

---

## 依赖管理铁律（§12）

### 12.1 仓库内部：ProjectReference 优先

**iot-sdk 仓库内部**（同一个 `IoT.Sdk.sln` 内）的项目互相引用，**必须使用 `<ProjectReference>`**，严禁使用 `<PackageReference>` 指向自身仓库内的项目。

```
✅ 允许（仓库内部）：
  <ProjectReference Include="..\..\platform\ZL.Iot.Interface\ZL.Iot.Interface.csproj" />

❌ 禁止（仓库内部）：
  <PackageReference Include="ZL.Iot.Interface" />
  <PackageReference Include="ZL.DB.Acc" />
  <PackageReference Include="ZL.Dao.IotDevice" />
```

**原因**：
- 仓库内部改代码后 ProjectReference 立刻生效，零版本管理开销
- PackageReference 需要 pack → 同步缓存 → restore 才能看到效果，极易产生版本漂移
- 仓库内部编译时 ProjectReference 自动保证类型一致性，不会出现 CS0433 歧义
- iot-sdk 内部 24 个项目依赖关系复杂，PackageReference 的传递依赖极易产生版本不一致

### 12.2 对 ZL.PlcBase 的引用：ProjectReference（开发期）

**iot-sdk 对 ZL.PlcBase 中的 ZL.IotHub 引用，开发期使用 `<ProjectReference>`**，指向本地源码路径。

```
✅ 允许（开发期）：
  <ProjectReference Include="..\..\ZL.PlcBase\ZL.IotHub\ZL.IotHub.csproj" />

❌ 禁止（开发期）：
  <PackageReference Include="ZL.IotHub" />    ← 需要 pack 同步，易版本漂移
```

**调试好之后**，通过 `deploy-fast.sh` 将 ZL.PlcBase 打包到 local-feed，消费者（UseThink.Iot / tmom）从 local-feed 取包。

### 12.3 对外部第三方包：PackageReference

第三方包（SqlSugarCore、HslCommunication、NLog 等）**必须使用 `<PackageReference>`**，版本在 `Directory.Packages.props` 中统一管理（CPM）。

```
✅ 允许（第三方包）：
  <PackageReference Include="SqlSugarCore" />
  <PackageReference Include="NLog" />
```

### 12.4 引用方式速查表

| 场景 | 引用方式 | 原因 |
|---|---|---|
| iot-sdk 内部项目互引 | `<ProjectReference>` | 同 sln，改了即用，零版本漂移 |
| iot-sdk → ZL.IotHub（开发期） | `<ProjectReference>` | 跨仓库但同机器，改了即用 |
| iot-sdk → ZL.IotHub（发布期） | `<PackageReference>` | 打包时自动切换 |
| iot-sdk → 第三方包 | `<PackageReference>` | CPM 统一版本 |
| UseThink.Iot → ZL.IotHub | `<PackageReference>` | 跨仓库，编译快 |
| UseThink.Iot → iot-sdk 包 | `<PackageReference>` | 跨仓库，编译快 |

### 12.5 开发/发布双模式切换

**开发期**（ProjectReference，改了即用）：

```xml
<!-- iot-sdk 内部的 csproj -->
<ItemGroup>
  <!-- 内部项目 → ProjectReference -->
  <ProjectReference Include="..\..\platform\ZL.DB.Acc\ZL.DB.Acc.csproj" />
  <ProjectReference Include="..\..\platform\ZL.Iot.Interface\ZL.Iot.Interface.csproj" />
  <!-- ZL.PlcBase → ProjectReference（开发期） -->
  <ProjectReference Include="..\..\ZL.PlcBase\ZL.IotHub\ZL.IotHub.csproj" />
  <!-- 第三方 → PackageReference -->
  <PackageReference Include="NLog" />
</ItemGroup>
```

**发布期**（`dotnet pack` 时 ProjectReference 自动转为 NuGet 依赖，无需手动改 csproj）。

### 12.6 同步工作流

```
日常开发（iot-sdk 内部）：
  改代码 → dotnet build IoT.Sdk.sln → dotnet test → git commit
  （ProjectReference 保证内部一致性，无需 pack）

同步到消费者：
  deploy-fast.sh 2.2.2    ← pack → local-feed
  cd 消费者 && dotnet restore && dotnet build

正式发布：
  zl-pipeline.py publish → nuget.org
```

### 12.7 ZL.PFLite 保持独立，ZL.Tag 由 ZL.IotHub 承载

**ZL.PFLite 是通用基础工具库，继续作为独立 NuGet 包发布**。与 IoT/设备采集无关的项目应继续直接引用 ZL.PFLite，不应为了基础工具能力引入 ZL.IotHub。

**ZL.Tag 包在 iot-sdk 中不再直接引用**。Tag 模型类型仍保持 `ZL.Tag` 命名空间，但实际由 ZL.IotHub 承载，避免同时引用 ZL.Tag.dll 与 ZL.IotHub.dll 时产生 CS0433 类型歧义。

| 包 | 状态 | 引用规则 |
|---|---|---|
| `ZL.PFLite` | ✅ 继续独立发布 | 基础工具/DTO/Auth 等能力继续从 ZL.PFLite 获取 |
| `ZL.IotHub` | ✅ IoT 驱动核心包 | 依赖 ZL.PFLite，并承载 `ZL.Tag` 命名空间的 Tag 模型 |
| `ZL.Tag` | ❌ iot-sdk 不再引用 | 旧包不应与 ZL.IotHub 同时出现在 iot-sdk 编译依赖中 |

**引用规则**：

```
✅ 非 IoT/设备采集项目：
  只引用 ZL.PFLite

✅ iot-sdk / UseThink.Iot 等设备采集项目：
  引用 ZL.IotHub
  需要 ZL.PFLite.Auth、LogKit、DicKit、pms_public 等基础能力时继续引用 ZL.PFLite

✅ 使用 TagItem / DeviceConfig / TagKit 的项目：
  using ZL.Tag; 保持不变
  包引用使用 ZL.IotHub，不再使用 ZL.Tag

❌ 禁止：iot-sdk 项目直接引用 ZL.Tag 包
❌ 禁止：将 ZL.PFLite 通用类型复制进 ZL.IotHub 形成双类型来源
```

---

## 技术红线

### 数据库操作

```
✅ 允许：ORM API（SqlSugar）
❌ 禁止：裸 SQL 字符串、字符串拼接的 DDL
```

### 架构变更

```
❌ 不允许：新增独立后台服务、新增独立 API 端点（已有机制可扩展时）
✅ 优先：在现有机制上扩展
```

### 代码质量

```
❌ 不允许：无注释的核心逻辑、裸 SQL
✅ 优先：ORM API、中文注释
```

---

## 开发流程

```
1. 理解需求 → 对照 docs/ 中的架构文档确认设计
2. 理解现有代码 → 找到最合适的扩展点
3. 实现 → 遵循 ORM 优先原则 + ProjectReference 优先原则
4. 测试 → 写正式的可复用测试脚本
5. 提交 → git add + git commit（中文）
6. 文档 → 更新或新增 docs/ 中的文档
7. 同步消费者 → deploy-fast.sh → 消费者 dotnet restore
```

## 引用

- 架构设计：`docs/PROJECT_OVERVIEW.md`
- 依赖关系：`docs/DEPENDENCY_GRAPH.md`
- NuGet 管理：`docs/nuget-management.md`
- 本地发布流程：`/Users/dingyuwang/0-X/deploy/tools/local-feed-workflow.md`
- ZL.PlcBase 铁律：`/Users/dingyuwang/0-X/ZL.PlcBase/AGENTS.md`
