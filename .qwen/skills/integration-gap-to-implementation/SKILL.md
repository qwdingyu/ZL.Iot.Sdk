---
name: integration-gap-to-implementation
description: 将跨系统集成差距分析转化为可执行的代码改造计划，并按 P0-P2 优先级逐步实现
source: auto-skill
extracted_at: '2026-06-08T09:44:00.872Z'
---

# Integration Gap to Implementation

## When to Use

当已完成跨系统集成差距分析（如通过 `cross-system-integration-analysis`），现在需要将分析结果转化为实际的代码改造时，使用此技能。典型场景：
- 识别了 5 个差距，需要决定先改哪个
- 需要将差距按 P0/P1/P2 分级，确定实现顺序
- 需要验证现有代码是否已经部分解决了某个差距

## Core Principle

**差距分析不是终点，实现才是。优先解决 P0（直接导致集成失败）的差距，每个改造必须经过 `dotnet build` 验证。**

## Procedure

### Step 1: 验证现有代码是否已有部分解决方案

在开始编码前，先确认哪些差距已经被现有代码覆盖：

1. **检查构造函数是否已支持传入共享连接** — grep `sharedLocalClient\|ISqlSugarClient` 在构造函数中
2. **检查是否有策略接口** — grep `ISyncStrategy` 确认接口定义
3. **检查策略工厂方法** — grep `CreateStrategy` 确认是否有路由逻辑

**经验发现**: 很多差距在现有代码中已经有部分基础设施（如 `SyncEngine` 已接受 `ISqlSugarClient?`），只需扩展而非从零开始。

### Step 2: 按优先级排序改造项

| 等级 | 标准 | 行动 |
|------|------|------|
| **P0** | 不改则集成直接失败 | 必须立即实现，阻塞其他工作 |
| **P1** | 需要适配但不阻塞核心功能 | P0 完成后实现 |
| **P2** | 可接受差异，配置/文档可解决 | 有时间再做 |

**示例排序**：
```
P0: SyncEngine 支持传入共享 SqlSugarClient ← 已存在，验证通过
P0: 新增 ProcessTimeSyncStrategy（基于 _UploadFlag） ← 需要新建
P1: 水位线 _SyncLog 适配 ← ProcessTimeSyncStrategy 内已包含
P2: 配置映射适配器 ← 可后续优化
```

### Step 3: 实施单个改造（逐个 P0 改造）

对每个改造项：

#### 3a. 理解现有代码边界

```bash
# 确认接口签名
grep "interface ISyncStrategy" -A 20
# 确认策略工厂
grep "CreateStrategy" -A 20
# 确认当前策略注册方式
grep "DatabaseSyncStrategy\|HttpSyncStrategy" -B 2 -A 2
```

#### 3b. 最小变更原则

- **新建文件** 优于 **修改现有文件**（降低回归风险）
- **扩展构造函数参数** 优于 **重构构造函数逻辑**
- **添加枚举值** 优于 **修改枚举含义**

#### 3c. 编译验证

每次修改后必须：
```bash
dotnet build src/application/ZL.DataSync/ZL.DataSync.csproj
```

#### 3d. 处理类型不匹配

常见问题：`ISqlSugarClient` vs `SqlSugarClient`。
解决方案：
- 修改接口签名接受更通用的类型（`ISqlSugarClient`）
- 或在调用点做安全的 `as` 转换 + throw

### Step 4: 改造完成后回归验证

1. `dotnet build` — 编译通过
2. `dotnet test` — 现有测试不失败
3. 确认新增功能可通过配置启用

## Common Gap-to-Code Patterns

### Pattern 1: 新增策略类

当差距是"增量同步机制不同"时：

```
差距: A系统用 _Synced，B系统用 _UploadFlag
解决: 新建 StrategyB : ISyncStrategy
      在 CreateStrategy 中根据配置路由
```

**关键**: 新策略复用公共基础设施（建表、批量写入、类型推断），只替换增量查询逻辑。

### Pattern 2: 构造函数扩展

当差距是"连接管理方式不同"时：

```
差距: A系统自建连接，B系统共享连接
解决: 构造函数增加可选参数 sharedLocalClient
      通过 _ownsLocalDb 标志控制释放所有权
```

**关键**: 保持向后兼容 — 旧调用方式（无参数）仍然工作。

### Pattern 3: 枚举路由扩展

当差距是"行为模式不同"时：

```
差距: 需要支持两种不同的同步策略
解决: RemoteTargetConfig 新增 StrategyType 枚举
      CreateStrategy 根据枚举值路由到不同策略类
```

**关键**: 默认值保持向后兼容（`Database` 走原有逻辑）。

## Anti-Patterns

| 反模式 | 问题 | 正确做法 |
|--------|------|----------|
| 一次性改造所有差距 | 风险大，难以回滚 | 按 P0→P1→P2 逐个实现 |
| 修改现有策略的语义 | 可能破坏现有用户 | 新建策略类，路由选择 |
| 不验证编译就继续 | 小错误积累成大问题 | 每改一行就 `dotnet build` |
| 忽略类型不匹配编译错误 | 错误传播到多处 | 统一接口接受 `ISqlSugarClient` |
| 改造后不运行测试 | 回归问题难以定位 | `dotnet test` 必须通过 |

## Output Format

对每个改造项输出：

```
### 改造 N: [名称]（P0/P1/P2）
- **状态**: ✅已完成 / 🔄进行中 / ⏳待实现
- **修改文件**: [文件列表]
- **向后兼容**: 是/否（如何保证）
- **验证**: dotnet build ✅, dotnet test ✅
- **使用方式**: 配置示例或调用示例
```
