---
name: iot-architecture-review
description: 对 .NET 工业 IoT SDK 模块进行架构级审查，关注连接管理、并发安全、配置验证和离线可靠性
source: auto-skill
extracted_at: '2026-06-08T06:10:00.000Z'
---

# IoT Architecture Review

## When to Use

当审查 / 重构 / 设计 .NET 工业 IoT 系统（特别是离线优先、SQLite 缓冲、多目标分发场景）中的模块时，使用此技能。

## Core Principles

1. **离线优先架构是核心约束** — 任何设计不能假设网络持续可用
2. **本地持久化是可靠性基石** — SQLite 缓冲层是数据不丢的最后防线
3. **连接管理是性能关键路径** — 频繁创建/销毁连接导致端口耗尽
4. **配置验证必须前置** — 配置错误应在启动时立即发现，而非运行时

## Review Checklist

### 连接与资源管理

- [ ] 数据库连接是否复用？（`IsAutoCloseConnection = false` + 共享 `SqlSugarClient`/`SqlSugarScope`）
- [ ] HttpClient 是否使用静态共享单例或 `IHttpClientFactory`？（避免端口耗尽）
- [ ] 所有 `IDisposable` 资源是否有正确的释放路径？（`Dispose` / `using`）
- [ ] 连接是否有连接池？（`SqlSugarScope` 内置连接池）

### 并发安全

- [ ] 共享状态是否有锁保护？（如 `_strategyLock` 保护 `_targetEntries`）
- [ ] 并发建表是否有双重锁？（check-then-act 模式）
- [ ] 取消令牌是否正确传播？（每个 async 方法都检查 `ct.IsCancellationRequested`）
- [ ] `StopAsync` 是否有超时保护？（防止优雅停止无限等待）

### 离线可靠性

- [ ] 数据是否先写本地再同步？（`_Synced` 标记模式）
- [ ] 失败时是否保留未同步数据？（失败时不标记 `_Synced`）
- [ ] 恢复后是否从断点继续？（读 `_Synced=0` 而非从头开始）
- [ ] 是否有过期数据清理？（`CleanupLoopAsync`）
- [ ] 清理是否分批执行？（避免大事务锁定）

### 配置验证

- [ ] 关键配置项是否有非空/非零校验？（`ValidateConfig`）
- [ ] 配置错误是否在启动时立即发现？（注册阶段 vs 运行阶段）
- [ ] 是否有配置默认值？（避免空引用）
- [ ] 目标名称是否有唯一性检查？

### 安全性

- [ ] SQL 是否参数化？（避免注入）
- [ ] 连接字符串是否明文存储？（提示使用加密）
- [ ] HTTP 请求是否有超时限制？
- [ ] 表名/列名转义是否完整？（`QuoteIdentifier`）

### 扩展性

- [ ] 新增目标类型是否需要修改核心逻辑？（策略模式解耦）
- [ ] 自定义日志是否可行？（`IStructuredLogger` 接口）
- [ ] 批次大小是否可配置？（`BatchSize`）
- [ ] 同步间隔是否可配置？（`SyncIntervalSeconds`）

## Key Patterns for IoT Offline-First

### Pattern 1: `_Synced` 标记模式

```
采集侧: INSERT → _Synced = false
同步侧: SELECT WHERE _Synced = false → 远程写入 → UPDATE _Synced = true
```

**优点**: 简单、可靠、无需复杂的消息队列
**缺点**: 无法感知远程端部分写入、需要远程端支持幂等

### Pattern 2: 自动建表 + 类型推断

```
IF NOT EXISTS target_table THEN
    读取样本行 → 推断列类型 → CREATE TABLE
```

**优点**: 零手动配置
**缺点**: 类型推断不精确（nullable 类型、自定义类型）、不支持索引/约束

### Pattern 3: 指数退避重试

```
failStreak++
wait = min(2^failStreak * backoff, maxBackoff)
```

**优点**: 应对暂时性故障
**缺点**: 没有重试次数上限（依赖退避上限）

### Pattern 4: 失败降级

```
批量写入 → 失败 → 逐条写入
Id 标记 → 失败 → ProcessTime 标记
```

**优点**: 最大化成功率
**缺点**: 性能下降、可能误标记

## Common Pitfalls in IoT SDKs

| 陷阱 | 后果 | 避免方式 |
|------|------|----------|
| 每次写入创建新 HttpClient | 端口耗尽 | 静态共享或 IHttpClientFactory |
| 每次查询创建新数据库连接 | 连接池耗尽 | 共享 SqlSugarClient/Scope |
| 同步失败后仍标记 _Synced=1 | 数据丢失 | 远程成功后才标记 |
| 清理一次性删除百万行 | 大事务锁表 | 分批删除（5000 条/批） |
| 配置错误在运行时报错 | 启动即崩溃 | 注册时 ValidateConfig |
| README 示例引用不存在的类型 | 用户照抄报错 | 文档与源码同步 |
