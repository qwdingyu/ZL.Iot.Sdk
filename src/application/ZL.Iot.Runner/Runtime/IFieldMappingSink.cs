// ============================================================
//  FieldMapping 数据写入抽象
//  替代手写 DDL，支持多数据库 + 文件降级
// ============================================================

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// FieldMapping 数据写入目标
/// 抽象建表和写入操作，使 TriggerExecutor 不直接依赖特定数据库方言
/// </summary>
public interface IFieldMappingSink
{
    /// <summary>
    /// 确保目标表存在（如不存在则自动创建）
    /// </summary>
    void EnsureTable(string tableName, List<FieldMappingRule> columns);

    /// <summary>
    /// 插入数据行
    /// </summary>
    void InsertRows(string tableName, List<Dictionary<string, object?>> rows);
}
