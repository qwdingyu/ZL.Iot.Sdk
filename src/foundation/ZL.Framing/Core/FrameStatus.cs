using System;

namespace ZL.Framing
{
    /// <summary>
    /// 帧拆分模式（用于描述帧是如何被分割的）。
    /// </summary>
    public enum FrameSplitMode
    {
        /// <summary>由分隔符分割</summary>
        Delimiter,

        /// <summary>由长度字段指定</summary>
        Length,

        /// <summary>因超时而分割</summary>
        Timeout,

        /// <summary>作为分块返回</summary>
        Chunk
    }

    /// <summary>
    /// 帧状态信息。
    /// </summary>
    public sealed class FrameStatus
    {
        public FrameStatus(string resourceName, FrameSplitMode mode, int length, string delimiter)
        {
            ResourceName = resourceName ?? string.Empty;
            Mode = mode;
            Length = length;
            Delimiter = delimiter ?? string.Empty;
            TimestampUtc = DateTime.UtcNow;
        }

        /// <summary>资源名称（如设备/传输标识）</summary>
        public string ResourceName { get; }

        /// <summary>拆分模式</summary>
        public FrameSplitMode Mode { get; }

        /// <summary>帧长度（字节）</summary>
        public int Length { get; }

        /// <summary>分隔符（如果适用）</summary>
        public string Delimiter { get; }

        /// <summary>时间戳（UTC）</summary>
        public DateTime TimestampUtc { get; }
    }
}
