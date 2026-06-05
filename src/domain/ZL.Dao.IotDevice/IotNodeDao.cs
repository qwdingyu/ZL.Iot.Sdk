using System;
using System.Collections.Generic;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.IotDevice
{
    public class IotNodeDao : Repository<iot_node>
    {
        public List<iot_node> getList(pms_public p)
        {
            return base.AsQueryable().Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id
            && it.line == p.line).ToList();
        }


        public iot_node GetByStation(pms_public p, string station_no)
        {
            iot_node one = new iot_node();
            one = base.AsQueryable().Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id
            && it.line == p.line && it.station_no == station_no).First();

            return one;
        }
        public bool isExist(iot_node one)
        {
            try
            {
                return db.Queryable<iot_node>()
                    .Any(it => it.company_id == one.company_id && it.plant_id == one.plant_id
                        && it.line == one.line && it.station_no == one.station_no);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public void Upsert(iot_node obj)
        {
            try
            {
                if (isExist(obj))
                {
                    db.Updateable<iot_node>(obj).ExecuteCommand();
                }
                else
                    db.Insertable<iot_node>(obj).ExecuteCommand();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public void DeleteId(int id)
        {
            try
            {
                db.Deleteable<iot_node>().Where(it => it.id == id).ExecuteCommand();
            }
            catch (Exception ex)
            { }
        }
        /// <summary>
        /// 更新节点是否在线
        /// </summary>
        /// <param name="id"></param>
        /// <param name="is_online"></param>
        public void UpdateOnline(int id, int is_online)
        {
            db.Updateable<iot_node>()
                .SetColumns(it => new iot_node
                {
                    is_online = (byte)is_online,
                    update_at = DateTime.Now
                })
                .Where(it => it.id == id)
                .ExecuteCommand();
        }
        /// <summary>
        /// 更新节点是否在线
        /// </summary>
        /// <param name="p"></param>
        /// <param name="station_no"></param>
        /// <param name="is_online"></param>
        public void UpdateOnline(pms_public p, string station_no, int is_online)
        {
            db.Updateable<iot_node>()
                .SetColumns(it => new iot_node
                {
                    is_online = (byte)is_online,
                    update_at = DateTime.Now
                })
                .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id
                        && it.line == p.line && it.station_no == station_no)
                .ExecuteCommand();
        }
    }
}
