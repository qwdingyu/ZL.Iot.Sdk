---
name: api-migration-cleanup
description: >-
  验证 API 迁移替换是否彻底：读取目标 API 源码确立正确形态 → 逐文件 grep 残留的旧名称
  → 区分注释(误导性)与代码(编译/逻辑错误)分类修复 → 编译验证通过
source: auto-skill
extracted_at: '2026-06-16T14:30:00.000Z'
---

# API 迁移残留清理验证

## 适用场景

当项目中的某个 API 被替换（如 `HslUnifiedDriver` → `DriverFactory`、旧工厂类 → 新工厂类等），
批量搜索替换后未能彻底清理干净时使用。典型表现：

- 搜索替换后仍引用不存在的类型（编译阻断）
- 静态类被当作实例类使用（`is` 检查、`new` 构造 — 逻辑 bug）
- 注释/Xmldoc 仍提及旧名称（误导后继开发者）
- 测试方法名/Assert 仍引用旧 API（测试维护负担）

## 核心原则

**先读源头的正确形态，再 grep 检查，最后编译验证。不做先验假设。**

## 步骤

### Phase 1: 确立"正确形态"

在动手修改之前，**必须先读取目标 API 的源代码**，理解：

- 正确的 namespace 和类型名
- 是 static class 还是 instance class？
- 工厂方法签名（参数、返回值类型）
- 事件/接口的定义位置（在基类还是具体类？）

```bash
# 读取目标 API 源码
read_file /path/to/TargetApi.cs
```

**Why**: 不做这一步就可能误判什么样的修改是正确的（比如把静态类当实例类用），
这正是当初迁移替换出错的原因。

**关键陷阱**：目标 API 可能只在源码中存在但**未包含在已发布的 NuGet 包中**（比如新文件还未被 `git add` / 未打包）。
核实方法：
```bash
# 检查目标类型是否在已发布的 NuGet 包里
strings ~/.nuget/packages/pkg/ver/lib/netX.0/Target.dll | grep "TypeName"

# 如果不在，需要从源码重新打包后本地安装
dotnet pack SourceProject/SourceProject.csproj -c Release -o /tmp/nupkg
dotnet nuget push /tmp/nupkg/Package.version.nupkg --source ~/.nuget/packages/pkg/ver/
# 或使用 local-feed 源
cp /tmp/nupkg/Package.version.nupkg ~/.nuget/local-feed/
```

### Phase 2: 全面 grep 残留

对用户指定的所有文件（以及可能遗漏的文件）逐一 grep 旧名称：

```bash
# 搜索旧类型名
grep -n "OldTypeName" file1.cs file2.cs file3.cs

# 搜索旧工厂方法
grep -n "OldFactoryMethod" file1.cs file2.cs
```

**输出格式建议**：对每个文件，记录：
- 文件名
- 命中行号
- 命中类型（注释 / XML doc / code）
- 问题严重度（编译阻断 / 逻辑错误 / 文档过时）

### Phase 3: 分类分析

将 grep 结果分为三类，分别处理：

| 类别 | 标识 | 处理方式 | 示例 |
|---|---|---|---|
| **编译错误** | 引用了不存在的类型/方法 | 改为目标 API 的正确写法 | `OldFactory.Create(x)` → `NewFactory.Create(x)` |
| **逻辑错误** | 语法正确但语义不对 | 理解类型本质后重写 | `if(x is OldFactory)` → `if(x is NewImpl1 / NewImpl2)`（静态类不可 `is`） |
| **文档过时** | 注释或 Xmldoc 仍引用旧名称 | 更新描述，无需纠结术语 | `// OldType 实现了 IDisposable` → `// 驱动实例实现了 IDisposable` |

#### 区分注释和代码（关键）

```bash
grep -n "OldName" file.cs | grep -v "^[0-9]*://"  # 只有代码中的命中
grep -n "OldName" file.cs | grep "^[0-9]*://"     # 只有注释中的命中
```

代码引用必须修复（否则编译失败或运行时逻辑错误）。
注释引用**也应该修复**（否则文档误导后继者）。

### Phase 4: 逐文件修复

按以下优先级修复：

1. **编译错误** — 阻断性修复，优先处理（不修无法编译）
2. **逻辑错误** — 运行时 bug，需要理解架构后再改（如 `static class` 的 `is` 检查）
3. **文档过时** — 命名空间/类型引用、参数描述、注释

#### 代码修复要点

- 添加缺失的 `using` 指令
- 静态工厂 → 调用改 `TypeName.Method()` 而不是 `new TypeName()`
- 实例类型检查 → 如果工厂返回的是多态实例，检查具体子类型而非工厂类
- 事件订阅 → 从正确的实例类型上订阅，不是从工厂类上

#### 注释修复要点

- XML doc 中的 `<param>` 描述更新为当前架构
- 注释中解释"为什么这么做"的因果关系保持准确
- 不要过度重写注释，只修复因迁移而过时的部分

### Phase 5: 编译验证

```bash
# 只编译受影响的项目（快）
dotnet build ProjectA.csproj --no-restore

# 确认所有 Error 中不包含你修改的文件
```

**重要**：区分**本次修改引入的错误**和**项目中已有的错误**。
只对前者负责，后者记录但不处理（除非用户明确要求）。

### Phase 6: 交付

交付时提供：

1. **检查结果表格** — 哪些文件干净、哪些有残留、各多少处
2. **修改摘要** — 每个文件的修改清单（按代码/注释分类）
3. **编译验证结论** — 修改文件编译通过/失败（如失败注明原因）

## 注意事项

1. **不要只改代码不改注释** — 过时的注释比没有注释更危险
2. **不要只改注释不改代码** — 注释说"用新 API"但代码还在用旧 API，更具欺骗性
3. **留意 using 指令** — 迁移后可能 namespace 变了，需要加/删 using
4. **留意测试文件** — 测试中的旧名称也很常见，容易被忽略
5. **遗留构建错误** — 如果项目中已有其他错误导致编译失败，用 `--no-restore` + 目标项目单独编译来隔离验证

## 示例工作流

```
1. read_file 目标 API 源码 → 确认正确用法
2. grep -n "OldName" file1.cs file2.cs   → 收集所有命中
3. 分类：编译错误(2处) / 逻辑错误(1处) / 注释(5处)
4. 修复：hslDriverFactory → DriverFactory.Create, is DriverFactory → is NativeUnifiedDriver / is HslUnifiedDriver
5. dotnet build 目标项目 --no-restore → verify
6. 交付：检查结果表 + 修改摘要
```
