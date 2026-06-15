// ============================================================
//  统一写入队列（P1-1 支柱 A：单写入器）
//
//  把现场三条落库路径（历史采集 / 规则 SQL / FieldMapping）收敛到
//  唯一的有界队列 + 单消费者线程。只有消费者线程触碰底层数据库执行器，
//  因此：
//   1. 非线程安全的 SqlSugarClient 永不被并发访问（取代 SerializedSqlExecutor）；
//   2. 采集/触发线程永不阻塞在 DB I/O（满足工业实时性）；
//   3. JSONL 降级写入收敛到单线程（消除 File.AppendAllText 竞争）。
//
//  历史写入仍走批量（高频小数据）；规则 SQL / FieldMapping 为低频结构化
//  写入，逐条执行但同样在消费者线程串行。落库成功后触发 OnCommitted
//  回调（FieldMapping 反馈写回 PLC，保证 PLC 收到完成信号时数据已落库）。
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZL.Iot.Interface;
using ZL.Iot.Runner.Configuration;

namespace ZL.Iot.Runner.Runtime;

/// <summary>
/// Runner 级统一写入队列：多生产者、单消费者，串行批量落库。
/// </summary>
public sealed class RunnerWriteQueue : IDisposable, IAsyncDisposable
{
    private readonly StorageOptions _historyOptions;
    private readonly ISqlExecutor? _sqlExecutor;
    private readonly ITableStorageExecutor? _tableStorage;
    private readonly ILogger<RunnerWriteQueue> _logger;

    private readonly Channel<WriteCommand> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;

    // 通用历史表确保只建一次。
    private readonly ConcurrentDictionary<string, byte> _ensuredTables = new(StringComparer.OrdinalIgnoreCase);
    // FieldMapping sink 复用（包含表名/列名注入校验 + 建表）。
    private readonly SqlExecutorFieldMappingSink? _fieldMappingSink;
    private bool _disposed;

