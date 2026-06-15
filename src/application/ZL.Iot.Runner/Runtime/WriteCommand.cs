// ============================================================
//  统一写入命令（P1-1 支柱 A：写入路径统一）
//
//  现场三条落库路径（历史采集、规则 SQL、FieldMapping）统一收敛为
//  对 RunnerWriteQueue 投递 WriteCommand，由单一消费者线程串行批量
//  落库。采集/触发线程只负责构造命令并入队，永不阻塞在 DB I/O 上。
//
//  设计要点：
//   - record 不可变：命令一旦构造即只读，跨线程传递安全。
//   - OnCommitted 回调在“落库成功后”由消费者线程触发，用于
//     FieldMapping 反馈写回 PLC——保证 PLC 收到“采集完成”信号时
//     数据已真正落库（防丢数据，用户决策）。
// ============================================================

using System;
using System.Collections.Generic;

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// 统一写入命令基类。三类派生：历史写入 / 表插入(FieldMapping) / 原始 SQL(规则)。
/// </summary>
public abstract record WriteCommand
{
    /// <summary>
    /// 落库成功后由消费者线程回调（可空）。用于 FieldMapping 在数据确认
    /// 落库后再反馈写回 PLC。回调内异常由消费者捕获并记录，不影响其他命令。
    /// </summary>
    public Action? OnCommitted { get; init; }

    /// <summary>来源设备编码，用于日志定位。</summary>
    public string DeviceCode { get; init; } = string.Empty;
}

/// <summary>
/// 历史采集写入：单条标签历史，由消费者按通用历史表批量落库。
/// </summary>
public sealed record HistoryWriteCommand : WriteCommand
{
    public required string TagId { get; init; }
    public required string TagType { get; init; }
    public object? Value { get; init; }
    public required string DataType { get; init; }
    public DateTimeOffset EventTime { get; init; }
}

/// <summary>
/// 表插入写入（FieldMapping）：已展开好的多行数据，连同列定义一并落库。
/// 表名/列名合法性由生产者侧校验保证。
/// </summary>
public sealed record TableInsertCommand : WriteCommand
{
    public required string TableName { get; init; }
    public required IReadOnlyList<FieldMappingRule> Columns { get; init; }
    public required IReadOnlyList<Dictionary<string, object?>> Rows { get; init; }
}

/// <summary>
/// 原始 SQL 写入（规则执行器渲染后的 SQL）。
/// 维持既有语义：脚本已在采集线程渲染完成，此处仅负责执行。
/// </summary>
public sealed record RawSqlCommand : WriteCommand
{
    public required string BizCode { get; init; }
    public required string ExeType { get; init; }
    public required string Sql { get; init; }
}
