using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ZL.Iot.Interface;
using ZL.Iot.Runner.Configuration;
using ZL.Tag;

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// 配置驱动的采集历史存储管线。
/// </summary>
public sealed class HistoryStoragePipeline : IDisposable
{
    // 历史存储运行参数；构造时会归一化批量大小、flush 间隔、队列容量和表名。
    private readonly StorageOptions _options;

    // 表存储执行器基于 ORM/元数据 API 创建表和批量写入，避免历史落库路径拼接裸 SQL。
    private readonly ITableStorageExecutor _tableStorage;
    private readonly ILogger<HistoryStoragePipeline> _logger;

    // 有界队列隔离 PLC 采集线程和数据库写入线程，避免数据库慢写阻塞采集。
    private readonly Channel<HistoryStorageRecord> _channel;

    // 配置中指定的可落库标签集合；为空表示所有启用标签都允许落库。
    private readonly HashSet<string> _configuredTags;

    // 管线生命周期令牌，仅用于停止后台消费者，不参与单条采集事件判断。
    private readonly CancellationTokenSource _cts = new();

    // 单消费者后台任务：负责按 BatchSize 或 FlushIntervalMs 批量写库。
    private readonly Task _consumerTask;

    // 表只需确保一次；ConcurrentDictionary 用作线程安全 set。
    private readonly ConcurrentDictionary<string, byte> _ensuredTables = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public HistoryStoragePipeline(
        StorageOptions options,
        ITableStorageExecutor tableStorage,
        ILogger<HistoryStoragePipeline> logger)
    {
        _options = NormalizeOptions(options);
        _tableStorage = tableStorage;
        _logger = logger;
        _configuredTags = BuildConfiguredTags(_options.Mappings);
        _channel = Channel.CreateBounded<HistoryStorageRecord>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    public bool TryEnqueue(string deviceCode, string tagId, object? value, TagItem? tag)
    {
        if (_disposed || !_options.Enabled || string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(tagId))
        {
            return false;
        }

        if (_configuredTags.Count > 0
            && !_configuredTags.Contains(BuildMappingKey(deviceCode, tagId))
            && !_configuredTags.Contains(BuildMappingKey(string.Empty, tagId)))
        {
            return false;
        }

        var record = new HistoryStorageRecord(
            deviceCode,
            tagId,
            tag?.TagType ?? string.Empty,
            value,
            tag?.DataTypeCode ?? string.Empty,
            DateTimeOffset.Now);

        if (_channel.Writer.TryWrite(record))
        {
            return true;
        }

        _logger.LogWarning("[{DeviceCode}] 历史存储队列已满，丢弃标签历史: {TagId}", deviceCode, tagId);
        return false;
    }

    public async Task StopAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();

        try
        {
            await _consumerTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _cts.Cancel();
            _logger.LogWarning("[{DeviceCode}] 历史存储管线停止超时", "shared");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            _cts.Dispose();
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();

        try
        {
            if (!_consumerTask.Wait(TimeSpan.FromSeconds(10)))
            {
                _cts.Cancel();
                _logger.LogWarning("[{DeviceCode}] 历史存储管线释放超时", "shared");
            }
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException || inner is TimeoutException))
        {
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("[{DeviceCode}] 历史存储管线释放超时", "shared");
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var batch = new List<HistoryStorageRecord>(_options.BatchSize);
        var flushInterval = TimeSpan.FromMilliseconds(_options.FlushIntervalMs);
        var delayTask = Task.Delay(flushInterval, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                while (_channel.Reader.TryRead(out var record))
                {
                    batch.Add(record);
                    if (batch.Count >= _options.BatchSize)
                    {
                        await FlushAsync(batch).ConfigureAwait(false);
                        delayTask = Task.Delay(flushInterval, ct);
                    }
                }

                if (_channel.Reader.Completion.IsCompleted)
                {
                    break;
                }

                var readTask = _channel.Reader.WaitToReadAsync(ct).AsTask();
                var completedTask = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
                if (completedTask == delayTask)
                {
                    if (batch.Count > 0)
                    {
                        await FlushAsync(batch).ConfigureAwait(false);
                    }

                    delayTask = Task.Delay(flushInterval, ct);
                    continue;
                }

                if (!await readTask.ConfigureAwait(false))
                {
                    break;
                }
            }

            if (batch.Count > 0)
            {
                await FlushAsync(batch).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{DeviceCode}] 历史存储管线异常退出", "shared");
        }
    }

