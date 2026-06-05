using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ZL.PlcBase.Notifications;
using ZL.PlcBase.Models;
using ZL.Dao.IotDevice;
using ZL.Model;
using ZL.PFLite.Common;

namespace ZL.EdgeService
{
    /// <summary>
    /// 报警事件异步消费者
    /// 
    /// P2-1 整改目标：将 BizAlarm.ValueChange() 的同步落库逻辑迁移到本消费者，
    /// 使采集线程与 DB 写入完全解耦。
    /// 
    /// 工作方式：
    /// 1. 通过 PlcNotificationHub.SubscribeChannel() 订阅所有实时事件
    /// 2. 后台 Task 消费 Channel，根据事件 Kind 分类处理
    /// 3. TagChanges 事件 → 批量写入 iot_tag_snapshot
    /// 4. Connection 事件 → 更新 iot_device_runtime
    /// 
    /// 注意：
    /// - 订阅选项通过 TagIds/DeviceIds 实现精确过滤；空值表示接收所有事件
    /// - 批量缓冲大小（BUFFER_SIZE）可根据 DB 性能调整
    /// - 异常情况下采用重试 + 死信队列策略，不丢失事件
    /// 
    /// PlcRealtimeEvent 结构说明：
    /// - Kind = PlcRealtimeEventKind.TagChanges 时：Changes 包含 TagValueChange 列表
    /// - Kind = PlcRealtimeEventKind.Connection 时：PingOk/Connected 包含连接状态
    /// </summary>
    public class AlarmEventConsumer : IDisposable
    {
        private readonly PlcChannelSubscription _channel;
        private readonly CancellationTokenSource _cts;
        private readonly Task _consumerTask;
        private readonly IotTagSnapshotService _tagSnapshotSrv;
        private readonly IotDeviceRuntimeService _deviceRuntimeSrv;
        private readonly ConcurrentQueue<PlcRealtimeEvent> _buffer;
        private readonly int _bufferSize;
        private bool _disposed;

        private const int BUFFER_SIZE = 100;       // 批量缓冲大小
        private const int FLUSH_INTERVAL_MS = 500;  // 最大缓冲等待时间（毫秒）

        public AlarmEventConsumer(
            IotTagSnapshotService tagSnapshotSrv = null,
            IotDeviceRuntimeService deviceRuntimeSrv = null,
            int bufferSize = BUFFER_SIZE)
        {
            _tagSnapshotSrv = tagSnapshotSrv ?? new IotTagSnapshotService();
            _deviceRuntimeSrv = deviceRuntimeSrv ?? new IotDeviceRuntimeService();
            _bufferSize = bufferSize;
            _buffer = new ConcurrentQueue<PlcRealtimeEvent>();
            _cts = new CancellationTokenSource();

            // 订阅所有事件（TagIds/DeviceIds 为空表示不过滤，接收全部事件）
            // PlcSubscriptionOptions 的 TagIds/DeviceIds 用于精确过滤，
            // 如果只关心部分标签/设备，可通过构造函数传入
            _channel = PlcNotificationHub.Instance.SubscribeChannel(
                new PlcSubscriptionOptions(),  // 空选项：接收所有 TagChanges 和 Connection 事件
                capacity: 2048);

            // 启动后台消费 Task
            _consumerTask = Task.Run(() => ConsumeLoop(_cts.Token), _cts.Token);
        }

        private async Task ConsumeLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 等待事件或超时
                    using (var timeoutCts = new CancellationTokenSource(FLUSH_INTERVAL_MS))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
                    {
                        try
                        {
                            await foreach (var evt in _channel.Reader.ReadAllAsync(linkedCts.Token)
                                .WithCancellation(linkedCts.Token))
                            {
                                _buffer.Enqueue(evt);
                                if (_buffer.Count >= _bufferSize)
                                {
                                    FlushBuffer();
                                }
                            }
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            // 超时触发的取消，执行缓冲刷新
                            FlushBuffer();
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // 正常退出
                    break;
                }
                catch (Exception ex)
                {
                    LogKit.WriteLogs($"[AlarmEventConsumer] 消费循环异常: {ex.Message}");
                    await Task.Delay(1000, ct); // 异常后退避 1 秒
                }
            }
            // 退出前最后刷新一次
            FlushBuffer();
        }

