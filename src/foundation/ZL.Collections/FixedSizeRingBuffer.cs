namespace ZL.Collections.Collections;

/// <summary>
/// 固定容量环形缓冲区 — 追加零分配（满时覆盖最旧项），读取返回最近 N 条（时间正序）。
/// 线程安全：Append 和 GetNewest 均通过 lock 保护。
/// </summary>
/// <typeparam name="T">元素类型</typeparam>
public sealed class FixedSizeRingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _capacity;
    private int _head; // 下一个要覆盖的位置（最旧项）
    private int _count;
    private readonly object _lock = new();

    /// <summary>
    /// 创建固定容量环形缓冲区。
    /// </summary>
    /// <param name="capacity">缓冲区容量，必须大于 0</param>
    public FixedSizeRingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _buffer = new T[capacity];
    }

    /// <summary>追加一项。满时自动覆盖最旧项，无分配。</summary>
    public void Append(T item)
    {
        lock (_lock)
        {
            int idx = (_head + _count) % _capacity;
            _buffer[idx] = item;
            if (_count < _capacity)
                _count++;
            else
                _head = (_head + 1) % _capacity;
        }
    }

    /// <summary>获取最近 count 条（按时间正序）。返回新数组，调用方拥有所有权。</summary>
    public IReadOnlyList<T> GetNewest(int count)
    {
        if (count <= 0) return Array.Empty<T>();

        lock (_lock)
        {
            int take = Math.Min(count, _count);
            if (take == 0) return Array.Empty<T>();

            var result = new T[take];
            // 最新项在 (_head + _count - 1) % _capacity，倒序拷贝再反转
            for (int i = 0; i < take; i++)
            {
                int idx = (_head + _count - 1 - i) % _capacity;
                result[take - 1 - i] = _buffer[idx];
            }
            return result;
        }
    }
}
