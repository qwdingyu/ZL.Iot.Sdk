using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlSugar;
using ZL.PFLite;
using ZL.PFLite.Common;

namespace ZL.Dao.IotDevice.Interfaces
{
    /// <summary>
    /// IoT 标签服务接口 - 提供标签数据的增删改查操作
    /// </summary>
    public interface IIotTagService
    {
        #region 同步方法

        /// <summary>
        /// 根据标签ID获取标签
        /// </summary>
        iot_tag GetByTagId(string Id);

        /// <summary>
        /// 根据设备ID获取标签列表
        /// </summary>
        List<iot_tag> GetTagListByDeviceId(string DeviceId);

        /// <summary>
        /// 条件查询标签列表
        /// </summary>
        List<iot_tag> QueryList(List<IConditionalModel> cList);

        /// <summary>
        /// 根据工位号获取标签列表
        /// </summary>
        List<iot_tag> GetTagListByStationNo(pms_public p, string StationNo, int purpose = -1);

        /// <summary>
        /// 更新标签值
        /// </summary>
        void UpdateTagValue(string id, string value);

        /// <summary>
        /// 获取所有设备标签
        /// </summary>
        List<iot_tag> GetAllDeviceTag(pms_public p, int purpose = -1);

        /// <summary>
        /// 根据边缘ID获取所有设备标签
        /// </summary>
        List<iot_tag> GetAllDeviceTagByEdgeId(string edgeId, int purpose = 1);

        /// <summary>
        /// 根据设备ID获取所有设备标签
        /// </summary>
        List<iot_tag> GetAllDeviceTagByDeviceId(string deviceId);

        /// <summary>
        /// 查询监控地址相关联的配方下发地址（tag_type='W', set_type IN ('I','P')）
        /// </summary>
        List<IotPfDto> GetPFNullList(string device_id, string pid);

        /// <summary>
        /// 根据SN查询配方参数
        /// </summary>
        List<IotPfDto> GetPFBySn(string device_id, string pid, pms_public p, string sn);

        #endregion

        #region 异步方法

        /// <summary>
        /// 异步根据标签ID获取标签
        /// </summary>
        Task<iot_tag> GetByTagIdAsync(string Id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据设备ID获取标签列表
        /// </summary>
        Task<List<iot_tag>> GetTagListByDeviceIdAsync(string DeviceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步条件查询标签列表
        /// </summary>
        Task<List<iot_tag>> QueryListAsync(List<IConditionalModel> cList, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据工位号获取标签列表
        /// </summary>
        Task<List<iot_tag>> GetTagListByStationNoAsync(pms_public p, string StationNo, int purpose = -1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步更新标签值
        /// </summary>
        Task UpdateTagValueAsync(string id, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步获取所有设备标签
        /// </summary>
        Task<List<iot_tag>> GetAllDeviceTagAsync(pms_public p, int purpose = -1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据边缘ID获取所有设备标签
        /// </summary>
        Task<List<iot_tag>> GetAllDeviceTagByEdgeIdAsync(string edgeId, int purpose = 1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据设备ID获取所有设备标签
        /// </summary>
        Task<List<iot_tag>> GetAllDeviceTagByDeviceIdAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步查询监控地址相关联的配方下发地址
        /// </summary>
        Task<List<IotPfDto>> GetPFNullListAsync(string device_id, string pid, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据SN查询配方参数
        /// </summary>
        Task<List<IotPfDto>> GetPFBySnAsync(string device_id, string pid, pms_public p, string sn, CancellationToken cancellationToken = default);

        #endregion
    }
}
