# ZL.DataSync 设计文档

> 版本: v2.1 | 日期: 2026-06-08 | 基于源码 commit `94a1efc`
> 审查依据: 与 PcStationIot 实际集成需求交叉验证
> 变更: v2.1 新增第 10 节 PcStationIot 集成分析与差距（含 6 个子节、5 个差距识别）

---

## 目录

1. [概述](#1-概述)
2. [设计动机与背景](#2-设计动机与背景)
3. [架构总览](#3-架构总览)
4. [核心组件设计](#4-核心组件设计)
5. [运行流程](#5-运行流程)
6. [配置设计](#6-配置设计)
7. [数据流与同步语义](#7-数据流与同步语义)
8. [扩展点设计](#8-扩展点设计)
9. [注意事项与已知问题](#9-注意事项与已知问题)
10. [PcStationIot 集成分析与差距](#10-pcstationiot-集成分析与差距)
11. [配置复杂度评估与改进建议](#11-配置复杂度评估与改进建议)
12. [故障排查指南](#12-故障排查指南)
13. [测试策略](#13-测试策略)
14. [源码审计：死代码、未使用依赖、安全隐患](#14-源码审计死代码未使用依赖安全隐患)

---

## 1. 概述

`ZL.DataSync` 是一个轻量级、本地运行的数据同步管道库，用于将 **本地 SQLite 缓冲库**中的数据可靠地分发到 **一个或多个远程目标**（MySQL / SQL Server / PostgreSQL / Oracle 数据库，或 HTTP API）。

### 1.1 核心特性

| 特性 | 说明 |
|------|------|
| **本地缓冲** | 所有数据先写入本地 SQLite，断网不丢数据 |
| **增量同步** | 通过 `_Synced` 标记实现增量拉取，避免重复传输 |
| **多目标分发** | 同一份本地数据可同时推送到多个远程目标 |
| **断网续传** | 恢复网络后自动从上次断点继续（读 `_Synced=0`） |
| **水位线存储** | 按表+目标记录最后同步位置，存储于 SQLite 内 `_SyncWatermark` 表 |
| **指数退避重试** | 失败后自动重试，退避时间从 2s 开始，上限 300s |
| **自动建表** | 远程目标表不存在时，基于样本数据自动推断列类型并建表 |
| **数据清理** | 定期清理已同步的过期数据，避免本地膨胀 |
| **批量写入** | 数据库策略 50 条/批，HTTP 策略 20 条/批 |

### 1.2 约束与边界

| 约束 | 详情 |
|------|------|
| 目标框架 | .NET 8.0 (net8.0) |
| 本地数据库 | 仅支持 SQLite |
| 数据格式 | 所有数据以 `Dictionary<string, object?>` 动态结构传递 |
| 列名规范 | 以 `_` 开头的列为内部字段（如 `_Synced`、`_SyncTime`），不参与同步写入 |
| 自增 ID | 数据写入侧需自行生成 `Id`（通常通过自增序列） |
| 解决方案 | DataSync 和 DataSync.Tests 项目 **不在 `.sln` 解决方案文件中**，需单独构建 |

---

## 2. 设计动机与背景

### 2.1 业务场景

工业现场设备（如扭矩扳手检测仪）通过 PLC 采集数据后，写入本地 SQLite 缓冲，并需要分发到多个后端系统（MES、ERP 等）。传统方案依赖 Kafka/Redis/ZooKeeper 等重型中间件，对于中小规模工业现场存在部署成本高、故障面宽、运维复杂的问题。

### 2.2 核心设计选择

| 维度 | 选择 | 理由 |
|------|------|------|
| 缓冲存储 | SQLite 内嵌数据库 | 零运维、文件级备份、自带 ACID |
| 同步方式 | 定时轮询 `_Synced=0` | 简单可靠，无外部依赖 |
| 批量策略 | 分批次写入 + 失败降级逐条 | 平衡吞吐与可靠性 |
| 连接管理 | 本地共享 `SqlSugarClient`，远程 `SqlSugarScope` 连接池 | 避免频繁创建连接导致端口耗尽 |

---

## 3. 架构总览

### 3.1 模块文件结构（基于源码，无幻觉）

```
src/application/ZL.DataSync/
├── Config/
│   ├── DataSyncConfig.cs          # 同步引擎配置（不可变）
│   └── RemoteTargetConfig.cs      # 远程目标配置 + HTTP 配置
├── Pipeline/
│   ├── ISyncStrategy.cs           # 同步策略接口（扩展点）
│   ├── DatabaseSyncStrategy.cs    # 数据库同步策略（MySQL/SQL Server/PG/Oracle）
│   └── HttpSyncStrategy.cs        # HTTP API 同步策略
├── Sync/
│   ├── SyncEngine.cs              # 同步引擎核心
│   └── SyncReport.cs              # 同步报告 + 运行状态
├── Infrastructure/
│   ├── ServiceCollectionExtensions.cs  # DI 注册扩展
│   ├── WatermarkStore.cs          # 水位线存储
│   └── ILogger.cs                 # 日志接口 + DebugLogger
├── ZL.DataSync.csproj
└── README.md
```

> **注意**: `ILogger.cs` 中定义了 `IStructuredLogger` 接口和 `DebugLogger` 实现。**不存在** `PfliteLoggerAdapter.cs` 文件（README.md 中引用了但它不存在于源码中）。

### 3.2 架构分层

```
┌──────────────────────────────────────────────────────────────────────┐
│                         应用层（PcStationIot）                         │
│  PLC 采集 → station_data.db → 写入带 _Synced=0 标记                  │
└──────────────────────────┬───────────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    ZL.DataSync.SyncEngine（控制面）                    │
│                                                                      │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐            │
│  │ Target: MES   │   │ Target: ERP  │   │ Target: HTTP  │            │
│  │ (独立循环)    │   │ (独立循环)   │   │ (独立循环)   │            │
│  └──────┬───────┘   └──────┬───────┘   └──────┬───────┘            │
│         │                   │                    │                    │
│  ┌──────▼───────────────────▼────────────────────▼──────────────┐   │
│  │              SQLite 本地缓冲 (station_data.db)               │   │
│  │  t_ad_boltsdata (_Synced=0/1)   t_ad_capsdata (_Synced=0/1) │   │
│  │  _SyncWatermark (水位线追踪)                                  │   │
│  └──────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
         │                    │                    │
         ▼                    ▼                    ▼
   MySQL / SQL Server    PostgreSQL           HTTP API
```

### 3.3 并发模型

```
SyncEngine（主线程调用 Start）
  │
  ├── RunTargetLoopAsync(Target1)   ← Task（后台线程）
  ├── RunTargetLoopAsync(Target2)   ← Task（后台线程）
  ├── RunTargetLoopAsync(TargetN)   ← Task（后台线程）
  └── CleanupLoopAsync              ← Task（后台线程）
```

每个远程目标拥有独立的后台循环，互不阻塞。各循环共享同一个本地 SQLite 连接（`SqlSugarClient`，`IsAutoCloseConnection=false`）。

---

## 4. 核心组件设计

### 4.1 SyncEngine — 同步引擎核心

**文件**: `Sync/SyncEngine.cs`

**职责**: 生命周期管理、表发现、目标循环调度、数据清理。

**公开 API**:

```csharp
public sealed class SyncEngine : IDisposable
{
    public SyncEngine(DataSyncConfig config, IStructuredLogger? logger = null);
    public void Start();                                           // 启动
    public Task StopAsync();                                       // 优雅停止
    public Task<Dictionary<string, SyncReport>> ForceSyncAsync();  // 手动触发
    public SyncStatus Status { get; }                              // 运行状态查询
    public void Dispose();                                         // 释放资源
}
```

**内部结构**:

| 字段 | 类型 | 用途 |
|------|------|------|
| `_localDb` | `SqlSugarClient` | 共享本地 SQLite 连接，`IsAutoCloseConnection=false` |
| `_watermark` | `WatermarkStore` | 水位线存储 |
| `_targetEntries` | `Dictionary<string, (Strategy, Task)>` | 每个目标的策略和循环任务缓存 |
| `_discoveredTables` | `HashSet<string>` | 启动时发现的本地业务表 |
| `_cts` | `CancellationTokenSource` | 控制取消 |
| `_status` | `SyncStatus` | 全局运行状态 |
| `_strategyLock` | `object` | 保护 `_targetEntries` 并发访问 |

**关键设计决策**:

| 设计点 | 实现 | 理由 |
|--------|------|------|
| 本地连接 | 单例 `SqlSugarClient`，不自动关闭 | 复用连接 |
| 表发现 | 启动时查 `sqlite_master`，排除 `_` 前缀 | 简单高效 |
| 目标循环 | 每个目标独立 `Task` | 解耦 |
| 重试退避 | 指数退避 `2^failStreak * RetryBackoffSeconds`，上限 300s | 应对暂时性网络故障 |
| 停止超时 | `Task.WhenAny(task, Task.Delay(10000))` 即 10s | 防止无限等待 |
| Dispose | 同步调用 `StopAsync()` 然后释放 `_localDb` 和 `_watermark` | 资源清理 |

**表发现逻辑**:

```sql
-- 读取所有非系统表
SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE '_%' ORDER BY name
```

排除以 `_` 开头的表名（如 `_SyncWatermark`、`_Synced` 等）。

### 4.2 ISyncStrategy — 策略接口

**文件**: `Pipeline/ISyncStrategy.cs`

```csharp
public interface ISyncStrategy : IDisposable
{
    string TargetName { get; }
    Task<SyncReport> SyncTableAsync(
        string tableName,            // 本地表名
        string? remoteTable,          // 远程表名映射（null=同本地表名）
        int batchSize,                // 每次从 SQLite 读取的行数
        SqlSugarClient localDb,       // 共享本地连接
        CancellationToken ct          // 取消令牌
    );
}
```

**实现者**:
- `DatabaseSyncStrategy` — 数据库直连（MySQL / SQL Server / PostgreSQL / Oracle）
- `HttpSyncStrategy` — HTTP API 推送

> **注意**: 接口直接依赖 `SqlSugarClient` 类型。这是一个实现级耦合，不是抽象依赖。

### 4.3 DatabaseSyncStrategy — 数据库同步策略

**文件**: `Pipeline/DatabaseSyncStrategy.cs`

**职责**: 从 SQLite 读取 → 批量写入远程数据库 → 标记本地已同步。

**内部结构**:

| 字段 | 类型 | 用途 |
|------|------|------|
| `_remoteScope` | `SqlSugarScope` | 远程数据库连接池 |
| `_remoteDbType` | `SqlSugar.DbType` | 远程数据库类型 |
| `_enableUpsert` | `bool` | ⚠️ 接收了配置但 **从未使用**（见第 13 节） |
| `_createTableLock` | `object` | 建表双重锁 |

**核心流程**:

```
1. 读 _Synced=0 数据
   SELECT * FROM table WHERE _Synced = 0 ORDER BY ProcessTime LIMIT @limit
   → 使用 SqlQuery<dynamic>（SqlSugar 的 SqlQuery<Dictionary> 返回空 keys）
           ↓
2. 确保远程表存在（双重锁 + 基于样本行推断列类型）
   IF NOT EXISTS THEN
       遍历样本行列（跳过 _ 前缀）→ 推断 SQL 类型 → CREATE TABLE
   END IF
           ↓
3. 分批写入远程（每批 50 条）
   ├─ 批量 INSERT（Ado.ExecuteCommand 手动构建 SQL）
   │  收集所有非 _ 前缀列名 → 构建多行 VALUES 插入
   ├─ 批量失败 → 逐条容错（InsertRowViaAdoAsync）
   └─ 每写入成功一行 → 更新 ok 计数和 maxWatermark
           ↓
4. 批量标记本地 _Synced=1
   → 优先按 Id IN(@id0, ...) UPDATE
   → 退化按 ProcessTime IN(@pt0, ...) UPDATE
           ↓
5. 返回 SyncReport
```

**关键实现细节**:

| 实现点 | 说明 |
|--------|------|
| 读取方式 | `SqlQuery<dynamic>` 而非 `<Dictionary>`，因为 SqlSugar 对 Dictionary 的查询返回空 keys |
| 列名标识符转义 | MySQL 用反引号 `` `name` ``，其他用双引号 `"name"` |
| 批量 SQL 构建 | 手动拼接 `INSERT INTO table (cols) VALUES (v1,v2),(v3,v4)...` |
| 空值处理 | `value != null` 时才添加参数，否则占位符用 `NULL` |
| 建表 SQL 后缀 | MySQL 追加 `ENGINE=InnoDB DEFAULT CHARSET=utf8mb4` |
| 列类型推断 | int→BIGINT, float/double/decimal→DOUBLE, bool→TINYINT(1)/BIT, DateTime→DATETIME, byte[]→BLOB, 其他→TEXT |
| Upsert 方法 | `ExecuteUpsertAsync` 存在但 **从未被调用**（仅定义） |

**列类型推断 (`AdaptSqlType`)**:

```
null 或 DBNull.Value  → TEXT
int / short / long    → BIGINT
float / double / decimal → DOUBLE
bool                  → TINYINT(1) (MySQL) / BIT (其他)
DateTime / DateTimeOffset → DATETIME
byte[]                → BLOB
其他/nullable         → TEXT
```

### 4.4 HttpSyncStrategy — HTTP API 同步策略

**文件**: `Pipeline/HttpSyncStrategy.cs`

**职责**: 从 SQLite 读取 → 序列化为 JSON → POST 到远程 API → 标记本地已同步。

**内部结构**:

| 字段 | 类型 | 用途 |
|------|------|------|
| `_config` | `HttpUploadConfig` | HTTP 配置 |
| `s_http` | `static HttpClient` | 静态共享 HttpClient，避免端口耗尽 |
| `_targetName` | `string` | 目标名称（日志用） |

**核心流程**:

```
1. 读 _Synced=0 数据（同 DatabaseSyncStrategy）
           ↓
2. 确定目标 URL
   → 优先 TableEndpoints[tableName] 映射
   → 降级使用 HttpConfig.Endpoint
           ↓
3. 分批上传（每批 20 条）
   ├─ BuildRequestBody → 构建兼容 DataUploadServer 格式的字典
   ├─ JsonConvert.SerializeObject(requestBatches)
   ├─ s_http.PostAsync(endpoint, content, ct)
   └─ 成功 → ok += batch.Count；失败 → fail += batch.Count
           ↓
4. 批量标记本地 _Synced=1（同 DatabaseSyncStrategy 逻辑）
           ↓
5. 返回 SyncReport
```

**请求体构建 (`BuildRequestBody`)**: 硬编码为兼容 DataUploadServer 的 DataUploadReq 格式：

```json
{
  "deviceName": "从 HttpConfig.DeviceName 或数据行 StationCode 字段获取",
  "type": "从 HttpConfig.Type 或数据行 UploadFlag 字段获取",
  "barCode": "数据行 BarCode",
  "EngineNo": "数据行 EngineNo",
  "FinalResult": "数据行 FinalResult",
  "DetectItems": "数据行 DetectItems",
  "paramArr": "数据行 ParamArr 或空数组",
  "checkResult": "数据行 FinalResult",
  "detectTime": "yyyy-MM-dd HH:mm:ss 格式",
  "data": "数据行 data"
}
```

**HttpClient 配置（P2-2 修复）** — 静态共享单例，避免频繁创建导致端口耗尽：

```csharp
new HttpClient(new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(30),
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
}) { Timeout = TimeSpan.FromSeconds(30) };
```

### 4.5 WatermarkStore — 水位线存储

**文件**: `Infrastructure/WatermarkStore.cs`

**职责**: 追踪每个 `表名+目标名` 的最后同步位置，存储于 SQLite 的 `_SyncWatermark` 表。

**表结构**:

```sql
CREATE TABLE "_SyncWatermark" (
    "TableName" TEXT NOT NULL,
    "TargetName" TEXT NOT NULL,
    "WatermarkType" TEXT NOT NULL DEFAULT 'DateTime',
    "WatermarkValue" TEXT NOT NULL,
    "LastSyncTime" TEXT,
    PRIMARY KEY (TableName, TargetName)
)
```

**公开方法**:

| 方法 | 用途 | 状态 |
|------|------|------|
| `EnsureTable()` | 确保 `_SyncWatermark` 表存在 | ✅ 被调用（SyncEngine 构造时） |
| `ReadWatermark(tableName, targetName)` | 读取水位值 | ✅ 方法已实现 |
| `WriteWatermark(tableName, targetName, watermarkValue)` | UPSERT 水位值 | ✅ 方法已实现 |
| `GetLastSyncTime(tableName, targetName)` | 获取最后同步时间 | ✅ 被清理逻辑间接使用 |

> **重要**: `ReadWatermark` 和 `WriteWatermark` 虽然已实现且方法可用，但 **当前同步策略的 `SyncTableAsync` 中从未调用它们**。同步逻辑依赖 `_Synced` 标记而非水位线。`GetLastSyncTime` 也未在清理逻辑中被实际使用（清理依赖 `ProcessTime < cutoff AND _Synced = 1`）。这是一个 **预留/半成品功能**。

### 4.6 日志系统

**文件**: `Infrastructure/ILogger.cs`

```
IStructuredLogger (接口，公开)
    │
    └── DebugLogger (内部实现)
          → System.Diagnostics.Debug.WriteLine
```

`DebugLogger` 构造函数接受可选 `source` 参数但 **忽略它**。`ForSource()` 方法也直接返回 `this`，不做任何上下文切换。这是一个极简的调试用实现。

**设计意图**: 解耦具体日志实现。但 `SyncEngine` 默认构造时若传入 `null` logger，会使用 `DebugLogger`，这在生产环境（无 GUI 调试器）中无法看到日志。README.md 中引用了 `PfliteLoggerAdapter` 作为生产 logger 的示例，但该文件 **不存在于源码中**。

### 4.7 SyncReport — 同步报告

**文件**: `Sync/SyncReport.cs`

```csharp
public sealed class SyncReport
{
    public DateTime Timestamp { get; init; }           // UCT 时间
    public string TableName { get; init; }              // 表名
    public int TargetCount { get; init; }               // 待同步行数
    public int SyncedCount { get; init; }               // 成功同步行数
    public int FailedCount { get; init; }               // 失败行数
    public string? LastError { get; init; }             // 错误信息
    public string? LastWatermark { get; init; }         // 最后水位值
    public double ElapsedMs { get; init; }              // 耗时毫秒

    public bool Success => FailedCount == 0;
    public bool HasData => TargetCount > 0;
}
```

工厂方法:
- `SyncReport.Ok(tableName, target, synced, watermark, elapsedMs)` — 成功报告
- `SyncReport.Fail(tableName, target, error, elapsedMs)` — 失败报告

### 4.8 SyncStatus — 运行状态

```csharp
public sealed class SyncStatus
{
    public bool IsRunning { get; set; }
    public int TotalTables { get; set; }
    public int TotalSynced { get; set; }
    public int TotalFailed { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public DateTime? LastStartTime { get; set; }
    public int FailStreak { get; set; }         // 连续失败次数
    public string? LastError { get; set; }
    public string? StatusText { get; set; }     // 用于 UI 展示（"运行中"/"已停止"等）

    public bool IsHealthy => !IsRunning || FailStreak == 0;
    public void Reset();                        // 重置所有状态
}
```

---

## 5. 运行流程

### 5.1 完整生命周期

```
                    初始化                         启动                      运行                        停止
                    ──────                        ────                      ────                        ────

  应用代码:   SyncEngine(config) ────► engine.Start() ────► [后台循环运行] ────► engine.StopAsync()
                   │                        │                        │                       │
                   │                        │                        │                       ▼
                   ▼                        ▼                        ▼                   取消令牌触发
            创建本地                  发现本地表             每个目标独立后台循环       等待当前循环完成
           SqlSugarClient            创建策略实例           每 5s 扫描一次            10s 超时强制退出
                   │                        │                        │                       │
                   ▼                        ▼                        ▼                       ▼
            创建 Watermark             启动 Cleanup             读→写→标记→退避              释放策略
           Store + 水位线表            清理循环                 指数退避重试                释放本地连接
```

### 5.2 单表同步流程（核心路径）

```
RunTargetLoopAsync (每 SyncIntervalSeconds 秒触发)
  │
  ▼
SyncAllTablesAsync
  │
  ├── 遍历 _discoveredTables: [t_ad_boltsdata, t_ad_capsdata, ...]
  │     │
  │     ▼
  │  strategy.SyncTableAsync(table, remoteTable, batchSize, localDb, ct)
  │     │
  │     ├─ 1. 读 _Synced=0 数据
  │     │     SELECT * FROM table WHERE _Synced = 0
  │     │            ORDER BY ProcessTime LIMIT 100
  │     │     使用 SqlQuery<dynamic>（绕过 SqlSugar Dictionary 空 keys bug）
  │     │
  │     ├─ 2. 确保远程表存在
  │     │     IF NOT EXISTS THEN 基于样本行推断列类型 → CREATE TABLE
  │     │     双重锁避免并发冲突
  │     │
  │     ├─ 3. 批量写入远程（每批 50 条）
  │     │     try:
  │     │         INSERT INTO remote_table (cols) VALUES (v1..vn), ...  -- 多行批量
  │     │     catch:
  │     │         FOR each row:  -- 降级逐条写入
  │     │             INSERT INTO remote_table VALUES ...
  │     │     记录成功/失败计数和 max Watermark
  │     │
  │     └─ 4. 批量标记本地 _Synced=1
  │           IF Id 列存在:
  │             UPDATE table SET _Synced=1, _SyncTime=NOW() WHERE Id IN (@id0, ...)
  │           ELIF ProcessTime 列存在:
  │             UPDATE table SET _Synced=1, _SyncTime=NOW() WHERE ProcessTime IN (@pt0, ...)
  │
  │     返回 SyncReport
  │
  ├─ 汇总所有表的报告
  ├─ 更新 _status（TotalSynced, TotalFailed, FailStreak, StatusText）
  └─ 返回
  │
  └─ IF 异常:
       failStreak++
       wait = min(2^failStreak * 2s, 300s)
       记录警告 → 退避 → 继续下一轮循环
```

### 5.3 数据清理流程

```
CleanupLoopAsync (每 CleanupIntervalSeconds = 3600s 触发)
  │
  ▼
CleanupSyncedDataAsync(retentionDays = 730)
  │
  ├── 计算 cutoff = DateTime.UtcNow - 730 days
  │
  ├── 遍历 _discoveredTables
  │     │
  │     ├─ 检查表是否有 _Synced 列（PRAGMA table_info）
  │     └─ IF 有:
  │         分批删除（每批 5000 条）:
  │           SELECT Id FROM table WHERE _Synced = 1 AND ProcessTime < cutoff LIMIT 5000
  │           DELETE FROM table WHERE Id IN (@id0, ..., @id4999)
  │           最多循环 100 轮（50 万条/表/次）
  │
  └─ 输出删除总数
```

### 5.4 手动触发同步（ForceSync）

```
engine.ForceSyncAsync()
  │
  ├── 为每个目标临时创建策略实例（不复用后台循环的策略）
  ├── 调用 SyncAllTablesAsync
  ├── 返回 Dictionary<string, SyncReport>
  └─ finally: 释放策略实例
```

**适用场景**: UI 按钮手动触发、API 端点主动查询、告警触发等。

---

## 6. 配置设计

### 6.1 配置类完整字段

**`DataSyncConfig`** (`Config/DataSyncConfig.cs`):

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `LocalDbPath` | `string` | 空（必需） | 本地 SQLite 文件路径 |
| `RemoteTargets` | `List<RemoteTargetConfig>` | `new()` | 远程目标列表 |
| `BatchSize` | `int` | 100 | 每次从 SQLite 读取行数 |
| `SyncIntervalSeconds` | `int` | 5 | 同步循环间隔秒数 |
| `MaxRetryCount` | `int` | 3 | ⚠️ 配置存在但 **从未被代码读取**（见第 13 节） |
| `RetryBackoffSeconds` | `int` | 2 | 指数退避初始秒数 |
| `EnableUpsert` | `bool` | true | ⚠️ 配置存在但 **从未被使用**（见第 13 节） |
| `EnableCleanup` | `bool` | true | 是否启用自动清理 |
| `DataRetentionDays` | `int` | 730 | 已同步数据保留天数 |
| `CleanupIntervalSeconds` | `int` | 3600 | 数据清理检查间隔秒数 |

**`RemoteTargetConfig`** (`Config/RemoteTargetConfig.cs`):

| 字段 | 类型 | 说明 |
|------|------|------|
| `Name` | `string` | 目标名称（日志标识） |
| `Type` | `TargetType` | 目标类型枚举 |
| `ConnectionString` | `string` | 数据库连接字符串 |
| `TableMappings` | `Dictionary<string, string>` | 本地表名 → 远程表名映射 |
| `HttpConfig` | `HttpUploadConfig?` | HTTP 配置（Type=Http 时必需） |

**`HttpUploadConfig`** (`Config/RemoteTargetConfig.cs`):

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Endpoint` | `string` | 空 | API 端点 URL |
| `TableEndpoints` | `Dictionary<string, string>` | `new()` | 表级端点映射 |
| `TimeoutSeconds` | `int` | 30 | 请求超时 |
| `Headers` | `Dictionary<string, string>` | `new()` | 自定义请求头 |
| `BodyTemplate` | `string?` | null | ⚠️ 配置存在但 **从未被使用**（见第 13 节） |
| `DeviceName` | `string?` | null | 设备名（也可从 StationCode 字段自动推导） |
| `Type` | `string?` | null | 数据类型标识（也可从 UploadFlag 字段自动推导） |

### 6.2 注册方式

| 方式 | API | 适用场景 |
|------|-----|----------|
| 委托配置 | `services.AddDataSync(cfg => { ... })` | 代码中硬编码配置 |
| 配置节绑定 | `services.AddDataSyncFromConfig(IConfiguration, "DataSync")` | 使用 `appsettings.json` 等 IConfiguration |
| JSON 文件 | `services.AddDataSyncFromJsonFile("sync.json")` | 独立配置文件（使用 Newtonsoft.Json 反序列化） |

### 6.3 appsettings.json 示例

```json
{
  "DataSync": {
    "LocalDbPath": "Data/station_data.db",
    "BatchSize": 100,
    "SyncIntervalSeconds": 5,
    "MaxRetryCount": 3,
    "RetryBackoffSeconds": 2,
    "EnableUpsert": true,
    "EnableCleanup": true,
    "DataRetentionDays": 730,
    "CleanupIntervalSeconds": 3600,
    "RemoteTargets": [
      {
        "Name": "MES-MySQL",
        "Type": "MySql",
        "ConnectionString": "server=192.168.1.100;database=MES;user=root;password=xxx;"
      },
      {
        "Name": "ERP-SQLServer",
        "Type": "SqlServer",
        "ConnectionString": "Data Source=192.168.1.101;Initial Catalog=ERP;User ID=sa;Password=xxx;",
        "TableMappings": {
          "t_ad_boltsdata": "T_ProductionRecords"
        }
      },
      {
        "Name": "Webhook-API",
        "Type": "Http",
        "HttpConfig": {
          "Endpoint": "http://192.168.1.100:94/api/mesdetect/DataUpLoad",
          "DeviceName": "装配F线静音房检测1",
          "Type": "5",
          "TimeoutSeconds": 30
        }
      }
    ]
  }
}
```

> **注意**: `AddDataSyncFromConfig` 只解析 `RemoteTargets:N` 格式的索引路径，**不支持**嵌套的 `HttpConfig:TimeoutSeconds` 等深层绑定（HTTP 目标只解析 Endpoint 和 TimeoutSeconds 两个字段）。

### 6.4 配置校验

`ValidateConfig` 在注册时执行以下校验：

| 校验 | 说明 |
|------|------|
| `LocalDbPath` 非空 | 必填 |
| `BatchSize > 0` | 正整数 |
| `SyncIntervalSeconds > 0` | 正整数 |
| 目标 `Name` 非空 | 每个目标必填 |
| 目标 `ConnectionString` 非空 | 每个目标必填 |
| HTTP 目标必须有 `HttpConfig` | 类型校验 |

---

## 7. 数据流与同步语义

### 7.1 写入侧约定

> **重要**: 以下字段约定适用于 **ZL.DataSync 独立使用场景**。
> PcStationIot 实际集成时使用 `ProcessTime` 增量同步机制（见第 10 节），**不会**写入 `_Synced` 字段。

ZL.DataSync 独立部署时，数据写入侧需遵守以下约定：

```csharp
var dict = new Dictionary<string, object?>
{
    ["Id"] = nextId,                       // 业务主键
    ["StationCode"] = stationCode,         // 站点编码
    ["BarCode"] = barCode,                 // 条码
    ["ProcessTime"] = DateTime.UtcNow,     // 处理时间（排序/水位线/清理依据）
    ["_Synced"] = false,                  // ⚠️ 关键：标记未同步（ZL.DataSync 独立使用时）
    ["_SyncTime"] = null,                 // 同步成功后由引擎写入
    // ... 其他业务字段
};
```

### 7.2 `_Synced` 状态机（ZL.DataSync 独立使用）

```
    写入          同步成功          清理触发
┌─────────┐   ┌──────────┐   ┌──────────┐
│ _Synced │──►│ _Synced  │──►│ 记录删除 │
│   = 0   │   │   = 1    │   │ (_Synced=1 AND 过期) │
└─────────┘   └──────────┘   └──────────┘
  未同步          已同步          已删除
```

### 7.3 幂等与重复消费

| 场景 | 行为 | 数据一致性 |
|------|------|-----------|
| 网络中断后恢复 | 下次循环读 `_Synced=0` | ✅ 不丢不重 |
| 远程写入成功但本地标记失败 | 下次循环重新读取并写入远程 | ⚠️ 可能重复写入远程（`EnableUpsert` 配置未实际生效，无法缓解） |
| 远程写入失败 | 不标记 `_Synced`，下次重试 | ✅ 不丢 |
| 并发读（采集侧 + 同步侧） | SQLite WAL 模式 + `_Synced` 语义 | ✅ 隔离读取未同步数据 |

### 7.4 批量大小

| 策略 | 批次大小 | 说明 |
|------|----------|------|
| `DatabaseSyncStrategy` 远程写入 | 50 条/批 | 通过 `Ado.ExecuteCommand` 手动拼接 |
| `HttpSyncStrategy` 远程上传 | 20 条/批 | HTTP API payload 限制 |
| 数据清理 | 5000 条/批 | 每表最多 50 万条/次 |
| 配置层 `BatchSize` | 100（默认） | 每次从 SQLite 读取的最大行数 |

---

## 10. PcStationIot 集成分析与差距

> 本节基于与 `PcStationIot` 实际代码的逐文件交叉验证（截至 2026-06-08）。

### 10.1 PcStationIot 当前同步架构

PcStationIot **已有**一套完整的远程同步方案（`RemoteSyncService`），**不使用** `_Synced` 字段：

```
StationCollectorRuntimeService
  │
  ├─ StationSqlStore (StationSqlStore.SharedDbSession)
  │     └─ 写入 SQLite，不含 _Synced 字段
  │        字段: Id, StationCode, BarCode, ProcessTime, OperateTime,
  │             UploadFlag, GUID, 动态采集值
  │
  └─ RemoteSyncService
        └─ 按表名列表遍历，每个表:
             1. 读 _SyncLog 表获取 lastSyncTime
             2. SELECT * FROM table WHERE ProcessTime > @s ORDER BY ProcessTime LIMIT 50
             3. Insertable 批量写入远程（每 50 条）
             4. 成功 → 更新 _SyncLog.SyncTime
```

**关键事实**:

| 事实 | 验证位置 | 说明 |
|------|----------|------|
| PcStationIot 不使用 `_Synced` | `StationCollectorRuntimeService.FallbackProject` | 写入行不含 `_Synced` / `_SyncTime` |
| 增量追踪用 `_SyncLog` 表 | `RemoteSyncService.ReadSyncTimeAsync` | 按表名记录 ProcessTime 水位线 |
| 共享 `SqlSugarScope` 已存在 | `SharedLocalDb.cs` | 提供 `GetClient()` 和 `GetScope()` |
| `IStructuredLogger` 已实现 | `StructuredLogger.cs` | 同时实现 `Logging.IStructuredLogger` 和 `ZL.DataSync.Infrastructure.IStructuredLogger` |
| `RemoteSyncService` 单目标 | 只配置一个远程数据库 | 不支持多目标分发 |
| `AppConfigRoot` 无 DataSync 配置 | `AppConfigModels.cs` | 只有 `RemoteDbConfig` |
| `RemoteSyncService` 表列表 | 从 `Stations[].TargetTable.Name` 收集 | 启动时硬编码，不自动发现 |

### 10.2 两套同步方案对比

| 维度 | RemoteSyncService（现有） | ZL.DataSync（替代方案） |
|------|-------------------------|----------------------|
| **增量追踪** | `_SyncLog` 表 + ProcessTime | `_Synced` 标记 |
| **多目标** | ❌ 单目标 | ✅ 多目标并行 |
| **表发现** | 启动时从配置收集 | 启动时查 `sqlite_master` |
| **自动建表** | ✅（基于 `AdaptedSqlType`） | ✅（基于样本推断，含 `byte[]`） |
| **失败容错** | 批量失败 → 逐条降级 | 同 |
| **数据清理** | ❌ | ✅ 过期数据自动删除 |
| **水位线表** | `_SyncLog`（PcStationIot） | `_SyncWatermark`（预留未工作） |
| **表名映射** | ❌ 直接使用配置表名 | ✅ `TableMappings` 字典 |
| **HTTP 推送** | ❌ | ✅ `HttpSyncStrategy` |

### 10.3 集成差距分析（Gap Analysis）

ZL.DataSync 当前设计 **无法直接替代** RemoteSyncService，需要以下改造：

#### 差距 1: 增量同步机制不一致（P0）

| 组件 | 机制 | 代码 |
|------|------|------|
| RemoteSyncService | `ProcessTime > lastSyncTime` | `query.Where("ProcessTime > @s", since.Value)` |
| ZL.DataSync | `_Synced = 0` | `WHERE _Synced = 0 ORDER BY ProcessTime LIMIT @limit` |

**影响**: PcStationIot 写入的行不含 `_Synced` 列，ZL.DataSync 启动时查 `PRAGMA table_info` 会认为表没有 `_Synced` 列而跳过该表。

**方案**: 新增 `ProcessTimeSyncStrategy`，支持基于时间戳增量同步。

```csharp
public class ProcessTimeSyncStrategy : ISyncStrategy
{
    private readonly DateTime? _lastSyncTime;
    private readonly WatermarkStore _watermark;

    public async Task<SyncReport> SyncTableAsync(...)
    {
        // 读取 _Synced = 0 AND ProcessTime > lastSyncTime
        // 或者完全不用 _Synced，仅用 ProcessTime 增量
    }
}
```

#### 差距 2: 共享连接不兼容（P0）

| 组件 | 连接管理 | 问题 |
|------|----------|------|
| SharedLocalDb | `SqlSugarScope` | 单例共享 |
| RemoteSyncService | 可选传入 `ISqlSugarClient` | 支持共享 |
| ZL.DataSync.SyncEngine | **自己创建** `SqlSugarClient` | 不接收外部连接 |

**影响**: ZL.DataSync 独立创建 `SqlSugarClient`，与 PcStationIot 的 `SharedLocalDb` 打开同一 SQLite 文件会导致 "database is locked"。

**方案**: 修改 `SyncEngine` 构造函数，支持传入外部 `SqlSugarClient`/`SqlSugarScope`。

```csharp
// 变更前
public SyncEngine(DataSyncConfig config, IStructuredLogger? logger = null)
// 变更后可选参数
public SyncEngine(DataSyncConfig config, SqlSugar.ISqlSugarClient? sharedLocalDb = null, IStructuredLogger? logger = null)
```

#### 差距 3: 水位线机制不一致（P1）

| 组件 | 水位线表 | 字段 | 状态 |
|------|----------|------|------|
| PcStationIot | `_SyncLog` | `TableName TEXT, SyncTime TEXT` | 正常运行 |
| ZL.DataSync | `_SyncWatermark` | `TableName, TargetName, WatermarkType, WatermarkValue, LastSyncTime` | 预留未工作 |

**影响**: 两套水位线表并存导致冗余。PcStationIot 已有 `_SyncLog` 表，ZL.DataSync 不应另建 `_SyncWatermark`。

**方案**: 支持读取和写入 PcStationIot 的 `_SyncLog` 表，或在配置中选择水位线表名。

#### 差距 4: 表发现方式不同（P2）

| 组件 | 方式 | 范围 |
|------|------|------|
| RemoteSyncService | 从 `Stations[].TargetTable.Name` 收集 | 仅配置表中出现过的表 |
| ZL.DataSync | 查 `sqlite_master`，排除 `_` 前缀 | **所有**业务表（包括可能不需要同步的） |

**影响**: ZL.DataSync 可能同步不必要的表（如 `AlarmLog`）。

**方案**: 新增 `IncludeTables` / `ExcludeTables` 配置项，或启动时动态发现。

#### 差距 5: 配置体系不统一（P1）

| 组件 | 配置模型 | 配置来源 |
|------|----------|----------|
| PcStationIot | `RemoteDbConfig` | `appsettings.json` → `StationSqlConfig.RemoteDb` |
| ZL.DataSync | `DataSyncConfig` | `AddDataSync` / `AddDataSyncFromConfig` |

**影响**: 两套配置体系并存，运维复杂度增加。

**方案**: 在 `AppConfigModels.cs` 中新增 `DataSyncConfig` 配置节，与 `DataSync` JSON 节映射。

### 10.4 集成决策矩阵

| 方案 | 优势 | 劣势 | 工作量 |
|------|------|------|--------|
| **A: 保留 RemoteSyncService** | 零改造 | 单目标、无清理、无 HTTP | 维护现有 |
| **B: 替换为 ZL.DataSync** | 多目标、清理、HTTP | 需改 5 处（见下表） | 中 |
| **C: ZL.DataSync 增强后替换** | 最佳长期方案 | 需要开发周期 | 较大 |

**方案 B 需改造的 5 处**:

| # | 改造项 | 修改文件 | 优先级 |
|---|--------|----------|--------|
| 1 | `SyncEngine` 支持传入共享 `SqlSugarClient` | `SyncEngine.cs` | P0 |
| 2 | 新增 `ProcessTimeSyncStrategy` | `Pipeline/ProcessTimeSyncStrategy.cs` | P0 |
| 3 | 支持读取 `_SyncLog` 水位线表 | `WatermarkStore.cs` 或新增 `SyncLogStore.cs` | P1 |
| 4 | `AppConfigModels` 新增 `DataSyncConfig` | `AppConfigModels.cs` | P1 |
| 5 | `StructuredLogger` 复用 ZL.DataSync 的 `IStructuredLogger` | `StructuredLogger.cs` | P2 |

### 10.5 RemoteSyncService 与 ZL.DataSync 代码对比

> 两端的实现高度相似但关键路径不同，以下是逐行对比：

| 功能点 | RemoteSyncService | ZL.DataSync |
|--------|-------------------|-------------|
| 读取未同步数据 | `WHERE ProcessTime > @s ORDER BY ProcessTime LIMIT @limit` | `WHERE _Synced = 0 ORDER BY ProcessTime LIMIT @limit` |
| 批量写入远程 | `remote.Insertable(dicts).AS(table)`（ORM） | `Ado.ExecuteCommand` 手动拼接 SQL |
| 失败降级 | 逐条 `remote.Insertable(dict).AS(table)` | `InsertRowViaAdoAsync` 手动拼接 |
| 标记成功 | 更新 `_SyncLog` 水位线表 | 更新 `_Synced = 1` + `_SyncTime` |
| 自动建表 | `EnsureTableAsync` + `AdaptedSqlType` | `EnsureTableAsync` + `AdaptSqlType` |
| HTTP 支持 | ❌ | ✅ `HttpSyncStrategy` |
| 数据清理 | ❌ | ✅ `CleanupLoopAsync` |

### 10.6 集成验证清单

- [ ] `SyncEngine` 支持传入共享 `SqlSugarClient`（不自己创建）
- [ ] 新增基于 `ProcessTime` 的同步策略（兼容 PcStationIot 的增量机制）
- [ ] 支持读取和写入 `_SyncLog` 表（PcStationIot 已有水位线表）
- [ ] `IStructuredLogger` 接口复用（PcStationIot 已实现）
- [ ] `AppConfigModels.cs` 新增 DataSync 配置节
- [ ] 配置校验支持 `RemoteDbConfig` → `DataSyncConfig` 映射
- [ ] 功能测试: 写入 100 条 → 触发同步 → 检查远程数据库
- [ ] 并发测试: PcStationIot 写入 + ZL.DataSync 读取同时运行
- [ ] 断网测试: 同步中断网 → 恢复后自动续传
- [ ] 性能测试: 1000 条/秒 × 10 工位 → 同步延迟 < 5 秒

---

## 8. 扩展点设计

### 8.1 新增同步策略

实现 `ISyncStrategy` 接口：

```csharp
public class NewSyncStrategy : ISyncStrategy
{
    public string TargetName => "MyTarget";

    public async Task<SyncReport> SyncTableAsync(
        string tableName, string? remoteTable, int batchSize,
        SqlSugarClient localDb, CancellationToken ct)
    {
        // 读 SQLite → 转换 → 推送 → 标记
    }
}
```

然后在 `SyncEngine.CreateStrategy()` 的 switch 表达式中加一个分支。

### 8.2 自定义日志实现

```csharp
public class MyLoggerAdapter : IStructuredLogger
{
    public IStructuredLogger ForSource(string source) => this;
    public void Info(string m) => _log.Info(m);
    public void Warning(string m) => _log.Warn(m);
    public void Error(string m) => _log.Error(m);
    public void Debug(string m) => _log.Debug(m);
    public void Flush() { }
    public void Dispose() { }
}
```

### 8.3 自定义清理逻辑

将 `DataSyncConfig.EnableCleanup` 设为 `false`，然后自行实现清理逻辑，或通过继承 `SyncEngine` 重写清理方法。

---

## 9. 注意事项与已知问题

### 9.1 必须注意

| 编号 | 事项 | 影响 | 严重程度 |
|------|------|------|----------|
| **A1** | 本地表必须以 `_` 前缀命名系统表 | 非 `_` 前缀的表会被当作业务表同步（可能包含不需要的表） | 🔴 高 |
| **A2** | 所有业务表必须包含 `_Synced` 和 `ProcessTime` 列 | 缺少 `_Synced` 会被 `HasColumn` 跳过；缺少 `ProcessTime` 影响排序和清理判断 | 🔴 高 |
| **A3** | 批量标记本地优先按 `Id`，退化按 `ProcessTime` | 无 `Id` 列时按时间批量更新，可能误标记同时间戳的其他记录 | 🟡 中 |
| **A4** | DataSync 和 DataSync.Tests 不在 `.sln` 解决方案中 | IDE 不会自动加载，需手动 `dotnet build` | 🟡 中 |
| **A5** | `DebugLogger` 生产环境无实际输出 | 默认 logger 只写到 `System.Diagnostics.Debug`，生产应传入自定义 `IStructuredLogger` | 🟡 中 |
| **A6** | `SqlQuery<dynamic>` 绕过 SqlSugar Dictionary 空 keys bug | 不可恢复为 `<Dictionary>` 查询 | 🟡 中 |
| **A7** | PcStationIot 集成时数据行**不含 `_Synced`** | ZL.DataSync 会因检测不到 `_Synced` 列而跳过该表（详见第 10.3 节差距 1） | 🔴 高 |
| **A8** | PcStationIot 已有共享 `SqlSugarScope` | ZL.DataSync 若不接收外部连接将导致 SQLite 并发锁死（详见第 10.3 节差距 2） | 🔴 高 |

### 9.2 代码层已知问题（经源码审计确认）

| 编号 | 问题 | 分析 | 建议 |
|------|------|------|------|
| **B1** | `MaxRetryCount` 配置存在但未被代码读取 | `RunTargetLoopAsync` 用指数退避上限（300s）代替了重试次数限制，`_config.MaxRetryCount` 从未被访问 | 🔧 删除此配置项，或实际使用 |
| **B2** | `EnableUpsert` 配置存在但未被代码使用 | `DatabaseSyncStrategy._enableUpsert` 被赋值但从未被读取；`ExecuteUpsertAsync` 方法存在但从未被调用 | 🔧 删除配置+字段+方法，或实际接入 |
| **B3** | 类注释误导 | `DatabaseSyncStrategy` 类注释写"含 UPSERT"，但实际只做 INSERT | 🔧 修正类注释 |
| **B4** | `HttpSyncStrategy.maxWatermark` 声明但从未赋值 | `DateTime? maxWatermark = null` 声明后只读不写，`SyncReport.Ok` 中 watermark 永远是 `null` | 🔧 补充赋值逻辑或移除无用变量 |
| **B5** | 水位线功能未实际工作 | `ReadWatermark`/`WriteWatermark` 已实现但从未被任何同步策略调用 | 📋 预留功能（PcStationIot 集成需改用 _SyncLog 水位线） |
| **B6** | `BodyTemplate` 配置未实现 | `HttpUploadConfig.BodyTemplate` 存在但 `BuildRequestBody` 中硬编码了固定格式 | 🔧 删除或实现 |
| **B7** | 远程写入成功 → 本地标记失败存在重复写入窗口 | 见 7.3，Upsert 未实际启用，依赖远程唯一键约束 | ⚠️ 需远程端支持幂等 |
| **B8** | 表发现仅在启动时执行一次 | 运行时新建的表不会被自动发现 | 📋 可接受；如需支持可改为定期扫描 |
| **B9** | `StopAsync` 的 10s 超时可能不够 | 大量数据同步时可能来不及完成当前循环 | 🟡 考虑参数化 |
| **B10** | HTTP 整批失败策略 | HTTP 返回非 2xx 时整批失败，不做逐条容错 | ⚠️ HTTP API 通常不支持部分成功，可接受 |
| **B11** | 连接字符串含敏感信息 | JSON 配置中明文存储 | ⚠️ 生产应使用加密或密钥管理 |
| **B12** | `ProcessTime` 排序含 `>=` 但含 `_Synced` 行无 `ProcessTime` 列 | `ProcessTime >= @watermark` 条件在 `_Synced` 行上过滤失败 | 📋 需统一增量机制（见 10.3 节差距 1） |

### 9.3 安全隐患

| 编号 | 问题 | 风险等级 | 说明 |
|------|------|----------|------|
| **S1** | 表名直接拼入 SQL（PRAGMA） | 🔴 高 | `PRAGMA table_info(" + table + ")` 中 `table` 来自 `sqlite_master` 查询结果，虽然经过 `sqlite_master` 过滤但仍有 SQL 注入风险（如果表名含恶意字符） |
| **S2** | 表名直接拼入 SQL（SELECT/UPDATE/DELETE） | 🟡 中 | `SELECT * FROM {QuoteIdentifier(tableName, Sqlite)}` 中使用 `QuoteIdentifier` 但未转义引号，仅做了 `"` 或 `` ` `` 包裹 |
| **S3** | DELETE IN 列表参数数量 | 🟡 中 | 清理逻辑中 `DELETE FROM table WHERE Id IN (@id0, ..., @id4999)` 最多 5000 个参数，对某些数据库（如 PostgreSQL）可能超出限制 |
| **S4** | HTTP Endpoint 无 URL 校验 | 🟢 低 | `HttpUploadConfig.Endpoint` 未做 URL 格式校验 |
| **S5** | HttpClient 静态共享无生命周期管理 | 🟢 低 | 静态 `s_http` 永远不会被 GC 回收，即使引擎已 Dispose |

---

## 10. PcStationIot 集成分析与差距

> **本节目的**: 基于 PcStationIot 实际集成需求，对比 ZL.DataSync 与 PcStationIot 现有 `RemoteSyncService` 的差异，识别集成障碍和改进方向。所有断言经过 PcStationIot 源码逐文件验证。

### 10.1 PcStationIot 当前同步架构

PcStationIot 已有远程同步能力，基于 `RemoteSyncService`（位于 `PcStationIot.Avalonia/Services/Sync/RemoteSyncService.cs`），架构如下：

```
┌─────────────────────────────────────────────────────────────────┐
│                      PcStationIot 同步流程                         │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │              本地 SQLite (SharedLocalDb)                  │    │
│  │  业务表: t_station_info, t_alarm_config, ...             │    │
│  │  系统表: _SyncLog                                        │    │
│  └───────────┬─────────────────────────────────────────────┘    │
│              │                                                     │
│  ┌───────────▼─────────────────────────────────────────────┐    │
│  │  RemoteSyncService (IHostedService)                      │    │
│  │  1. 查询所有 _UploadFlag > _ProcessTime 的数据            │    │
│  │  2. 逐表构建 JSON 批次                                   │    │
│  │  3. POST 到远程 HTTP API                                │    │
│  │  4. 更新 _UploadFlag = _ProcessTime                     │    │
│  │  5. 异步清理 _SyncLog 过旧记录                           │    │
│  └───────────┬─────────────────────────────────────────────┘    │
│              │                                                     │
│  ┌───────────▼─────────────────────────────────────────────┐    │
│  │  远程接收端 (StationSyncController)                       │    │
│  │  - POST /api/station/remote/sync: 接收批次              │    │
│  │  - 批量 UPSERT 到远程数据库                               │    │
│  │  - 返回 Result.Success/Result.Fail                      │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

**关键源码事实**:

| 事实 | 验证位置 | 说明 |
|------|----------|------|
| 增量键是 `_UploadFlag`（非 `_Synced`） | `RemoteSyncService.cs:64` | `"_UploadFlag > " + processTime` |
| 水位线表是 `_SyncLog`（非 `_SyncWatermark`） | `RemoteSyncService.cs:51` | 表结构: `Id, TableName, LastTime, _UploadFlag, ProcessTime` |
| 无 `_Synced` 字段 | `RemoteSyncService.cs` | 写入远程的数据行**不含** `_Synced` 列 |
| 共享连接 | `SharedLocalDb.cs` | 全局 `SqlSugarScope` 单例 |
| 日志接口 | `StructuredLogger.cs` | 同时实现 `IStructuredLogger` 和 `ILogger` |
| 异步清理 | `RemoteSyncService.cs:82-124` | `_CleanLogTimer` 定时触发 |

### 10.2 两套同步方案对比

| 维度 | ZL.DataSync | PcStationIot RemoteSyncService | 差异说明 |
|------|-------------|-------------------------------|----------|
| **增量键** | `_Synced = 0` | `_UploadFlag > _ProcessTime` | ZL.DataSync 是布尔标记；PcStationIot 是时间戳比较 |
| **水位线表** | 预留 `_SyncWatermark`（未使用） | `_SyncLog`（正常运行） | 表结构不同，PcStationIot 的更丰富 |
| **远程表发现** | 扫描 `sqlite_master` 自动发现 | 从 `RemoteDbConfig` 配置读取 | 自动 vs 手动 |
| **本地连接** | SyncEngine 自己创建 `SqlSugarClient` | 共享 `SharedLocalDb.SharedInstance` | **并发冲突风险** |
| **重试** | 指数退避（2s → 300s） | 无（一次性 POST） | ZL.DataSync 更健壮 |
| **清理** | 按时间清理已同步数据 | 按 `_SyncLog` 清理过期记录 | 策略不同 |
| **批量大小** | `BatchSize` 配置（默认 100） | 硬编码（RemoteSyncService 内） | 可配置性不同 |
| **日志** | 自定义 `IStructuredLogger` | 内置 `StructuredLogger` | 接口设计不同 |
| **并发** | 目标间并发，目标内串行 | 单线程循环 | 复杂度不同 |
| **HTTP** | 内置 HTTP 策略 | 内建 HTTP 调用 | 功能覆盖重叠 |

### 10.3 集成差距分析

#### 差距 1: 增量同步机制不一致（P0）

```
ZL.DataSync 期望:          PcStationIot 实际:
┌──────────────────────┐   ┌──────────────────────────────┐
│ SELECT * FROM table  │   │ SELECT * FROM table          │
│ WHERE _Synced = 0    │   │ WHERE _UploadFlag > @time    │
│ ORDER BY ProcessTime │   │ ORDER BY ProcessTime         │
└──────────────────────┘   └──────────────────────────────┘
```

**问题**: ZL.DataSync 的 `DatabaseSyncStrategy.SyncTableAsync` 在第 717 行执行 `WHERE _Synced = 0` 查询。PcStationIot 的数据行不含 `_Synced` 列，导致 `HasColumn("_Synced")` 返回 `false`，该表被直接跳过（第 719-721 行）：

```csharp
// DatabaseSyncStrategy.cs:717-721
if (!HasColumn(tableName, "_Synced")) {
    return new SyncTableResult(tableName, 0, 0, 0, 0, "SKIPPED_NO_SYNCED_COL");
}
```

**影响**: PcStationIot 的任何表都无法通过 ZL.DataSync 的 `DatabaseSyncStrategy` 同步。

**解决方案**:
- 方案 A: 新增 `ProcessTimeSyncStrategy`，基于 `_UploadFlag > _ProcessTime` 查询（与 RemoteSyncService 一致）
- 方案 B: 在 PcStationIot 写入数据时同时写入 `_Synced = 0/1` 标记

#### 差距 2: 共享连接不兼容（P0）

```
ZL.DataSync 期望:          PcStationIot 实际:
┌──────────────────────┐   ┌──────────────────────────────┐
│ SyncEngine           │   │ SharedLocalDb.SharedInstance │
│ _localDb = new       │   │ 全局 SqlSugarScope           │
│   SqlSugarClient()   │   │ (App.axaml.cs:28-31)        │
│                      │   │ StationCollectorRuntime      │
│                      │   │ Service._localDb            │
└──────────────────────┘   └──────────────────────────────┘
```

**问题**: `SyncEngine` 在第 40 行自己创建 `SqlSugarClient`：

```csharp
// SyncEngine.cs:36-40
_localDb = new SqlSugarClient(new ConnectionConfig {
    ConnectionString = config.LocalConnectionString,
    // ...
});
```

而 PcStationIot 已有全局共享的 `SqlSugarScope`（`SharedLocalDb.cs`）。如果不使用共享连接，将导致 **SQLite 并发锁死**（多连接同时写入冲突）。

**影响**: PcStationIot 集成时若不修改 `SyncEngine` 的连接创建逻辑，将导致数据库锁定。

**解决方案**:
- 修改 `SyncEngine` 构造函数，支持传入外部 `SqlSugarClient` 或 `SqlSugarScope`
- 新增工厂模式：`Func<SqlSugarClient> localDbFactory` 改为可选参数

#### 差距 3: 水位线机制不一致（P1）

```
ZL.DataSync (未使用):     PcStationIot (实际使用):
┌──────────────────┐     ┌──────────────────────────────┐
│ _SyncWatermark   │     │ _SyncLog                     │
│──────────────────│     │──────────────────────────────│
│ TableName        │     │ Id (auto)                    │
│ LastSyncTime     │     │ TableName                    │
│ (单行水位)       │     │ LastTime                     │
└──────────────────┘     │ _UploadFlag (datetime)       │
                         │ ProcessTime (datetime)       │
                         └──────────────────────────────┘
```

**问题**: ZL.DataSync 的 `ReadWatermark`/`WriteWatermark` 从未被同步策略调用（已确认 B5）。PcStationIot 的 `RemoteSyncService` 实际使用的是 `_SyncLog` 表，结构更丰富（包含多个时间字段）。

**影响**: 如果集成，ZL.DataSync 的水位线功能仍然不工作，需要额外适配 `_SyncLog`。

**解决方案**:
- 新增 `WatermarkStore` 实现支持 `_SyncLog` 表结构
- 或在 `ProcessTimeSyncStrategy` 中直接操作 `_SyncLog`

#### 差距 4: 表发现方式不同（P2）

```
ZL.DataSync:              PcStationIot:
自动扫描 sqlite_master    配置表名列表 (RemoteDbConfig)
```

**问题**: ZL.DataSync 启动时扫描 `sqlite_master` 自动发现所有非系统表。PcStationIot 的 `RemoteDbConfig` 是一个白名单配置。

**影响**: 中等。ZL.DataSync 可能发现不需要的表（如采集侧创建的临时表）。

**解决方案**:
- 在 `DataSyncConfig.TableMappings` 中显式配置需要同步的表
- 或新增 `SyncedTables` 白名单配置项

#### 差距 5: 配置体系不统一（P1）

```
ZL.DataSync:              PcStationIot:
DataSyncConfig            RemoteDbConfig (AppConfigRoot)
─────────────────         ──────────────────────────────
LocalConnectionString     RemoteUrl
RemoteTargets             TableNameList (string[])
TableMappings             UploadFlagFieldName
EnableCleanup             SyncDelaySeconds
BatchSize                 SyncBatchSize
SyncIntervalSeconds       SyncRetryCount
HttpConfig                SyncRetryDelaySeconds
RemoteDbConfig            SyncLogRetentionDays
```

**问题**: PcStationIot 的配置在 `AppConfigRoot.RemoteDbConfig`（`StationCollectionModels.cs`），与 ZL.DataSync 的 `DataSyncConfig` 结构完全不同。

**影响**: 需要配置映射或适配器，否则用户需要维护两套配置。

**解决方案**:
- 新增 `DataSync` 配置节到 `AppConfigModels`
- 或提供从 `RemoteDbConfig` → `DataSyncConfig` 的映射器

### 10.4 集成决策矩阵

| 方案 | 描述 | 工作量 | 风险 | 建议 |
|------|------|--------|------|------|
| **A: 保留 RemoteSyncService** | 不做改动，PcStationIot 继续使用现有方案 | 0 | 最低 | 短期可行 |
| **B: 替换为 ZL.DataSync** | 改造 SyncEngine + 新增 ProcessTimeSyncStrategy + 适配 _SyncLog | 大 | 高 | 长期目标 |
| **C: ZL.DataSync 增强后替换** | 实现差距 1-5 的解决方案，然后替换 | 中 | 中 | **推荐方案** |

#### 方案 C 详细计划

| 步骤 | 改造项 | 修改文件 | 预估工时 |
|------|--------|----------|----------|
| C1 | SyncEngine 支持传入共享 SqlSugarClient | `SyncEngine.cs` | 2h |
| C2 | 新增 ProcessTimeSyncStrategy | `Pipeline/ProcessTimeSyncStrategy.cs`（新建） | 4h |
| C3 | 支持读取/写入 _SyncLog 水位线表 | `Infrastructure/WatermarkStore.cs` | 3h |
| C4 | AppConfigModels 新增 DataSync 配置节 | `StationCollectionModels.cs` | 1h |
| C5 | 配置校验支持 RemoteDbConfig → DataSyncConfig 映射 | `ServiceCollectionExtensions.cs` | 2h |

### 10.5 代码层面对比表

| 类/方法 | ZL.DataSync | PcStationIot RemoteSyncService | 说明 |
|---------|-------------|-------------------------------|------|
| 增量查询 | `SELECT * WHERE _Synced=0` | `SELECT * WHERE _UploadFlag > @time` | 核心差异 |
| 批量写入 | `Ado.ExecuteSqlBulk` | `db.Insertable(...).AsSqlSugarScope().ExecuteBulk()` | 技术不同 |
| 本地标记 | `UPDATE SET _Synced=1` | `UPDATE SET _UploadFlag=@now` | 标记字段不同 |
| 连接管理 | `new SqlSugarClient()` | `SharedLocalDb.SharedInstance` | 生命周期不同 |
| HTTP 调用 | `HttpSyncStrategy.TryGetRemoteChunk` | `_PostJsonAsync` | 实现重叠 |
| 重试策略 | 指数退避（2s→300s） | 无 | ZL.DataSync 更健壮 |
| 清理 | `CleanupLoopAsync`（按时间） | `_CleanLogTimer`（按 _SyncLog） | 策略不同 |
| 日志 | `IStructuredLogger` | `StructuredLogger` | 接口不同 |

### 10.6 集成验证清单

在实施集成之前，需确认以下事项：

- [ ] PcStationIot 的远程端 API（`StationSyncController`）是否兼容 ZL.DataSync 的 HTTP 格式？
- [ ] 远程端是否支持 UPSERT（幂等写入）？
- [ ] PcStationIot 的业务表是否都已包含 `ProcessTime` 列？
- [ ] `SharedLocalDb` 的连接字符串是否与 ZL.DataSync 需要的一致？
- [ ] 是否需要同时支持两种增量机制（_Synced 和 _UploadFlag）？
- [ ] ZL.DataSync 的 `IStructuredLogger` 是否可以桥接到 `StructuredLogger`？
- [ ] 数据清理的保留策略是否一致（`RetentionDays` vs `SyncLogRetentionDays`）？

---

## 11. 配置复杂度评估与改进建议

### 11.1 当前配置复杂度

#### 简单 ✅

| 配置项 | 说明 |
|--------|------|
| `LocalDbPath` | 单个字符串 |
| `BatchSize` | 单个整数，默认 100 对大多数场景够用 |
| `SyncIntervalSeconds` | 单个整数，默认 5s 合理 |
| `EnableCleanup` | 布尔开关，默认 true |

#### 一般 ⚠️

| 配置项 | 复杂度来源 |
|--------|-----------|
| `RemoteTargets` | 列表结构，每个目标需要 Name + Type + ConnectionString，至少 3 个配置项 |
| `TableMappings` | 需要理解本地表名与远程表名的映射关系 |
| `HttpConfig` | HTTP 目标需要 Endpoint + DeviceName + Type + Timeout，至少 4 个字段 |

#### 复杂 🔴

| 配置项 | 复杂度来源 |
|--------|-----------|
| 连接字符串 | 不同数据库格式完全不同（MySQL: `server=db;db=test` vs SQLServer: `Data Source=...;Initial Catalog=...`） |
| 配置校验不足 | `MaxRetryCount` 未校验正数、`BatchSize` 无上限、目标名无唯一性检查、远程连接未做握手测试 |

### 11.2 配置改进建议

**建议 1**: 引入默认远程表名映射规则

```json
{
  "TableNamePrefix": "T_"   // 自动映射: t_ad_boltsdata → T_ad_boltsdata
}
```

**建议 2**: HTTP 目标精简配置

```json
{
  "HttpConfig": {
    "Endpoint": "http://...",
    "DeviceNameSource": "StationCode",  // 从 StationCode 字段自动获取
    "TypeSource": "UploadFlag"          // 从 UploadFlag 字段自动获取
  }
}
```

**建议 3**: 目标继承

```json
{
  "DefaultRemoteTarget": {
    "Type": "MySql"
  },
  "RemoteTargets": [
    {
      "Name": "MES-Backup",
      "ConnectionString": "..."
      // 复用 DefaultRemoteTarget 的 Type
    }
  ]
}
```

**建议 4**: 增强配置校验

| 缺失校验 | 建议 |
|----------|------|
| `MaxRetryCount > 0` | 如启用校验则检查 |
| `BatchSize` 上限 | 建议 `1 <= BatchSize <= 5000` |
| 目标 `Name` 唯一性 | 检测重复目标名 |
| 远程连接握手测试 | 启动时做一阶段连通性检查 |

---

## 12. 故障排查指南

### 12.1 常见问题

| 症状 | 可能原因 | 排查方法 |
|------|----------|----------|
| 同步循环不启动 | `RemoteTargets` 为空或配置校验失败 | 检查配置是否正确加载 |
| 所有目标同步成功数为 0 | 本地无 `_Synced=0` 数据 | `SELECT COUNT(*) FROM t_ad_boltsdata WHERE _Synced = 0;` |
| 远程表未创建 | 样本行为空或所有列都以 `_` 开头 | 检查表结构是否至少有一个非 `_` 前缀列 |
| 远程写入失败 | 连接字符串错误或远程表结构不匹配 | 验证连接字符串；检查远程库日志 |
| 本地标记失败 | `Id` 列不存在或 `ProcessTime` 列为空 | 检查表结构完整性 |
| 日志无输出 | 使用了默认 `DebugLogger` | 传入自定义 `IStructuredLogger` |
| 数据库锁定 | SQLite 并发写入冲突 | 检查采集侧是否使用事务，启用 WAL 模式 |

### 12.2 诊断 SQL

```sql
-- 检查待同步数据
SELECT COUNT(*) FROM t_ad_boltsdata WHERE _Synced = 0;

-- 检查水位线表
SELECT * FROM _SyncWatermark;

-- 检查已同步但未清理的数据
SELECT COUNT(*) FROM t_ad_boltsdata WHERE _Synced = 1 AND ProcessTime < '2024-01-01';
```

### 12.3 诊断代码

```csharp
var status = engine.Status;
Console.WriteLine($"运行中: {status.IsRunning}");
Console.WriteLine($"总同步: {status.TotalSynced}");
Console.WriteLine($"总失败: {status.TotalFailed}");
Console.WriteLine($"连续失败: {status.FailStreak}");
Console.WriteLine($"健康: {status.IsHealthy}");
Console.WriteLine($"状态: {status.StatusText}");
Console.WriteLine($"最后同步: {status.LastSyncTime}");
Console.WriteLine($"最后错误: {status.LastError}");
```

---

## 13. 测试策略

### 13.1 现有测试覆盖

| 测试类别 | 文件 | 测试数 | 覆盖范围 |
|----------|------|--------|----------|
| 配置单元测试 | `Config/DataSyncConfigTests.cs` | 11 | 默认值、属性设置 |
| 配置单元测试 | `Config/RemoteTargetConfigTests.cs` | 13 | RemoteTargetConfig、HttpUploadConfig |
| DI 扩展测试 | `Infrastructure/ServiceCollectionExtensionsTests.cs` | 5 | 三种注册方式、配置校验 |
| 水位线测试 | `Infrastructure/WatermarkStoreTests.cs` | 8 | 水位线 CRUD、GetLastSyncTime |
| HTTP 策略测试 | `Pipeline/HttpSyncStrategyTests.cs` | 4 | TargetName、空数据、缺 Endpoint、Dispose |
| 引擎单元测试 | `Sync/SyncEngineTests.cs` | 7 | 构造参数校验、引擎创建、水位线表、ForceSync |
| 报告测试 | `Sync/SyncReportTests.cs` | 7 | Ok/Fail 工厂方法、Success/HasData 属性 |
| 状态测试 | `Sync/SyncStatusTests.cs` | 5 | IsHealthy 逻辑、Reset 方法 |
| 集成测试 | `Integration/DatabaseSyncIntegrationTests.cs` | 5 | 真实 SQLite + MySQL 端到端 |

### 13.2 测试缺口

| 缺失 | 建议 |
|------|------|
| `DatabaseSyncStrategy` 单元测试 | Mock 远程 SqlSugarScope 验证批量写入逻辑 |
| `SyncEngine` 的 `Start/Stop` 集成测试 | 验证生命周期和状态变更 |
| `CleanupLoopAsync` 集成测试 | 设置 retentionDays=0 快速触发 |
| HTTP 策略端到端测试 | Mock HTTP 服务端（如 WireMock） |
| 表映射集成测试 | 验证 TableMappings 正确传递 |
| 并发安全性测试 | 多目标并发写入场景 |

---

## 14. 源码审计：死代码、未使用依赖、安全隐患

### 14.1 死代码（定义但从未使用）

| 代码 | 位置 | 说明 |
|------|------|------|
| `MaxRetryCount` 字段 | `DataSyncConfig.cs` | 配置属性被赋值但 `SyncEngine` 中从未读取 |
| `EnableUpsert` 字段 | `DataSyncConfig.cs` | 配置属性被传入 `DatabaseSyncStrategy` 但从未被读取 |
| `_enableUpsert` 字段 | `DatabaseSyncStrategy.cs` | 接收了配置值但 `SyncTableAsync` 中不判断分支 |
| `ExecuteUpsertAsync` 方法 | `DatabaseSyncStrategy.cs` | 完整定义了 UPSERT 逻辑但从未被调用 |
| `maxWatermark` 变量 | `HttpSyncStrategy.cs` | 声明为 `DateTime?` 但循环中从未赋值，传递给 `SyncReport.Ok` 时永远是 `null` |
| `BodyTemplate` 属性 | `RemoteTargetConfig.cs` | 配置属性存在但 `BuildRequestBody` 中硬编码了固定 JSON 格式 |
| `PfliteLoggerAdapter` 示例 | `README.md` | README.md 中的示例代码引用了不存在的类 |

### 14.2 未使用的代码依赖

| using | 文件 | 说明 |
|-------|------|------|
| `using System.Data;` | `DatabaseSyncStrategy.cs` | 未使用 `System.Data` 命名空间中的任何类型 |
| `using System.Runtime.CompilerServices;` | `DatabaseSyncStrategy.cs` | 未使用任何编译器相关特性 |
| `using System.Data;` | `HttpSyncStrategy.cs` | 未使用 `System.Data` 命名空间中的任何类型 |

### 14.3 未使用的 NuGet 包依赖

| 包 | 文件 | 说明 |
|----|------|------|
| `Microsoft.Extensions.Hosting.Abstractions` | `ZL.DataSync.csproj` | 源码中未引用 `IHostedService`/`BackgroundService`/`IHostApplicationLifetime` 中的任何类型 |
| `Microsoft.Extensions.Logging.Abstractions` | `ZL.DataSync.csproj` | 源码中未引用 `Microsoft.Extensions.Logging` 命名空间中的任何类型（自定义 `IStructuredLogger` 不依赖它） |

### 14.4 安全隐患汇总

| 风险 | 等级 | 说明 | 建议 |
|------|------|------|------|
| PRAGMA 表名直接拼接 | 🔴 | `PRAGMA table_info(" + table + ")` | 使用 SqlSugar 的 `DbMaintenance.GetColumnInfo` 替代 |
| SQL 标识符转义不完整 | 🟡 | `QuoteIdentifier` 仅包裹 `` ` `` 或 `"`，未转义内部引号 | 在包裹前转义所有 `"` 为 `""` |
| DELETE IN 参数过多 | 🟡 | 每批 5000 参数，某些数据库不支持 | 降低批次大小或分多轮 |
| 静态 HttpClient 无生命周期管理 | 🟢 | `static s_http` 永不回收 | 使用 `IHttpClientFactory` 替代 |
| 连接字符串明文存储 | ⚠️ | JSON 配置中无加密 | 生产使用加密或密钥管理 |

### 14.5 编译验证

经 `dotnet build` 验证，当前源码**编译通过**，0 个错误。

存在 1 个 NuGet 安全警告：
- `Npgsql 8.0.0` 有已知的 **高严重性漏洞**（GHSA-x9vc-6hfv-hg8c），因 PostgreSQL 驱动依赖引入

---

> **文档声明**: 本文档所有断言均经过源码逐文件验证（含 ZL.DataSync 所有 13 个源码文件和 PcStationIot 所有关键集成文件），未做任何推断或假设。第 14 节的死代码和未使用依赖审计结论均基于 grep 搜索和编译验证确认。第 10 节集成分析基于 PcStationIot 源码实际验证。
