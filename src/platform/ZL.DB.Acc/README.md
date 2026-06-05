# ZL.DB.Acc 快速上手与目录说明

## 1. 推荐使用方式

`ZL.DB.Acc` 当前建议采用三层调用模型：

### 1.1 新项目默认入口：`Repository<T>`

适用于：

- 常规 CRUD
- Queryable 条件查询
- 原生 SQL 查询
- 分页
- 事务模板
- 常用异步访问

示例：

```csharp
public class IotDeviceService : Repository<iot_device>
{
    public List<iot_device> GetActiveList()
    {
        return FindList(x => x.is_active == 1);
    }
}
```

### 1.2 原生 SQL / 报表型 DAO：`DaoBase`

适用于：

- 动态表名
- 复杂报表 SQL
- DataTable 兼容场景
- 无固定实体模型的查询

示例：

```csharp
public class ReportDao : DaoBase
{
    public DataTable QueryReport(string sql, params SugarParameter[] parameters)
    {
        return GetDataTable(sql, parameters);
    }
}
```

### 1.3 历史兼容层：`Legacy/BaseRepository<T>`

适用于：

- 旧项目兼容
- 已存在的历史扩展仓储

不建议新项目继续新增依赖。

---

## 2. 当前主能力面

### 2.1 `Repository<T>` 已提供

- `Query()`
- `Any / AnyAsync`
- `Count / CountAsync`
- `FirstOrDefault / FirstOrDefaultAsync`
- `GetById`
- `FindList / FindListAsync`
- `GetPage / GetPageAsync`
- `GetList / GetListAsync`
- `GetSingle / GetSingleAsync`
- `GetScalar`
- `Execute / ExecuteAsync`
- `GetDataTable`
- `GetPageTable`
- `ExecuteInTransaction`

### 2.2 `DaoBase` 已提供

- 参数化 SQL 查询
- `DataTable` 查询
- 日期与方言辅助
- 实体转换辅助

### 2.3 `Schema` 已提供

- 模型注册
- 表初始化
- 表存在判断
- 差异 SQL 提取

---

## 3. 目录结构说明

```text
ZL.DB.Acc/
├── Aop/          SqlSugar AOP 配置
├── Connections/  连接入口、连接配置、连接串解析
├── Extensions/   SqlSugar 扩展方法
├── Internal/     内部工具类（分页/DataTable/Trace/辅助枚举等）
├── Legacy/       历史兼容仓储层
├── Schema/       表结构初始化、结构维护
├── DaoBase.cs    原生 SQL / 报表 DAO 基类
├── Repository.cs 泛型主仓储入口（推荐）
└── ZL.DB.Acc.csproj
```

### 3.1 `Connections/`

- `SugarAcc.cs`：统一数据库入口工厂
- `ConnKit.cs`：连接串与数据库类型解析
- `Config.cs`：连接层简化配置

### 3.2 `Aop/`

- `SqlAopConfigurator.cs`：SQL 日志、错误日志、审计字段填充

### 3.3 `Schema/`

- `DbSchemaInitializer.cs`：CodeFirst 初始化与差异分析
- `DbMaintenanceKit.cs`：结构维护辅助

### 3.4 `Extensions/`

- `SqlSugarEx.cs`：SqlSugar 范围扩展方法
- `SqlSugarFuncEx.cs`：函数扩展
- `SqlSugarConfig.cs`：扩展函数配置
- `ReportEx.cs`：报表扩展

### 3.5 `Legacy/`

- `BaseRepository.cs`
- `IBaseRepository.cs`

仅用于历史兼容，不建议作为新项目主入口。

### 3.6 `Internal/`

- `PageTable.cs`：DataTable 内存分页辅助
- `EntityKit.cs`：DataTable/DataRow 转实体
- `SqlKit.cs`：SQL 辅助
- `TraceKit.cs`：追踪辅助
- `CEnums.cs` / `CommonValidate.cs`：内部通用能力

---

## 4. 推荐开发约定

### 4.1 新业务默认继承 `Repository<T>`

除非明确属于报表/动态 SQL/无实体场景，否则不要优先使用 `DaoBase`。

### 4.2 优先参数化 SQL

正确示例：

```csharp
var list = GetList<MyDto>(
    "select * from iot_device where id=@id",
    new SugarParameter("@id", id));
```

不要继续拼接：

```csharp
// 不推荐
$"select * from iot_device where id='{id}'"
```

### 4.3 事务统一走模板方法

推荐：

```csharp
ExecuteInTransaction(db =>
{
    db.Insertable(entity).ExecuteCommand();
    db.Updateable(other).ExecuteCommand();
});
```

### 4.4 查询优先级建议

1. 简单实体查询：`Query()` / `FindList()`
2. 通用仓储查询：`Any/Count/FirstOrDefault/GetPage`
3. 特殊 SQL：`GetList/GetSingle/GetScalar/Execute`
4. 报表/无模型查询：`DaoBase`

---

## 5. 当前仍建议继续优化的方向

- 抽取 `Repository<T>` 与 `DaoBase` 共用方言辅助
- 抽取统一日志格式帮助器
- 增加 `Upsert` / 批量操作能力
- 增加更标准的分页结果对象
- 逐步清理解决方案内其它项目历史 warning

---

## 6. 一句话结论

如果你是新项目开发者：

- **默认继承 `Repository<T>`**
- **需要原生 SQL 报表时再用 `DaoBase`**
- **不要再新增依赖 `Legacy/BaseRepository<T>`**

这就是当前 `ZL.DB.Acc` 最推荐、最稳定、最容易快速上手的使用姿势。
