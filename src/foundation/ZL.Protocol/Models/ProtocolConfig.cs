using System.Collections.Generic;

namespace ZL.Protocol.Models
{
    /// <summary>
    /// 协议配置模型
    /// </summary>
    public sealed class ProtocolConfig
    {
        /// <summary>协议名称</summary>
        public string ProtocolName { get; set; } = string.Empty;

        /// <summary>帧模式：Text / Hex / Binary</summary>
        public string FrameMode { get; set; } = "Text";

        /// <summary>校验和算法类型（CRC16 / Modbus / Sum8 / Xor8）</summary>
        public string? CheckSum { get; set; }

        /// <summary>是否自动追加校验和</summary>
        public bool AutoAppendCheckSum { get; set; }

        /// <summary>帧终止符</summary>
        public string Terminator { get; set; } = "\n";

        /// <summary>默认超时时间（毫秒）</summary>
        public int DefaultTimeoutMs { get; set; } = 2000;

        /// <summary>命令间等待时间（毫秒）</summary>
        public int InterCommandWaitMs { get; set; } = 50;

        /// <summary>异步事件分发模式（Broadcast / Single）</summary>
        public string AsyncEventDispatchMode { get; set; } = "Broadcast";

        // --- 行为控制与混沌模拟（护城河功能） ---
        /// <summary>响应延迟抖动（毫秒）</summary>
        public int JitterMs { get; set; } = 0;

        /// <summary>模拟固定延时（毫秒）</summary>
        public int SimDelayMs { get; set; } = 0;

        /// <summary>模拟丢包率 (0.0 - 1.0)</summary>
        public double SimPacketLossRate { get; set; } = 0.0;

        /// <summary>数值抖动比例</summary>
        public double ValueJitter { get; set; } = 0.0;

        /// <summary>随机噪声幅度</summary>
        public double ValueNoise { get; set; } = 0.0;

        /// <summary>是否为受保护的资产（隐藏响应模板）</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>命令定义字典（键为命令名）</summary>
        public Dictionary<string, CommandDefinition> Commands { get; set; } = new Dictionary<string, CommandDefinition>();

        /// <summary>默认读取策略</summary>
        public ReadStrategyDefinition? ReadStrategy { get; set; }

        /// <summary>未知命令的响应</summary>
        public string? UnknownResponse { get; set; }

        /// <summary>事件定义列表</summary>
        public List<EventDefinition>? Events { get; set; }

        /// <summary>仿真标签配置（用于主动生成仿真数据，如温度斜坡、正弦波等）</summary>
        public Dictionary<string, SimulationTagConfig>? SimulationTags { get; set; }
    }

    /// <summary>
    /// 仿真标签配置
    /// </summary>
    public sealed class SimulationTagConfig
    {
        /// <summary>仿真模式：Fixed, Ramp, Sine, Random, Step</summary>
        public string Mode { get; set; } = "Fixed";

        /// <summary>初始值 / 固定值</summary>
        public double InitialValue { get; set; }

        /// <summary>固定值（Fixed 模式）</summary>
        public double? FixedValue { get; set; }

        /// <summary>最小值（Ramp/Random/Sine 模式）</summary>
        public double Min { get; set; }

        /// <summary>最大值（Ramp/Random/Sine 模式）</summary>
        public double Max { get; set; }

        /// <summary>斜坡速率（Ramp 模式，单位/秒）</summary>
        public double RampRate { get; set; }

        /// <summary>正弦波频率（Sine 模式，Hz）</summary>
        public double Frequency { get; set; } = 0.1;

        /// <summary>正弦波幅度（Sine 模式）</summary>
        public double Amplitude { get; set; } = 10.0;

        /// <summary>正弦波偏移（Sine 模式，中心值）</summary>
        public double Offset { get; set; } = 50.0;

        /// <summary>步长时间（Step 模式，毫秒）</summary>
        public int StepDurationMs { get; set; } = 5000;

        /// <summary>步长值列表（Step 模式）</summary>
        public List<double>? StepValues { get; set; }

        /// <summary>数值格式（如 "0.##", "F2", "N0"）</summary>
        public string Format { get; set; } = "0.##";

        /// <summary>单位（如 "°C", "V", "mA"）</summary>
        public string? Unit { get; set; }

        /// <summary>更新间隔（毫秒），0 表示使用引擎默认间隔</summary>
        public int UpdateIntervalMs { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;
    }
}
