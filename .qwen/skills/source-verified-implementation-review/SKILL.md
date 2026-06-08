---
name: source-verified-implementation-review
description: 对跨系统集成实现进行深度审查，验证源码与实现一致、消除注释/代码/文档的幻觉偏差，识别并发与原子性隐患
source: auto-skill
extracted_at: '2026-06-08T12:30:00.000Z'
---

# Source-Verified Implementation Review

## When to Use

当已完成跨系统集成模块的代码实现后，需要对实现进行深度审查以确保：
- 实现与集成方实际源码一致（非设计文档推断）
- 注释、代码、文档之间无幻觉偏差
- 并发安全和数据库原子操作正确
- 构造函数/接口契约无歧义

典型触发场景：
- 新增策略类（Strategy）或适配器后
- 集成方使用不同增量同步机制时
- 多个系统共享同一 SQLite 文件时

## Core Principle

**代码审查必须"读源码不读文档"。所有实现断言必须与集成方实际源码逐行对比，注释和注释中的描述也是审查对象（注释本身可能是幻觉载体）。**

## Procedure

### Step 1: 读取集成方实际源码（非设计文档）

**关键动作**：
```bash
# 定位集成方的关键实现文件
find <project> -name "*SyncService*.cs" -o -name "*RemoteSync*.cs"

# 读取并 grep 确认增量条件
grep "WHERE.*ProcessTime\|WHERE.*_UploadFlag\|WHERE.*_Synced" -B 2 -A 5 <integration_file>

# 确认水位线表结构
grep "TableName\|SyncTime\|_SyncLog" <integration_file>
```

**必须验证的维度**：
| 维度 | 搜索命令 | 示例 |
|------|----------|------|
| 增量同步条件 | `grep "WHERE.*Time\|WHERE.*Flag"` | `WHERE ProcessTime > @s`（非 `_UploadFlag`） |
| 水位线表结构 | `grep "CREATE TABLE.*Sync\|TableName.*SyncTime"` | `(TableName TEXT PK, SyncTime TEXT)` |
| 写入后标记 | `grep "UPDATE.*table.*SET\|UPDATE.*_SyncLog"` | 只更新 _SyncLog，不碰业务表 |
| 连接管理 | `grep "new SqlSugarClient\|SharedLocalDb"` | 共享 vs 自建 |
| 并发模式 | `grep "lock\|async/await\|Task\|SynchronizationContext"` | 双重锁、async/await 传播 |

### Step 2: 审查注释中的幻觉

注释是最容易被遗漏的幻觉载体——它们描述了"意图"而非"事实"。

**审查清单**：
- [ ] 类注释中的增量条件描述与实际代码一致？
- [ ] 枚举值的 `<summary>` 注释与实际实现一致？
- [ ] 方法注释中的"与某某一致"是否真的与源码一致？
- [ ] 注释中的文件名/行号引用是否准确？

**常见幻觉模式**：
```csharp
// ❌ 幻觉：注释写的是设计文档的描述，不是实际代码
/// 基于 _UploadFlag + _SyncLog（PcStationIot 集成使用）
public enum SyncStrategyType { ProcessTime }

// ✅ 修正：注释反映实际代码
/// 基于 ProcessTime 增量 + _SyncLog 水位线（PcStationIot 集成使用）
public enum SyncStrategyType { ProcessTime }
```

### Step 3: 审查并发安全

**SQLite 共享连接审查**：
```
检查点:
1. 同一 SqlSugarClient 上是否有并发执行多个查询的风险？
   → 单线程内串行调用 = 安全
   → 多线程共享同一连接 = SQLite "database is locked" 风险

2. IsAutoCloseConnection = false 时，连接池复用是否正确处理？
   → 确保 Dispose() 不会释放共享连接

3. SQLite 写入操作是否在事务内？
   → 原子操作需要 BEGIN TRANSACTION / COMMIT
```

**异步一致性审查**：
```
检查点:
1. async 方法内是否混用了同步调用（SqlQuery vs SqlQueryAsync）？
   → 同步调用阻塞线程，影响并发性能

2. await 后是否有 ConfigureAwait(false)？
   → 避免不必要的上下文切换

3. CancellationToken 是否在所有 await 点传播？
   → ct.ThrowIfCancellationRequested()
```

