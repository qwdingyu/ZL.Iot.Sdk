using System.Collections.Generic;
using ZL.DB.Acc;
using ZL.Model;
using ZL.PFLite;

namespace ZL.Dao.IotDevice
{
    public class IotStationService : Repository<iot_station>
    {
        public List<iot_station> getList(pms_public p)
        {
            return base.AsQueryable().Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line).ToList();
        }


        public iot_station GetByStation(pms_station_no p)
        {
            iot_station one = new iot_station();
            one = base.AsQueryable().Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.station_no == p.station_no).First();

            return one;
        }

        /// <summary>
        /// 获取是否有报警
        /// </summary>
        /// <param name="station_no"></param>
        /// <returns></returns>
        public bool GetStationHasAlarm(pms_station_no p)
        {
            bool res = false;
            iot_station one = new iot_station();
            one = base.AsQueryable()
                .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.station_no == p.station_no)
                .First();
            if (one.status_alarm == 1)
                res = true;
            else
                res = false;
            return res;
        }

    }
}
