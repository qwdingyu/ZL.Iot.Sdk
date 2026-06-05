using SqlSugar;
using System.Collections.Generic;
using ZL.DB.Acc;

namespace ZL.Dao.IotDevice
{
    public class IotExeValDao : Repository<iot_exeval>
    {
        /// <summary>
        /// 获取所有的补充值
        /// </summary>
        /// <param name="enable"></param>
        /// <returns></returns>
        public List<iot_exeval> GetList(int enable = -1)
        {
            List<iot_exeval> list = new List<iot_exeval>();
            if (enable == -1)
                list = db.Queryable<iot_exeval>().OrderBy(x => x.id).ToList();
            else
                list = db.Queryable<iot_exeval>().Where(x => x.enable == enable).OrderBy(x => x.id).ToList();
            return list;
        }
        public List<iot_exeval> GetListByTagId(string tagId)
        {
            List<iot_exeval> list = new List<iot_exeval>();
            list = db.Queryable<iot_exeval>().Where(x => x.p_id == tagId && x.enable == 1).ToList();
            return list;
        }

        public List<iot_exeval> GetListByDeviceId(string deviceId)
        {
            List<iot_exeval> list = new List<iot_exeval>();
            list = db.Queryable<iot_exeval>()
                .Where(x => x.enable == 1)
                .Where(a => SqlFunc.Subqueryable<iot_tag>().Where(b => b.device_id == deviceId && b.id == a.p_id && b.tag_type == "M").Any())
                .ToList();
            return list;
        }
    }
}
