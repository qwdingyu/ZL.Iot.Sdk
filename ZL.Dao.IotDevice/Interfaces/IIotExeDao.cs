using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Dao.IotDevice.Interfaces
{
    /// <summary>
    /// IoT 执行器DAO接口 - 提供执行器配置数据的查询操作
    /// </summary>
    public interface IIotExeDao
    {
        #region 同步方法

        /// <summary>
        /// 获取所有执行器配置
        /// </summary>
        /// <param name="enable">启用状态：-1表示全部，0表示禁用，1表示启用</param>
        List<iot_exe> GetList(int enable = -1);

        /// <summary>
        /// 根据标签ID获取执行器配置列表
        /// </summary>
        List<iot_exe> GetListByTagId(string tagId);

        /// <summary>
        /// 根据设备ID获取执行器配置列表
        /// </summary>
        List<iot_exe> GetListByDeviceId(string deviceId);

        #endregion

        #region 异步方法

        /// <summary>
        /// 异步获取所有执行器配置
        /// </summary>
        /// <param name="enable">启用状态：-1表示全部，0表示禁用，1表示启用</param>
        Task<List<iot_exe>> GetListAsync(int enable = -1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据标签ID获取执行器配置列表
        /// </summary>
        Task<List<iot_exe>> GetListByTagIdAsync(string tagId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据设备ID获取执行器配置列表
        /// </summary>
        Task<List<iot_exe>> GetListByDeviceIdAsync(string deviceId, CancellationToken cancellationToken = default);

        #endregion
    }
}
