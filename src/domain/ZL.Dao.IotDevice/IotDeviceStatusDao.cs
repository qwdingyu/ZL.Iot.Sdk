using SqlSugar;
using System;
using System.Collections.Generic;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.IotDevice
{
    public class IotDeviceStatusDao : Repository<iot_device_stauts>
    {
        private object sync = new object();
        /// <summary>
        /// 获取所有的业务模型定义
        /// </summary>
        /// <param name="enable"></param>
        /// <returns></returns>
        public List<iot_device_stauts> GetList(pms_station_no ps)
        {
            List<iot_device_stauts> list = new List<iot_device_stauts>();
            list = db.Queryable<iot_device_stauts>()
                .Where(x => x.company_id == ps.company_id && x.plant_id == ps.plant_id && x.line == ps.line && x.station_no == ps.station_no)
                .OrderBy(x => x.id).ToList();
            return list;
        }
        public bool isExist(iot_device_stauts one)
        {
            try
            {
                return db.Queryable<iot_device_stauts>()
                    .Any(it => it.company_id == one.company_id && it.plant_id == one.plant_id && it.line == one.line && it.station_no == one.station_no);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public bool isExist(int id, out iot_device_stauts one)
        {
            try
            {
                one = db.Queryable<iot_device_stauts>()
                    .Where(it => it.id == id)
                    .First();
                if (one == null)
                    return false;
                else return true;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public bool isExist(pms_station_no ps, out iot_device_stauts one)
        {
            try
            {
                one = db.Queryable<iot_device_stauts>()
                    .Where(it => it.company_id == ps.company_id && it.plant_id == ps.plant_id && it.line == ps.line && it.station_no == ps.station_no)
                    .First();
                if (one == null)
                    return false;
                else return true;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public bool Upsert(iot_device_stauts it)
        {
            bool ok = false;
            try
            {
                if (isExist(it))
                {
                    it.updated_at = DateTime.Now;
                    db.Updateable<iot_device_stauts>(it).ExecuteCommand();
                }
                else
                    db.Insertable<iot_device_stauts>(it).ExecuteCommand();
                ok = true;
            }
            catch (Exception)
            {
                throw;
            }
            return ok;
        }
        public bool Upsert(pms_station_no ps, string deviceId, string deviceName, string mac_code, string mac_stauts)
        {
            lock (sync)
            {
                bool ok = false;
                try
                {
                    iot_device_stauts one = null;
                    bool has = isExist(ps, out one);
                    if (has)
                    {
                        one.mac_code = mac_code;
                        one.mac_stauts = mac_stauts;
                        one.updated_at = DateTime.Now;
                        db.Updateable<iot_device_stauts>(one).ExecuteCommand();
                    }
                    else
                    {
                        one = new iot_device_stauts
                        {
                            device_id = deviceId,
                            device_name = deviceName,
                            company_id = ps.company_id,
                            plant_id = ps.plant_id,
                            line = ps.line,
                            station_no = ps.station_no,
                            mac_code = mac_code,
                            mac_stauts = mac_stauts,
                            created_at = DateTime.Now,
                            updated_at = DateTime.Now,
                        };
                        db.Insertable<iot_device_stauts>(one).ExecuteCommand();
                    }
                    ok = true;
                }
                catch (Exception)
                {
                    throw;
                }
                return ok;
            }
        }
        public bool UpdateConnStatus(string deviceId, string conn_code, string conn_stauts)
        {
            bool ok = false;
            try
            {
                //根据deviceid查询对应的信息， 因为存在一个设备管理多个工位的情况
                db.Updateable<iot_device_stauts>()
                    .SetColumns(it => new iot_device_stauts() { conn_code = conn_code, conn_stauts = conn_stauts })
                    .Where(it => it.device_id == deviceId)
                    .ExecuteCommand();
                ok = true;
            }
            catch (Exception)
            {
                throw;
            }
            return ok;
        }
    }
}
