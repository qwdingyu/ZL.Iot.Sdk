# 使用示例 — ZL.Foundation 组件

本文档提供端到端的使用示例，展示如何将 ZL.Protocol、ZL.Probing、ZL.Framing 和 ZL.Shared 组合使用。

---

## 1. 完整示例：协议录制与仿真

### 1.1 初始化日志

```csharp
using ZL.Shared;
using ZL.Protocol;
using ZL.Probing;
using ZL.Framing;

// 初始化结构化日志
StructuredLog.Initialize(new LogBootstrapOptions
{
    BaseDirectory = AppContext.BaseDirectory,
    EnableConsole = true
});
```

### 1.2 加载协议配置

```csharp
// 从 JSON 文件加载协议配置
var config = new ProtocolConfig
{
    ProtocolName = "MyScpiDevice",
    FrameMode = "Text",
    Terminator = "\n",
    DefaultTimeoutMs = 2000,
    Commands = new Dictionary<string, CommandDefinition>
    {
        ["VOL"] = new CommandDefinition
        {
            CommandTemplate = "VOLT {v}",
            ResponseTemplate = "{STATE:voltage}",
            Parser = new ResponseParserDefinition
            {
                Type = "Regex",
                Pattern = @"VOLT\s+([\d.]+)",
                Index = 1,
                TargetType = "Double"
            }
        },
        ["IDN"] = new CommandDefinition
        {
            CommandTemplate = "*IDN?",
            ResponseTemplate = "{manufacturer},{model},{serial},{firmware}"
        }
    }
};

// 保存为 JSON 文件
var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions 
{ 
    WriteIndented = true 
});
File.WriteAllText("my_device.json", json);
```

### 1.3 从 JSON 文件加载协议

```csharp
// 方式 1: 从 JSON 字符串
var config = ProtocolConfigLoader.ParseJson(json, applyDefaults: true);

// 方式 2: 从文件
var config = ProtocolConfigLoader.LoadFromFile("my_device.json");

// 验证
if (config == null)
{
    Console.WriteLine("协议配置解析失败");
    return;
}

Console.WriteLine($"协议名称: {config.ProtocolName}");
Console.WriteLine($"命令数量: {config.Commands.Count}");
```

### 1.4 使用协议录制器学习协议

```csharp
// 准备录制的日志行（从 TCP 监听或串口捕获）
var logLines = new List<string>
{
    // 格式: {"Timestamp":"...","Direction":"TX|RX","SessionId":"...","Payload":"..."}
    @"{""Timestamp"":""2024-01-01T10:00:00Z"",""Direction"":""TX"",""Payload"":""VOLT 5""}",
    @"{""Timestamp"":""2024-01-01T10:00:00.010Z"",""Direction"":""RX"",""Payload"":""5.000""}",
    @"{""Timestamp"":""2024-01-01T10:00:01Z"",""Direction"":""TX"",""Payload"":""VOLT 10""}",
    @"{""Timestamp"":""2024-01-01T10:00:01.010Z"",""Direction"":""RX"",""Payload"":""10.000""}",
    @"{""Timestamp"":""2024-01-01T10:00:02Z"",""Direction"":""TX"",""Payload"":""*IDN?""}",
    @"{""Timestamp"":""2024-01-01T10:00:02.050Z"",""Direction"":""RX"",""Payload"":""MyCorp,Model1,SN123,1.0""}",
};

var options = new ProtocolRecorder.RecordingOptions
{
    ProtocolName = "LearnedProtocol",
    FrameMode = "Text",
    Terminator = "\n",
    SimilarityThreshold = 0.8,
    AutoMergeTemplates = true,
    InferParameters = true,
    InferChecksums = true
};

var learnedProtocol = ProtocolRecorder.Analyze(logLines, options);

Console.WriteLine($"学到的命令数: {learnedProtocol.Commands.Count}");
foreach (var kvp in learnedProtocol.Commands)
{
    Console.WriteLine($"  {kvp.Key}: {kvp.Value.CommandTemplate}");
}
```

---

## 2. 透明代理传输层使用

### 2.1 TCP Server 模式 — 监听客户端连接

```csharp
// 创建目标传输（连接到实际设备的传输）
var targetTransport = new TcpByteTransport(5025, 30, new ByteFramingOptions());

// 配置透明代理
var proxyConfig = new TransparentProxyConfig
{
    ListenMode = ListenMode.TcpServer,
    SourcePort = 5026,                // 监听本地端口 5026
    TargetHost = "localhost",         // 转发到 localhost:5025
    TargetPort = 5025,
    SnifferOnly = false,              // 启用转发（true = 只监听不转发）
    LogFile = "capture.jsonl",        // 主日志文件
    SessionLogDir = "sessions/"       // 会话日志目录
};

// 创建透明代理
var proxy = new TransparentProxyTransport(proxyConfig, targetTransport);

// 注册事件
proxy.DataReceived += (data) =>
{
    Console.WriteLine($"收到 {data?.Length ?? 0} 字节");
};

proxy.SessionStarted += (sessionId) =>
{
    Console.WriteLine($"会话开始: {sessionId}");
};

proxy.SessionEnded += (sessionId) =>
{
    Console.WriteLine($"会话结束: {sessionId}");
};

// 打开传输
proxy.Open();

// 客户端现在可以连接到 localhost:5026，流量将被透明转发到 localhost:5025
// 并自动录制到 capture.jsonl

// 使用完毕后关闭
proxy.Close();
proxy.Dispose();
```

