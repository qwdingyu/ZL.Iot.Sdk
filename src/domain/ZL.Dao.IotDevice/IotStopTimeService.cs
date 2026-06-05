using System;
using System.Data;
using System.Linq;
using ZL.DB.Acc;
using ZL.PFLite;
using ZL.PFLite.Common;

namespace ZL.Dao.IotDevice
{
    public class IotStopTimeService : Repository<iot_stop_time>
    {
        public bool Upsert(iot_tag tag, IotDeviceDriverDto DeviceInfo, bool stop)
        {
            bool ok = false;
            string sql = "";
            try
            {
                string tag_id = tag.id;
                string device_id = tag.device_id;
                string company_id = DeviceInfo.company_id;
                string plant_id = DeviceInfo.plant_id;
                string line = DeviceInfo.line;
                string StationNo = DeviceInfo.station_no;
                base.AsTenant().BeginTran();
                string DateStr = GetDateStr;
                if (stop)
                {
                    byte status = 1;
                    string alarm_text = string.IsNullOrEmpty(tag.description) ? tag.tag_name.Trim() : tag.description;
                    DateTime now = GetDate;
                    sql = $"update iot_stop_time set end_time = {DateStr}, duration = {GetDuration("created_at")}, updated_at = {DateStr}  WHERE tag_id = '{tag_id}' AND status =1 AND end_time IS NULL ";
                    Execute(sql);

                    var st = new iot_stop_time();
                    st.company_id = company_id;
                    st.plant_id = plant_id;
                    st.line = line;
                    st.station_no = StationNo;
                    st.device_id = device_id;
                    st.tag_id = tag_id;
                    st.status = status;
                    st.remark = tag.address;
                    st.start_time = now;
                    st.created_at = now;
                    st.created_by = "admin";
                    Insert(st);
                    db.Insertable<iot_stop_time>(st).ExecuteCommandIdentityIntoEntity();
                    sql = $"update iot_station set status_alarm = {status}, updated_at = {DateStr}  WHERE company_id = '{company_id}' AND plant_id = '{plant_id}' AND line = '{line}' AND station_no = '{StationNo}'";
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
                    sql = $"update iot_station set status_alarm = {hasAlarm}, updated_at = {DateStr}  WHERE company_id = '{company_id}' AND plant_id = '{plant_id}' AND line = '{line}' AND station_no = '{StationNo}'";
                    Execute(sql);
                    //计算结束时间和时长
                    sql = $"update iot_stop_time set status = 0, end_time = {DateStr}, duration = {GetDuration("created_at")}, updated_at = {DateStr}  WHERE tag_id = '{tag_id}' AND status = 1 AND end_time IS NULL AND duration IS NULL";
                    Execute(sql);
                    //用orm的写法太麻烦，回归到sql语句的写法
                    //List<iot_stop_time> AlarmList = db.Queryable<iot_stop_time>()
                    //    .Where(i => i.p_id == p_id && i.status == 1 && i.station_no == StationNo && i.duration == null && i.end_time == null)
                    //    .ToList();
                    //foreach (var item in AlarmList)
                    //{
                    //    double duration = 0;
                    //    //在此处需要手动用c#计算报警时长--如果后期采集数量巨大，可以在C#中计算，减少数据库的压力
                    //    //GetDuration("created_at");
                    //    db.Updateable<iot_stop_time>()
                    //        .SetColumns(it => new iot_stop_time() { status = 0, end_time = SqlFunc.GetDate(), duration = duration, updated_at = SqlFunc.GetDate() })
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
                LogKit.WriteLogs("记录iot_stop_time信息异常:" + ex.Message);
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
                sql = $"update iot_stop_time set status = 0, end_time = {DateStr}, duration = {GetDuration("created_at")}, updated_at = {DateStr}  WHERE device_id = '{device_id}' AND status = 1 AND end_time IS NULL AND duration IS NULL";
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
