using System;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 审计字段标记接口
    /// 
    /// 实现此接口的模型，其时间戳字段将在插入/更新时由 SqlSugar DataExecuting AOP 自动赋值，
    /// 无需业务代码手动设置。符合 DRY 原则，避免每个 Service 都要写 updated_at = DateTime.Now。
    /// 
    /// 使用方式：
    /// public class iot_tag_snapshot : BaseClass, IAuditable
    /// {
    ///     public DateTime updated_at { get; set; }  // 由 AOP 自动赋值
    /// }
    /// 
    /// AOP 规则（在 SugarAcc.GetScope / Repository 构造时配置）：
    /// - Insert: created_at / CreatedTime → DateTime.Now
    /// - Insert: updated_at / UpdatedTime → DateTime.Now  
    /// - Update: updated_at / UpdatedTime → DateTime.Now
    /// 
    /// 支持字段名（大小写不敏感）：
    /// - updated_at / UpdatedTime / UpdatedTimeUtc
    /// - created_at / CreatedTime / CreatedTimeUtc
    /// </summary>
    public interface IAuditable
    {
        /// <summary>
        /// 记录更新时间（AOP 自动赋值，Insert 和 Update 均触发）
        /// </summary>
        // DateTime UpdatedTime { get; set; }

        /// <summary>
        /// 记录创建时间（Insert 时 AOP 自动赋值）
        /// </summary>
        // DateTime CreatedTime { get; set; }
    }

    /// <summary>
    /// 软删除标记接口
    /// 实现此接口的模型支持逻辑删除（IsDeleted 字段），物理删除将自动转换为逻辑删除
    /// </summary>
    public interface ISoftDelete
    {
        /// <summary>
        /// 是否已删除（0=未删除，1=已删除）
        /// </summary>
        // int IsDeleted { get; set; }
    }

    /// <summary>
    /// 多租户标记接口
    /// 实现此接口的模型自动注入 TenantId 字段（由 AOP 从当前上下文获取）
    /// </summary>
    public interface ITenant
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        // long TenantId { get; set; }
    }
}