### 2.2 TCP Client 模式 — 主动连接目标

```csharp
var targetTransport = new TcpByteTransport(5025, 30, new ByteFramingOptions());

var proxyConfig = new TransparentProxyConfig
{
    ListenMode = ListenMode.TcpClient,
    SourcePort = 5026,                // 本地监听端口
    TargetHost = "192.168.1.100",    // 目标设备 IP
    TargetPort = 5025
};

var proxy = new TransparentProxyTransport(proxyConfig, targetTransport);
proxy.Open();
// ...
proxy.Close();
```

### 2.3 串口监听模式

```csharp
var targetTransport = new SerialByteTransport("/dev/ttyUSB0", 9600, new ByteFramingOptions());

var proxyConfig = new TransparentProxyConfig
{
    ListenMode = ListenMode.Serial,
    SourcePortName = "/dev/ttyS1",    // 监听串口
    TargetPortName = "/dev/ttyUSB0",  // 目标串口
    SnifferOnly = false
};

var proxy = new TransparentProxyTransport(proxyConfig, targetTransport);
proxy.Open();
// ...
proxy.Close();
```

### 2.4 只侦听模式（SnifferOnly）

```csharp
// SnifferOnly = true 时，代理只录制流量但不转发
var proxyConfig = new TransparentProxyConfig
{
    ListenMode = ListenMode.TcpServer,
    SourcePort = 5026,
    TargetHost = "localhost",
    TargetPort = 5025,
    SnifferOnly = true  // 只录制，不转发
};
```

---

## 3. 帧解析器使用

### 3.1 超时模式帧解析

```csharp
// 配置超时模式：帧之间用空闲时间分割
var options = new ByteFramingOptions
{
    Strategy = "Timeout",
    TimeoutMode = "Hold",
    MaxFrameLength = 4096,
    BufferInitialCapacity = 4096,
    BufferMaxCapacity = 1024 * 1024
};

var assembler = new FrameAssembler(options, timeoutMs: 30, (frame, mode) =>
{
    Console.WriteLine($"收到帧 ({mode}): {BitConverter.ToString(frame)}");
    // 处理接收到的完整帧
});

// 模拟接收字节流
assembler.Append(Encoding.UTF8.GetBytes("VOLT?"));
// 等待 30ms 超时...
// 触发帧回调
```

### 3.2 固定长度模式

```csharp
var options = new ByteFramingOptions
{
    Strategy = "FixedLength",
    FixedLength = 16  // 每帧 16 字节
};

var assembler = new FrameAssembler(options, timeoutMs: 0, (frame, mode) =>
{
    Console.WriteLine($"收到固定长度帧: {frame.Length} 字节");
});
```

### 3.3 LengthField 模式

```csharp
var options = new ByteFramingOptions
{
    Strategy = "LengthField",
    LengthFieldOffset = 0,       // 长度字段在帧首
    LengthFieldSize = 2,          // 长度字段占 2 字节
    LengthFieldEndian = "Big",    // 大端序
    LengthFieldIncludesHeader = false,
    MinFrameLength = 4,
    MaxFrameLength = 4096
};

var assembler = new FrameAssembler(options, timeoutMs: 0, (frame, mode) =>
{
    Console.WriteLine($"收到 LengthField 帧: {frame.Length} 字节");
});
```

### 3.4 带校验和的 LengthField 模式

```csharp
var options = new ByteFramingOptions
{
    Strategy = "LengthFieldWithChecksum",
    LengthFieldOffset = 0,
    LengthFieldSize = 2,
    Checksum = "CRC16",           // 校验和算法
    ChecksumOffset = -1,          // -1 表示校验和在校验范围末尾
    ChecksumRangeStart = 2,       // 从第 2 字节开始计算
    ChecksumRangeLength = -1      // -1 表示到校验和前一位
};
```

---

## 4. 字节缓冲区使用

### 4.1 StickyWindowBuffer

```csharp
var buffer = new StickyWindowBuffer(initialCapacity: 4096, maxCapacity: 1024 * 1024);

// 写入数据
buffer.Write(new byte[] { 0x01, 0x02, 0x03, 0x04 });

// 读取数据
ushort value = buffer.GetUShort(0, littleEndian: false);
byte[] bytes = buffer.ReadBytes(4);

// 丢弃已读取的字节，收缩缓冲区
buffer.DiscardReadBytes();
```

