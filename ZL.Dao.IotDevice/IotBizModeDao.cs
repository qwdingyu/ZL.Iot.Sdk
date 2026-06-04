using SqlSugar;
using System.Collections.Generic;
using ZL.DB.Acc;

namespace ZL.Dao.IotDevice
{
    public class IotBizModeDao : Repository<iot_biz_mode>
    {
        public List<iot_biz_mode> GetList(int enable = -1)
        {
            List<iot_biz_mode> list = new List<iot_biz_mode>();
            if (enable == -1)
                list = db.Queryable<iot_biz_mode>().OrderBy(x => x.id).ToList();
            else
                list = db.Queryable<iot_biz_mode>().Where(x => x.enable == enable).OrderBy(x => x.id).ToList();
            return list;
        }
    }
}
