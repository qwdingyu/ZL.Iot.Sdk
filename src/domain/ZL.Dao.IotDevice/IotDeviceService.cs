using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using SqlSugar;
using ZL.Dao.IotDevice;
using ZL.Dao.IotDevice.Interfaces;
using ZL.DB.Acc;
using ZL.PFLite;
using ZL.PFLite.Common;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// IoT 设备服务 - 提供设备数据的增删改查操作
    /// </summary>
    public class IotDeviceService : Repository<iot_device>, IIotDeviceService
    {
        public override bool Insert(iot_device obj)
        {
            if (obj.id == null) obj.id = Guid.NewGuid().ToString();
            return base.Insert(obj);
        }
        public bool DeleteById(string id)
        {
            return base.DeleteById(id);
        }
        public iot_device GetById(string id)
        {
            return base.GetSingle(it => it.id == id);
        }
        public iot_device GetByStationNo(pms_public p, string StationNo, byte Purpose = 1)
        {
            return base.GetSingle(
                it =>
                it.is_active == 1 &&
                it.purpose == Purpose &&
                it.company_id == p.company_id &&
                it.plant_id == p.plant_id &&
                it.line == p.line &&
                it.station_no == StationNo
            );
        }
        //public iot_device GetByTagId(string tagId)
        //{
        //    return base.GetSingle(
        //        it =>
        //        it.is_active == 1 &&
        //        it.purpose == Purpose &&
        //        it.company_id == p.company_id &&
        //        it.plant_id ==  p.plant_id &&
        //        it.line ==  p.line &&
        //        it.station_no == Config.StationNo
        //    );
        //}

        public List<iot_device> GetList(pms_public p, byte IsActive = 1, byte Purpose = 1)
        {
            return base.GetList(
                it =>
                it.is_active == IsActive &&
                it.purpose == Purpose &&
                it.company_id == p.company_id &&
                it.plant_id == p.plant_id &&
                it.line == p.line
            );
        }
        /// <summary>
        /// 获取设备对应的驱动
        /// </summary>
        /// <param name="purpose">-1用于边缘计算网关(不区分purpose)，0报警采集， 1设备交互</param>
        /// <returns></returns>
        public List<IotDeviceDriverDto> GetDeviceDriverList(pms_public p, bool debug, int purpose = -1)
        {
            List<IotDeviceDriverDto> list = new List<IotDeviceDriverDto>();
            try
            {
                const string sqlBase = @"SELECT
                                        a.company_id,
                                        a.plant_id,
                                        a.line,
                                        a.region_no,
                                        a.station_no,
                                     a.driver_id,
                                     a.id device_id,
                                     a.device_type_id,
                                     a.device_name,
                                        a.class_name as device_class_name,
                                        a.assembly_name as device_assembly_name,
                                     a.address as ip,
                                     a.time_out,
                                     b.assembly_name as driver_assembly_name,
                                     b.class_full_name as driver_full_class_name,
                                     b.class_name as driver_class_name,
                                        c.brand,
                                        a.purpose
                                    FROM
                                     iot_device a
                                     JOIN iot_driver b ON a.driver_id = b.id
                                        JOIN iot_device_type c ON a.device_type_id = c.id
                                    WHERE
                                     a.is_active = 1
                                        AND a.company_id=@company_id
                                        AND a.plant_id=@plant_id
                                        AND a.line=@line";

                string sql;
                var pars = new System.Collections.Generic.List<SugarParameter>
                {
                    new SugarParameter("@company_id", p.company_id),
                    new SugarParameter("@plant_id",   p.plant_id),
                    new SugarParameter("@line",       p.line),
                };

                //-1用于边缘计算网关(不区分purpose)
                if (purpose == -1)
                {
                    sql = sqlBase;
                }
                else if (purpose == 999)
                {
                    sql = sqlBase + " AND a.purpose IN (2, 3)";
                }
                else
                {
                    sql = sqlBase + " AND a.purpose=@purpose";
                    pars.Add(new SugarParameter("@purpose", purpose));
                }

                list = GetList<IotDeviceDriverDto>(sql, pars.ToArray());
                foreach (var it in list)
                {
                    it.debug = debug;
                }
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotDeviceService.GetDeviceDriverList 异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return list;
        }
        /// <summary>
        /// 获取设备对应的驱动
        /// </summary>
        /// <param name="purpose">-1用于边缘计算网关(不区分purpose)，0报警采集， 1设备交互</param>
        /// <returns></returns>
        public List<IotDeviceDriverDto> GetDeviceDriverListByEdgeId(string edgeId, bool debug = false, int purpose = -1)
        {
            List<IotDeviceDriverDto> list = new List<IotDeviceDriverDto>();
            try
            {
                const string sqlBase = @"SELECT
                                        a.company_id,
                                        a.plant_id,
                                        a.line,
                                        a.region_no,
                                        a.station_no,
                                     a.driver_id,
                                     a.id device_id,
                                     a.device_type_id,
                                     a.device_name,
                                        a.class_name as device_class_name,
                                        a.assembly_name as device_assembly_name,
                                     a.address as ip,
                                     a.time_out,
                                     b.assembly_name as driver_assembly_name,
                                     b.class_full_name as driver_full_class_name,
                                     b.class_name as driver_class_name,
                                        c.brand,
                                        a.purpose
                                    FROM
                                     iot_device a
                                     JOIN iot_driver b ON a.driver_id = b.id
                                        JOIN iot_device_type c ON a.device_type_id = c.id
                                        JOIN iot_edge_relation d on a.id = d.device_id
                                    WHERE
                                     a.is_active = 1
                                        AND d.edge_id=@edge_id";

                string sql;
                var pars = new System.Collections.Generic.List<SugarParameter>
                {
                    new SugarParameter("@edge_id", edgeId),
                };

                //-1用于边缘计算网关(不区分purpose)
                if (purpose == -1)
                {
                    sql = sqlBase;
                }
                else if (purpose == 999)
                {
                    sql = sqlBase + " AND a.purpose IN (2, 3)";
                }
                else
                {
                    sql = sqlBase + " AND a.purpose=@purpose";
                    pars.Add(new SugarParameter("@purpose", purpose));
                }

                list = GetList<IotDeviceDriverDto>(sql, pars.ToArray());
                foreach (var it in list)
                {
                    it.debug = debug;
                }
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotDeviceService.GetDeviceDriverListByEdgeId 异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return list;
        }
        /// <summary>
        /// 获取设备对应的驱动--练习orm写法，但是效率比较慢
        /// </summary>
        /// <param name="purpose">-1用于边缘计算网关(不区分purpose)，0报警采集， 1设备交互</param>
        /// <returns></returns>
        public List<IotDeviceDriverDto> GetDeviceDriverList2(pms_public p, int purpose)
        {
            List<IotDeviceDriverDto> list = new List<IotDeviceDriverDto>();
            try
            {
                if (purpose == -1)
                    list = base.AsSugarClient().Queryable<iot_device, iot_driver, iot_device_type>
                        ((a, b, c) => a.driver_id == b.id && a.device_type_id == c.id)
                        .Where(a => a.is_active == 1 && a.company_id == p.company_id && a.plant_id == p.plant_id && a.line == p.line)
                        .Select((a, b, c) => new IotDeviceDriverDto
                        {
                            company_id = a.company_id,
                            plant_id = a.plant_id,
                            line = a.line,
                            region_no = a.region_no,
                            station_no = a.station_no,
                            driver_id = a.driver_id,
                            device_id = a.id,
                            device_type_id = a.device_type_id,
                            device_name = a.device_name,
                            device_class_name = a.class_name,
                            device_assembly_name = a.assembly_name,
                            ip = a.address,
                            time_out = a.time_out,
                            driver_assembly_name = b.assembly_name,
                            driver_full_class_name = b.class_full_name,
                            driver_class_name = b.class_name,
                            brand = c.brand,
                            purpose = a.purpose
                        })
                        .ToList();
                else
                    list = base.AsSugarClient().Queryable<iot_device, iot_driver, iot_device_type>
                        ((a, b, c) => a.driver_id == b.id && a.device_type_id == c.id)
                        .Where(a => a.is_active == 1 && a.purpose == purpose && a.company_id == p.company_id && a.plant_id == p.plant_id && a.line == p.line)
                        .Select((a, b, c) => new IotDeviceDriverDto
                        {
                            company_id = a.company_id,
                            plant_id = a.plant_id,
                            line = a.line,
                            region_no = a.region_no,
                            station_no = a.station_no,
                            driver_id = a.driver_id,
                            device_id = a.id,
                            device_type_id = a.device_type_id,
                            device_name = a.device_name,
                            device_class_name = a.class_name,
                            device_assembly_name = a.assembly_name,
                            ip = a.address,
                            time_out = a.time_out,
                            driver_assembly_name = b.assembly_name,
                            driver_full_class_name = b.class_full_name,
                            driver_class_name = b.class_name,
                            brand = c.brand,
                            purpose = a.purpose
                        })
                        .ToList();
                //foreach (var it in list)
                //{
                //    it.debug = debug;
                //}
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotDeviceService.GetDeviceDriverList2 异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return list;
        }
        public List<IotDeviceDriverDto> GetDeviceDriverListByStation(pms_public p, string StationNo, bool debug = false)
        {
            List<IotDeviceDriverDto> list = new List<IotDeviceDriverDto>();
            try
            {
                const string sql = @"SELECT
                                        a.company_id,
                                        a.plant_id,
                                        a.line,
                                        a.region_no,
                                        a.station_no,
                                     a.driver_id,
                                     a.id device_id,
                                     a.device_type_id,
                                     a.device_name,
                                        a.class_name as device_class_name,
                                        a.assembly_name as device_assembly_name,
                                     a.address as ip,
                                     a.time_out,
                                     b.assembly_name as driver_assembly_name,
                                     b.class_full_name as driver_full_class_name,
                                     b.class_name  as driver_class_name,
                                        c.brand,
                                        a.purpose
                                    FROM
                                     iot_device a
                                     JOIN iot_driver b ON a.driver_id = b.id
                                        JOIN iot_device_type c ON a.device_type_id = c.id
                                    WHERE
                                     a.is_active = 1
                                        AND a.company_id=@company_id
                                        AND a.plant_id=@plant_id
                                        AND a.line=@line
                                        AND a.station_no=@station_no";
                list = GetList<IotDeviceDriverDto>(sql,
                    new SugarParameter("@company_id", p.company_id),
                    new SugarParameter("@plant_id",   p.plant_id),
                    new SugarParameter("@line",       p.line),
                    new SugarParameter("@station_no", StationNo));
                foreach (var it in list)
                {
                    it.debug = debug;
                }
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotDeviceService.GetDeviceDriverListByStation 异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return list;
        }
        /// <summary>
        /// 根据设备ip地址获取设备及驱动，不关心purpose，由负责调用的地方处理
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public List<IotDeviceDriverDto> GetDeviceDriverListByIp(pms_public p, string ip)
        {
            List<IotDeviceDriverDto> list = new List<IotDeviceDriverDto>();
            try
            {
                const string sql = @"SELECT
                                        a.company_id,
                                        a.plant_id,
                                        a.line,
                                        a.region_no,
                                        a.station_no,
                                     a.driver_id,
                                     a.id device_id,
                                     a.device_type_id,
                                     a.device_name,
                                        a.class_name as device_class_name,
                                        a.assembly_name as device_assembly_name,
                                     a.address as ip,
                                     a.time_out,
                                     b.assembly_name as driver_assembly_name,
                                     b.class_full_name as driver_full_class_name,
                                     b.class_name  as driver_class_name,
                                        c.brand,
                                        a.purpose
                                    FROM
                                     iot_device a
                                     JOIN iot_driver b ON a.driver_id = b.id
                                        JOIN iot_device_type c ON a.device_type_id = c.id
                                    WHERE
                                     a.is_active = 1
                                        AND a.company_id=@company_id
                                        AND a.plant_id=@plant_id
                                        AND a.line=@line
                                        AND a.address=@address";
                var one = GetSingle<IotDeviceDriverDto>(sql,
                    new SugarParameter("@company_id", p.company_id),
                    new SugarParameter("@plant_id",   p.plant_id),
                    new SugarParameter("@line",       p.line),
                    new SugarParameter("@address",    ip));
                if (one != null)
                    list.Add(one);
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotDeviceService.GetDeviceDriverListByIp 异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return list;
        }
        public List<IotDeviceDriverDto> GetDeviceDriverListById(string device_id, bool debug = false)
        {
            List<IotDeviceDriverDto> list = new List<IotDeviceDriverDto>();
            var one = GetDriverByDeviceId(device_id, debug);
            if (one != null)
                list.Add(one);
            return list;
        }
        /// <summary>
        /// 根据设备id获取设备及驱动，不关心purpose，由负责调用的地方处理
        /// </summary>
        /// <param name="device_id">设备id</param>
        /// <returns></returns>
        public IotDeviceDriverDto GetDriverByDeviceId(string device_id, bool debug = false)
        {
            IotDeviceDriverDto one = new IotDeviceDriverDto();
            try
            {
                const string sql = @"SELECT
                                        a.company_id,
                                        a.plant_id,
                                        a.line,
                                        a.region_no,
                                        a.station_no,
                                     a.driver_id,
                                     a.id device_id,
                                     a.device_type_id,
                                     a.device_name,
                                        a.class_name as device_class_name,
                                        a.assembly_name as device_assembly_name,
                                     a.address as ip,
                                     a.time_out,
                                     b.assembly_name as driver_assembly_name,
                                     b.class_full_name as driver_full_class_name,
                                     b.class_name as driver_class_name,
                                        c.brand,
                                        a.purpose
                                    FROM
                                     iot_device a
                                     JOIN iot_driver b ON a.driver_id = b.id
                                        JOIN iot_device_type c ON a.device_type_id = c.id
                                    WHERE
                                     a.is_active = 1 AND a.id=@device_id";

                one = GetSingle<IotDeviceDriverDto>(sql,
                    new SugarParameter("@device_id", device_id));
                one.debug = debug;
                //写法练习 效率低下
                //List<IotDeviceDriverDto> list = base.AsSugarClient().Queryable<iot_device, iot_driver, iot_device_type>
                //    ((a, b, c) => a.driver_id == b.id && a.device_type_id == c.id)
                //    .Where(a => a.is_active == 1 && a.id == device_id)
                //    .Select((a, b, c) => new IotDeviceDriverDto
                //    {
                //        company_id = a.company_id,
                //        plant_id = a.plant_id,
                //        line = a.line,
                //        region_no = a.region_no,
                //        station_no = a.station_no,
                //        driver_id = a.driver_id,
                //        device_id = a.id,
                //        device_type_id = a.device_type_id,
                //        device_name = a.device_name,
                //        device_class_name = a.class_name,
                //        device_assembly_name = a.assembly_name,
                //        ip = a.address,
                //        time_out = a.time_out,
                //        driver_assembly_name = b.assembly_name,
                //        driver_full_class_name = b.class_full_name,
                //        driver_class_name = b.class_name,
                //        brand = c.brand,
                //    })
                //    .ToList();
                //if (list.Count > 0)
                //    one = list[0];
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("ZL.Dao.IotDevice.GetDriverByDeviceId异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return one;
        }

        ////获取所有的iot_device
        //public List<iot_device> GetList()
        //{
        //    return base.GetList(); //使用自已的仓储方法
        //}
        //var IotDevice = iotDevice.GetList(it => it.Id > 0)
        //public List<iot_device> GetList(Expression<Func<iot_device, bool>> where)
        //{
        //    return base.GetList(where); //使用自已的仓储方法
        //}

        //分页
        public List<iot_device> GetPage(Expression<Func<iot_device, bool>> where, int pagesize = 1, int pageindex = 20)
        {
            return base.GetPageList(where, new SqlSugar.PageModel() { PageIndex = pageindex, PageSize = pagesize });
        }

        //调用方式：IotDeviceService.GetOrderByJson("{id:1}");
        public List<iot_device> GetOrderByJson(string Json)
        {
            return base.CommQuery(Json);
        }

        ////获取所有子订单
        //public List<OrderItem> GetOrderItems()
        //{
        //    var orderItemDb = base.Change<OrderItem>();//切换仓仓（新功能）
        //    return orderItemDb.GetList();
        //}

        #region 异步方法

        /// <summary>
        /// 异步插入设备记录（自动生成ID）
        /// </summary>
        public async Task<bool> InsertAsync(iot_device obj, CancellationToken cancellationToken = default)
        {
            if (obj.id == null) obj.id = Guid.NewGuid().ToString();
            return await Task.Run(() => base.Insert(obj), cancellationToken);
        }

        /// <summary>
        /// 异步根据ID删除设备
        /// </summary>
        public async Task<bool> DeleteByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => base.DeleteById(id), cancellationToken);
        }

        /// <summary>
        /// 异步根据ID获取设备
        /// </summary>
        public async Task<iot_device> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetById(id), cancellationToken);
        }

        /// <summary>
        /// 异步根据工位号获取设备
        /// </summary>
        public async Task<iot_device> GetByStationNoAsync(pms_public p, string StationNo, byte Purpose = 1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetByStationNo(p, StationNo, Purpose), cancellationToken);
        }

        /// <summary>
        /// 异步获取设备列表
        /// </summary>
        public async Task<List<iot_device>> GetListAsync(pms_public p, byte IsActive = 1, byte Purpose = 1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetList(p, IsActive, Purpose), cancellationToken);
        }

        /// <summary>
        /// 异步获取设备驱动列表
        /// </summary>
        public async Task<List<IotDeviceDriverDto>> GetDeviceDriverListAsync(pms_public p, bool debug, int purpose = -1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetDeviceDriverList(p, debug, purpose), cancellationToken);
        }

        /// <summary>
        /// 异步根据边缘ID获取设备驱动列表
        /// </summary>
        public async Task<List<IotDeviceDriverDto>> GetDeviceDriverListByEdgeIdAsync(string edgeId, bool debug = false, int purpose = -1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetDeviceDriverListByEdgeId(edgeId, debug, purpose), cancellationToken);
        }

        /// <summary>
        /// 异步根据工位号获取设备驱动列表
        /// </summary>
        public async Task<List<IotDeviceDriverDto>> GetDeviceDriverListByStationAsync(pms_public p, string StationNo, bool debug = false, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetDeviceDriverListByStation(p, StationNo, debug), cancellationToken);
        }

        /// <summary>
        /// 异步根据IP地址获取设备驱动列表
        /// </summary>
        public async Task<List<IotDeviceDriverDto>> GetDeviceDriverListByIpAsync(pms_public p, string ip, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetDeviceDriverListByIp(p, ip), cancellationToken);
        }

        /// <summary>
        /// 异步根据设备ID获取驱动信息
        /// </summary>
        public async Task<IotDeviceDriverDto> GetDriverByDeviceIdAsync(string device_id, bool debug = false, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetDriverByDeviceId(device_id, debug), cancellationToken);
        }

        /// <summary>
        /// 异步分页查询设备
        /// </summary>
        public async Task<List<iot_device>> GetPageAsync(Expression<Func<iot_device, bool>> where, int pagesize = 1, int pageindex = 20, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetPage(where, pagesize, pageindex), cancellationToken);
        }

        #endregion
    }
}
