# ZL.Script 脚本引擎使用指南

> **版本**: 1.0  
> **更新日期**: 2026-02-14  
> **目标**: 为工控协议仿真提供强大的动态表达式计算能力

---

## 📋 目录

1. [核心概念](#1-核心概念)
2. [基础用法](#2-基础用法)
3. [工业级扩展库](#3-工业级扩展库)
4. [高级特性](#4-高级特性)
5. [实战案例](#5-实战案例)

---

## 1. 核心概念

### 🎯 什么是 ZL.Script？

ZL.Script 是一个基于 **DynamicExpresso** 的轻量级脚本引擎，专为工控协议仿真场景设计。它允许您在 JSON 模板中嵌入动态表达式，实现：

- ✅ **动态响应生成**：根据输入参数计算响应值
- ✅ **工业协议支持**：内置 CRC、Hex、Binary 等工业级助手
- ✅ **状态管理**：支持会话级和全局变量
- ✅ **字符串插值**：使用 `${expression}` 语法嵌入计算结果

### 📦 核心组件

| 组件 | 说明 |
|:---|:---|
| `ScriptEngine` | 主引擎，负责表达式解析和执行 |
| `ScriptContext` | 上下文管理，提供变量存储和访问 |
| `Industrial.*` | 工业级扩展库（Hex、CRC、Binary 等） |

---

## 2. 基础用法

### 2.1 在 JSON 模板中使用表达式

#### 语法规则

```json
{
  "ResponseTemplate": "@<expression>"  // @ 前缀表示这是一个脚本表达式
}
```

#### 示例 1：简单计算

```json
{
  "Commands": {
    "ADD": {
      "CommandTemplate": "ADD {A} {B}",
      "ResponseTemplate": "@A + B"  // 返回两数之和
    }
  }
}
```

**测试**:
```
> ADD 10 20
< 30
```

#### 示例 2：使用内置函数

```json
{
  "Commands": {
    "SQRT": {
      "CommandTemplate": "SQRT {Val}",
      "ResponseTemplate": "@Math.Sqrt(Val)"  // 使用 Math 库
    }
  }
}
```

**测试**:
```
> SQRT 16
< 4
```

---

## 3. 工业级扩展库

### 3.1 Hex 助手 (HexHelper)

#### 功能列表

| 方法 | 说明 | 示例 |
|:---|:---|:---|
| `Hex.ToBytes(string)` | 将 Hex 字符串转为字节数组 | `Hex.ToBytes("01 02 03")` |
| `Hex.ToString(byte[])` | 将字节数组转为 Hex 字符串 | `Hex.ToString(data)` |
| `Hex.ToInt(string)` | 将 Hex 字符串转为整数 | `Hex.ToInt("FF")` → 255 |

#### 实战案例：Modbus RTU 响应

```json
{
  "Commands": {
    "READ_HOLDING_REGISTER": {
      "CommandTemplate": "03 {Addr:2} {Count:2}",
      "ResponseTemplate": "@'03 ' + Hex.ToString(new byte[]{(byte)Count}) + ' ' + Hex.ToString(GenerateData(Count))"
    }
  }
}
```

---

### 3.2 CRC/Checksum 助手

#### 功能列表

| 方法 | 说明 | 支持的算法 |
|:---|:---|:---|
| `Checksum.Calculate(byte[], method)` | 计算校验和 | CRC16_Modbus, CRC16_CCITT, CRC32, SUM8, XOR 等 |
| `Checksum.GetLength(method)` | 获取校验和字节长度 | - |

#### 支持的 CRC 算法

```
CRC8, CRC8_ITU, CRC8_MAXIM, CRC8_SAE_J1850, CRC8_ROHC
CRC16_Modbus, CRC16_CCITT, CRC16_IBM, CRC16_X25, CRC16_XMODEM
CRC32, CRC32C, CRC32_MPEG2
SUM8, SUM16, SUM32, XOR8, XOR16, XOR32
```

#### 实战案例：自动追加 CRC

```json
{
  "Commands": {
    "WRITE_REGISTER": {
      "CommandTemplate": "06 {Addr:2} {Val:2}",
      "ResponseTemplate": "@AppendCrc('06 ' + Addr + ' ' + Val, 'CRC16_Modbus')"
    }
  }
}
```

**测试**:
```
> 06 00 01 00 0A
< 06 00 01 00 0A C4 6B  // 自动追加 CRC16
```

---

### 3.3 Binary 助手 (BinaryHelper)

#### 功能列表

| 方法 | 说明 | 示例 |
|:---|:---|:---|
| `Binary.GetBit(value, index)` | 获取指定位 | `Binary.GetBit(0b1010, 1)` → 1 |
| `Binary.SetBit(value, index, bit)` | 设置指定位 | `Binary.SetBit(0, 3, 1)` → 8 |
| `Binary.ToBinaryString(value)` | 转为二进制字符串 | `Binary.ToBinaryString(5)` → "101" |

#### 实战案例：位操作

```json
{
  "Commands": {
    "SET_BIT": {
      "CommandTemplate": "SETBIT {Reg} {Bit}",
      "ResponseTemplate": "@Binary.SetBit(GetReg(Reg), Bit, 1)"
    }
  }
}
```

---

### 3.4 Format 助手 (FormatHelper)

#### 功能列表

| 方法 | 说明 | 示例 |
|:---|:---|:---|
| `Format.ToScientific(value, digits)` | 科学计数法 | `Format.ToScientific(0.00123, 2)` → "1.23E-3" |
| `Format.ToEngineering(value)` | 工程计数法 | `Format.ToEngineering(1500)` → "1.5E3" |
| `Format.PadHex(value, width)` | 补齐 Hex 字符串 | `Format.PadHex("FF", 4)` → "00FF" |

---

## 4. 高级特性

### 4.1 字符串插值 (Interpolation)

使用 `${expression}` 语法在字符串中嵌入表达式：

```json
{
  "ResponseTemplate": "VOLT ${Voltage}V, CURR ${Current}A, POWER ${Voltage * Current}W"
}
```

**测试**:
```
> MEAS?
< VOLT 12.5V, CURR 2.3A, POWER 28.75W
```

---

### 4.2 状态变量 (State Management)

#### 会话级变量 (Session Variables)

```json
{
  "Commands": {
    "SET_VOLT": {
      "CommandTemplate": "VOLT {Val}",
      "SetStateKey": "Voltage",  // 保存到会话变量
      "ResponseTemplate": "OK"
    },
    "GET_VOLT": {
      "CommandTemplate": "VOLT?",
      "ResponseTemplate": "@GetState('Voltage', 0)"  // 读取会话变量
    }
  }
}
```

**测试**:
```
> VOLT 12.5
< OK
> VOLT?
< 12.5
```

#### 全局变量 (Global Variables)

```json
{
  "ResponseTemplate": "@SetGlobal('Counter', GetGlobal('Counter', 0) + 1)"
}
```

---

### 4.3 条件响应 (Conditional Responses)

```json
{
  "Commands": {
    "CHECK_RANGE": {
      "CommandTemplate": "CHECK {Val}",
      "ResponseTemplate": "@Val > 100 ? 'HIGH' : (Val < 10 ? 'LOW' : 'OK')"
    }
  }
}
```

**测试**:
```
> CHECK 150
< HIGH
> CHECK 5
< LOW
> CHECK 50
< OK
```

---

## 5. 实战案例

### 案例 1：Modbus RTU 仿真器

```json
{
  "ProtocolName": "Modbus_RTU",
  "FrameMode": "Hex",
  "Commands": {
    "READ_HOLDING_REGISTERS": {
      "MatchPattern": "^03 ([0-9A-F]{2}) ([0-9A-F]{2})$",
      "ResponseTemplate": "@BuildModbusResponse(3, $1, $2)"
    }
  }
}
```

**辅助函数** (在 ScriptContext 中注册):
```csharp
context.SetFunction("BuildModbusResponse", (int func, string addr, string count) => {
    var data = GenerateRegisterData(Hex.ToInt(addr), Hex.ToInt(count));
    var response = $"{func:X2} {count} {Hex.ToString(data)}";
    var crc = Checksum.Calculate(Hex.ToBytes(response), "CRC16_Modbus");
    return response + " " + Hex.ToString(crc);
});
```

---

### 案例 2：SCPI 仿真器（带状态）

```json
{
  "Commands": {
    "CONF:VOLT": {
      "CommandTemplate": "CONF:VOLT {Range}",
      "SetStateKey": "VoltRange",
      "ResponseTemplate": "OK"
    },
    "MEAS:VOLT?": {
      "CommandTemplate": "MEAS:VOLT?",
      "ResponseTemplate": "@SimulateVoltage(GetState('VoltRange', 10))"
    }
  }
}
```

**辅助函数**:
```csharp
context.SetFunction("SimulateVoltage", (double range) => {
    var random = new Random();
    return (random.NextDouble() * range).ToString("F6");
});
```

---

### 案例 3：动态 CRC 追加

```json
{
  "Commands": {
    "WRITE_CMD": {
      "CommandTemplate": "{Payload}",
      "AutoAppendCheckSum": true,
      "CheckSum": "CRC16_Modbus",
      "ResponseTemplate": "@AppendCrc(Payload, 'CRC16_Modbus')"
    }
  }
}
```

---

## 🎓 最佳实践

### 1. 性能优化

- ✅ **缓存编译结果**：ScriptEngine 会自动缓存已编译的表达式
- ✅ **避免复杂嵌套**：将复杂逻辑封装为 C# 函数并注册到 Context
- ❌ **不要在循环中编译**：预先编译表达式，重复调用

### 2. 调试技巧

```json
{
  "ResponseTemplate": "@Trace('Current Value: ' + Val) + Val"
}
```

**日志输出**:
```
[Trace] Current Value: 12.5
```

### 3. 错误处理

```json
{
  "ResponseTemplate": "@TryCatch(() => RiskyOperation(), 'ERROR')"
}
```

---

## ❓ 常见问题

### Q1: 如何在表达式中使用正则捕获组？

**A**: 使用 `$1`, `$2` 等占位符：

```json
{
  "MatchPattern": "^SET (\\d+) (\\d+)$",
  "ResponseTemplate": "@'Received: A=' + $1 + ', B=' + $2"
}
```

### Q2: 如何调用自定义 C# 函数？

**A**: 在 ScriptContext 中注册：

```csharp
var context = new ScriptContext();
context.SetFunction("MyFunc", (int x) => x * 2);
```

然后在表达式中调用：
```json
{
  "ResponseTemplate": "@MyFunc(Val)"
}
```

### Q3: 支持哪些 .NET 类型？

**A**: 默认支持：
- `Math`, `Convert`, `TimeSpan`, `DateTime`
- 所有基础类型（int, double, string, bool 等）
- 自定义注册的类型和函数

---

## 📞 技术支持

如有疑问，请参考：
- **源码**: `src/ZL.Script/`
- **示例**: `UserTemplates/` 中的 JSON 模板
- **GitHub Issues**: https://github.com/your-repo/issues
