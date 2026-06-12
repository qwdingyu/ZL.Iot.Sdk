// ============================================================
//  标签值提供者接口
//  Runner 运行时提供读取和写入 PLC 标签的能力
//  SingleDeviceRunner 通过 HslUnifiedDriver 实现此接口
// ============================================================

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// 标签值提供者 — TriggerExecutor 通过此接口读取标签值和写回反馈信号
/// </summary>
public interface ITagValueProvider
{
    /// <summary>
    /// 读取标签当前值（从驱动缓存或实时读取）
    /// </summary>
    object? ReadTag(string tagId);

    /// <summary>
    /// 写入标签值到 PLC（带重试和回读校验）
    /// </summary>
    bool WriteTag(string tagId, object value);
}
