using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 死区过滤 Transformer — 当 TagWrite 值变化小于阈值时丢弃该 Tag。
    /// 工业现场刚需：防止传感器微小抖动产生海量消息。
    /// 
    /// 仅对 Writes 非空的消息生效；对纯 Payload 消息透传。
    /// </summary>
    public class DeadbandTransformer
    {
        /// <summary>
        /// 各数据类型的死区阈值。Key 为 DataType 字符串（如 "FLOAT"），Value 为该类型的死区值。
        /// 未配置的 DataType 使用 DefaultDeadband。
        /// 如果 DefaultDeadband 和具体类型都未配置，则所有消息透传（不过滤）。
        /// </summary>
        private readonly ConcurrentDictionary<string, double> _deadbands = new();

        /// <summary>默认死区阈值（应用于未显式配置的 DataType）</summary>
        private double _defaultDeadband;

        /// <summary>是否启用绝对死区。true=绝对差值小于阈值时丢弃，false=相对变化率小于阈值时丢弃</summary>
        private readonly bool _absolute;

        /// <summary>上次各 Address 的值（用于计算变化量）— 线程安全的并发字典</summary>
        private readonly ConcurrentDictionary<string, double> _lastValues = new();

        public DeadbandTransformer(double defaultDeadband = 0.0, bool absolute = true)
        {
            _defaultDeadband = defaultDeadband;
            _absolute = absolute;
        }

        /// <summary>
        /// 为特定 DataType 设置死区阈值。
        /// </summary>
        public DeadbandTransformer SetDeadband(string dataType, double deadband)
        {
            _deadbands[dataType.ToUpperInvariant()] = deadband;
            return this;
        }

        /// <summary>
        /// 返回一个 Func&lt;Message, Task&lt;Message&gt;&gt;，可直接传入 pipeline.AddTransformer()。
        /// </summary>
        public Func<Message, Task<Message>> Build()
        {
            return async (message) =>
            {
                // 死区为 0 且无特定类型配置 → 透传
                if (_defaultDeadband == 0.0 && _deadbands.Count == 0)
                {
                    return await Task.FromResult(message);
                }

                // 仅处理有 Writes 的消息
                if (message?.Writes == null || message.Writes.Count == 0)
                {
                    return await Task.FromResult(message);
                }

                var filtered = new List<TagWrite>();
                foreach (var tw in message.Writes)
                {
                    if (!ShouldFilter(tw))
                    {
                        filtered.Add(tw);
                    }
                }

                if (filtered.Count == message.Writes.Count)
                {
                    // 无过滤，返回原消息
                    return await Task.FromResult(message);
                }

                if (filtered.Count == 0)
                {
                    // 全部被死区过滤，返回 null 让 Pipeline 丢弃整条消息
                    return await Task.FromResult<Message>(null!);
                }

                // 部分过滤，返回克隆消息（修改 Writes 列表）
                var clone = message.Clone();
                // Clone() 创建新的 Writes 列表，清空后添加过滤后的
                clone.Writes.Clear();
                clone.Writes.AddRange(filtered);
                return await Task.FromResult(clone);
            };
        }

        private bool ShouldFilter(TagWrite tw)
        {
            double deadband;
            var dataType = tw.DataType.ToUpperInvariant();
            if (!_deadbands.TryGetValue(dataType, out deadband))
            {
                deadband = _defaultDeadband;
            }

            if (deadband == 0.0) return false;

            // 将值转为 double
            double currentValue;
            if (!TryConvertToDouble(tw.Value, tw.DataType, out currentValue))
            {
                return false; // 无法转换的类型不过滤
            }

            // BOOL 类型：值变了就不过滤
            if (dataType == "BOOL")
            {
                return false;
            }

            // STRING 类型：不过滤
            if (dataType == "STRING")
            {
                return false;
            }

            // 使用 Address 作为 key 追踪历史值
            var key = tw.Address;

            // P2 修复：使用自旋重试循环保证"读取→比较→更新"的原子性。
            // 避免多线程并发访问同一 key 时互相覆盖历史值导致死区精度丢失。
            const int maxRetries = 10;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                if (!_lastValues.TryGetValue(key, out double lastValue))
                {
                    // 首次看到该地址：尝试原子添加
                    if (_lastValues.TryAdd(key, currentValue))
                    {
                        return false; // 首次，不过滤
                    }
                    // TryAdd 失败：另一线程抢先添加了，重试读取新值
                    continue;
                }

                bool filter;
                if (_absolute)
                {
                    filter = Math.Abs(currentValue - lastValue) < deadband;
                }
                else
                {
                    double denominator = Math.Abs(lastValue);
                    if (denominator < 1e-9)
                    {
                        filter = Math.Abs(currentValue - lastValue) < deadband;
                    }
                    else
                    {
                        filter = (Math.Abs(currentValue - lastValue) / denominator) < deadband;
                    }
                }

                if (filter)
                {
                    // 在死区内：不更新历史值，直接返回
                    return true;
                }

                // 超出死区：尝试原子更新历史值
                if (_lastValues.TryUpdate(key, currentValue, lastValue))
                {
                    return false; // 更新成功，不过滤
                }
                // TryUpdate 失败：另一线程已修改了 lastValue，重试
            }

            // 超过最大重试次数，保守策略：不过滤（避免漏报）
            return false;
        }

        /// <summary>
        /// 清除历史值缓存（重置死区状态）。
        /// </summary>
        public void Reset()
        {
            _lastValues.Clear();
        }

        /// <summary>
        /// 获取当前死区配置（用于诊断）。
        /// </summary>
        public IReadOnlyDictionary<string, double> Deadbands => _deadbands;

        private static bool TryConvertToDouble(object value, string dataType, out double result)
        {
            result = 0.0;
            if (value == null) return false;

            try
            {
                result = dataType.ToUpperInvariant() switch
                {
                    "BOOL" => Convert.ToBoolean(value) ? 1.0 : 0.0,
                    "BYTE" or "UINT8" => Convert.ToByte(value),
                    "SBYTE" or "INT8" => Convert.ToSByte(value),
                    "UINT16" or "WORD" => Convert.ToUInt16(value),
                    "INT16" or "SHORT" or "WORD" => Convert.ToInt16(value),
                    "UINT32" or "DWORD" => Convert.ToUInt32(value),
                    "INT32" or "INT" or "DWORD" => Convert.ToInt32(value),
                    "UINT64" or "LWORD" => Convert.ToUInt64(value),
                    "INT64" or "LONG" or "LWORD" => Convert.ToInt64(value),
                    "FLOAT" or "REAL" => Convert.ToSingle(value),
                    "DOUBLE" or "LREAL" => Convert.ToDouble(value),
                    _ => Convert.ToDouble(value)
                };
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
