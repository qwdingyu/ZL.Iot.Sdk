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
    private readonly string _deviceCode;
    private readonly StorageOptions _options;
    private readonly ISqlExecutor _sqlExecutor;
    private readonly ILogger<HistoryStoragePipeline> _logger;
    private readonly Channel<HistoryStorageRecord> _channel;
    private readonly HashSet<string> _configuredTags;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;
    private readonly ConcurrentDictionary<string, byte> _ensuredTables = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public HistoryStoragePipeline(
        string deviceCode,
        StorageOptions options,
        ISqlExecutor sqlExecutor,
        ILogger<HistoryStoragePipeline> logger)
    {
        _deviceCode = deviceCode;
        _options = NormalizeOptions(options);
        _sqlExecutor = sqlExecutor;
        _logger = logger;
        _configuredTags = BuildConfiguredTags(deviceCode, _options.Mappings);
        _channel = Channel.CreateBounded<HistoryStorageRecord>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    public bool TryEnqueue(string tagId, object? value, TagItem? tag)
    {
        if (_disposed || !_options.Enabled || string.IsNullOrWhiteSpace(tagId))
        {
            return false;
        }

        if (_configuredTags.Count > 0 && !_configuredTags.Contains(tagId))
        {
            return false;
        }

        var record = new HistoryStorageRecord(
            _deviceCode,
            tagId,
            tag?.TagType ?? string.Empty,
            value,
            tag?.DataTypeCode ?? string.Empty,
            DateTimeOffset.Now);

        if (_channel.Writer.TryWrite(record))
        {
            return true;
        }

        _logger.LogWarning("[{DeviceCode}] 历史存储队列已满，丢弃标签历史: {TagId}", _deviceCode, tagId);
        return false;
    }

    public async Task StopAsync()
    {
        if (_disposed)
        {
            return;
        }

        _channel.Writer.TryComplete();
        await _consumerTask.ConfigureAwait(false);
        _cts.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        try
        {
            _consumerTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
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
            _logger.LogError(ex, "[{DeviceCode}] 历史存储管线异常退出", _deviceCode);
        }
    }

    private async Task FlushAsync(List<HistoryStorageRecord> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        await EnsureTableAsync().ConfigureAwait(false);

        const string sql = "INSERT INTO {0} (process_time, device_code, tag_id, tag_type, data_type, value_text, value_number) VALUES (@process_time, @device_code, @tag_id, @tag_type, @data_type, @value_text, @value_number)";
        var parameters = batch.Select(record => new Dictionary<string, object>
        {
            ["@process_time"] = record.ProcessTime.ToString("O"),
            ["@device_code"] = record.DeviceCode,
            ["@tag_id"] = record.TagId,
            ["@tag_type"] = record.TagType,
            ["@data_type"] = record.DataType,
            ["@value_text"] = record.Value?.ToString() ?? string.Empty,
            ["@value_number"] = TryConvertNumber(record.Value, out var number) ? number : DBNull.Value
        }).ToList();

        await _sqlExecutor.ExecuteBatchNonQueryAsync(
            string.Format(CultureInfo.InvariantCulture, sql, _options.TableName),
            parameters).ConfigureAwait(false);

        _logger.LogDebug("[{DeviceCode}] 历史存储已批量写入 {Count} 行", _deviceCode, batch.Count);
        batch.Clear();
    }

    private async Task EnsureTableAsync()
    {
        if (!_ensuredTables.TryAdd(_options.TableName, 0))
        {
            return;
        }

        var sql = $"CREATE TABLE IF NOT EXISTS {_options.TableName} (id INTEGER PRIMARY KEY, process_time TEXT NOT NULL, device_code TEXT NOT NULL, tag_id TEXT NOT NULL, tag_type TEXT, data_type TEXT, value_text TEXT, value_number REAL);";
        await _sqlExecutor.ExecuteNonQueryAsync(sql).ConfigureAwait(false);
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

    private static HashSet<string> BuildConfiguredTags(string deviceCode, List<StorageMapping> mappings)
    {
        return mappings
            .Where(m => string.IsNullOrWhiteSpace(m.DeviceCode) || string.Equals(m.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.IsNullOrWhiteSpace(m.TagId))
            .Select(m => m.TagId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
