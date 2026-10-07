# ZL.Connection — 连接基础设施库

## 概述

ZL.Connection 提供连接生命周期管理的基础设施组件，包括确定性状态机和指数退避策略。它是 `ZL.ConnectionGuard` 的底层依赖，也可被任何需要连接状态管理的组件独立使用。

该库从 `PlcSimulator.Core.Drivers` 中提取，经过命名空间重构（`PlcSimulator.Core.Drivers` → `ZL.Connection`），消除了对 PLC 仿真场景的依赖，成为协议无关的通用连接基础设施。

---

## 1. 命名空间与类型一览

| 命名空间 | 类型 | 说明 |
|---|---|---|
| `ZL.Connection` | `ConnectionState` | 连接状态枚举 |
| `ZL.Connection` | `ConnectionStateChangedEventArgs` | 状态变更事件参数 |
| `ZL.Connection` | `ConnectionStateMachine` | 确定性连接状态机 |
| `ZL.Connection` | `ExponentialBackoffStrategy` | 指数退避重连策略 |

---

## 2. ConnectionState — 连接状态枚举

### 2.1 定义

```csharp
public enum ConnectionState
{
    Disconnected = 0,   // 未连接 - 初始状态或已主动断开
    Connecting = 1,      // 连接中 - 正在建立连接
    Connected = 2,       // 已连接 - 连接正常，可以进行读写操作
    Reconnecting = 3,    // 重连中 - 连接断开，正在自动重连
    Error = 4            // 错误 - 连接失败或协议错误，需要手动干预
}
```

### 2.2 状态转换规则

```
Disconnected ──[Connect]──> Connecting ──[Success]──> Connected
    ▲                           │                          │
    │                           │[Timeout/Error]           │[Disconnect]
    │                           ▼                          │
    └─────────────────────  Error  <──────────────────────┘
                               │
                               │[AutoReconnect]
                               ▼
                          Reconnecting ──[Success]──> Connected
                               │
                               │[MaxRetries]
                               ▼
                            Error
```

| 转换 | 合法性 | 说明 |
|---|---|---|
| Disconnected → Connecting | ✅ | 发起连接 |
| Connecting → Connected | ✅ | 连接成功 |
| Connecting → Error | ✅ | 连接失败/超时 |
| Connecting → Disconnected | ✅ | 取消连接 |
| Connected → Disconnected | ✅ | 主动断开 |
| Connected → Reconnecting | ✅ | 连接断开，自动重连 |
| Connected → Error | ✅ | 协议错误 |
| Reconnecting → Connected | ✅ | 重连成功 |
| Reconnecting → Error | ✅ | 重连失败 |
| Reconnecting → Disconnected | ✅ | 手动停止 |
| Error → Connecting | ✅ | 手动重试 |
| Error → Disconnected | ✅ | 重置 |
| Disconnected → Connected | ❌ | 不能跳过 Connecting |
| Connected → Connecting | ❌ | 已连接不能再次连接 |

---

## 3. ConnectionStateMachine — 确定性状态机

### 3.1 类型签名

```csharp
public class ConnectionStateMachine
{
    public ConnectionState CurrentState { get; }
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public ConnectionStateMachine(ConnectionState initialState = ConnectionState.Disconnected);
    public bool TryTransition(ConnectionState targetState, string? errorMessage = null, Exception? exception = null);
    public void ForceTransition(ConnectionState targetState, string? errorMessage = null, Exception? exception = null);
    public bool CanTransition(ConnectionState targetState);
    public IReadOnlyCollection<ConnectionState> GetAvailableTransitions();
    public void Reset();
}
```

### 3.2 方法说明

| 方法 | 说明 |
|---|---|
| `TryTransition(target, error, ex)` | 尝试转换到目标状态。检查合法性与守卫条件，成功则触发 `StateChanged` 事件。返回 `true` 表示转换成功 |
| `ForceTransition(target, error, ex)` | 强制转换到目标状态，跳过守卫检查。仅用于紧急停止等特殊场景 |
| `CanTransition(target)` | 检查是否可以转换到目标状态，不执行实际转换 |
| `GetAvailableTransitions()` | 获取从当前状态可以转换到的所有状态 |
| `Reset()` | 重置状态机到 Disconnected |

### 3.3 使用示例

```csharp
using ZL.Connection;

var sm = new ConnectionStateMachine();

// 注册状态变更监听
sm.StateChanged += (sender, e) =>
{
    Console.WriteLine($"State changed: {e}");
};

// 合法转换
sm.TryTransition(ConnectionState.Connecting);  // true
sm.TryTransition(ConnectionState.Connected);    // true

// 非法转换被拒绝
sm.TryTransition(ConnectionState.Connecting);   // false (Connected → Connecting 不合法)

// 正常断开
sm.TryTransition(ConnectionState.Disconnected); // true
```