### Step 4: 审查数据库原子操作

**COUNT + INSERT/UPDATE 非原子问题**：
```csharp
// ❌ 非原子：多线程同时 COUNT 看到 0 → 同时 INSERT → UNIQUE constraint
var count = localDb.Ado.SqlQuery<int>("SELECT COUNT(*) FROM _SyncLog WHERE TableName = @tn", ...);
if (hasRow) { /* UPDATE */ } else { /* INSERT */ }

// ✅ 原子：INSERT OR REPLACE 一步完成 UPSERT
await localDb.Ado.ExecuteCommandAsync(
    "INSERT OR REPLACE INTO _SyncLog (TableName, SyncTime) VALUES (@tn, @st)", ...);
```

**审查清单**：
- [ ] 所有"先查后写"模式是否可以用 UPSERT 替代？
- [ ] 批量操作是否有事务保护？
- [ ] 参数化查询是否覆盖了所有用户输入？

### Step 5: 审查接口契约与构造函数

**构造函数参数审查**：
```
检查点:
1. 构造函数参数是否在方法体中实际使用？
   → 未使用的参数应添加注释说明用途

2. 参数类型是否与调用方传递的类型一致？
   → ISqlSugarClient vs SqlSugarClient 转换

3. 是否有"预留"参数但未实现？
   → 应记录 TODO 或实现
```

**接口契约审查**：
```
检查点:
1. ISyncStrategy 所有成员是否实现？
2. 方法签名是否与接口完全一致？
3. 返回类型是否允许 null？（C# nullability 检查）
```

### Step 6: 编译验证

```bash
# 增量编译
dotnet build src/application/ZL.DataSync/ZL.DataSync.csproj

# 全量重建
dotnet build --no-incremental src/application/ZL.DataSync/ZL.DataSync.csproj
```

## Common Anti-Patterns Found in This Skill's Training

| 反模式 | 症状 | 修复 |
|--------|------|------|
| 注释与代码不一致 | 注释写 `_UploadFlag`，代码用 `ProcessTime` | 以代码为准更新注释 |
| COUNT+INSERT/UPDATE 非原子 | 多线程同时 COUNT=0 → 双 INSERT 失败 | 改用 `INSERT OR REPLACE` |
| 构造函数参数未使用 | 参数名 `sharedLocalDb` 但从未赋值给字段 | 添加注释或移除 |
| 同步/异步混用 | `async Task` 方法内调 `SqlQuery` 而非 `SqlQueryAsync` | 全部改为异步 |
| 接口契约不匹配 | 实现类方法签名与接口不一致 | 编译错误直接暴露 |

## Output Format

审查报告应包含：

```
## 审查结论

### ✅ 已验证的改造点
| 改造点 | 状态 | 验证结果 |

### ✅ 无幻觉确认
| 潜在幻觉 | 状态 | 说明 |

### ⚠️ 发现的问题
| # | 问题 | 严重度 | 位置 | 建议 |

### 📋 结论
零编译错误，零契约违反。
```

## Verification Checklist

输出审查报告前逐项确认：
- [ ] 增量条件与集成方源码逐行对比确认
- [ ] 水位线表结构与集成方源码逐行对比确认
- [ ] 所有注释中的描述已与代码事实对齐
- [ ] COUNT+INSERT/UPDATE 模式已替换为原子 UPSERT
- [ ] 同步调用已改为异步（在 async 方法中）
- [ ] 构造函数参数使用已审查
- [ ] 接口契约已验证匹配
- [ ] `dotnet build` 通过

## Anti-Hallucination Rules

1. **注释不是事实** — 注释可能描述过去或未来的意图，以代码为准
2. **设计文档不是集成方代码** — 必须读取集成方的实际源码确认
3. **"看起来应该这样"不是事实** — 必须 grep/read_file 确认
4. **编译通过不等于正确** — 编译只检查语法，不检查业务逻辑一致性
5. **接口实现不等于语义正确** — 签名匹配不代表数据语义一致
