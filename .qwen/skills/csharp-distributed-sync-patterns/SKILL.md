---
name: csharp-distributed-sync-patterns
description: 跨数据库/HTTP 数据同步管道的设计模式，包括策略分发、水位线管理、批量写入容错
source: auto-skill
extracted_at: '2026-06-08T12:21:03.755Z'
---

# C# 分布式数据同步管道模式

## When to Use

当需要实现从本地 SQLite 到多个远程目标（MySQL/PostgreSQL/SQL Server/HTTP API）的数据同步时，使用此技能。典型场景：
- IoT 设备数据采集后本地存储，再上传到远程服务器
- 边缘计算节点到云端的数据同步
- 离线优先应用的断网续传

## Core Principle

**每个策略只替换增量查询逻辑，复用公共基础设施（建表、批量写入、标记同步）。编译验证是完成的标准，不是起点。**

## Procedure

### Phase 1: 确定同步策略接口

```csharp
public interface ISyncStrategy
{
    string TargetName { get; }
    Task<SyncReport> SyncTableAsync(string tableName, string? remoteTable, int batchSize, SqlSugarClient localDb, CancellationToken ct);
}
```

**关键设计**：
- `SqlSugarClient` 而非 `ISqlSugarClient` — 需要 `Ado.SqlQuery<dynamic>` 等具体 API
- `localDb` 作为方法参数而非构造函数参数 — 确保每次使用最新共享连接

### Phase 2: 实现具体策略

#### 策略 A: DatabaseSyncStrategy（基于 `_Synced` 标记）

```
读取: SELECT * FROM table WHERE _Synced = 0 ORDER BY ProcessTime LIMIT @limit
写入: INSERT INTO remoteTable (col1, col2, ...) VALUES (...)
标记: UPDATE table SET _Synced = 1 WHERE Id IN (...)
```

适用场景：本地表有 `_Synced` 字段，用于标记是否已同步。

#### 策略 B: ProcessTimeSyncStrategy（基于 `_UploadFlag` / `_SyncLog`）

```
读取: SELECT * FROM table WHERE ProcessTime > @lastTime ORDER BY ProcessTime LIMIT @limit
写入: INSERT INTO remoteTable (col1, col2, ...) VALUES (...)
标记: INSERT OR REPLACE INTO _SyncLog (TableName, SyncTime) VALUES (...)
```

适用场景：与 PcStationIot RemoteSyncService 集成，使用 `_SyncLog` 水位线表，不修改业务表。

#### 策略 C: HttpSyncStrategy（基于 HTTP POST）

```
读取: SELECT * FROM table WHERE _Synced = 0 ORDER BY ProcessTime LIMIT @limit
写入: POST JSON payload to remote API
标记: UPDATE table SET _Synced = 1 WHERE Id IN (...)
```

适用场景：远程目标是 HTTP API（如 DataUploadServer），需要序列化 JSON 上传。

### Phase 3: 构建策略分发器（SyncEngine）

```csharp
private ISyncStrategy CreateStrategy(RemoteTargetConfig target)
{
    var localClient = _localDb as SqlSugarClient 
        ?? throw new InvalidOperationException("本地数据库必须是 SqlSugarClient");

    if (target.StrategyType == Config.SyncStrategyType.ProcessTime)
        return new ProcessTimeSyncStrategy(target, localClient, _logger);

    return target.Type switch
    {
        Config.TargetType.MySql or Config.TargetType.SqlServer or Config.TargetType.PostgreSql or Config.TargetType.Oracle =>
            new Pipeline.DatabaseSyncStrategy(target, localClient, _logger),
        Config.TargetType.Http =>
            new Pipeline.HttpSyncStrategy(target.HttpConfig ?? throw new InvalidOperationException(...), target.Name, _logger),
        _ => throw new ArgumentOutOfRangeException(nameof(target.Type))
    };
}
```

**关键设计**：
- `SyncEngine` 持有共享的 `ISqlSugarClient _localDb`（从外部传入），避免多连接冲突 SQLite
- 每个策略持有自己的 `SqlSugarScope _remoteScope`（管理远程连接生命周期）
- 构造函数支持 `sharedLocalClient` 参数保持向后兼容

### Phase 4: 批量写入容错

