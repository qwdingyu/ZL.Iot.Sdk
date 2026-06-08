---
name: csharp-interlocked-ref-fix
description: 修复 C# Interlocked 操作与非引用返回属性的兼容性问题和 nullable 列表类型不匹配
source: auto-skill
extracted_at: '2026-06-08T12:21:03.755Z'
---

# C# Interlocked 非引用属性修复 & Nullable 类型匹配

## When to Use

当 C# 项目编译出现以下错误时使用此技能：
- `CS0206: 非引用返回属性或索引器不能用作 out 或 ref 值` — 调用 `Interlocked.Add(ref obj.Property, ...)` 时
- `CS8620: 由于引用类型的可为 null 性差异，List<object?> 类型的实参不能用于 List<object> 类型的形参` — 泛型参数 nullable 不匹配时

## Core Principle

**先理解 C# 语言规则再修改代码。编译错误信息直接指出了根本原因，对症下药而非盲目修改。**

## Procedure

### Step 1: 诊断 CS0206（非引用返回属性）

**原因**：`Interlocked.Add` 签名是 `Add(ref long location1, int value)`，需要 `ref long`。当属性返回 `int` 值拷贝时，无法提供 `ref`。

```csharp
// ❌ 编译错误: TotalSynced 返回 int 值拷贝，无法 ref
public int TotalSynced { get => (int)Interlocked.Read(ref _totalSynced); set => ...; }
Interlocked.Add(ref _status.TotalSynced, report.SyncedCount);  // CS0206

// ✅ 修复方案 1: 直接操作 long 字段
internal long _totalSynced;  // 改为 internal（允许跨项目访问）
Interlocked.Add(ref _status._totalSynced, report.SyncedCount);

// ✅ 修复方案 2: 避免 Interlocked.Add，改用属性 setter
_status.TotalSynced += report.SyncedCount;  // 但这不是线程安全的

// ✅ 修复方案 3: 用 spinlock 保护属性操作
```

**关键判断**：
- 如果需要同线程安全的累加 → 方案 1（internal long 字段 + Interlocked.Add）
- 如果不在高并发路径 → 方案 3（spinlock 保护属性）

### Step 2: 诊断 CS8620（nullable 列表类型不匹配）

**原因**：`List<object?>` 和 `List<object>` 不是兼容的泛型参数类型（C# 不支持协变引用类型）。

```csharp
// ❌ 编译警告: 实参 List<object?> 不能赋给形参 List<object>
void BuildMarkSyncedSql(string table, DbType db, List<object>? ids, ...) { ... }
List<object?> rowIds = ...;  // 包含可空元素
BuildMarkSyncedSql(table, db, rowIds, ...);  // CS8620

// ✅ 修复: 统一参数类型为 object?
void BuildMarkSyncedSql(string table, DbType db, List<object?>? ids, ...) { ... }
```

**关键判断**：
- 如果方法内部只读取不修改 → 参数改为 `IEnumerable<object?>?`
- 如果方法内部会写入 → 参数改为 `List<object?>`

### Step 3: 修改后验证

```bash
dotnet build src/application/ZL.DataSync/ZL.DataSync.csproj --no-restore 2>&1 | tail -20
```

**通过标准**：
- 零 CS 错误
- 零新增 CS 警告（NuGet 警告如 NU1903 可忽略）

## Common Scenarios

### Scenario 1: 跨项目字段访问

当 `SyncStatus` 在 `ZL.DataSync.Core` 项目，`SyncEngine` 在 `ZL.DataSync` 项目：

```csharp
// 在定义项目的类中
internal long _totalSynced;  // 不是 private，允许 ZL.DataSync 访问
```

**为什么用 `internal` 而非 `public`**：
- `internal` 限制在同一程序集内可见
- 跨程序集需要 `public` + 封装层，增加复杂度
- 如果跨程序集，考虑用 `public` property + 在各自程序集内用 `Interlocked` 操作

### Scenario 2: 泛型方法 nullable 签名不匹配

当工具类方法签名与调用方类型不一致时：

```csharp
// 工具类方法（旧签名）
public static (string, List<SugarParameter>) BuildMarkSyncedSql(
    string tableName, DbType dbType, List<object>? ids, List<DateTime>? processTimes)

// 调用方传入类型
List<object?> ids = ...;  // 可空元素列表

// ✅ 修复：工具类签名改为 object?
public static (string, List<SugarParameter>) BuildMarkSyncedSql(
    string tableName, DbType dbType, List<object?>? ids, List<DateTime>? processTimes)
```

## Anti-Patterns

| 反模式 | 问题 | 正确做法 |
|--------|------|----------|
| 将字段改为 `public` 暴露内部状态 | 破坏封装，任何代码都可随意修改 | 用 `internal` 限制在程序集内 |
| 用 `(int)Interlocked.Add(ref property, val)` 强制转换 | 修改的是拷贝，原值不变 | 直接操作 long 字段 |
| 忽视 CS8620 警告 | 可能导致运行时 NullReferenceException | 统一 nullable 签名 |
| 在锁中 await | 阻塞线程且死锁风险 | 锁外构建 SQL，锁内执行同步操作 |

## Verification Checklist

- [ ] `Interlocked.Add` 操作的是 `long` 字段而非 `int` 属性
- [ ] 跨程序集字段访问使用 `internal` 而非 `public`
- [ ] 泛型方法参数 nullable 与调用方一致
- [ ] 编译零错误 + 零新增 CS 警告
- [ ] 原有 NuGet/警告不受影响
