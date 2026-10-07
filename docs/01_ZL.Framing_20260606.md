# ZL.Framing — 帧解析基础设施

## 概述

ZL.Framing 提供二进制和文本帧的解析基础设施，包括帧分割选项配置、字节缓冲区管理、帧解码器和传输层接口定义。它是整个传输层栈的核心底层组件，直接面向字节流操作。

---

## 1. 传输层接口体系

ZL.Framing 定义了四层传输接口，呈继承关系逐级扩展能力。

### 1.1 IByteTransport — 基础传输接口

```csharp
public interface IByteTransport : IDisposable
{
    string ResourceName { get; }
    bool IsOpen { get;