    private async Task FlushAsync(List<HistoryStorageRecord> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await EnsureTableAsync().ConfigureAwait(false);

            var rows = batch.Select(record => new Dictionary<string, object>
            {
                ["process_time"] = record.ProcessTime.DateTime,
                ["device_code"] = record.DeviceCode,
                ["tag_id"] = record.TagId,
                ["tag_type"] = record.TagType,
                ["data_type"] = record.DataType,
                ["value_text"] = record.Value?.ToString() ?? string.Empty,
                ["value_number"] = TryConvertNumber(record.Value, out var number) ? number : DBNull.Value
            }).ToList();

            await _tableStorage.InsertRowsAsync(_options.TableName, rows).ConfigureAwait(false);

            _logger.LogDebug("[{DeviceCode}] 历史存储已批量写入 {Count} 行", batch[0].DeviceCode, batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{DeviceCode}] 历史存储批量写入失败", batch[0].DeviceCode);
        }
        finally
        {
            batch.Clear();
        }
    }

    private async Task EnsureTableAsync()
    {
        if (!_ensuredTables.TryAdd(_options.TableName, 0))
        {
            return;
        }

        var columns = new List<TableColumnDefinition>
        {
            new()
            {
                Name = "id",
                DataType = "bigint",
                IsPrimaryKey = true,
                IsIdentity = true,
                IsNullable = false
            },
            new() { Name = "process_time", DataType = "datetime", IsNullable = false },
            new() { Name = "device_code", DataType = "nvarchar", Length = 128, IsNullable = false },
            new() { Name = "tag_id", DataType = "nvarchar", Length = 128, IsNullable = false },
            new() { Name = "tag_type", DataType = "nvarchar", Length = 64 },
            new() { Name = "data_type", DataType = "nvarchar", Length = 64 },
            new() { Name = "value_text", DataType = "nvarchar", Length = 1024 },
            new() { Name = "value_number", DataType = "double" }
        };

        await _tableStorage.EnsureTableAsync(_options.TableName, columns).ConfigureAwait(false);
    }

    private static StorageOptions NormalizeOptions(StorageOptions options)
    {
        options.BatchSize = Math.Max(1, options.BatchSize);
        options.FlushIntervalMs = Math.Max(100, options.FlushIntervalMs);
        options.QueueCapacity = Math.Max(options.BatchSize, options.QueueCapacity);
        options.TableName = string.IsNullOrWhiteSpace(options.TableName)
            ? "iot_tag_history"
            : NormalizeIdentifier(options.TableName.Trim());
        return options;
    }

    private static string NormalizeIdentifier(string identifier)
    {
        if (identifier.Length == 0 || identifier.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
        {
            throw new InvalidOperationException($"历史存储表名非法: {identifier}");
        }

        return identifier;
    }

    private static HashSet<string> BuildConfiguredTags(List<StorageMapping> mappings)
    {
        return mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.TagId))
            .Select(m => BuildMappingKey(m.DeviceCode, m.TagId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildMappingKey(string? deviceCode, string tagId)
    {
        return $"{deviceCode?.Trim() ?? string.Empty}::{tagId.Trim()}";
    }


    private static bool TryConvertNumber(object? value, out double number)
    {
        return value switch
        {
            null => TryParse(null, out number),
            bool b => SetNumber(b ? 1 : 0, out number),
            byte b => SetNumber(b, out number),
            short s => SetNumber(s, out number),
            int i => SetNumber(i, out number),
            long l => SetNumber(l, out number),
            float f => SetNumber(f, out number),
            double d => SetNumber(d, out number),
            decimal d => SetNumber((double)d, out number),
            _ => TryParse(value.ToString(), out number)
        };
    }

    private static bool SetNumber(double value, out double number)
    {
        number = value;
        return true;
    }

    private static bool TryParse(string? value, out double number)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private sealed record HistoryStorageRecord(
        string DeviceCode,
        string TagId,
        string TagType,
        object? Value,
        string DataType,
        DateTimeOffset ProcessTime);
}
