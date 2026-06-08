---
name: source-verified-code-refactor
description: 在源码验证基础上对代码进行系统性重构与优化，消除重复代码、修复潜在 bug、优化性能，所有改动经过编译验证
source: auto-skill
extracted_at: '2026-06-08T12:00:00.000Z'
---

# Source-Verified Code Refactor & Optimization

## When to Use

当需要对已有代码进行系统性重构与优化时使用此技能。典型场景：
- 多个模块/策略类有大量重复代码需要提取共享逻辑
- 发现潜在的 bug（如未递增变量导致无限循环）
- 性能热点需要优化（如 O(n²) 循环改为 O(n)）
- 死代码、未使用变量/引用需要清理
- 在跨系统集成实现后，对代码质量和可维护性进行提升

## Core Principle

**每次重构必须经过源码逐文件理解，编译验证通过才算完成。不修改不理解的逻辑，所有改动最小化且可追溯。**

## Procedure

### Phase 1: 全量源码收集

1. **读取所有待优化文件** — `read_file` 获取完整源码上下文
2. **识别共享模式** — grep 重复的方法名、逻辑片段、代码模式
3. **确认调用链** — 确认每个方法/字段的使用点

```bash
# 识别重复模式
grep -rn "QuoteIdentifier\|MapDbType\|TryGetProcessTime" src/
grep -rn "INSERT.*VALUES\|BuildBatchInsert\|InsertRows" src/

# 识别未使用的代码
grep -rn "using.*System.Data" src/   # 未使用的 using
grep -rn "maxWatermark" src/        # 未使用的变量
```

### Phase 2: 优化点分类与优先级

将发现的优化点按以下标准分类：

| 等级 | 标准 | 行动 |
|------|------|------|
| **P0** | 会导致运行时错误 | 必须立即修复 |
| **P1** | 影响性能或可维护性 | 重构时优先处理 |
| **P2** | 代码风格/小改进 | 有空再做 |

**常见 P0 问题**：
- 未递增计数器导致 while 条件恒为 true（潜在无限循环）
- 空指针风险（null 检查在 dead code 路径中）
- 接口契约不匹配（编译错误直接暴露，但也可能漏掉）

**常见 P1 问题**：
- 重复代码超过 3 处 → 提取共享工具类
- O(n²) 操作（如 Skip().Take() 在循环中）→ 改用索引迭代
- COUNT + INSERT/UPDATE 非原子模式 → INSERT OR REPLACE

**常见 P2 问题**：
- 未使用的 using 引用
- 未使用的变量声明
- 魔法数字 → 提取为常量

### Phase 3: 重构执行（逐个优化项）

#### 3a. 提取共享工具类

当发现 3 个以上地方有相同逻辑时：

```bash
# 确认重复程度
grep -c "QuoteIdentifier" src/Pipeline/*.cs  # 3+ 次 → 提取

# 创建共享文件
write_file: src/Pipeline/SqlSugarHelpers.cs
```

**共享工具类设计原则**：
- 全部 `static` 方法，无状态
- 放在公共命名空间下
- 方法签名使用共享类型（如 `Config.TargetType` 而非具体类型）
- 包含 XML 文档注释

#### 3b. 修复运行时 bug

```csharp
// ❌ bug: deleted 从未递增，while 条件恒为 true
int deleted = 0;
do {
    // ... 删除逻辑 ...
    totalDeleted += rowsAffected;
    // deleted 永远 = 0
} while (deleted < 500000);

// ✅ 修复: 正确递增 deleted
do {
    // ... 删除逻辑 ...
    int rowsAffected = await ExecuteAsync(...).ConfigureAwait(false);
    deleted += rowsAffected;
    totalDeleted += rowsAffected;
} while (deleted < 500000);
```

#### 3c. 性能优化

```csharp
// ❌ O(n²): Skip/Take 每次遍历列表
for (int i = 0; i < rows.Count; i += batchSize) {
    var batch = rows.Skip(i).Take(batchSize).ToList();  // O(n) per iteration

// ✅ O(n): 索引迭代
for (int i = 0; i < rows.Count; i += batchSize) {
    int batchEnd = Math.Min(i + batchSize, rows.Count);
    // 直接遍历 rows[i..batchEnd]
```

#### 3d. 消除 dead code

| 模式 | 搜索策略 | 操作 |
|------|----------|------|
| 未使用的 using | `grep using → 确认类型未被引用` | 删除 using 行 |
| 未使用的变量 | `grep 变量名 → 确认无读取点` | 删除变量声明和使用 |
| 永远为 null 的检查 | 确认变量赋值路径 | 移除冗余 null 检查 |

### Phase 4: 编译验证（每次修改后）