### 3.4 线程安全

`ConnectionStateMachine` 使用 `lock` 保证所有操作的线程安全性。`StateChanged` 事件在锁内触发，确保事件回调中读取 `CurrentState` 时状态一致。

---

## 4. ExponentialBackoffStrategy — 指数退避策略

### 4.1 类型签名

```csharp
public class ExponentialBackoffStrategy
{
    public int BaseDelayMs { get; }
    public int MaxDelayMs { get; }
    public int MaxRetries { get; }
    public double JitterFactor { get; }
    public int CurrentAttempt { get; }

    public ExponentialBackoffStrategy(
        int baseDelayMs = 1000,
        int maxDelayMs = 60000,
        int maxRetries = -1,
        double jitterFactor = 0.2);

    public int GetNextDelayMs();       // 返回延迟毫秒数，-1 表示不再重试
    public TimeSpan? GetNextDelay();    // 返回 TimeSpan，null 表示不再重试
    public void Reset();                // 重置计数器（连接成功后调用）
    public bool CanRetry();             // 是否还可以重试
    public int GetCurrentDelayMs();     // 查看当前延迟（不增加计数器）
}
```

### 4.2 算法

```
delay = min(baseDelay * 2^attempt + jitter, maxDelay)
jitter = delay * jitterFactor * (random * 2 - 1)  // ±jitterFactor
```

### 4.3 重连序列示例（baseDelay=1000ms, maxDelay=60000ms, jitter=0.2）

| 尝试次数 | 指数延迟 | 抖动范围 | 实际延迟 |
|---|---|---|---|
| 第 1 次 | 1000ms | ±200ms | ~1000ms |
| 第 2 次 | 2000ms | ±400ms | ~2000ms |
| 第 3 次 | 4000ms | ±800ms | ~4000ms |
| 第 4 次 | 8000ms | ±1600ms | ~8000ms |
| 第 5 次 | 16000ms | ±3200ms | ~16000ms |
| 第 6 次 | 32000ms | ±6400ms | ~32000ms |
| 第 7 次+ | 60000ms | ±12000ms | ~60000ms (上限) |

### 4.4 使用示例

```csharp
using ZL.Connection;

var backoff = new ExponentialBackoffStrategy(
    baseDelayMs: 1000,
    maxDelayMs: 30000,
    maxRetries: 5);

while (backoff.CanRetry())
{
    var delay = backoff.GetNextDelay();
    if (delay == null) break;

    await Task.Delay(delay.Value);

    if (await TryConnectAsync())
    {
        backoff.Reset();  // 连接成功，重置计数器
        break;
    }
}
```

### 4.5 设计要点

- **随机抖动**：防止多个客户端同时重连导致"雷群效应"（thundering herd）
- **线程安全**：`GetNextDelayMs()` 和 `Reset()` 使用 `lock` 保护
- **最大重试**：`MaxRetries = -1` 表示无限重试

---

## 5. 与其他组件的关系

```
ZL.ConnectionGuard (依赖 ZL.Connection)
  └─ 使用 ConnectionStateMachine 管理连接状态
  └─ 使用 ExponentialBackoffStrategy 计算重连延迟

ZL.ProtocolGateway (未来可能依赖)
  └─ 输出插件可使用状态机管理连接生命周期
```

ZL.Connection 不依赖任何其他 ZL.* 组件，是 Foundation 层中最底层的库之一（与 ZL.Shared 同级）。

---

## 6. 来源与迁移

| 原始文件 | 来源项目 | 命名空间变更 |
|---|---|---|
| `ConnectionState.cs` | `PlcSimulator.Core.Drivers` | `PlcSimulator.Core.Drivers` → `ZL.Connection` |
| `ConnectionStateChangedEventArgs.cs` | `PlcSimulator.Core.Drivers` | `PlcSimulator.Core.Drivers` → `ZL.Connection` |
| `ConnectionStateMachine.cs` | `PlcSimulator.Core.Drivers` | `PlcSimulator.Core.Drivers` → `ZL.Connection` |
| `ExponentialBackoffStrategy.cs` | `PlcSimulator.Core.Drivers` | `PlcSimulator.Core.Drivers` → `ZL.Connection` |

提取时移除了与 PLC 仿真场景相关的文档注释，保留通用连接管理语义。

---

## 7. 总结

| 特性 | 说明 |
|---|---|
| 定位 | 连接生命周期基础设施 |
| 依赖 | 无项目依赖（纯 .NET 8 标准库） |
| 被依赖 | ZL.ConnectionGuard |
| 核心功能 | 确定性状态机、指数退避策略 |
| 文件数 | 4 个 `.cs` 文件 |
| 代码量 | 约 470 行 |
| 设计原则 | 线程安全、零外部依赖、协议无关 |
