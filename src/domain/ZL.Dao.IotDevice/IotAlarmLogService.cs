using System;
using System.Data;
using System.Linq;
using ZL.DB.Acc;
using ZL.PFLite;
using ZL.PFLite.Common;

namespace ZL.Dao.IotDevice
{
    public class IotAlarmLogService : Repository<iot_alarmlog>
    {
        public bool Upsert(string tag_id, string device_id, string address, string alarm_text, IotDeviceDriverDto DeviceInfo, bool val)
        {
            bool ok = false;
            string sql = "";
            try
            {
                string StationNo = DeviceInfo.station_no;
                base.AsTenant().BeginTran();
                string DateStr = GetDateStr;
                if (val)
                {
                    int status = 1;
                    string alarm_type = "1";
                    DateTime now = GetDate;
                    sql = $"update iot_alarmlog set end_time = {DateStr}, duration = {GetDuration("created_at")}, updated_at = {DateStr}  WHERE tag_id = '{tag_id}' AND status =1 AND end_time IS NULL AND duration IS NULL";
                    Execute(sql);
                    //第一种原生的sql
                    sql = string.Format(@"INSERT INTO iot_alarmlog ( device_id, tag_id, statistics_id, status, address, alarm_text, 
                                        start_time, created_at, created_by, alarm_type, station_no ) 
                                          VALUES ( '{0}','{1}',{2},{3},'{4}','{5}', {6}, {6}, 'admin','{7}' , '{8}' )",
                                          device_id, tag_id, 0, status, address, alarm_text, DateStr, alarm_type, StationNo);
                    Execute(sql);
                    sql = $"update iot_station set status_alarm = {status}, updated_at = {DateStr}  WHERE company_id = '{DeviceInfo.company_id}' AND plant_id = '{DeviceInfo.plant_id}' AND line = '{DeviceInfo.line}' AND station_no = '{StationNo}'";
                    Execute(sql);
                }
                else
                {
                    //一个工位只能定义一个device_id，否则无法准确计算该工位是否有故障
                    sql = $"SELECT id, value FROM iot_tag WHERE device_id = '{device_id}' and (address <> '' or address is not null)";
                    DataTable dt = GetDataTable(sql);
                    string[] statusList = new string[dt.Rows.Count];
                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        statusList[i] = dt.Rows[i]["value"]?.ToString() ?? "";
                    }
                    int hasAlarm = HasAlarm(statusList);
                    //更新工位状态--是否有故障
                    sql = $"update iot_station set status_alarm = {hasAlarm}, updated_at = {DateStr}  WHERE company_id = '{DeviceInfo.company_id}' AND plant_id = '{DeviceInfo.plant_id}' AND line = '{DeviceInfo.line}' AND station_no = '{StationNo}'";
                    Execute(sql);
                    //计算结束时间和时长
                    sql = $"update iot_alarmlog set status = 0, end_time = {DateStr}, duration = {GetDuration("created_at")}, updated_at = {DateStr}  WHERE tag_id = '{tag_id}' AND status = 1 AND end_time IS NULL AND duration IS NULL";
                    Execute(sql);
                }
                base.AsTenant().CommitTran();
                ok = true;
            }
            catch (Exception ex)
            {
                base.AsTenant().RollbackTran();
                LogKit.WriteLogs("记录报警日志异常:" + ex.Message);
                throw;
            }
            return ok;
        }

        public bool Upsert(iot_tag tag, IotDeviceDriverDto DeviceInfo, bool val)
        {
            bool ok = false;
            string sql = "";
            try
            {
                //事务的写法
                //https://www.donet5.com/home/Doc?typeId=1183
                //var db = base.AsSugarClient();
                //var result = db.Ado.UseTran(() =>
                //{
                //    var beginCount = db.Queryable<Order>().ToList();
                //    db.Ado.ExecuteCommand("delete Order");
                //    var endCount = db.Queryable<Order>().Count();
                //});
                ////当然你也可以在这里面处理错误
                //if (!result.IsSuccess)
                //{
                //    throw result.ErrorException;
                //}
                string tag_id = tag.id;
                string device_id = tag.device_id;
                string StationNo = DeviceInfo.station_no;
                base.AsTenant().BeginTran();
                string DateStr = GetDateStr;
                if (val)
                {
                    int status = 1;
                    string alarm_type = "1";
                    string alarm_text = string.IsNullOrEmpty(tag.description) ? tag.tag_name.Trim() : tag.description;
                    DateTime now = GetDate;
                    sql = $"update iot_alarmlog set end_time = {DateStr}, duration = {GetDuration("created_at")}, updated_at = {DateStr}  WHERE tag_id = '{tag_id}' AND status =1 AND end_time IS NULL AND duration IS NULL";
                    Execute(sql);
                    //第一种原生的sql
                    sql = string.Format(@"INSERT INTO iot_alarmlog ( device_id, tag_id, statistics_id, status, address, alarm_text, 
                                        start_time, created_at, created_by, alarm_type, station_no ) 
                                          VALUES ( '{0}','{1}',{2},{3},'{4}','{5}', {6}, {6}, 'admin','{7}' , '{8}' )",
                                          tag.device_id, tag_id, 0, status, tag.address, alarm_text, DateStr, alarm_type, StationNo);
                    Execute(sql);
                    ////第二种orm的写法
                    //var alarm = new iot_alarmlog();
                    //alarm.device_id = device_id;
                    //alarm.p_id = p_id;
                    //alarm.statistics_id = 0;
                    //alarm.status = status;
                    //alarm.address = tag.address;
                    //alarm.alarm_text = tag.description;
                    //alarm.start_time = now;
                    //alarm.created_at = now;
                    //alarm.created_by = "admin";
                    //alarm.alarm_type = alarm_type;
                    //alarm.station_no = StationNo;
                    //Insert(alarm);
                    //db.Insertable<iot_alarmlog>(alarm).ExecuteCommandIdentityIntoEntity();
                    sql = $"update iot_station set status_alarm = {status}, updated_at = {DateStr}  WHERE company_id = '{DeviceInfo.company_id}' AND plant_id = '{DeviceInfo.plant_id}' AND line = '{DeviceInfo.line}' AND station_no = '{StationNo}'";
                    Execute(sql);
                }
                else
                {
                    //一个工位只能定义一个device_id，否则无法准确计算该工位是否有故障
                    sql = $"SELECT id, value FROM iot_tag WHERE device_id = '{device_id}' and (address <> '' or address is not null)";
                    DataTable dt = GetDataTable(sql);
                    string[] statusList = new string[dt.Rows.Count];
                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        statusList[i] = dt.Rows[i]["value"]?.ToString() ?? "";
                    }
                    int hasAlarm = HasAlarm(statusList);
                    //更新工位状态--是否有故障
                    sql = $"update iot_station set status_alarm = {hasAlarm}, updated_at = {DateStr}  WHERE company_id = '{DeviceInfo.company_id}' AND plant_id = '{DeviceInfo.plant_id}' AND line = '{DeviceInfo.line}' AND station_no = '{StationNo}'";
                    Execute(sql);
                    //计算结束时间和时长
                    sql = $"update iot_alarmlog set status = 0, end_time = {DateStr}, duration = {GetDuration("created_at")}, updated_at = {DateStr}  WHERE tag_id = '{tag_id}' AND status = 1 AND end_time IS NULL AND duration IS NULL";
                    Execute(sql);
                    //用orm的写法太麻烦，回归到sql语句的写法
                    //List<iot_alarmlog> AlarmList = db.Queryable<iot_alarmlog>()
                    //    .Where(i => i.p_id == p_id && i.status == 1 && i.station_no == StationNo && i.duration == null && i.end_time == null)
                    //    .ToList();
                    //foreach (var item in AlarmList)
                    //{
                    //    double duration = 0;
                    //    //在此处需要手动用c#计算报警时长--如果后期采集数量巨大，可以在C#中计算，减少数据库的压力
                    //    //GetDuration("created_at");
                    //    db.Updateable<iot_alarmlog>()
                    //        .SetColumns(it => new iot_alarmlog() { status = 0, end_time = SqlFunc.GetDate(), duration = duration, updated_at = SqlFunc.GetDate() })
                    //        .Where(i => i.id == item.id)
                    //        .ExecuteCommand();
                    //}
                }
                base.AsTenant().CommitTran();
                ok = true;
            }
            catch (Exception ex)
            {
                base.AsTenant().RollbackTran();
                LogKit.WriteLogs("记录报警日志异常:" + ex.Message);
                throw;
            }
            return ok;
        }

        /// <summary>
        /// 复位某个工位的所有报警
        /// </summary>
        /// <param name="p"></param>
        /// <param name="station_no"></param>
        /// <param name="device_id"></param>
        /// <returns></returns>
        public bool ResetAlarmByStation(pms_public p, string StationNo, string device_id)
        {
            bool res = false;
            string sql = string.Empty;
            base.AsTenant().BeginTran();
            string DateStr = GetDateStr;
            try
            {
                sql = $"UPDATE iot_tag SET value='0', updated_at = {DateStr} WHERE device_id = '{device_id}' and info_type='ALARM' and (address <> '' or address is not null)";
                Execute(sql);
                //更新工位状态--无故障
                sql = $"update iot_station set status_alarm = 0, updated_at = {DateStr}  WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' AND station_no = '{StationNo}'";
                Execute(sql);
                //计算结束时间和时长
                sql = $"update iot_alarmlog set status = 0, end_time = {DateStr}, duration = {GetDuration("created_at")}, updated_at = {DateStr}  WHERE device_id = '{device_id}' AND status = 1 AND end_time IS NULL AND duration IS NULL";
                Execute(sql);

                base.AsTenant().CommitTran();
                res = true;
            }
            catch (Exception ex)
            {
                base.AsTenant().RollbackTran();
                throw;
            }
            return res;
        }

        /// <summary>
        /// 判断该工位是否有报警
        /// </summary>
        /// <param name="statusList"></param>
        /// <returns></returns>
        public int HasAlarm(string[] statusList)
        {
            int has = 1;
            for (int i = 0; i < statusList.Length; i++)
            {
                if (statusList[i] == "1") return 1;
            }
            has = 0;
            return has;
        }
    }
}