    public RunnerWriteQueue(
        StorageOptions historyOptions,
        ISqlExecutor? sqlExecutor,
        ITableStorageExecutor? tableStorage,
        ILogger<RunnerWriteQueue> logger)
    {
        _historyOptions = NormalizeHistoryOptions(historyOptions);
        _sqlExecutor = sqlExecutor;
        _tableStorage = tableStorage;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fieldMappingSink = sqlExecutor is not null
            ? new SqlExecutorFieldMappingSink(sqlExecutor, logger)
            : null;

        _channel = Channel.CreateBounded<WriteCommand>(new BoundedChannelOptions(_historyOptions.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    /// <summary>
    /// 入队写命令。队列满时丢弃并返回 false（与历史管线既有行为一致）。
    /// </summary>
    public bool TryEnqueue(WriteCommand command)
    {
        if (_disposed || command is null)
        {
            return false;
        }

        if (_channel.Writer.TryWrite(command))
        {
            return true;
        }

        _logger.LogWarning("[{DeviceCode}] 写入队列已满，丢弃命令 {Type}", command.DeviceCode, command.GetType().Name);
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
            _logger.LogWarning("写入队列停止超时");
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
                _logger.LogWarning("写入队列释放超时");
            }
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException || inner is TimeoutException))
        {
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("写入队列释放超时");
        }
        finally
        {
            _cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    // ── 消费者循环 ──────────────────────────────────────────────

    private async Task ConsumeAsync(CancellationToken ct)
    {
        // 历史命令累积成批，其他命令即到即处理；flush 间隔到达时把历史批次落库。
        var historyBatch = new List<HistoryWriteCommand>(_historyOptions.BatchSize);
        var flushInterval = TimeSpan.FromMilliseconds(_historyOptions.FlushIntervalMs);
        var delayTask = Task.Delay(flushInterval, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                while (_channel.Reader.TryRead(out var command))
                {
                    await DispatchAsync(command, historyBatch).ConfigureAwait(false);
                    if (historyBatch.Count >= _historyOptions.BatchSize)
                    {
                        await FlushHistoryAsync(historyBatch).ConfigureAwait(false);
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
                    await FlushHistoryAsync(historyBatch).ConfigureAwait(false);
                    delayTask = Task.Delay(flushInterval, ct);
                    continue;
                }

                if (!await readTask.ConfigureAwait(false))
                {
                    break;
                }
            }

            // 收尾：把残留历史批次落库。
            await FlushHistoryAsync(historyBatch).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写入队列消费者异常退出");
        }
    }

    private async Task DispatchAsync(WriteCommand command, List<HistoryWriteCommand> historyBatch)
    {
        switch (command)
        {
            case HistoryWriteCommand history:
                historyBatch.Add(history);
                break;

            case TableInsertCommand table:
                await ExecuteTableInsertAsync(table).ConfigureAwait(false);
                break;

            case RawSqlCommand raw:
                await ExecuteRawSqlAsync(raw).ConfigureAwait(false);
                break;

            default:
                _logger.LogWarning("未知写入命令类型: {Type}", command.GetType().Name);
                break;
        }
    }

    // ── 历史批量落库 ────────────────────────────────────────────

    private async Task FlushHistoryAsync(List<HistoryWriteCommand> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var committed = new List<HistoryWriteCommand>(batch.Count);
        try
        {
            if (_tableStorage is not null)
            {
                await EnsureHistoryTableAsync().ConfigureAwait(false);

                var rows = batch.Select(record => new Dictionary<string, object>
                {
                    ["event_time"] = record.EventTime.DateTime,
                    ["process_time"] = DateTime.Now,
                    ["device_code"] = record.DeviceCode,
                    ["tag_id"] = record.TagId,
                    ["tag_type"] = record.TagType,
                    ["data_type"] = record.DataType,
                    ["value_text"] = record.Value?.ToString() ?? string.Empty,
                    ["value_number"] = TryConvertNumber(record.Value, out var number) ? number : DBNull.Value
                }).ToList();

                await _tableStorage.InsertRowsAsync(_historyOptions.TableName, rows).ConfigureAwait(false);
                _logger.LogDebug("[{DeviceCode}] 历史批量写入 {Count} 行", batch[0].DeviceCode, batch.Count);
            }
            else
            {
                WriteHistoryFallback(batch);
            }

            committed.AddRange(batch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{DeviceCode}] 历史批量写入失败", batch[0].DeviceCode);
        }
        finally
        {
            batch.Clear();
        }

        // 落库成功后触发回调（历史一般无回调，保持对称）。
        foreach (var record in committed)
        {
            InvokeCommitted(record);
        }
    }

    private async Task EnsureHistoryTableAsync()
    {
        if (!_ensuredTables.TryAdd(_historyOptions.TableName, 0) || _tableStorage is null)
        {
            return;
        }

        var columns = new List<TableColumnDefinition>
        {
            new() { Name = "id", DataType = "bigint", IsPrimaryKey = true, IsIdentity = true, IsNullable = false },
            new() { Name = "event_time", DataType = "datetime", IsNullable = false },
            new() { Name = "process_time", DataType = "datetime", IsNullable = false },
            new() { Name = "device_code", DataType = "nvarchar", Length = 128, IsNullable = false },
            new() { Name = "tag_id", DataType = "nvarchar", Length = 128, IsNullable = false },
            new() { Name = "tag_type", DataType = "nvarchar", Length = 64 },
            new() { Name = "data_type", DataType = "nvarchar", Length = 64 },
            new() { Name = "value_text", DataType = "nvarchar", Length = 1024 },
            new() { Name = "value_number", DataType = "double" }
        };

        await _tableStorage.EnsureTableAsync(_historyOptions.TableName, columns).ConfigureAwait(false);
    }

    // ── FieldMapping 表插入 ─────────────────────────────────────

    private async Task ExecuteTableInsertAsync(TableInsertCommand command)
    {
        if (command.Rows.Count == 0)
        {
            return;
        }

        var success = false;
        try
        {
            if (_fieldMappingSink is not null)
            {
                _fieldMappingSink.EnsureTable(command.TableName, command.Columns.ToList());
                _fieldMappingSink.InsertRows(command.TableName, command.Rows.ToList());
                success = true;
            }
            else
            {
                WriteTableInsertFallback(command);
                success = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{DeviceCode}] FieldMapping 落库失败: {Table}", command.DeviceCode, command.TableName);
        }

        await Task.CompletedTask.ConfigureAwait(false);

        // 仅在落库成功后反馈写回（防丢数据）。
        if (success)
        {
            InvokeCommitted(command);
        }
    }

    // ── 规则原始 SQL ────────────────────────────────────────────

    private async Task ExecuteRawSqlAsync(RawSqlCommand command)
    {
        var success = false;
        try
        {
            if (_sqlExecutor is not null)
            {
                var affected = await _sqlExecutor.ExecuteNonQueryAsync(command.Sql).ConfigureAwait(false);
                _logger.LogInformation("[{BizCode}] 执行完成 | Type={ExeType} | 影响行数: {Rows}",
                    command.BizCode, command.ExeType, affected);
            }
            else
            {
                WriteRawSqlFallback(command);
            }

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{BizCode}] SQL 执行异常 | SQL: {Sql}", command.BizCode, command.Sql);
        }

        if (success)
        {
            InvokeCommitted(command);
        }
    }

    // ── JSONL 降级（单消费者线程，无并发） ──────────────────────

    private void WriteHistoryFallback(List<HistoryWriteCommand> batch)
    {
        try
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dataDir);
            var filePath = Path.Combine(dataDir, $"{_historyOptions.TableName}.jsonl");
            foreach (var record in batch)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    eventTime = record.EventTime,
                    deviceCode = record.DeviceCode,
                    tagId = record.TagId,
                    tagType = record.TagType,
                    dataType = record.DataType,
                    value = record.Value?.ToString()
                });
                File.AppendAllText(filePath, json + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "历史 JSONL 降级写入失败");
        }
    }

    private void WriteTableInsertFallback(TableInsertCommand command)
    {
        try
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dataDir);
            var filePath = Path.Combine(dataDir, $"{command.TableName}.jsonl");
            foreach (var row in command.Rows)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(row);
                File.AppendAllText(filePath, json + Environment.NewLine);
            }
            _logger.LogInformation("[FieldMapping] 已写入 JSON Lines: {FilePath} ({Rows} 行)", filePath, command.Rows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FieldMapping] 降级写入失败: {TableName}", command.TableName);
        }
    }

    private void WriteRawSqlFallback(RawSqlCommand command)
    {
        try
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dataDir);
            var filePath = Path.Combine(dataDir, $"sql_exec_{DateTime.Now:yyyyMMdd}.jsonl");
            var entry = new { time = DateTime.Now, bizCode = command.BizCode, exeType = command.ExeType, sql = command.Sql };
            var json = System.Text.Json.JsonSerializer.Serialize(entry);
            File.AppendAllText(filePath, json + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{BizCode}] SQL JSONL 降级写入失败", command.BizCode);
        }
        _logger.LogInformation("[{BizCode}] 模拟执行 {ExeType} | SQL: {Sql}", command.BizCode, command.ExeType, command.Sql);
    }

    private void InvokeCommitted(WriteCommand command)
    {
        if (command.OnCommitted is null)
        {
            return;
        }

        try
        {
            command.OnCommitted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{DeviceCode}] 落库回调（反馈写回）异常", command.DeviceCode);
        }
    }

    // ── 工具方法 ────────────────────────────────────────────────

    private static StorageOptions NormalizeHistoryOptions(StorageOptions options)
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

    private static bool TryConvertNumber(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case bool b:
                number = b ? 1 : 0;
                return true;
            case byte or short or int or long or float or double or decimal:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }
    }
}
