using System;

namespace ZL.Iot.Interface
{
    /// <summary>
    /// 发送WebSocket消息的类别
    /// </summary>
    public enum WsMsgType
    {
        HeartBeat = 1,
        MoniterData = 2,
        Error = 999
    }
    public enum DataSource
    {
        Cache = 1,
        Device = 2
    }

    public enum DataType : byte
    {
        NONE = 0,//未知
        BOOL = 1,//布尔
        BYTES = 2,//数组
        BYTE = 3,//Byte
        SHORT = 4,//Short
        WORD = 5,//字
        DWORD = 6,//双字
        INT = 7,//整型
        FLOAT = 8,//浮点型
        DOUBLE = 9,//浮点型
        STR = 11,//字符串
        SYS = 99//系统
    }

    public enum DeviceType
    {
        S7200 = 1,
        S7200Smart = 2,
        S7300 = 3,
        S7400 = 4,
        S71200 = 5,
        S71500 = 6
    }

    [Flags]
    public enum ByteOrder : byte
    {
        None = 0,
        BigEndian = 1,
        LittleEndian = 2,
        Network = 4,
        Host = 8
    }

    [Flags]
    public enum SubAlarmType
    {
        None = 0,
        LoLo = 1,
        Low = 2,
        High = 4,
        HiHi = 8,
        MajDev = 16,
        MinDev = 32,
        Dsc = 64,

        BadPV = 128,
        MajROC = 256,
        MinROC = 512
    }

    public enum Severity
    {
        Error = 7,
        High = 6,
        MediumHigh = 5,
        Medium = 4,
        MediumLow = 3,
        Low = 2,
        Information = 1,
        Normal = 0
    }

    [Flags]
    public enum ConditionState : byte
    {
        Acked = 4,
        Actived = 2,
        Enabled = 1
    }

    public enum QUALITIES : short
    {
        // Fields
        LIMIT_CONST = 3,
        LIMIT_HIGH = 2,
        LIMIT_LOW = 1,
        //LIMIT_MASK = 3,
        //LIMIT_OK = 0,
        QUALITY_BAD = 0,
        QUALITY_COMM_FAILURE = 0x18,
        QUALITY_CONFIG_ERROR = 4,
        QUALITY_DEVICE_FAILURE = 12,
        QUALITY_EGU_EXCEEDED = 0x54,
        QUALITY_GOOD = 0xc0,
        QUALITY_LAST_KNOWN = 20,
        QUALITY_LAST_USABLE = 0x44,
        QUALITY_LOCAL_OVERRIDE = 0xd8,
        QUALITY_MASK = 0xc0,
        QUALITY_NOT_CONNECTED = 8,
        QUALITY_OUT_OF_SERVICE = 0x1c,
        QUALITY_SENSOR_CAL = 80,
        QUALITY_SENSOR_FAILURE = 0x10,
        QUALITY_SUB_NORMAL = 0x58,
        QUALITY_UNCERTAIN = 0x40,
        QUALITY_WAITING_FOR_INITIAL_DATA = 0x20,
        STATUS_MASK = 0xfc,
    }

    /// <summary>
    /// 设备用途/运行模式
    /// </summary>
    public enum DevicePurpose
    {
        /// <summary>
        /// 默认/未定义
        /// </summary>
        None = 0,

        /// <summary>
        /// C# 代码交互模式 (反射加载具体处理类)
        /// </summary>
        CodeInteraction = 1,

        /// <summary>
        /// 自动交互模式 (Legacy BizAutoInteract)
        /// </summary>
        AutoInteract = 2,

        /// <summary>
        /// 配置采集模式
        /// </summary>
        CollectConfig = 3,

        /// <summary>
        /// 配置控制模式
        /// </summary>
        ControlConfig = 4,

        /// <summary>
        /// 报警处理模式
        /// </summary>
        AlarmHandle = 9,

        /// <summary>
        /// 边缘自治执行器核心模式 (Edge-Executor Kernel)
        /// </summary>
        EdgeExecutor = 10
    }
}
