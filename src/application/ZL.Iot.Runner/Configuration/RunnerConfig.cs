// ============================================================
//  Runner 根配置 - JSON/XML 双格式支持
//  支持多设备实例，每个设备独立驱动、标签、执行器
// ============================================================

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZL.Iot.Runner.Configuration
{
    /// <summary>
    /// Runner 根配置，对应 runner.config.json / runner.config.xml
    /// 
    /// 使用方法：
    /// 1. 创建或编辑 runner.config.json 文件
    /// 2. 运行 ZL.Iot.Runner.Cli 加载配置：dotnet run runner.config.json
    /// 3. 或使用 ConfigLoader.Load("path") 程序化加载
    /// </summary>
    public class RunnerConfig
    {
        /// <summary>Runner 全局选项（名称/日志/数据存储）</summary>
        public RunnerOptions Runner { get; set; } = new();

        /// <summary>设备列表，支持多设备实例独立运行</summary>
        [JsonPropertyName("devices")]
        [System.Xml.Serialization.XmlArray("Devices")]
        [System.Xml.Serialization.XmlArrayItem("Device")]
        public List<DeviceProfile> Devices { get; set; } = new();
    }

    /// <summary>
    /// Runner 全局运行选项
    /// </summary>
    public class RunnerOptions
    {
        /// <summary>Runner 实例名称，用于日志/状态显示</summary>
        public string Name { get; set; } = "ZL.Iot.Runner";

        /// <summary>日志级别：Trace/Debug/Information/Warning/Error</summary>
        public string LogLevel { get; set; } = "Information";

        /// <summary>数据存储配置（Phase 1: 仅 Sqlite，Phase 2: MySql）</summary>
        public DataStorageOptions DataStorage { get; set; } = new();
    }

    /// <summary>
    /// 数据存储配置
    /// Phase 1 支持 SQLite 本地历史存储；远端同步配置先随包下发，运行时后续接线。
    /// </summary>
    public class DataStorageOptions
    {
        /// <summary>存储类型：Sqlite / MySql / None</summary>
        public string Type { get; set; } = "Sqlite";

        /// <summary>
        /// 连接字符串
        /// Sqlite 示例：Data Source=./data/iot_runner.db
        /// MySql 示例：Server=192.168.1.10;Port=3306;Database=iot_edge;Uid=root;Pwd=123456;
        /// </summary>
        public string ConnectionString { get; set; } = "Data Source=./data/iot_runner.db";

        /// <summary>采集历史存储配置，由配置决定是否落库，不由 TagType 决定。</summary>
        public StorageOptions History { get; set; } = new();

        /// <summary>远端异步同步配置；当前 Runner 只保留模型，不阻塞本地采集。</summary>
        public RemoteSyncOptions RemoteSync { get; set; } = new();
    }

    /// <summary>
    /// 采集历史存储配置。
    /// </summary>
    public class StorageOptions
    {
        /// <summary>是否启用采集历史存储。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>目标历史表名。</summary>
        public string TableName { get; set; } = "iot_tag_history";

        /// <summary>批量写入大小。</summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>批量写入最大等待毫秒数。</summary>
        public int FlushIntervalMs { get; set; } = 1000;

        /// <summary>有界队列容量，防止现场异常数据风暴拖垮采集进程。</summary>
        public int QueueCapacity { get; set; } = 10000;

        /// <summary>需要落库的标签映射；为空表示存储所有启用标签。</summary>
        public List<StorageMapping> Mappings { get; set; } = new();
    }

    /// <summary>
    /// 单标签历史存储映射。
    /// </summary>
    public class StorageMapping
    {
        /// <summary>设备编码；为空表示匹配任意设备。</summary>
        public string DeviceCode { get; set; } = "";

        /// <summary>标签 Id，对应 TagProfile.Id。</summary>
        public string TagId { get; set; } = "";

        /// <summary>
        /// 业务语义标签类型，仅随历史记录保存，不参与是否落库的判断。
        /// 现场验证阶段使用 M，表示监控/触发标签会进入采集与存储闭环。
        /// </summary>
        public string TagType { get; set; } = "";

        /// <summary>目标列名；通用历史表模式下默认写入 tag_id/value 等标准列。</summary>
        public string ColumnName { get; set; } = "";
    }

    /// <summary>
    /// 远端异步同步配置。
    /// </summary>
    public class RemoteSyncOptions
    {
        /// <summary>是否启用远端同步。</summary>
        public bool Enabled { get; set; }

        /// <summary>远端数据库类型。</summary>
        public string Type { get; set; } = "";

        /// <summary>远端数据库连接串。</summary>
        public string ConnectionString { get; set; } = "";
    }

    /// <summary>
    /// 单设备配置 Profile，对应配置文件中的单个设备节点
    /// 包含协议参数、标签列表、执行器列表
    /// 
    /// 配置示例：
    /// {
    ///   "code": "plc_1",
    ///   "protocol": "SiemensS7",
    ///   "ip": "192.168.1.100",
    ///   "port": 102,
    ///   "tags": [...]
    ///   "executors": [...]
    /// }
    /// </summary>
    public class DeviceProfile
    {
        /// <summary>设备编码，唯一标识</summary>
        public string Code { get; set; } = "";

        /// <summary>
        /// 协议类型，对应 HslProtocolRegistry 中注册的类型
        /// 支持：SiemensS7 / SiemensS7200Smart / ModbusTcp / MitsubishiMC / OmronFinsTcp / BacnetIp /等
        /// </summary>
        public string Protocol { get; set; } = "SiemensS7";

        /// <summary>PLC IP 地址</summary>
        public string Ip { get; set; } = "192.168.1.100";

        /// <summary>端口号，默认 102（西门子）</summary>
        public int Port { get; set; } = 102;

        /// <summary>机架号（西门子 S7 专用）</summary>
        public int Rack { get; set; } = 0;

        /// <summary>槽位号（西门子 S7 专用）</summary>
        public int Slot { get; set; } = 1;

        /// <summary>连接超时时间（毫秒）</summary>
        public int ConnectTimeout { get; set; } = 5000;

        /// <summary>读取间隔（毫秒），默认 200ms</summary>
        public int ReadInterval { get; set; } = 200;

        /// <summary>标签列表</summary>
        [System.Xml.Serialization.XmlArray("Tags")]
        [System.Xml.Serialization.XmlArrayItem("TagProfile")]
        public List<TagProfile> Tags { get; set; } = new();

        /// <summary>执行器列表</summary>
        [System.Xml.Serialization.XmlArray("Executors")]
        [System.Xml.Serialization.XmlArrayItem("ExecutorProfile")]
        public List<ExecutorProfile> Executors { get; set; } = new();
    }

    /// <summary>
    /// 标签配置，对应 PLC 中的一个数据点
    /// 
    /// TagType 说明：
    /// - ""（空）：纯数据采集，不触发执行器
    /// - "M"：监控触发，当值变化时触发关联执行器
    /// - "D"：数据采集，仅记录数据到存储
    /// - "HB"：心跳标签，DeviceRunner 监控心跳判断连接状态
    /// - "RO"：只读标签，仅读取不写入
    /// </summary>
    public class TagProfile
    {
        /// <summary>业务标签名，唯一标识（如 Temperature_01 / Alarm_OverTemp）</summary>
        public string Id { get; set; } = "";

        /// <summary>描述信息，用于日志和 UI 显示</summary>
        public string Description { get; set; } = "";

        /// <summary>PLC 地址（如 DB1.DBD0 / M0.0 / IW100）</summary>
        public string Address { get; set; } = "";

        /// <summary>
        /// 数据类型，对应 TagItem.DataTypeCode
        /// 支持：bool / short / ushort / int / uint / float / double / string / bytes
        /// </summary>
        public string DataType { get; set; } = "bool";

        /// <summary>数据长度（string/bytes 类型使用）</summary>
        public int Length { get; set; } = 1;

        /// <summary>字符串编码（ASCII/UTF-8/GB2312），string 类型使用</summary>
        public string StringEncoding { get; set; } = "ASCII";

        /// <summary>是否启用该标签</summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// 标签类型：空字符串（纯采集）/ M（触发）/ D（采集）/ HB（心跳）/ RO（只读）
        /// </summary>
        public string TagType { get; set; } = "";

        /// <summary>死区值，浮点型标签使用，当变化超过死区才触发</summary>
        public double Deadband { get; set; } = 0;

        /// <summary>扫描频率（毫秒），0 表示使用设备默认 ReadInterval</summary>
        public int ScanRate { get; set; } = 0;
    }

    /// <summary>
    /// 执行器配置，对应标签触发后的业务逻辑
    /// 
    /// JudgeType 说明（0-8）：
    /// - 0：值任意（无条件触发）
    /// - 1：值==1（bool 型 true）
    /// - 2：值==0（bool 型 false）
    /// - 3：值变化（任意变化）
    /// - 4：值>Threshold
    /// - 5：值<Threshold
    /// - 6：值>=Threshold
    /// - 7：值<=Threshold
    /// - 8：值!=Threshold
    /// 
    /// ExeType 说明：
    /// - "S"：Select 模式，执行 SQL 并回填变量
    /// - "Q"：Query 模式，仅执行查询
    /// - "M"：Modify 模式，执行 INSERT/UPDATE/DELETE
    /// </summary>
    public class ExecutorProfile
    {
        /// <summary>业务编码，唯一标识</summary>
        public string BizCode { get; set; } = "";

        /// <summary>触发标签 Id，引用 Tags 中的 Id</summary>
        public string TagId { get; set; } = "";

        /// <summary>触发条件类型（0-8），见上表</summary>
        [JsonPropertyName("judgeType")]
        public int JudgeType { get; set; } = 0;

        /// <summary>触发条件表达式，配合 JudgeType 使用（如 Threshold 值）</summary>
        public string JudgeExp { get; set; } = "";

        /// <summary>执行类型：S/Select, Q/Query, M/Modify</summary>
        public string ExeType { get; set; } = "M";

        /// <summary>
        /// 执行脚本，支持 Scriban 变量替换语法：{{VariableName}}
        /// 也支持遗留格式：?VariableName? / #VariableName# / @VariableName@
        /// 示例：INSERT INTO alerts (tag, value, time) VALUES ('{{TagId}}', {{Value}}, datetime('now'))
        /// </summary>
        public string Script { get; set; } = "";

        /// <summary>执行顺序，数字越小越先执行</summary>
        public int ExeOrder { get; set; } = 0;

        /// <summary>是否启用该执行器</summary>
        public bool Enable { get; set; } = true;
    }
}