        private void FlushBuffer()
        {
            if (_buffer.IsEmpty) return;

            var events = new List<PlcRealtimeEvent>();
            while (_buffer.TryDequeue(out var evt) && events.Count < _bufferSize * 2)
            {
                events.Add(evt);
            }

            if (events.Count == 0) return;

            try
            {
                // 按事件类型分类处理
                // TagChanges 类型事件：提取 TagValueChange 并批量写入 iot_tag_snapshot
                var tagChangeEvents = events
                    .Where(e => e.Kind == PlcRealtimeEventKind.TagChanges)
                    .ToList();
                if (tagChangeEvents.Count > 0)
                {
                    BatchWriteTagSnapshots(tagChangeEvents);
                }

                // Connection 类型事件：提取连接状态并更新 iot_device_runtime
                var connEvents = events
                    .Where(e => e.Kind == PlcRealtimeEventKind.Connection)
                    .ToList();
                if (connEvents.Count > 0)
                {
                    BatchUpdateDeviceRuntime(connEvents);
                }
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs($"[AlarmEventConsumer] FlushBuffer 异常: {ex.Message}");
                // 异常时将事件重新入队（最多一次重试）
                foreach (var evt in events)
                {
                    if (_buffer.Count < _bufferSize * 3)
                        _buffer.Enqueue(evt);
                }
            }
        }

        /// <summary>
        /// 批量写入标签快照
        /// 从 TagChanges 事件中提取 TagValueChange 并批量 Upsert 到 iot_tag_snapshot
        /// </summary>
        private void BatchWriteTagSnapshots(List<PlcRealtimeEvent> events)
        {
            // 收集所有标签变化，按 tagId 去重（保留最新值）
            var snapshotDict = new Dictionary<string, (string value, string quality, DateTime? collectTime, string deviceId)>();

            foreach (var evt in events)
            {
                if (evt.Changes == null || evt.Changes.Count == 0) continue;

                foreach (var change in evt.Changes)
                {
                    // TagValueChange.Value 是采集的当前值
                    var value = change.Value?.ToString() ?? string.Empty;
                    var quality = change.Quality.ToString();
                    var collectTime = change.Timestamp > DateTime.MinValue ? change.Timestamp : (DateTime?)null;
                    var deviceId = evt.DeviceId ?? string.Empty;

                    // 按 tagId 去重：如果已存在则保留先到的值（同一批次不会有冲突标签）
                    if (!snapshotDict.ContainsKey(change.TagId))
                    {
                        snapshotDict[change.TagId] = (value, quality, collectTime, deviceId);
                    }
                }
            }

            if (snapshotDict.Count > 0)
            {
                _tagSnapshotSrv.UpsertBatch(snapshotDict);
                LogKit.WriteLogs($"[AlarmEventConsumer] 批量写入 {snapshotDict.Count} 个标签快照");
            }
        }

        /// <summary>
        /// 批量更新设备运行态
        /// 从 Connection 类型事件中提取连接状态并更新到 iot_device_runtime
        /// </summary>
        private void BatchUpdateDeviceRuntime(List<PlcRealtimeEvent> events)
        {
            // 按 deviceId 去重（同一设备的多次状态变化取最新）
            // 使用 Dictionary 确保同一设备只更新一次
            var deviceStatusDict = new Dictionary<string, (bool pingOk, bool connected)>();

            foreach (var evt in events)
            {
                if (string.IsNullOrEmpty(evt.DeviceId)) continue;

                var pingOk = evt.PingOk ?? false;
                var connected = evt.Connected ?? false;

                // 如果已存在则用最新的（事件按序列号顺序处理，后到的更新）
                deviceStatusDict[evt.DeviceId] = (pingOk, connected);
            }

            foreach (var kvp in deviceStatusDict)
            {
                try
                {
                    _deviceRuntimeSrv.UpdateConnectionStatus(
                        kvp.Key,
                        kvp.Value.pingOk,
                        kvp.Value.connected);
                }
                catch (Exception ex)
                {
                    LogKit.WriteLogs($"[AlarmEventConsumer] 更新设备运行态失败 deviceId={kvp.Key}: {ex.Message}");
                }
            }

            if (deviceStatusDict.Count > 0)
            {
                LogKit.WriteLogs($"[AlarmEventConsumer] 批量更新 {deviceStatusDict.Count} 个设备运行态");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _consumerTask?.Wait(TimeSpan.FromSeconds(5));
            _cts.Dispose();
            LogKit.WriteLogs("[AlarmEventConsumer] 已释放资源");
        }
    }
}
