using System.Collections.Generic;
using ZL.DB.Acc;

namespace ZL.Dao.IotDevice
{
    public class IotBizDefDao : Repository<iot_biz_def>
    {
        /// <summary>
        /// 获取所有的业务模型定义
        /// </summary>
        /// <param name="enable"></param>
        /// <returns></returns>
        public List<iot_biz_def> GetList(int enable = -1)
        {
            List<iot_biz_def> list = new List<iot_biz_def>();
            if (enable == -1)
                list = db.Queryable<iot_biz_def>().OrderBy(x => x.id).ToList();
            else
                list = db.Queryable<iot_biz_def>().Where(x => x.enable == enable).OrderBy(x => x.id).ToList();
            return list;
        }
        /// <summary>
        /// 根据业务代码获取定义
        /// </summary>
        /// <param name="biz_code"></param>
        /// <returns></returns>
        public iot_biz_def GetByCode(string biz_code)
        {
            iot_biz_def one = new iot_biz_def();
            one = db.Queryable<iot_biz_def>().Where(x => x.biz_code == biz_code).OrderBy(x => x.id).First();
            return one;
        }
    }
}
