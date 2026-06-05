using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Dao.IotDevice.Interfaces
{
    /// <summary>
    /// IoT 业务详情DAO接口 - 提供业务配置详细数据的查询操作
    /// </summary>
    public interface IIotBizDetailDao
    {
        #region 同步方法

        /// <summary>
        /// 获取所有业务详情配置
        /// </summary>
        /// <param name="enable">启用状态：-1表示全部，0表示禁用，1表示启用</param>
        List<iot_biz_detail> GetList(int enable = -1);

        /// <summary>
        /// 根据业务代码获取执行定义列表
        /// </summary>
        List<iot_biz_detail> GetListByCode(string biz_code);

        #endregion

        #region 异步方法

        /// <summary>
        /// 异步获取所有业务详情配置
        /// </summary>
        /// <param name="enable">启用状态：-1表示全部，0表示禁用，1表示启用</param>
        Task<List<iot_biz_detail>> GetListAsync(int enable = -1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据业务代码获取执行定义列表
        /// </summary>
        Task<List<iot_biz_detail>> GetListByCodeAsync(string biz_code, CancellationToken cancellationToken = default);

        #endregion
    }
}
