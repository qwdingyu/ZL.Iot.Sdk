using SqlSugar;
using System;
using System.Collections.Generic;
using System.Linq;
using ZL.DB.Acc;
using ZL.Model;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 标签运行态快照服务
    /// 替代直接写入 iot_tag.value 的旧模式，提供：
    /// - 冪等 Upsert（InsertOrUpdate）
    /// - 批量写入（减少 DB 往返）
    /// - 与 BizAlarm.ValueChange 分离（采集链路不直接写快照，由订阅事件异步触发）
    /// </summary>
    public class IotTagSnapshotService : Repository<iot_tag_snapshot>
    {
        /// <summary>
        /// 更新单个标签的运行态快照（InsertOrUpdate，冪等）
        /// </summary>
        /// <param name="tagId">标签ID</param>
        /// <param name="value">当前值</param>
        /// <param name="quality">质量码（可选）</param>
        /// <param name="collectTime">采集时间（可选）</param>
        /// <param name="deviceId">设备ID（可选）</param>
        public void UpsertSnapshot(string tagId, string value, string quality = null,
            DateTime? collectTime = null, string deviceId = null)
        {
            var snapshot = new iot_tag_snapshot
            {
                tag_id = tagId,
                value = value,
                quality = quality,
                collect_time = collectTime,
                // updated_at 由 SqlSugar DataExecuting AOP 自动赋值（Insert/Update 均触发）
                device_id = deviceId
            };
            var existing = GetByTagId(tagId);
            if (existing == null)
            {
                Insert(snapshot);
                return;
            }

            existing.value = value;
            existing.quality = quality;
            existing.collect_time = collectTime;
            existing.device_id = deviceId;
            existing.updated_at = DateTime.Now;
            Update(existing);
        }

        /// <summary>
        /// 批量更新标签快照（减少 DB 往返，推荐在异步消费者中使用）
        /// </summary>
        /// <param name="snapshots">快照列表（tag_id → value）</param>
        public void UpsertBatch(Dictionary<string, (string value, string quality, DateTime? collectTime, string deviceId)> snapshots)
        {
            foreach (var kv in snapshots)
            {
                UpsertSnapshot(kv.Key, kv.Value.value, kv.Value.quality, kv.Value.collectTime, kv.Value.deviceId);
            }
        }

        /// <summary>
        /// 获取单个标签的最新快照
        /// </summary>
        public iot_tag_snapshot GetByTagId(string tagId)
        {
            return GetFirst(it => it.tag_id == tagId);
        }

        /// <summary>
        /// 获取指定设备的全部标签快照
        /// </summary>
        public List<iot_tag_snapshot> GetByDeviceId(string deviceId)
        {
            return GetList(it => it.device_id == deviceId);
        }
    }
}
