using System;
using System.Collections.Generic;
using ZL.DB.Acc;
using ZL.PFLite.Common;

namespace ZL.Dao.IotDevice
{
    public class IotGroupService : Repository<iot_group>
    {
        public List<iot_group> GetGroupList()
        {
            List<iot_group> list = new List<iot_group>();
            try
            {
                list = base.GetList(it => it.is_active == 1);
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotGroupService.GetGroup异常：" + ex.Message, Config.LogFile);
                throw ex;
            }
            return list;
        }

        //public List<iot_group> GetGroupList(string device_id)
        //{
        //    List<iot_group> list = new List<iot_group>();
        //    try
        //    {
        //        list = base.GetList(it => it.device_id == device_id && it.is_active == 1);
        //    }
        //    catch (Exception ex)
        //    {
        //        LogKit.WriteLogs("IotGroupService.GetGroup异常：" + ex.Message, Config.LogFile);
        //        throw ex;
        //    }
        //    return list;
        //}
    }
}
