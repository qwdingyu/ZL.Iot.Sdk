using SqlSugar;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Dao.IotDevice.Interfaces;
using ZL.DB.Acc;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// IoT 执行器DAO - 提供执行器配置数据的查询操作
    /// </summary>
    public class IotExeDao : Repository<iot_exe>, IIotExeDao
    {
        /// <summary>
        /// 获取所有的业务模型定义
        /// </summary>
        /// <param name="enable"></param>
        /// <returns></returns>
        public List<iot_exe> GetList(int enable = -1)
        {
            List<iot_exe> list = new List<iot_exe>();
            if (enable == -1)
                list = db.Queryable<iot_exe>().OrderBy(x => x.id).ToList();
            else
                list = db.Queryable<iot_exe>().Where(x => x.enable == enable).OrderBy(x => x.id).ToList();
            return list;
        }
        public List<iot_exe> GetListByTagId(string tagId)
        {
            List<iot_exe> list = new List<iot_exe>();
            list = db.Queryable<iot_exe>().Where(x => x.tag_id == tagId).ToList();
            return list;
        }
        public List<iot_exe> GetListByDeviceId(string deviceId)
        {
            List<iot_exe> list = new List<iot_exe>();
            try
            {
                list = db.Queryable<iot_exe>()
                        .Where(a => SqlFunc.Subqueryable<iot_tag>().Where(b => b.device_id == deviceId && b.id == a.tag_id && b.tag_type == "M").Any())
                        .ToList();
            }
            catch (System.Exception ex)
            {
                string err = ex.Message;
                throw;
            }
            return list;
        }

        #region 异步方法

        /// <summary>
        /// 异步获取所有执行器配置
        /// </summary>
        public async Task<List<iot_exe>> GetListAsync(int enable = -1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetList(enable), cancellationToken);
        }

        /// <summary>
        /// 异步根据标签ID获取执行器配置列表
        /// </summary>
        public async Task<List<iot_exe>> GetListByTagIdAsync(string tagId, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetListByTagId(tagId), cancellationToken);
        }

        /// <summary>
        /// 异步根据设备ID获取执行器配置列表
        /// </summary>
        public async Task<List<iot_exe>> GetListByDeviceIdAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetListByDeviceId(deviceId), cancellationToken);
        }

        #endregion
    }
}
