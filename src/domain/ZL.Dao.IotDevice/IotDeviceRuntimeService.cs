using SqlSugar;
using System;
using System.Collections.Generic;
using ZL.DB.Acc;
using ZL.Model;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 设备运行态服务
    /// 记录设备的在线状态、连接状态、ping 状态、失败计数等运行时信息，
    /// 由 PlcNotificationHub 连接状态事件驱动更新，不依赖定时轮询。
    /// </summary>
    public class IotDeviceRuntimeService : Repository<iot_device_runtime>
    {
        /// <summary>
        /// 更新设备连接状态（由 PlcNotificationHub.PublishConnectionStatus 事件触发）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="pingOk">Ping 是否成功</param>
        /// <param name="connected">连接是否建立</param>
        /// <param name="companyId">公司ID（可选）</param>
        public void UpdateConnectionStatus(string deviceId, bool pingOk, bool connected, string companyId = null)
        {
            var now = DateTime.Now;
            var isOnline = pingOk && connected;
            var runtime = new iot_device_runtime
            {
                device_id = deviceId,
                is_online = isOnline,
                is_connected = connected,
                is_ping_ok = pingOk,
                last_connect_time = connected ? now : null,
                last_ping_time = now,
                runtime_status = isOnline ? "Running" : (connected ? "Degraded" : "Offline"),
                // updated_at 由 SqlSugar DataExecuting AOP 自动赋值（Insert/Update 均触发）
                company_id = companyId
            };
            var existing = GetByDeviceId(deviceId);
            if (existing == null)
            {
                runtime.updated_at = now;
                Insert(runtime);
                return;
            }

            existing.is_online = runtime.is_online;
            existing.is_connected = runtime.is_connected;
            existing.is_ping_ok = runtime.is_ping_ok;
            existing.last_connect_time = runtime.last_connect_time;
            existing.last_ping_time = runtime.last_ping_time;
            existing.runtime_status = runtime.runtime_status;
            existing.updated_at = now;
            existing.company_id = companyId;
            Update(existing);
        }

        /// <summary>
        /// 记录采集异常（失败计数 + 异常信息）
        /// </summary>
        public void RecordCollectError(string deviceId, string errorMsg)
        {
            var entity = GetFirst(it => it.device_id == deviceId);
            if (entity == null)
            {
                entity = new iot_device_runtime
                {
                    device_id = deviceId,
                    is_online = false,
                    is_connected = false,
                    is_ping_ok = false,
                    fail_count = 1,
                    last_error_time = DateTime.Now,
                    last_error_msg = errorMsg,
                    // updated_at 由 SqlSugar DataExecuting AOP 自动赋值（Insert 时触发）
                };
                Insert(entity);
            }
            else
            {
                entity.fail_count++;
                entity.last_error_time = DateTime.Now;
                entity.last_error_msg = errorMsg?.Length > 1000 ? errorMsg.Substring(0, 1000) : errorMsg;
                // updated_at 由 SqlSugar DataExecuting AOP 自动赋值（Update 时触发）
                Update(entity);
            }
        }

        /// <summary>
        /// 记录采集成功（重置失败计数）
        /// </summary>
        public void RecordCollectSuccess(string deviceId)
        {
            var entity = GetFirst(it => it.device_id == deviceId);
            if (entity == null)
            {
                entity = new iot_device_runtime
                {
                    device_id = deviceId,
                    is_online = true,
                    is_connected = true,
                    is_ping_ok = true,
                    fail_count = 0,
                    last_collect_time = DateTime.Now,
                    // updated_at 由 SqlSugar DataExecuting AOP 自动赋值（Insert 时触发）
                };
                Insert(entity);
            }
            else
            {
                entity.fail_count = 0;
                entity.last_collect_time = DateTime.Now;
                entity.runtime_status = "Running";
                // updated_at 由 SqlSugar DataExecuting AOP 自动赋值（Update 时触发）
                Update(entity);
            }
        }

        /// <summary>
        /// 获取设备运行态
        /// </summary>
        public iot_device_runtime GetByDeviceId(string deviceId)
        {
            return GetFirst(it => it.device_id == deviceId);
        }

        /// <summary>
        /// 获取全部在线设备
        /// </summary>
        public List<iot_device_runtime> GetOnlineDevices()
        {
            return GetList(it => it.is_online == true);
        }
    }
}
