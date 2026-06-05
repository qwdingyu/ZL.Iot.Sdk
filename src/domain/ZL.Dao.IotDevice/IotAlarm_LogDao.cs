using System.Collections.Generic;
using ZL.DB.Acc;
using ZL.Model;
using ZL.PFLite;

namespace ZL.Dao.IotDevice
{
    public class IotAlarm_LogDao : Repository<iot_alarm_log>
    {
        public List<iot_alarm_log> Query(pms_station_no p, string start, string end)
        {
            string sql = $"select * from iot_alarm_log where company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' AND station_no='{p.station_no}' and created_at between '{start}' and '{end}' order by created_at desc";
            return GetList<iot_alarm_log>(sql);
        }
    }
}
