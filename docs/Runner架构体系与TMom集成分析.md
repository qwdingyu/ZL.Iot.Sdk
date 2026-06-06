# ZL.Iot.Runner 新架构体系 — 设计理念、架构详解与 TMom 集成分析

> 日期: 2026-06-05
> 版本: v1.0
> 范围: iot-sdk 新架构 (Runner 系列) 完整架构文档 + 与 TMom.Device.Runtime.Host / web_mini 的集成分析
> 目标读者: 架构师、后端开发、前端开发、运维

---

## 目录

1. [设计理念](#1-设计理念)
2. [分层架构总览](#2-分层架构总览)
3. [核心模块详解](#3-核心模块详解)
4. [流水线流程详解](#4-流水线流程详解)
5. [运行时架构详解](#5-运行时架构详解)
6. [配置模型详解](#6-配置模型详解)
7. [模板体系详解](#7-模板体系详解)
8. [与 TMom.Device.Runtime.Host 的集成分析](#8-与-tmomdevice_runtimehost-的集成分析)
9. [与 web_mini 前端的集成分析](#9-与-web_mini-前端的集成分析)
10. [Runner vs TMom.Host 对比与演进路线](#10-runner-vs-tmomhost-对比与演进路线)
11. [部署架构](#11-部署架构)
12. [关键设计决策](#12-关键设计决策)

---

## 1. 设计理念

### 1.1 核心哲学：配置驱动 → 代码生成 → 编译打包

Runner 架构的根本设计理念是 **"编译时固化，运行时轻量"**：

| 维度 | 旧架构 (Plugin/EdgeService) | Runner 新架构 |
|------|---------------------------|--------------|
| 设计范式 | 运行时插件反射加载 | 编译时代码生成 |
| 部署形态 | 通用运行时 + 动态 DLL | 每个站点独立编译的二进制 |
| 启动速度 | 慢（反射发现 + 动态加载） | 快（直接执行） |
| 运行时依赖 | 重型（完整 DI + 插件框架） | 轻量（仅 Runner.Lib NuGet） |
| 安全性 | 低（任意 DLL 可被加载） | 高（编译时锁定代码） |
| 可维护性 | 低（运行时行为不可预测） | 高（代码即配置，可审查） |

### 1.2 设计原则

1. **配置即代码（Config-as-Code）**：用户通过 JSON/XML 配置文件描述设备、标签、执行器，系统自动生成完整 .NET 项目
2. **一次生成，到处运行（Generate Once, Run Anywhere）**：生成的二进制自包含（Self-Contained），无需目标机器安装 .NET Runtime
3. **关注点分离（Separation of Concerns）**：
   - Generator 层：负责"如何生成"（模板、编译、打包）
   - Runner 层：负责"如何运行"（驱动、采集、触发）
   - Templates 层：负责"生成什么"（项目文件模板）
4. **SKU 分层商业化**：Binary 模式（免费，输出 exe）/ Source 模式（付费，输出完整源码）
5. **多平台适配**：Console / Windows Service / Linux systemd / WinForms 四种宿主形态

---

## 2. 分层架构总览

```
┌─────────────────────────────────────────────────────────────────┐
│                        用户交互层                                 │
│  ┌─────────────────────┐    ┌──────────────────────────────┐    │
│  │  web_mini (Vue 3)   │    │  GeneratorCli HTTP API       │    │
│  │  设备/标签/执行器配置  │    │  :5000/api/generate         │    │
│  │  监控/告警/仿真      │    │  :5000/api/jobs/{id}/sse     │    │
│  └─────────┬───────────┘    └──────────┬───────────────────┘    │
├────────────┼───────────────────────────┼────────────────────────┤
│            │                           │                         │
│     TMom.Device.Runtime.Host    JobScheduler                      │
│     (当前运行时，逐步迁移)         (任务调度引擎)                     │
│                                                    │              │
├────────────────────────────────────────────────────┼──────────────┤
│                  Generator 层 (应用层)               │              │
│  ┌─────────────────────────────────────────────────┘              │
│  │  ZL.Iot.Runner.Generator                                       │
│  │  ┌──────────┐ ┌──────────────┐ ┌──────────┐ ┌────────────┐  │
│  │  │BuildEngine│ │ProjectGenerator│ │PackageBuilder│ │TemplateRenderer│  │
│  │  │(编译发布) │ │(项目生成编排) │ │(ZIP打包)  │ │(Scriban渲染)│  │
│  │  └──────────┘ └──────────────┘ └──────────┘ └────────────┘  │
│  └──────────────────────────────────────────────────────────────┘│
│                                                                   │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │  ZL.Iot.Runner.Templates (Scriban 模板资源)                   ││
│  │  console/ │ windows-service/ │ linux-systemd/ │ winform/    ││
│  └──────────────────────────────────────────────────────────────┘│
├──────────────────────────────────────────────────────────────────┤
│                  Runner 层 (运行时核心)                             │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │  ZL.Iot.Runner                                              │ │
│  │  ┌───────────────┐ ┌──────────────────┐ ┌───────────────┐ │ │
│  │  │ RunnerConfig   │ │ DeviceRunner     │ │TriggerExecutor│ │ │
│  │  │ (配置模型)     │ │ (多设备协调器)    │ │ (触发执行器)  │ │ │
│  │  ├───────────────┤ ├──────────────────┤ ├───────────────┤ │ │
│  │  │ ConfigLoader   │ │SingleDeviceRunner│ │               │ │ │
│  │  │ (JSON/XML加载) │ │ (单设备驱动)     │ │               │ │ │
│  │  └───────────────┘ └──────────────────┘ └───────────────┘ │ │
│  └─────────────────────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────────────────────┤
│                  基础层 (Foundation)                               │
│  ZL.PlcBase (HslUnifiedDriver) │ ZL.Tag (TagItem) │ ZL.Biz.Execute│
│  ZL.Dao.IotDevice (数据访问)   │ SqlSugar (ORM)   │ HslCommunication│
└──────────────────────────────────────────────────────────────────┘
```

### 2.1 项目结构

```
iot-sdk/
├── src/
│   ├── application/                          # 应用层
│   │   ├── ZL.Iot.Runner/                    # 运行时核心库 (net8.0)
│   │   │   ├── Configuration/
│   │   │   │   ├── RunnerConfig.cs           # 配置模型 (214行)
│   │   │   │   └── ConfigLoader.cs           # JSON/XML 加载器 (203行)
│   │   │   └── Runtime/
│   │   │       ├── DeviceRunner.cs           # 多设备协调器 (312行)
│   │   │       ├── SingleDeviceRunner.cs     # 单设备运行器 (391行)
│   │   │       └── TriggerExecutor.cs        # 触发执行器 (392行)
│   │   │
│   │   ├── ZL.Iot.Runner.Generator/          # 打包流水线引擎 (net8.0)
│   │   │   └── Core/
│   │   │       ├── BuildEngine.cs            # 编译发布引擎 (318行)
│   │   │       ├── ProjectGenerator.cs       # 项目生成编排 (392行)
│   │   │       ├── PackageBuilder.cs         # ZIP 打包 (203行)
│   │   │       ├── TemplateRenderer.cs       # Scriban 模板渲染 (189行)
│   │   │       ├── JobScheduler.cs           # 任务调度器 (290行)
│   │   │       ├── JobStore.cs               # 内存任务存储 (156行)
│   │   │       └── Models/
│   │   │           ├── GenerateRequest.cs    # 生成请求模型
│   │   │           ├── GenerateResult.cs     # 生成结果模型
│   │   │           └── GenerateJob.cs        # 任务状态机
│   │   │
│   │   ├── ZL.Iot.Runner.Templates/          # Scriban 模板资源
│   │   │   ├── console/                      # Console 宿主模板
│   │   │   ├── windows-service/              # Windows 服务模板
│   │   │   ├── linux-systemd/                # Linux systemd 模板
│   │   │   └── winform/                      # WinForms GUI 模板
│   │   │
│   │   ├── ZL.Iot.Plugin/                    # [旧架构] 插件系统 (netstandard2.1)
│   │   └── ZL.EdgeService/                   # [旧架构] 边缘服务 (netstandard2.1)
│   │
│   ├── app/                                  # 入口应用
│   │   ├── ZL.Iot.Runner.Cli/                # 开发期 CLI 入口
│   │   └── ZL.Iot.Runner.GeneratorCli/       # Generator HTTP 服务 + CLI
│   │       └── HttpServer/
│   │
│   ├── domain/                               # 领域层
│   ├── foundation/                           # 基础层
│   └── platform/                             # 平台适配层
│
├── tests/                                    # 测试项目
│   ├── ZL.Iot.Runner.Tests/                  # Runner 单元测试 (65 tests)
│   ├── ZL.Iot.Runner.Generator.Tests/        # Generator 单元测试 (107 tests)
│   ├── ZL.Biz.Execute.Tests/                 # 业务执行测试 (107 tests)
│   ├── ZL.EdgeService.Tests/                 # 边缘服务测试 (14 tests)
│   └── ZL.Watchdog.Tests/                    # 看门狗测试 (18 tests)
│
└── docs/                                     # 文档
```

---

## 3. 核心模块详解

### 3.1 ZL.Iot.Runner — 运行时核心库

#### 3.1.1 RunnerConfig.cs（配置模型，214 行）

**职责**：定义完整的 Runner 配置数据模型，支持 JSON/XML 双格式序列化。

**核心类层次**：

```
RunnerConfig                      ← 根配置，对应 runner.config.json
├── RunnerOptions                 ← 全局选项（名称/日志级别/数据存储）
│   └── DataStorageOptions        ← 存储配置（Sqlite/MySql/None）
└── DeviceProfile[]               ← 设备列表（支持多设备）
    ├── TagProfile[]              ← 标签列表
    │   ├── Id                    ← 业务标签名
    │   ├── Address               ← PLC 地址 (如 DB1.DBD0)
    │   ├── DataType              ← 数据类型 (float/int/bool/string/byte)
    │   ├── TagType               ← 标签类型 (""/M/D/HB/RO)
    │   ├── Deadband              ← 死区值
    │   └── ScanRate              ← 独立扫描频率
    └── ExecutorProfile[]         ← 执行器列表
        ├── BizCode               ← 业务编码
        ├── TagId                 ← 关联标签
        ├── JudgeType             ← 触发条件 (0-8, int 类型)
        ├── JudgeExp              ← 触发表达式
        ├── ExeType               ← 执行类型 (S/Q/M)
        ├── Script                ← SQL 脚本（支持 {{Variable}} 语法）
        └── ExeOrder              ← 执行顺序
```

**JudgeType 枚举（0-8）**：

| 值 | 含义 | 说明 |
|---|------|------|
| 0 | 值任意 | 无条件触发 |
| 1 | 值==1 | bool 型 true |
| 2 | 值==0 | bool 型 false |
| 3 | 值变化 | 任意变化即触发 |
| 4 | 值>Threshold | 大于阈值 |
| 5 | 值<Threshold | 小于阈值 |
| 6 | 值>=Threshold | 大于等于阈值 |
| 7 | 值<=Threshold | 小于等于阈值 |
| 8 | 值!=Threshold | 不等于阈值 |

**ExeType 枚举**：

| 值 | 含义 | 说明 |
|---|------|------|
| S | Select | 查询并回填变量 |
| Q | Query | 仅执行查询 |
| M | Modify | 执行 INSERT/UPDATE/DELETE |

#### 3.1.2 ConfigLoader.cs（配置加载器，203 行）

**职责**：自动识别 JSON/XML 格式，反序列化为 RunnerConfig，执行配置验证。

**关键设计**：

```
Load(configPath)
    ├── 按扩展名识别: .json → LoadFromJson / .xml → LoadFromXml
    ├── JSON: System.Text.Json (CamelCase, 大小写不敏感)
    ├── XML:  XmlSerializer (兼容老项目格式)
    └── ValidateConfig()
        ├── 设备列表非空
        ├── 每个设备: Code/Protocol/Ip 非空
        ├── 协议白名单校验 (14种协议)
        ├── 标签 Id 唯一性
        └── 执行器引用的 TagId 必须存在且启用
```

**协议白名单**：SiemensS7, SiemensS7200Smart, ModbusTcp, MitsubishiMC, OmronFinsTcp, BacnetIp, ModbusRtu, MelsecA, MelsecQnA, OmronFinsSerial, Bacnet, KepwareOPC, OPCUA

#### 3.1.3 DeviceRunner.cs（多设备协调器，312 行）

**职责**：管理多个 SingleDeviceRunner 实例，统一提供启动/停止/状态查询。

**生命周期**：

```
new DeviceRunner(config, loggerFactory)
    → Initialize()          // 为每个 DeviceProfile 创建 SingleDeviceRunner
        → Start()           // 并发启动所有设备
            → Run(cancellationToken)  // 阻塞运行，打印状态摘要
                → Stop()            // 优雅停止所有设备
                    → Dispose()     // 释放资源
```

**关键特性**：
- 一个配置文件 → 多个设备驱动实例 → 独立并发运行
- 单个设备启动失败不影响其他设备
- Ctrl+C 优雅退出
- 定期打印运行摘要（设备连接状态、采集计数）

#### 3.1.4 SingleDeviceRunner.cs（单设备运行器，391 行）

**职责**：封装单个设备的完整生命周期 — 驱动创建、连接、采集、触发。

**工厂方法 Create()**：

```csharp
SingleDeviceRunner.Create(profile, loggerFactory)
    ├── 创建 HslUnifiedDriver(deviceCode, ProfileToDeviceConfig(profile))
    ├── 为每个 TagProfile 创建 TagItem 并注册到 driver.Tags
    ├── 创建 TriggerExecutor(executors, loggerFactory)
    ├── 订阅 driver.TriggerDataChanged → executor.OnTagChanged
    └── 返回 SingleDeviceRunner(deviceCode, driver, executor, logger)
```

**Start() 流程**：

```
Start()
    ├── driver.Connect()
    ├── 启动后台采集任务 (Task.Run 循环)
    │   ├── driver.ReadAsync()     // 批量读取所有标签
    │   └── 等待 ReadInterval ms
    └── 事件回调链:
        HslUnifiedDriver.TriggerDataChanged
            → SingleDeviceRunner.OnTagTriggered()
                → TriggerExecutor.OnTagChanged()
                    → EvaluateCondition() [判断是否触发]
                        → RenderScript() [变量替换]
                            → ExecuteSql() [执行 SQL]
```

#### 3.1.5 TriggerExecutor.cs（触发执行器，392 行）

**职责**：评估触发条件，渲染 SQL 脚本，执行数据库操作。

**OnTagChanged() 流程**：

```
OnTagChanged(tagId, value)
    ├── 按 ExeOrder 排序执行器
    ├── foreach 启用的执行器:
    │   ├── EvaluateCondition(exe, value)
    │   │   ├── judgeType >= 6 → EvaluateRuleEngine() [RulesEngine 库]
    │   │   └── judgeType < 6  → EvaluateLegacyJudgeType() [传统 0-5]
    │   ├── if 触发:
    │   │   ├── RenderScript(script, tagId, value)  // {{TagId}}, {{Value}} 替换
    │   │   └── ExecuteSql(exe, renderedScript)
    │   │       └── SqlExecutor.ExecuteNonQueryAsync() [通过 DI 注入]
    │   └── 记录日志
    └── 返回触发的执行器数量
```

**变量替换语法**（三种格式兼容）：
- `{{VariableName}}` — Scriban 标准语法（推荐）
- `?VariableName?` — 遗留格式
- `#VariableName#` — 遗留格式
- `@VariableName@` — 遗留格式

### 3.2 ZL.Iot.Runner.Generator — 打包流水线引擎

#### 3.2.1 ProjectGenerator.cs（项目生成编排，392 行）

**职责**：编排完整的"模板渲染 → 编译发布 → 打包"流水线。

**GenerateAsync() 流水线**：

```
GenerateAsync(request, cancellationToken)
    │
    ├── Stage 1: 创建工作目录
    │   └── {ProjectName}-{timestamp}/
    │
    ├── Stage 2: 渲染模板 (TemplateRenderer + Scriban)
    │   ├── 根据 Platform 选择模板目录
    │   ├── 动态发现所有 .scriban 文件
    │   ├── 逐个渲染 → 输出去掉 .scriban 后缀
    │   ├── MyApp.csproj → {ProjectName}.csproj
    │   └── 写入 runner.config.json
    │
    ├── Stage 3: 编译发布 (BuildEngine)
    │   ├── if Sku == Binary:
    │   │   ├── dotnet publish -c Release -r {RID} --self-contained
    │   │   └── 解析日志提取警告/错误
    │   └── if Sku == Source:
    │       └── 跳过编译，直接打包源码
    │
    ├── Stage 4: 打包 (PackageBuilder)
    │   ├── 生成 PackageManifest (文件级 SHA256)
    │   ├── 写入 manifest.json
    │   ├── 重新打包（含更新后的 manifest）
    │   └── 计算整体 SHA256
    │
    ├── Stage 5: 生成 README.md
    │
    └── Stage 6: 清理工作目录
```

**进度回调**：通过 `OnProgress(string phase, int percent)` 回调报告进度，JobScheduler 转发为 SSE 事件推送到前端。

#### 3.2.2 BuildEngine.cs（编译发布引擎，318 行）

**职责**：封装 `dotnet publish` 命令，解析编译日志。

**关键设计**：
- 使用 `ProcessStartInfo` 启动 dotnet CLI
- 实时读取标准输出/错误流
- 解析日志提取警告和错误信息
- 支持自定义输出目录

**BuildPublishOptions**：

| 参数 | 默认值 | 说明 |
|------|--------|------|
| Configuration | Release | 编译配置 |
| RuntimeIdentifier | win-x64 | 目标运行时 |
| SelfContained | true | 自包含发布 |
| PublishSingleFile | true | 单文件发布 |
| IncludeNativeLibrariesForSelfExtract | true | 包含原生库 |
| EnableCompressionInSingleFile | true | 单文件压缩 |

#### 3.2.3 PackageBuilder.cs（打包引擎，203 行）

**职责**：将输出目录压缩为 ZIP 字节流，生成包清单。

**PackWithManifest() 流程**：

```
PackWithManifest(sourceDir, applicationName, runtimeIdentifier, ...)
    ├── 1. 首次打包为 ZIP
    ├── 2. 解压到临时目录
    ├── 3. 计算每个文件的 SHA256 → 生成 PackageManifest
    ├── 4. 计算整体 ZIP 的 SHA256
    ├── 5. 将 manifest.json 写回源目录
    ├── 6. 重新打包（含更新后的 manifest）
    └── 7. 清理临时文件
    │
    → 返回 (zipBytes, manifest)
```

**注意**：存在经典的"九头蛇问题"（hydra problem）— manifest 中的 sha256 是第一次打包的哈希，第二次打包后实际 zip 的哈希会变化。但文件级哈希不受影响，足以验证完整性。

#### 3.2.4 TemplateRenderer.cs（Scriban 模板渲染器，189 行）

**职责**：从 EmbeddedResource 读取 Scriban 模板，渲染为实际代码。

**模板上下文变量**：

| 变量 | 类型 | 说明 |
|------|------|------|
| project_name | string | 项目名（如 FactoryLine1_Runner） |
| namespace | string | 命名空间（PascalCase） |
| version | string | 版本号（从程序集元数据读取） |
| runner_version | string | Runner.Lib NuGet 版本号 |
| runtime_identifier | string | 目标 RID（如 win-x64） |
| config | GenerateRequest | 完整请求对象（供 Scriban 表达式使用） |

**模板程序集加载策略**：
1. 检查已加载程序集
2. 从当前目录加载 DLL
3. 失败则抛出异常

#### 3.2.5 JobScheduler.cs（任务调度器，290 行）

**职责**：信号量限流 + FIFO 队列 + 超时控制 + 用户限流。

**架构**：

```
用户请求 → Enqueue() → Channel<FIFO> → WorkerLoop
                                        ↓
                                  SemaphoreSlim (限流)
                                        ↓
                                  ProjectGenerator (执行)
```

**关键参数**：

| 参数 | 默认值 | 说明 |
|------|--------|------|
| maxConcurrency | 2 | 最大并发构建数 |
| maxQueueLength | 50 | 队列容量（防 DoS） |
| jobTimeout | 120s | 单任务超时 |
| userRateLimitInterval | 10s | 用户请求间隔 |

**SSE 事件推送**：每个 GenerateJob 有独立的 `BoundedChannel<JobStatusEvent>`，前端通过 `/api/jobs/{id}/sse` 订阅实时进度。

#### 3.2.6 JobStore.cs（内存任务存储，156 行）

**职责**：线程安全的内存任务存储 + 自动清理。

**TTL 策略**：
- 结果字节（ResultBytes）保留 5 分钟
- 任务元数据保留 30 分钟
- 每 5 分钟自动清理一次

### 3.3 ZL.Iot.Runner.Templates — 模板资源

#### 3.3.1 模板目录结构

```
ZL.Iot.Runner.Templates/
├── console/                              # Console 应用（开发/测试）
│   ├── MyApp.csproj.scriban             # .csproj 模板
│   ├── ProgramCs.scriban                # 入口程序
│   └── NLog.config.scriban              # NLog 日志配置
│
├── windows-service/                      # Windows 服务（生产）
│   ├── MyApp.csproj.scriban
│   ├── ProgramCs.scriban                # IHostedService + WindowsService
│   ├── NLog.config.scriban
│   ├── install.bat.scriban              # sc create 安装脚本
│   └── uninstall.bat.scriban            # sc delete 卸载脚本
│
├── linux-systemd/                        # Linux systemd 服务（生产）
│   ├── MyApp.csproj.scriban
│   ├── ProgramCs.scriban                # IHostedService + Kestrel 健康检查
│   ├── NLog.config.scriban
│   ├── runner.service.scriban           # systemd unit 文件
│   ├── installSh.scriban                # systemctl enable 安装脚本
│   └── uninstallSh.scriban              # systemctl disable 卸载脚本
│
└── winform/                              # WinForms GUI 应用
    ├── MyApp.csproj.scriban             # WinForms 项目
    ├── ProgramCs.scriban
    ├── MainFormCs.scriban               # 主窗口
    ├── ExecutorPanelCs.scriban          # 执行器面板
    ├── TagGridViewCs.scriban            # 标签数据网格
    └── NLog.config.scriban
```

#### 3.3.2 各模板形态特点

| 形态 | 适用场景 | 宿主类型 | 健康检查 | 安装方式 |
|------|---------|---------|---------|---------|
| Console | 开发/测试/调试 | 控制台应用 | 无 | 直接运行 |
| Windows Service | Windows 生产环境 | IHostedService | localhost:5000/health | sc create |
| Linux systemd | Linux 生产环境 | IHostedService | 0.0.0.0:5000/health | systemctl enable |
| WinForms | 需要 GUI 的现场 | WinForms 窗口 | 无 | 直接运行 |

**所有模板的共同特征**：
- 引用 `ZL.Iot.Runner.Lib` NuGet 包（无需 clone monorepo）
- 使用 NLog 结构化日志
- 单文件自包含发布（PublishSingleFile + SelfContained）
- 禁用 IL Trimmer（Modbus/MQTT 等重度反射库不兼容）
- runner.config.json 标记为 Content（单文件发布时正确输出）

---

## 4. 流水线流程详解

### 4.1 完整流水线时序图

```
web_mini 前端                    GeneratorCli HTTP 服务           Generator 引擎层
    │                                  │                              │
    │  1. 用户配置设备/标签/执行器       │                              │
    │  (iot_device/iot_tag/iot_exe 表)  │                              │
    │                                  │                              │
    │  2. POST /api/generate           │                              │
    │  { config: RunnerConfig,         │                              │
    │    platform, sku, rid, name }    │                              │
    │─────────────────────────────────>│                              │
    │                                  │  3. API Key 验证              │
    │                                  │  4. JobScheduler.Enqueue()   │
    │                                  │─────────────────────────────>│
    │                                  │   (返回 jobId)               │  5. Channel 入队
    │                                  │<─────────────────────────────│
    │  6. { jobId }                    │                              │
    │<─────────────────────────────────│                              │
    │                                  │                              │
    │  7. GET /api/jobs/{id}/sse       │                              │
    │─────────────────────────────────>│                              │
    │                                  │  8. WorkerLoop 出队          │
    │                                  │  9. ProjectGenerator.       │
    │                                  │     GenerateAsync()         │
    │                                  │─────────────────────────────>│
    │                                  │                              │  10. 渲染模板
    │                                  │  ◀─ SSE: progress 10% ──────│
    │  ◀─ event: {status,progress} ───│                              │
    │                                  │                              │  11. dotnet publish
    │                                  │  ◀─ SSE: progress 50% ──────│
    │  ◀─ event: {status,progress} ───│                              │
    │                                  │                              │  12. 打包 ZIP
    │                                  │  ◀─ SSE: progress 90% ──────│
    │  ◀─ event: {status,progress} ───│                              │
    │                                  │                              │  13. 返回 GenerateResult
    │                                  │<─────────────────────────────│
    │                                  │  14. job.SetSucceeded()      │
    │  ◀─ event: {status:"complete"} ──│                              │
    │                                  │                              │
    │  15. GET /api/jobs/{id}/download │                              │
    │─────────────────────────────────>│                              │
    │  16. application/zip (ZIP 字节流) │                              │
    │<─────────────────────────────────│                              │
```

### 4.2 GenerateRequest 模型

```csharp
GenerateRequest
├── Config: RunnerConfig        // 设备/标签/执行器配置
├── Platform: TargetPlatform    // Console/WindowsService/LinuxSystemd/WinForm/Web
├── Sku: SkuMode               // Binary(免费exe) / Source(付费源码)
├── RuntimeIdentifier: string   // win-x64 / linux-x64 / osx-x64
├── ProjectName: string         // 项目名
├── Version: string             // 版本号
└── OutputFormat: string        // zip / dir
```

### 4.3 GenerateJob 状态机

```
        创建
         │
         ▼
      [Queued] ──── 取消请求 ────→ [Cancelled]
         │                          │
         │  Worker 拾取             │ 最终态
         ▼                          │
      [Running] ──── 120s 超时 ──→ [TimedOut]
         │                           │
         │  成功/失败                │ 最终态
         ▼                           │
   [Succeeded] 或 [Failed]          │
         │                          │
         └──────────────────────────┘
              最终态
```

每个状态变更通过 `BoundedChannel<JobStatusEvent>` (容量 100) 推送 SSE 事件到前端。

---

## 5. 运行时架构详解

### 5.1 生成的 Runner 应用运行时架构

```
┌────────────────────────────────────────────────────┐
│                  生成的 Runner 应用                   │
│                                                     │
│  Program.cs                                         │
│  ├── ConfigLoader.Load("runner.config.json")        │
│  ├── LoggerFactory.Create(b => b.AddNLog())         │
│  └── new DeviceRunner(config, loggerFactory)        │
│       └── runner.Run(cancellationToken)             │
│           ├── runner.Initialize()                    │
│           │   └── foreach device in config.Devices:  │
│           │       SingleDeviceRunner.Create(profile) │
│           │           ├── HslUnifiedDriver(profile)  │
│           │           ├── TagItem[] → driver.Tags    │
│           │           └── TriggerExecutor(executors) │
│           ├── runner.Start()                         │
│           │   └── foreach singleRunner:              │
│           │       singleRunner.Start()               │
│           │           ├── driver.Connect()           │
│           │           └── Task.Run(采集循环)          │
│           │               while !ct.IsCancellationRequested:
│           │                   driver.ReadAsync()     │
│           │                   Thread.Sleep(interval) │
│           └── 等待取消信号                             │
│                                                     │
│  事件回调链:                                         │
│  HslUnifiedDriver.TriggerDataChanged                │
│    → SingleDeviceRunner.OnTagTriggered()            │
│      → TriggerExecutor.OnTagChanged()               │
│        → EvaluateCondition()                         │
│          → RenderScript()                            │
│            → SqlExecutor.ExecuteNonQueryAsync()     │
│                                                     │
│  日志: NLog → 文件/控制台                            │
│  存储: SqlSugar → Sqlite/MySql                      │
└────────────────────────────────────────────────────┘
```

### 5.2 多设备并发模型

```
DeviceRunner (协调器)
    ├── SingleDeviceRunner[plc_1]  ← 独立 Thread/Task
    │       ├── HslUnifiedDriver (SiemensS7@192.168.1.100:102)
    │       ├── 采集循环 (ReadInterval=200ms)
    │       └── TriggerExecutor (3 executors)
    │
    ├── SingleDeviceRunner[plc_2]  ← 独立 Thread/Task
    │       ├── HslUnifiedDriver (ModbusTcp@192.168.1.101:502)
    │       ├── 采集循环 (ReadInterval=500ms)
    │       └── TriggerExecutor (5 executors)
    │
    └── SingleDeviceRunner[plc_3]  ← 独立 Thread/Task
            ├── HslUnifiedDriver (MitsubishiMC@192.168.1.102:9600)
            ├── 采集循环 (ReadInterval=1000ms)
            └── TriggerExecutor (2 executors)
```

每个设备完全独立：独立驱动连接、独立采集线程、独立触发执行器。一个设备故障不影响其他设备。

---

## 6. 配置模型详解

### 6.1 完整配置示例

```json
{
  "runner": {
    "name": "FactoryLine1_Runner",
    "logLevel": "Information",
    "dataStorage": {
      "type": "Sqlite",
      "connectionString": "Data Source=./data/iot_runner.db"
    }
  },
  "devices": [
    {
      "code": "plc_line1_main",
      "protocol": "SiemensS7",
      "ip": "192.168.1.100",
      "port": 102,
      "rack": 0,
      "slot": 1,
      "connectTimeout": 5000,
      "readInterval": 200,
      "tags": [
        {
          "id": "Temperature_01",
          "description": "1号炉温度",
          "address": "DB1.DBD0",
          "dataType": "float",
          "tagType": "M",
          "deadband": 0.5,
          "scanRate": 0,
          "enable": true
        },
        {
          "id": "Alarm_Status",
          "description": "报警状态",
          "address": "DB1.DBX0.0",
          "dataType": "bool",
          "tagType": "M",
          "enable": true
        },
        {
          "id": "Heartbeat",
          "description": "心跳标签",
          "address": "DB1.DBD100",
          "dataType": "int",
          "tagType": "HB",
          "scanRate": 1000,
          "enable": true
        }
      ],
      "executors": [
        {
          "bizCode": "EXE_Temp_High",
          "tagId": "Temperature_01",
          "judgeType": 4,
          "judgeExp": "100",
          "exeType": "M",
          "script": "INSERT INTO alerts (tag_id, tag_value, alert_time) VALUES ('{{TagId}}', {{Value}}, datetime('now'))",
          "exeOrder": 1,
          "enable": true
        },
        {
          "bizCode": "EXE_Alarm_On",
          "tagId": "Alarm_Status",
          "judgeType": 1,
          "judgeExp": "",
          "exeType": "M",
          "script": "INSERT INTO alarms (device, tag, status, time) VALUES ('plc_line1_main', '{{TagId}}', 'ACTIVE', datetime('now'))",
          "exeOrder": 2,
          "enable": true
        }
      ]
    }
  ]
}
```

### 6.2 配置验证规则

| 规则 | 级别 | 说明 |
|------|------|------|
| Devices 非空 | 必填 | 至少一个设备 |
| Device.Code 非空 | 必填 | 设备编码唯一标识 |
| Device.Protocol 在白名单中 | 必填 | 14种支持协议 |
| Device.Ip 非空 | 必填 | PLC IP 地址 |
| Tag.Id 唯一 | 必填 | 同设备内标签不重复 |
| Executor.TagId 存在 | 必填 | 执行器引用的标签必须存在且启用 |

---

## 7. 模板体系详解

### 7.1 Scriban 模板渲染机制

**模板文件命名约定**：`{输出文件名}.scriban`

渲染时去掉 `.scriban` 后缀，特殊处理 `.csproj` 重命名：
- `MyApp.csproj.scriban` → `{ProjectName}.csproj`
- `ProgramCs.scriban` → `Program.cs`
- `NLog.config.scriban` → `NLog.config`

**上下文对象**：

```scriban
// 可用变量
{{ project_name }}          → "FactoryLine1_Runner"
{{ namespace }}             → "FactoryLine1Runner"
{{ version }}               → "1.0.0+20260605-143000"
{{ runner_version }}        → "1.0.1"
{{ runtime_identifier }}    → "win-x64"
{{ config.Devices[0].Code }} → "plc_line1_main"  (通过 config 对象访问完整配置)
```

### 7.2 各平台模板差异

| 特性 | Console | Windows Service | Linux systemd | WinForms |
|------|---------|----------------|---------------|----------|
| 项目 SDK | NetSDK | NetSDK | NetSDK | NetSDK |
| OutputType | Exe | Exe | Exe | WinExe |
| 日志 | NLog | NLog | NLog | NLog |
| 健康检查 | 无 | localhost:5000 | 0.0.0.0:5000 | 无 |
| 服务管理 | Ctrl+C | sc start/stop | systemctl | 窗口关闭 |
| 安装脚本 | 无 | install.bat | install.sh | 无 |
| 卸载脚本 | 无 | uninstall.bat | uninstall.sh | 无 |
| .csproj 依赖 | Runner.Lib + NLog | +Microsoft.Extensions.Hosting.WindowsServices | +Microsoft.Extensions.Hosting | +System.Windows.Forms |

---

## 8. 与 TMom.Device.Runtime.Host 的集成分析

### 8.1 TMom.Device.Runtime.Host 架构概览

TMom.Device.Runtime.Host 是当前生产运行的 **集成运行时宿主**，它是一个完整的 ASP.NET Core Web API 应用，承担了以下职责：

```
TMom.Device.Runtime.Host (ZL.PlcBase.Host.exe)
├── Web API 层 (Minimal API Endpoints)
│   ├── /api/health          → HealthEndpoints
│   ├── /api/auth/*          → LoginEndpoints
│   ├── /api/devices/*       → DeviceCrudEndpoints
│   ├── /api/devices/runtime/* → DeviceRuntimeEndpoints
│   ├── /api/tags/*          → TagCrudEndpoints
│   ├── /api/exes/*          → ExeCrudEndpoints
│   ├── /api/exec-logs/*     → ExecLogEndpoints
│   ├── /api/simulators/*    → IotSimEndpoints + ZLSimulatorEndpoints
│   ├── /api/gateway/*       → GatewayEndpoints
│   ├── /api/alerts/*        → AlertEndpoints
│   ├── /api/integration/*   → IntegrationEndpoints
│   └── /api/data-query      → DataQueryEndpoints
│
├── SignalR Hubs
│   └── /plcHub              → PlcHub (实时 PLC 数据推送)
│
├── 后台服务 (IHostedService)
│   ├── PlcEngineStartupService    → 启动时从 DB 加载设备并启动采集
│   ├── IotSimHeartbeatService     → 仿真器心跳检测
│   ├── LogCleanupService          → 日志清理
│   ├── TagCacheCleanupService     → 标签缓存清理
│   ├── WebhookRetryService        → Webhook 重试
│   └── EventStatsConsumer         → 事件统计
│
├── 核心业务服务
│   ├── DeviceRuntimeService       → 设备运行时管理 (启停/读写/健康检查)
│   ├── ExecutionEngineService     → 执行引擎 (触发判断 → 脚本渲染 → SQL 执行)
│   ├── AlertService               → 告警服务
│   ├── AuthService / JwtService   → 认证授权
│   └── IotSimManagerService       → IoT 仿真器管理
│
├── 数据访问层
│   ├── LightweightDeviceRepository  → 设备/标签/执行器 CRUD (SqlSugar)
│   ├── TagHistoryRepository         → 标签历史数据
│   └── DeviceConfigCacheService     → 配置缓存
│
├── 执行引擎组件
│   ├── TriggerEvaluator           → 标准触发判断 (0-5)
│   ├── EnhancedTriggerEvaluator   → 增强触发 (6-8, NCalc/时间窗口/滑动窗口)
│   ├── ScriptParser               → SQL 脚本变量替换
│   └── SqlExecutor                → SQL 执行 (含安全校验)
│
└── 中间件
    ├── ExceptionMiddleware         → 全局异常处理
    ├── RateLimitMiddleware         → 限流
    └── RequestLoggingMiddleware    → 请求日志
```

### 8.2 TMom.Host 与 Runner 的核心差异

| 维度 | TMom.Device.Runtime.Host | Runner 生成的应用 |
|------|------------------------|------------------|
| **架构范式** | 运行时动态加载 | 编译时固化 |
| **应用类型** | ASP.NET Core Web API | 控制台/Windows服务/systemd |
| **设备管理** | 从 MySQL 数据库动态加载 | 配置文件静态定义 |
| **配置来源** | 数据库 (iot_device/iot_tag/iot_exe 表) | runner.config.json |
| **驱动创建** | 运行时通过 DB 查询 → 动态构建 HslUnifiedDriver | 编译时配置 → 启动时直接创建 |
| **触发执行** | ExecutionEngineService (含 TriggerEvaluator + EnhancedTriggerEvaluator) | TriggerExecutor (轻量级) |
| **JudgeType** | string 类型 ("0"-"8") | int 类型 (0-8) |
| **触发器种类** | 11种 (0-5 标准 + 6-8 增强 + 边沿/周期) | 9种 (0-8) |
| **SQL 执行** | SqlExecutor (含安全校验、MySql/Sqlite) | SqlExecutor (DI 注入，MySql/Sqlite) |
| **实时推送** | SignalR (PlcHub) | 无（纯后台运行） |
| **Web API** | 完整 REST API + Swagger | 无（仅健康检查端点） |
| **认证** | JWT Bearer | 无 |
| **前端** | web_mini (Vue 3 SPA) | 无 |
| **仿真器** | PlcSimulator + ZLSimulator (gRPC) | 无 |
| **告警** | AlertService (邮件/Webhook/钉钉) | 无 |
| **审计日志** | AuditLogService | 无 |
| **部署** | Docker / 直接运行 | 单文件自包含 exe |
| **目标用户** | 平台运维人员（多站点管理） | 现场工程师（单站点运行） |

### 8.3 关键集成点分析

#### 8.3.1 PlcEngineStartupService — 当前设备启动流程

```csharp
// PlcEngineStartupService.StartAsync()
// 应用启动时从数据库加载设备并启动采集

1. 从 DB 加载启用设备: repo.GetActiveDevicesAsync()
2. foreach 设备:
   ├── 构建 DeviceConfig (从 DB 字段映射)
   ├── 创建 HslUnifiedDriver(deviceConfig)
   ├── 从 DB 加载标签: repo.GetTagsByDeviceIdAsync(deviceId)
   ├── foreach 标签: driver.Tags.TryAdd(tagName, new TagItem { ... })
   ├── driver.Initialize()
   ├── driver.StartBackgroundTasks()
   └── runtimeService.RegisterDevice(deviceId, driver)
3. 启动 SignalR 桥接 (PlcBridgeBootstrap)
```

**对应 Runner 架构**：这段逻辑等价于 `DeviceRunner.Initialize()` + `SingleDeviceRunner.Create()`，但数据来源不同（DB vs 配置文件）。

#### 8.3.2 ExecutionEngineService — 当前执行流程

```csharp
// 标签值变化 → 执行引擎处理
// 由 HslUnifiedDriver.TriggerDataChanged 事件触发

1. ProcessTagValueChangeAsync(tagName, value)
2. GetRelatedExesAsync(tagName)  // 从缓存获取相关执行器
3. foreach 执行器 (并行 Task.WhenAll):
   ├── ParseJudgeType(exe.JudgeType)  // string → int
   ├── if judgeType >= 6: EnhancedTriggerEvaluator.EvaluateAsync()
   ├── else: TriggerEvaluator.Evaluate()
   ├── if 触发:
   │   ├── ScriptParser.Render(script, variables)  // 变量替换
   │   ├── SqlExecutor.ValidateSql(renderedScript)  // 安全校验
   │   └── SqlExecutor.ExecuteAsync(exeType, sql)   // 执行
   ├── PersistExecutionLogAsync()  // 写入 iot_exe_log 表
   └── TriggerExecutionAlertAsync()  // 告警
```

**对应 Runner 架构**：等价于 `TriggerExecutor.OnTagChanged()`，但 TMom.Host 版本更丰富：
- 支持增强触发器 (NCalc 表达式/时间窗口/滑动窗口)
- 有 SQL 安全校验
- 有执行日志持久化
- 有告警集成

#### 8.3.3 DeviceRuntimeService — 设备运行时管理

```csharp
// DeviceRuntimeService
// 管理已启动设备的运行时状态

- RegisterDevice(deviceId, driver)     // 注册设备驱动
- StartDeviceAsync(deviceId)           // 动态启动设备（从 DB 加载配置）
- StopDeviceAsync(deviceId)            // 停止设备
- ReadTagAsync(deviceId, tagName)      // 读取单个标签
- WriteTagAsync(deviceId, tagName, value) // 写入标签
- GetDeviceState(deviceId)             // 获取设备状态
- CheckDeviceHealth(deviceId)          // 健康检查
- RemoveDeviceAsync(deviceId)          // 移除设备
- _configCache: ConcurrentDictionary   // 配置缓存（1小时过期清理）
```

**对应 Runner 架构**：Runner 没有动态启停能力，设备在启动时全部加载。TMom.Host 支持运行时动态添加/移除设备。

### 8.4 TMom.Host 中已引用的 iot-sdk 组件

```xml
<!-- TMom.Device.Runtime.Host.csproj -->
<ProjectReference Include="..\..\iot-sdk\ZL.Dao.IotDevice\ZL.Dao.IotDevice.csproj" />
<ProjectReference Include="..\..\iot-sdk\ZL.Biz.Execute\ZL.Biz.Execute.csproj" />
<ProjectReference Include="../../../ZL.PlcBase/ZL.PlcBase/ZL.PlcBase.csproj" />
<ProjectReference Include="../../../ZL.PlcBase/ZL.PlcBase.Bridges/ZL.PlcBase.Bridges.csproj" />
```

**引用关系**：
- `ZL.Dao.IotDevice` — 数据访问对象（IotDevice/IotTag/PlcExeEntity 等实体 + SqlSugar 操作）
- `ZL.Biz.Execute` — 业务执行逻辑
- `ZL.PlcBase` — PLC 驱动基础库（HslUnifiedDriver）
- `ZL.PlcBase.Bridges` — SignalR 桥接（PlcBridgeBootstrap/SignalRPlcBridgeSink）

**注意**：TMom.Host **未直接引用** `ZL.Iot.Runner` 或 `ZL.Iot.Runner.Generator`。Runner 系列是独立的新架构，与当前 TMom.Host 并行存在。

### 8.5 数据库实体映射

TMom.Host 使用的数据库表与 Runner 配置模型的对应关系：

| 数据库表 | TMom.Host 实体 | Runner 配置模型 | 说明 |
|---------|---------------|----------------|------|
| iot_device | IotDevice | DeviceProfile | 设备配置 |
| iot_tag | IotTag | TagProfile | 标签配置 |
| iot_exe | PlcExeEntity | ExecutorProfile | 执行器配置 |
| iot_exe_log | PlcExeLogEntity | — | 执行日志（Runner 无此概念） |

**关键字段映射**：

| iot_device | DeviceProfile |
|-----------|--------------|
| Code | Code |
| DeviceType → Program.MapProtocol() | Protocol |
| Address | Ip |
| Port | Port |
| ExtraConfig (JSON) | Rack, Slot, ConnectTimeout 等 |

| iot_tag | TagProfile |
|---------|-----------|
| BizTagName | Id |
| Address | Address |
| DataType | DataType |
| IsActive | Enable |

| iot_exe | ExecutorProfile |
|---------|----------------|
| TagId | TagId |
| BizCode | BizCode |
| JudgeType (string "0"-"8") | JudgeType (int 0-8) |
| JudgeExp | JudgeExp |
| ExeType | ExeType |
| Script | Script |
| Enable (int 0/1) | Enable (bool) |
| ExeOrder | ExeOrder |

---

## 9. 与 web_mini 前端的集成分析

### 9.1 web_mini 前端架构

```
web_mini/src/
├── views/device/                           # 设备管理模块
│   ├── iotDeviceConfig/                    # 设备配置 CRUD
│   │   ├── index.vue                       # 设备列表页
│   │   ├── components/
│   │   │   ├── DeviceFormModal.vue         # 设备新增/编辑弹窗
│   │   │   ├── DeviceTable.vue             # 设备表格
│   │   │   ├── DeviceToolbar.vue           # 工具栏
│   │   │   ├── DeviceTemplateModal.vue     # 设备模板
│   │   │   └── ExportImportAction.vue      # 导入导出
│   │   └── helpers/
│   │       ├── importExport.ts             # 导入导出逻辑
│   │       ├── operationLog.ts             # 操作日志
│   │       └── undoOperation.ts            # 撤销操作
│   │
│   ├── TagManagePage.vue                   # 标签管理页
│   ├── TagImportWizard.vue                 # 标签导入向导
│   ├── DeviceSetupWizard.vue               # 设备设置向导
│   ├── DeviceWorkbench.vue                 # 设备工作台
│   │   └── components/
│   │       ├── MonitorTab.vue              # 实时监控
│   │       ├── ExeRulesTab.vue             # 执行规则
│   │       ├── AlertTab.vue                # 告警
│   │       ├── SimulationTab.vue           # 仿真
│   │       └── TagManagementTab.vue        # 标签管理
│   │
│   ├── exeConfig.vue                       # 执行器配置
│   ├── exeExecLog.vue                      # 执行日志
│   ├── plcMonitor.vue                      # PLC 监控
│   ├── plcSimulator/index.vue              # PLC 仿真器
│   ├── zlSimulator/index.vue               # ZL 仿真器
│   ├── DataQueryTool.vue                   # 数据查询工具
│   ├── gateway/index.vue                   # 协议网关
│   ├── correlatedView.vue                  # 关联视图
│   └── auditLog.vue                        # 审计日志
│
├── api/                                    # API 客户端
└── components/                             # 通用组件
```

### 9.2 web_mini 与 TMom.Host 的 API 交互

web_mini 通过 API 调用与 TMom.Host 交互，核心接口：

| 前端页面 | API 端点 | 功能 |
|---------|---------|------|
| 设备列表 | GET/POST/PUT/DELETE /api/devices/* | 设备 CRUD |
| 标签管理 | GET/POST/PUT/DELETE /api/tags/* | 标签 CRUD |
| 执行器配置 | GET/POST/PUT/DELETE /api/exes/* | 执行器 CRUD |
| 设备运行时 | POST /api/devices/runtime/{id}/start/stop | 启停设备 |
| 标签读写 | GET /api/devices/runtime/{id}/tags/{name}/value | 读写标签 |
| 实时监控 | SignalR /plcHub | 实时数据推送 |
| 执行日志 | GET /api/exec-logs/* | 查询执行日志 |
| 手动触发 | POST /api/exes/{id}/execute | 手动执行执行器 |
| 脚本测试 | POST /api/exes/test-script | 测试 SQL 脚本 |
| 仿真器 | /api/simulators/* | 管理仿真器 |

### 9.3 Runner Generator 与 web_mini 的集成

GeneratorCli 提供了 HTTP API 供 web_mini 调用：

```
POST /api/generate
├── Authorization: ApiKey {apiKey}
├── Content-Type: application/json
└── Body:
    {
      "config": { /* RunnerConfig 格式 */ },
      "platform": 0,          // Console
      "sku": 0,               // Binary
      "runtimeIdentifier": "win-x64",
      "projectName": "MyRunner"
    }
→ 返回: { "jobId": "guid" }

GET /api/jobs/{jobId}/sse
→ SSE 流: data: {"status":"Running","progress":50,"phase":"building"}

GET /api/jobs/{jobId}/download
→ application/zip (ZIP 字节流)

GET /api/jobs
→ 任务列表

POST /api/jobs/{jobId}/cancel
→ 取消任务
```

### 9.4 web_mini 中 Runner 集成的现状与缺口

**现状**：
- web_mini 已完整实现了设备/标签/执行器的配置管理（通过 TMom.Host API）
- 配置数据存储在 MySQL 数据库 (iot_device/iot_tag/iot_exe 表)
- DeviceWorkbench 提供了一站式工作台（监控/规则/告警/仿真）

**缺口（需要新增的前端功能）**：

| 功能 | 说明 | 优先级 |
|------|------|--------|
| 代码生成页面 | 将 DB 配置转换为 RunnerConfig → 调用 Generator API → 下载 ZIP | P0 |
| 生成任务管理 | 查看任务队列、进度、历史 | P0 |
| Runner 部署管理 | 记录已部署的 Runner 实例、版本、状态 | P1 |
| Runner 远程更新 | 重新生成 → 推送更新到目标站点 | P1 |

**关键转换逻辑**（DB → RunnerConfig）：

```typescript
// 伪代码：将 TMom.DB 实体转换为 RunnerConfig
function convertDbToRunnerConfig(devices: IotDevice[]): RunnerConfig {
  return {
    runner: {
      name: "Runner_" + Date.now(),
      logLevel: "Information",
      dataStorage: { type: "Sqlite", connectionString: "Data Source=./data/iot_runner.db" }
    },
    devices: devices.map(dbDev => ({
      code: dbDev.code,
      protocol: mapProtocol(dbDev.deviceType),  // "Siemens" → "SiemensS7"
      ip: dbDev.address,
      port: dbDev.port,
      rack: parseExtraConfig(dbDev.extraConfig).rack ?? 0,
      slot: parseExtraConfig(dbDev.extraConfig).slot ?? 1,
      readInterval: 200,
      tags: dbDev.tags.map(t => ({
        id: t.bizTagName,
        address: t.address,
        dataType: t.dataType,
        tagType: "M",
        enable: t.isActive
      })),
      executors: dbDev.executors.map(e => ({
        bizCode: e.bizCode,
        tagId: e.tagId,
        judgeType: parseInt(e.judgeType),  // string → int
        judgeExp: e.judgeExp,
        exeType: e.exeType,
        script: e.script,
        exeOrder: e.exeOrder,
        enable: e.enable === 1
      }))
    }))
  };
}
```

---

## 10. Runner vs TMom.Host 对比与演进路线

### 10.1 能力矩阵对比

| 能力 | TMom.Host | Runner (生成物) | 说明 |
|------|----------|----------------|------|
| 多设备采集 | ✅ | ✅ | 都支持 |
| 多协议支持 | ✅ (14种) | ✅ (14种) | 共用 HslUnifiedDriver |
| 触发执行器 | ✅ (11种) | ✅ (9种) | TMom 多了增强触发 |
| SQL 执行 | ✅ (含安全校验) | ✅ (基本) | Runner 的 TriggerExecutor 无安全校验 |
| 执行日志 | ✅ (iot_exe_log 表) | ❌ | Runner 仅日志输出 |
| Web API | ✅ (完整 REST) | ❌ | Runner 无 API |
| SignalR 推送 | ✅ | ❌ | Runner 无实时推送 |
| 认证授权 | ✅ (JWT) | ❌ | Runner 无认证 |
| 告警通知 | ✅ (邮件/Webhook) | ❌ | Runner 无告警 |
| 仿真器 | ✅ (PlcSimulator+ZLSim) | ❌ | Runner 无仿真 |
| 健康检查 | ✅ | ✅ (端口 5000) | Runner 服务形态有 |
| 前端管理 | ✅ (web_mini) | ❌ | Runner 无前端 |
| 动态启停设备 | ✅ | ❌ | Runner 启动时固化 |
| 配置热更新 | ✅ (DB 实时读取) | ❌ | Runner 需重启 |
| 单文件部署 | ✅ (PublishSingleFile) | ✅ | 都支持 |
| 自包含发布 | ✅ | ✅ | 都支持 |
| 看门狗 | ❌ | ✅ (计划中) | Runner 有 Watchdog 测试 |

### 10.2 定位差异

```
┌──────────────────────────────────────────────────────────┐
│                    产品定位对比                             │
│                                                          │
│  TMom.Host                    Runner (生成物)             │
│  "管理平台"                   "现场运行"                   │
│                                                          │
│  ┌─────────────────┐              ┌─────────────────┐    │
│  │  多站点管理      │              │  单站点专用      │    │
│  │  配置中心        │              │  配置固化        │    │
│  │  实时监控        │              │  后台采集        │    │
│  │  审计/告警       │              │  轻量执行        │    │
│  │  仿真测试        │              │  无需管理界面    │    │
│  │  Web 前端        │              │  无需 Web API   │    │
│  └─────────────────┘              └─────────────────┘    │
│                                                          │
│  运行环境: 数据中心/云服务器        运行环境: 工厂边缘/工控机    │
│  资源需求: 中高 (ASP.NET Core)    资源需求: 低 (Console exe) │
│  目标用户: 平台运维人员            目标用户: 现场工程师       │
└──────────────────────────────────────────────────────────┘
```

### 10.3 演进路线

```
阶段 0 (当前): 双架构并行
├── TMom.Host 继续服务现有站点（管理平台）
├── Runner 新架构独立开发测试
└── web_mini 仅对接 TMom.Host

         ↓ 新增"代码生成"功能

阶段 1: Runner Generator 集成到 web_mini
├── web_mini 新增"代码生成"页面
├── 将 DB 配置 → RunnerConfig → 调用 Generator API
├── 下载生成的 ZIP 包部署到边缘站点
└── TMom.Host 继续作为管理平台

         ↓ 边缘站点逐步迁移

阶段 2: Runner 增强 + 边缘部署
├── TriggerExecutor 增强（对齐 TMom.Host 的增强触发器）
├── 添加看门狗进程
├── 添加配置加密
├── 边缘站点逐步从 TMom.Host → Runner 生成物
└── TMom.Host 保留管理/监控/审计功能

         ↓ 长期演进

阶段 3: 管理-运行分离
├── TMom.Host 精简为纯管理平台（去掉采集运行时）
├── Runner 生成物负责所有边缘采集
├── TMom.Host 通过 API 管理 Runner 实例（远程更新/状态查询）
└── 形成"管理端 + 边缘端"清晰分离的架构
```

---

## 11. 部署架构

### 11.1 完整部署拓扑

```
                    ┌─────────────────────────────────┐
                    │         数据中心 / 云服务器        │
                    │                                  │
                    │  ┌──────────────────────────┐   │
                    │  │   TMom.Device.Runtime.Host │   │
                    │  │   (管理平台)                │   │
                    │  │   :5000 (Web API + SPA)   │   │
                    │  │   + SignalR               │   │
                    │  └────────┬─────────────────┘   │
                    │           │                      │
                    │  ┌────────┴─────────────────┐   │
                    │  │   GeneratorCli            │   │
                    │  │   :5001 (生成服务)         │   │
                    │  │   /api/generate           │   │
                    │  └────────┬─────────────────┘   │
                    │           │                      │
                    │  ┌────────┴─────────────────┐   │
                    │  │   MySQL 数据库             │   │
                    │  │   iot_device/iot_tag/     │   │
                    │  │   iot_exe/iot_exe_log     │   │
                    │  └──────────────────────────┘   │
                    │                                  │
                    └─────────────────────────────────┘
                             │
                             │  下载 ZIP 包
                             ▼
                    ┌─────────────────────────────────┐
                    │         工厂边缘 / 工控机         │
                    │                                  │
                    │  ┌──────────────────────────┐   │
                    │  │   FactoryLine1_Runner.exe  │   │
                    │  │   (Runner 生成物)           │   │
                    │  │   单文件 ~80MB              │   │
                    │  │   健康检查 :5000/health     │   │
                    │  └────────┬─────────────────┘   │
                    │           │                      │
                    │  ┌────────┴─────────────────┐   │
                    │  │   PLC / 设备              │   │
                    │  │   Siemens S7 / Modbus /   │   │
                    │  │   Mitsubishi / Omron ...  │   │
                    │  └──────────────────────────┘   │
                    │                                  │
                    │  ┌──────────────────────────┐   │
                    │  │   Sqlite 数据库            │   │
                    │  │   ./data/iot_runner.db    │   │
                    │  │   (执行日志/告警数据)       │   │
                    │  └──────────────────────────┘   │
                    └─────────────────────────────────┘
```

### 11.2 部署方式对比

| 部署方式 | 适用场景 | 步骤 |
|---------|---------|------|
| Console | 开发/测试 | 直接运行 `./MyApp.exe runner.config.json` |
| Windows Service | Windows 工控机 | `install.bat` → `sc start MyApp` |
| Linux systemd | Linux 边缘网关 | `./install.sh` → `systemctl start runner` |
| Docker | 容器化部署 | TMom.Host 有 Dockerfile + docker-compose |

### 11.3 Runner 生成物文件结构

```
FactoryLine1_Runner.zip
├── FactoryLine1_Runner.exe          # 主程序 (~80MB 自包含)
├── runner.config.json               # 设备配置
├── NLog.config                      # 日志配置
├── manifest.json                    # 包清单 (SHA256)
├── README.md                        # 使用说明
├── install.bat / install.sh         # 服务安装脚本 (服务形态)
├── uninstall.bat / uninstall.sh     # 服务卸载脚本 (服务形态)
└── runner.service                   # systemd unit (Linux 形态)
```

---

## 12. 关键设计决策

### 12.1 为什么选择"编译时生成"而非"运行时加载"

| 考量 | 编译时生成 (Runner) | 运行时加载 (TMom.Host) |
|------|-------------------|---------------------|
| 启动速度 | ~1s (直接执行) | ~5-10s (反射+DB查询+驱动初始化) |
| 内存占用 | ~50MB | ~200MB+ (ASP.NET Core + 所有服务) |
| CPU 占用 | 低 (仅采集循环) | 中高 (Web API + SignalR + 后台服务) |
| 安全性 | 高 (编译锁定，无动态代码) | 中 (依赖运行时权限控制) |
| 可调试性 | 高 (可附加调试器到生成代码) | 中 (运行时行为依赖配置) |
| 部署复杂度 | 低 (单文件 exe) | 高 (需要 .NET Runtime / Docker) |
| 灵活性 | 低 (修改配置需重新生成) | 高 (DB 修改即时生效) |

**决策依据**：边缘现场环境通常资源有限（工控机 2G 内存 / 双核 CPU），且网络不稳定。编译时生成的轻量二进制更适合这种场景。管理平台则需要灵活性，继续使用 TMom.Host。

### 12.2 为什么选择 Scriban 而非其他模板引擎

| 引擎 | 优点 | 缺点 |
|------|------|------|
| Scriban | 沙箱安全、语法简洁、无代码执行风险 | 生态较小 |
| Razor | 功能强大、.NET 原生 | 需要编译、安全风险 |
| Mustache | 简单 | 功能太弱，不支持条件/循环 |
| Liquid | 生态大 | 较重、语法冗余 |

**决策依据**：Scriban 的沙箱特性确保模板无法执行任意代码，语法足够表达条件/循环/字符串操作，适合代码生成场景。

### 12.3 为什么 JudgeType 从 string 改为 int

**历史原因**：数据库 `iot_exe.judge_type` 字段定义为 `varchar(1)`，TMom.Host 的 `PlcExeEntity.JudgeType` 是 string。

**Runner 改进**：RunnerConfig 中 JudgeType 改为 int 类型，原因：
1. 类型安全：避免 "abc" 等非法值
2. 性能：int 比较比 string 比较快
3. 语义清晰：0-8 是枚举值，用 int 表达更准确
4. JSON 原生支持：`"judgeType": 4` 比 `"judgeType": "4"` 更符合 JSON Schema

**兼容性处理**：TMom.Host 的 `ExecutionEngineService.ParseJudgeType()` 负责 string → int 转换。前端生成 RunnerConfig 时也需要做类型转换。

### 12.4 SKU 分层商业化设计

| SKU | 产物 | 价格策略 | 目标客户 |
|-----|------|---------|---------|
| Binary (免费) | 单文件 exe + 配置 | 免费 | 个人开发者、小型项目 |
| Source (付费) | 完整 .sln + .csproj + .cs | 付费 | 企业客户、需要定制的客户 |

**技术实现**：
- Binary 模式：`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`
- Source 模式：跳过编译，直接打包源码（含 .sln/.csproj/.cs）

---

## 附录 A：术语表

| 术语 | 全称 | 说明 |
|------|------|------|
| Runner | Iot Runner | IoT 设备数据采集运行时 |
| Generator | Project Generator | 项目代码生成引擎 |
| RID | Runtime Identifier | .NET 运行时标识符 (win-x64, linux-x64) |
| RID | Runtime Identifier | .NET 运行时标识符 |
| SCE | Self-Contained Extraction | 自包含发布+自解压 |
| PLC | Programmable Logic Controller | 可编程逻辑控制器 |
| Hsl | HslCommunication | 工业通信库 |
| Tag | 标签 | PLC 数据点 |
| Executor | 执行器 | 标签触发后的业务逻辑 |
| JudgeType | 判断类型 | 触发条件类型 (0-8) |
| SSE | Server-Sent Events | 服务端推送事件 |
| DI | Dependency Injection | 依赖注入 |
| NuGet | .NET Package Manager | .NET 包管理器 |
| Scriban | Scriban Template Engine | 模板引擎 |
| SqlSugar | ORM Framework | .NET ORM 框架 |

## 附录 B：测试覆盖

| 测试项目 | 测试数 | 覆盖模块 |
|---------|--------|---------|
| ZL.Iot.Runner.Tests 