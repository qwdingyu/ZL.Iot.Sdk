using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using SqlSugar;
using ZL.PFLite;
using ZL.PFLite.Common;

namespace ZL.Dao.IotDevice.Interfaces
{
    /// <summary>
    /// IoT 设备服务接口 - 提供设备数据的增删改查操作
    /// </summary>
    public interface IIotDeviceService
    {
        #region 同步方法

        /// <summary>
        /// 插入设备记录（自动生成ID）
        /// </summary>
        bool Insert(iot_device obj);

        /// <summary>
        /// 根据ID删除设备
        /// </summary>
        bool DeleteById(string id);

        /// <summary>
        /// 根据ID获取设备
        /// </summary>
        iot_device GetById(string id);

        /// <summary>
        /// 根据工位号获取设备
        /// </summary>
        iot_device GetByStationNo(pms_public p, string StationNo, byte Purpose = 1);

        /// <summary>
        /// 获取设备列表
        /// </summary>
        List<iot_device> GetList(pms_public p, byte IsActive = 1, byte Purpose = 1);

        /// <summary>
        /// 获取设备驱动列表
        /// </summary>
        /// <param name="purpose">-1用于边缘计算网关(不区分purpose)，0报警采集，1设备交互</param>
        List<IotDeviceDriverDto> GetDeviceDriverList(pms_public p, bool debug, int purpose = -1);

        /// <summary>
        /// 根据边缘ID获取设备驱动列表
        /// </summary>
        List<IotDeviceDriverDto> GetDeviceDriverListByEdgeId(string edgeId, bool debug = false, int purpose = -1);

        /// <summary>
        /// 获取设备驱动列表（ORM写法，效率较慢）
        /// </summary>
        List<IotDeviceDriverDto> GetDeviceDriverList2(pms_public p, int purpose);

        /// <summary>
        /// 根据工位号获取设备驱动列表
        /// </summary>
        List<IotDeviceDriverDto> GetDeviceDriverListByStation(pms_public p, string StationNo, bool debug = false);

        /// <summary>
        /// 根据IP地址获取设备驱动列表
        /// </summary>
        List<IotDeviceDriverDto> GetDeviceDriverListByIp(pms_public p, string ip);

        /// <summary>
        /// 根据设备ID获取设备驱动列表
        /// </summary>
        List<IotDeviceDriverDto> GetDeviceDriverListById(string device_id, bool debug = false);

        /// <summary>
        /// 根据设备ID获取驱动信息
        /// </summary>
        IotDeviceDriverDto GetDriverByDeviceId(string device_id, bool debug = false);

        /// <summary>
        /// 分页查询设备
        /// </summary>
        List<iot_device> GetPage(Expression<Func<iot_device, bool>> where, int pagesize = 1, int pageindex = 20);

        /// <summary>
        /// 根据JSON条件查询设备
        /// </summary>
        List<iot_device> GetOrderByJson(string Json);

        #endregion

        #region 异步方法

        /// <summary>
        /// 异步插入设备记录（自动生成ID）
        /// </summary>
        Task<bool> InsertAsync(iot_device obj, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据ID删除设备
        /// </summary>
        Task<bool> DeleteByIdAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据ID获取设备
        /// </summary>
        Task<iot_device> GetByIdAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据工位号获取设备
        /// </summary>
        Task<iot_device> GetByStationNoAsync(pms_public p, string StationNo, byte Purpose = 1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步获取设备列表
        /// </summary>
        Task<List<iot_device>> GetListAsync(pms_public p, byte IsActive = 1, byte Purpose = 1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步获取设备驱动列表
        /// </summary>
        /// <param name="purpose">-1用于边缘计算网关(不区分purpose)，0报警采集，1设备交互</param>
        Task<List<IotDeviceDriverDto>> GetDeviceDriverListAsync(pms_public p, bool debug, int purpose = -1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据边缘ID获取设备驱动列表
        /// </summary>
        Task<List<IotDeviceDriverDto>> GetDeviceDriverListByEdgeIdAsync(string edgeId, bool debug = false, int purpose = -1, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据工位号获取设备驱动列表
        /// </summary>
        Task<List<IotDeviceDriverDto>> GetDeviceDriverListByStationAsync(pms_public p, string StationNo, bool debug = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据IP地址获取设备驱动列表
        /// </summary>
        Task<List<IotDeviceDriverDto>> GetDeviceDriverListByIpAsync(pms_public p, string ip, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步根据设备ID获取驱动信息
        /// </summary>
        Task<IotDeviceDriverDto> GetDriverByDeviceIdAsync(string device_id, bool debug = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步分页查询设备
        /// </summary>
        Task<List<iot_device>> GetPageAsync(Expression<Func<iot_device, bool>> where, int pagesize = 1, int pageindex = 20, CancellationToken cancellationToken = default);

        #endregion
    }
}
