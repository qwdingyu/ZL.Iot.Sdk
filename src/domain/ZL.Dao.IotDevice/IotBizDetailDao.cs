using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Dao.IotDevice.Interfaces;
using ZL.DB.Acc;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// IoT 业务详情DAO - 提供业务配置详细数据的查询操作
    /// </summary>
    public class IotBizDetailDao : Repository<iot_biz_detail>, IIotBizDetailDao
    {
        /// <summary>
        /// 获取所有的业务模型详细定义
        /// </summary>
        /// <param name="enable"></param>
        /// <returns></returns>
        public List<iot_biz_detail> GetList(int enable = -1)
        {
            List<iot_biz_detail> list = new List<iot_biz_detail>();
            if (enable == -1)
                list = db.Queryable<iot_biz_detail>().OrderBy(x => x.id).ToList();
            else
                list = db.Queryable<iot_biz_detail>().Where(x => x.enable == enable).OrderBy(x => x.id).ToList();
            return list;
        }
        /// <summary>
        /// 根据业务代码获取详细的执行定义
        /// </summary>
        /// <param name="biz_code"></param>
        /// <returns></returns>
        public List<iot_biz_detail> GetListByCode(string biz_code)
        {
            List<iot_biz_detail> list = new List<iot_biz_detail>();
            list = db.Queryable<iot_biz_detail>().Where(x => x.biz_code == biz_code).OrderBy(x => x.id).ToList();
            return list;
        }

        #region 异步方法

        /// <summary>
        /// 异步获取所有业务详情配置
        /// </summary>
        public async Task<List<iot_biz_detail>> GetListAsync(int enable = -1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetList(enable), cancellationToken);
        }

        /// <summary>
        /// 异步根据业务代码获取执行定义列表
        /// </summary>
        public async Task<List<iot_biz_detail>> GetListByCodeAsync(string biz_code, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetListByCode(biz_code), cancellationToken);
        }

        #endregion
    }
}