---

## 5. 帧解码器使用

### 5.1 FixedLengthFrameDecoder

```csharp
var decoder = new FixedLengthFrameDecoder(
    length: 16,
    syncBytes: Array.Empty<byte>(),  // 无需同步字节
    resync: ResyncMode.ScanForSync,
    maxSkip: 2048
);

var buffer = new StickyWindowBuffer();
buffer.Write(data);  // 写入待解析数据

switch (decoder.TryDecode(buffer, out byte[] frame))
{
    case DecodeResult.FrameAvailable:
        Console.WriteLine($"解析到帧: {BitConverter.ToString(frame)}");
        break;
    case DecodeResult.NeedMoreData:
        Console.WriteLine("需要更多数据");
        break;
    case DecodeResult.Recovery:
        Console.WriteLine("恢复状态，跳过坏数据");
        break;
}
```

### 5.2 LengthFieldFrameDecoder

```csharp
var options = new ByteFramingOptions
{
    Strategy = "LengthField",
    LengthFieldOffset = 0,
    LengthFieldSize = 2,
    LengthFieldEndian = "Big"
};

var decoder = new LengthFieldFrameDecoder(options);
var buffer = new StickyWindowBuffer();
buffer.Write(data);

if (decoder.TryDecode(buffer, out byte[] frame) == DecodeResult.FrameAvailable)
{
    // 处理帧
}
```

---

## 6. 校验和工具

### 6.1 ChecksumUtil

```csharp
using ZL.Framing.Utils;

byte[] data = { 0x01, 0x02, 0x03, 0x04 };

// 计算校验和
byte[] checksum = ChecksumUtil.Calculate(data, "CRC16");

// 附加校验和到数据
byte[] dataWithChecksum = ChecksumUtil.Append(data, "CRC16");

// 获取校验和方法的字节长度
int length = ChecksumUtil.GetLength("CRC16");  // 2
```

### 6.2 FramingHex

```csharp
// 解析十六进制字符串
byte[] bytes = FramingHex.ParseHexBytes("01 02 03 04");
// 结果: [0x01, 0x02, 0x03, 0x04]
```

---

## 7. 字符串距离工具

### 7.1 StringDistance

```csharp
using ZL.Shared.Utils;

// Levenshtein 编辑距离
int distance = StringDistance.Levenshtein("kitten", "sitting");
// distance = 3

// 相似度（0.0 ~ 1.0）
double similarity = StringDistance.Similarity("VOLT", "VOLTS");
// similarity ≈ 0.8
```

---

## 8. 端到端集成示例：协议学习 + 仿真

```csharp
using ZL.Shared;
using ZL.Protocol;
using ZL.Probing;
using ZL.Framing;

// 1. 初始化日志
StructuredLog.Initialize(new LogBootstrapOptions { EnableConsole = true });

// 2. 启动透明代理录制通信日志
var proxyConfig = new TransparentProxyConfig
{
    ListenMode = ListenMode.TcpServer,
    SourcePort = 5026,
    TargetHost = "localhost",
    TargetPort = 5025,
    LogFile = "capture.jsonl",
    SessionLogDir = "sessions/"
};

var targetTransport = new TcpByteTransport(5025, 30, new ByteFramingOptions());
var proxy = new TransparentProxyTransport(proxyConfig, targetTransport);
proxy.Open();

// 3. 客户端与设备通信后，从日志学习协议
proxy.Close();

var logLines = File.ReadAllLines("capture.jsonl");
var options = new ProtocolRecorder.RecordingOptions
{
    ProtocolName = "AutoLearned",
    SimilarityThreshold = 0.8,
    AutoMergeTemplates = true,
    InferParameters = true
};

var protocol = ProtocolRecorder.Analyze(logLines, options);

// 4. 保存协议配置
var json = System.Text.Json.JsonSerializer.Serialize(protocol, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
File.WriteAllText("auto_learned.json", json);

Console.WriteLine($"学习到 {protocol.Commands.Count} 个命令");
```

---

## 9. 总结

| 组件 | 核心用途 | 关键类 |
|---|---|---|
| 协议配置 | 定义设备命令/响应模型 | `ProtocolConfig`, `CommandDefinition` |
| 协议录制 | 从通信日志自动学习协议 | `ProtocolRecorder` |
| 透明代理 | 录制/转发网络流量 | `TransparentProxyTransport` |
| 帧解析 | 字节流分割为帧 | `FrameAssembler`, `ByteFramingOptions` |
| 帧解码 | 二进制帧解析 | `LengthFieldFrameDecoder`, `FixedLengthFrameDecoder` |
| 日志 | 结构化日志 | `StructuredLog` |
| 工具 | 字符串距离 | `StringDistance` |