```csharp
// 批量写入，失败后逐条回退
try
{
    await InsertRowsViaAdoAsync(targetTable, batchValidRows, ct).ConfigureAwait(false);
    // 成功后再累加计数
    foreach (var row in batchValidRows) { ok++; successRows.Add(row); }
}
catch (Exception ex)
{
    _logger.Warning($"批量写入失败 {targetTable}: {ex.Message}");
    // 逐条回退
    for (int j = i; j < batchEnd; j++)
    {
        try { await InsertRowViaAdoAsync(targetTable, rows[j], ct).ConfigureAwait(false); ok++; }
        catch { fail++; }
    }
}
```

**设计决策**：
- 批量写入成功后才更新成功计数 — 避免"写入失败但计数增加"
- 逐条回退的隔离 — 一条失败不影响同批次其他行

### Phase 5: 公共基础设施（SqlSugarHelpers）

所有策略共享的工具方法：

| 方法 | 用途 | 被策略复用次数 |
|------|------|---------------|
| `QuoteIdentifier` | 防止 SQL 注入（表名/列名转义） | 3+ |
| `MapDbType` | DbType 枚举互转 | 3+ |
| `BuildBatchInsertSql` | 构建批量 INSERT SQL | 3+ |
| `BuildCreateTableSql` | 基于样本数据构建 CREATE TABLE | 3+ |
| `ConvertToDictionary` | dynamic → Dictionary 转换 | 3+ |
| `TryGetProcessTime` | 安全获取 ProcessTime 字段 | 3+ |
| `BatchMarkSyncedAsync` | 批量标记本地记录已同步 | 3+ |

## Anti-Patterns

| 反模式 | 问题 | 正确做法 |
|--------|------|----------|
| 每个策略自建建表逻辑 | 重复代码，维护困难 | 提取到 `SqlSugarHelpers.BuildCreateTableSql` |
| 在 `async` 方法中调用同步 DB 操作 | 阻塞线程池 | 使用 `ConfigureAwait(false)` + 异步方法 |
| Skip/Take 在循环中 | O(n²) 复杂度 | 改用索引迭代 `for (int i = 0; i < count; i += step)` |
| 批量写入失败后继续标记成功 | 数据不一致 | 只有写入成功后才更新成功计数 |
| 每个目标创建独立的 `SqlSugarClient` | SQLite 并发锁死 | 共享本地连接，远程各自创建 `SqlSugarScope` |
| 水位线用 COUNT + INSERT/UPDATE | 非原子操作，并发冲突 | 用 `INSERT OR REPLACE` 原子 UPSERT |

## Key Patterns

### Pattern 1: 水位线管理（_SyncLog）

```sql
-- 读取水位线
SELECT SyncTime FROM _SyncLog WHERE TableName = @tableName ORDER BY SyncTime DESC LIMIT 1;

-- 写入水位线（原子 UPSERT）
INSERT OR REPLACE INTO _SyncLog (TableName, SyncTime) VALUES (@tableName, @syncTime);
```

### Pattern 2: 双重检查锁（DCL）建表

```csharp
// 快速路径：已存在则跳过（无锁）
if (_remoteScope.DbMaintenance.IsAnyTable(targetTable, false))
    return;

// 构建 SQL（锁外执行，避免 lock 中 await）
string createSql = SqlSugarHelpers.BuildCreateTableSql(...);

lock (_createTableLock)
{
    // 双重锁：另一个线程可能在等待锁期间完成了建表
    if (_remoteScope.DbMaintenance.IsAnyTable(targetTable, false))
        return;
    _remoteScope.Ado.ExecuteCommand(createSql);
}
```

### Pattern 3: 共享 HttpClient

```csharp
private static readonly HttpClient s_http = new HttpClient(new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(30),
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),  // 短于 DNS 刷新
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
}) { Timeout = TimeSpan.FromSeconds(30) };
```

### Pattern 4: 指数退避重试

```csharp
int wait = Math.Min(
    (int)Math.Pow(2, Math.Min(failStreak, 8)) * _config.RetryBackoffSeconds,
    300);
await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false);
```

## Verification Checklist

- [ ] 每个策略实现 `ISyncStrategy` 接口
- [ ] 共享本地连接由 `SyncEngine` 管理，策略通过方法参数传入
- [ ] 批量写入有容错（批量 → 逐条回退）
- [ ] 水位线使用原子 UPSERT（INSERT OR REPLACE）
- [ ] 无 Skip/Take O(n²) 循环
- [ ] `dotnet build` 零错误 + 零新增 CS 警告
