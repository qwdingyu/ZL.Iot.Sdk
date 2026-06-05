# ZL.Framing 分包指南

本文档说明分包解决方案的设计目标、使用场景与配置方法，避免后期遗忘。

## 1. 背景与目标

工业现场常见问题：

- 粘包：一次读取包含多帧
- 断包：一帧被拆成多次读取
- 噪声字节导致错位
- 传输时延/抖动导致边界不稳定

目标：

- 在不同协议场景下稳定分包
- 支持自愈（重同步）
- 受控内存占用
- 可配置策略，便于协议扩展

## 2. 架构概览

三层职责清晰分离：

- 缓冲区：`StickyWindowBuffer`，滑动窗口持有字节流
- 解码器：`IFrameDecoder`，根据协议规则切帧
- 组装器：`FrameAssembler`，协调解码、超时行为与回调

数据流：

1. 传输层收到字节
2. 传给 `FrameAssembler.Append`
3. 解码器从缓冲区解析帧
4. 上层只处理完整帧

## 3. 支持的分包策略

策略通过 `ByteFramingOptions.Strategy` 配置。

### 3.1 Timeout（空闲超时成帧）

适用于无长度、无分隔符协议，靠“静默时间”作为边界。

### 3.2 FixedLength（固定长度）

适用于固定帧长协议；可配同步头用于快速重同步。

### 3.3 LengthField（长度字段）

适用于帧内包含长度字段的协议；支持偏移、大小端、长度调整。

### 3.4 LengthFieldWithChecksum（长度 + 校验）

适用于长度字段 + 校验组合协议；支持校验位置在尾部或中间。

## 4. 重同步（Resync）

遇到噪声错位时：

- `DropOneByte`：丢 1 字节重试
- `ScanForSync`：扫描同步头（推荐）

建议工业协议优先使用同步头 + 扫描重同步。

## 5. 超时行为（Timeout Action）

`FrameTimeoutAction` 控制空闲超时后的处理：

- `Emit`：把当前缓冲视为一帧输出
- `Hold`：保留缓冲，等待更多数据
- `Clear`：清空缓冲

当 `FrameStrategy=Timeout` 且未指定 `FrameTimeoutAction` 时，默认使用 `Emit`。

## 6. 配置项参考

所有键均通过 `InstrumentConfig.Parameters` 配置。

### 6.1 通用参数

- `FrameStrategy`：Timeout / FixedLength / LengthField / LengthFieldWithChecksum
- `FrameTimeoutMs`：超时毫秒
- `FrameTimeoutAction`：Emit / Hold / Clear
- `FrameSyncBytes`：同步头（十六进制字符串，例如 AA55）
- `ResyncPolicy`：DropOneByte / ScanForSync
- `MaxResyncSkip`：重同步扫描上限
- `FrameBufferInitialCapacity`：缓冲初始容量
- `FrameBufferMaxCapacity`：缓冲最大容量

### 6.2 FixedLength

- `FrameLength`：固定帧长度

### 6.3 LengthField

- `LengthFieldOffset`：长度字段偏移（0 开始）
- `LengthFieldSize`：1 / 2 / 3 / 4
- `LengthFieldEndian`：Big / Little
- `LengthFieldIncludesHeader`：1/0
- `LengthFieldIncludesChecksum`：1/0
- `LengthFieldAdjustment`：长度修正（可正可负）
- `MinFrameLength` / `MaxFrameLength`：长度边界

### 6.4 Checksum

- `FrameChecksum`：SUM8 / XOR / CRC16 / CRC16_Modbus / CRC16_CCITT / CRC32
- `ChecksumOffset`：校验字段偏移（-1 表示尾部）
- `ChecksumRangeStart` / `ChecksumRangeLength`：校验覆盖范围（-1 表示自动）

## 7. 使用场景与示例

### 7.1 固定长度 + 同步头

```
FrameStrategy=FixedLength
FrameLength=16
FrameSyncBytes=AA55
ResyncPolicy=ScanForSync
```

### 7.2 长度字段（含头部）

```
FrameStrategy=LengthField
LengthFieldOffset=2
LengthFieldSize=2
LengthFieldEndian=Big
LengthFieldIncludesHeader=1
MinFrameLength=6
MaxFrameLength=1024
```

### 7.3 长度 + 尾部校验

```
FrameStrategy=LengthFieldWithChecksum
LengthFieldOffset=2
LengthFieldSize=1
LengthFieldIncludesHeader=1
LengthFieldIncludesChecksum=1
FrameChecksum=SUM8
```

### 7.4 校验位在中间

```
FrameStrategy=LengthFieldWithChecksum
LengthFieldOffset=0
LengthFieldSize=1
LengthFieldIncludesHeader=1
LengthFieldIncludesChecksum=1
FrameChecksum=SUM8
ChecksumOffset=2
```

## 8. 集成说明

- 串口/TCP 字节流已统一接入 `FrameAssembler`。
- 诊断信息可通过 `FrameStatus` 观察 `Length/Timeout/Chunk`。
- 有同步头的协议优先启用 `FrameSyncBytes + ScanForSync`。

## 9. 排查建议

帧收不到：

- 检查 `FrameStrategy`、长度字段偏移与大小端。
- 确认 `LengthFieldIncludesHeader/Checksum` 是否正确。
- 校验算法与校验位置是否匹配。

频繁恢复/丢帧：

- 使用同步头并启用 `ScanForSync`。
- 适当增大 `MaxResyncSkip`。

缓冲溢出：

- 调整 `FrameBufferMaxCapacity` 或降低 `MaxFrameLength`。