```bash
# 增量编译
dotnet build src/application/ZL.DataSync/ZL.DataSync.csproj

# 全量重建（确认无缓存）
dotnet build --no-incremental src/application/ZL.DataSync/ZL.DataSync.csproj

# 检查是否有新的 CS 警告
dotnet build 2>&1 | grep "warning CS"
```

**必须通过的标准**：
- 零编译错误
- 零新产生的 CS 警告
- 原有 NuGet 警告（如 NU1903）可忽略

### Phase 5: 输出重构报告

```markdown
## 重构完成总结

### 新增文件
- **文件名** — 用途说明

### 修改文件
| 文件 | 修改内容 |

### 关键修复
- Bug 修复: 描述
- 性能优化: 描述

### 编译结果
- 零错误 ✅
- 零 CS 警告 ✅
```

## Common Refactor Patterns

### Pattern 1: 提取共享工具类

**触发条件**：相同逻辑出现 3+ 次，且参数类型一致。

**步骤**：
1. `grep` 确认重复次数
2. 创建 `StaticHelpers.cs`，将所有重复逻辑提取为 `public static` 方法
3. 逐个文件替换调用
4. 编译验证

### Pattern 2: 修复计数器未递增

**触发条件**：while/do-while 循环中有计数器声明但从未递增。

**步骤**：
1. 确认循环退出条件
2. 确认计数器应在何处递增
3. 在循环体内添加 `counter += affectedCount`
4. 编译验证

### Pattern 3: O(n²) → O(n) 循环优化

**触发条件**：循环体内使用 `Skip().Take()` 或类似操作。

**步骤**：
1. 确认循环边界和步长
2. 改用索引迭代 `for (int i = 0; i < count; i += step)`
3. 使用 `Math.Min(i + step, count)` 处理最后一次迭代
4. 编译验证

### Pattern 4: 消除未使用的 using/变量

**触发条件**：`grep` 确认变量/using 定义但无读取点。

**步骤**：
1. `grep 变量名 → 确认所有使用点`
2. 删除声明和使用
3. 编译验证

## Verification Checklist

重构完成后逐项确认：

- [ ] 所有修改文件通过 `dotnet build` 编译
- [ ] 无新增 CS 警告
- [ ] 共享工具类的方法签名与所有调用点匹配
- [ ] 无运行时 bug 引入（如计数器正确递增）
- [ ] 无性能退化（O(n²) → O(n) 确认）
- [ ] 死代码已完全清理
- [ ] 重构报告准确记录了所有改动

## Anti-Patterns

| 反模式 | 问题 | 正确做法 |
|--------|------|----------|
| 一次性重构所有文件 | 风险大，难以定位回归 | 逐个文件修改，每次编译验证 |
| 重构中引入新逻辑 | 改变行为，难以验证 | 只修改实现方式，不改变行为 |
| 不验证编译就继续 | 小错误积累 | 每次修改后立即 `dotnet build` |
| 删除未使用的代码不验证 | 可能误删实际使用的代码 | grep 确认无读取点后再删除 |
| 提取工具类不检查参数类型 | 类型不匹配导致编译错误 | 确认所有调用点的参数类型一致 |

## Key Patterns

### Pattern: 增量同步中 COUNT+INSERT/UPDATE → INSERT OR REPLACE

当需要在 SQLite 中实现 UPSERT 语义时：

```sql
-- ❌ 非原子: 多线程竞争导致 UNIQUE constraint failed
SELECT COUNT(*) FROM _SyncLog WHERE TableName = @tn;
-- 如果 COUNT = 0 → INSERT; 否则 → UPDATE

-- ✅ 原子: SQLite INSERT OR REPLACE 一步完成
INSERT OR REPLACE INTO _SyncLog (TableName, SyncTime) VALUES (@tn, @st);
```

**适用场景**：主键冲突时自动替换已有行，避免 COUNT + INSERT/UPDATE 的非原子问题。

### Pattern: 异步方法中的同步调用阻塞

```csharp
// ❌ 阻塞线程: 在 async Task 方法中调用同步方法
public async Task SyncTableAsync(...) {
    WriteSyncLog(localDb, tableName, processTime);  // 阻塞

// ✅ 异步: 使用异步变体
public async Task SyncTableAsync(...) {
    await WriteSyncLogAsync(localDb, tableName, processTime, ct).ConfigureAwait(false);
```

**影响**：同步调用在 `async` 方法中会阻塞线程池线程，降低并发性能。

## Output Format

重构报告应包含：

```
## 重构完成总结

### 新增文件
- **文件名** — 用途说明

### 修改文件
| 文件 | 修改内容 |

### 关键修复
- Bug 修复: 描述

### 性能优化
- 描述

### 编译结果
- 零错误 ✅
- 零 CS 警告 ✅
```
