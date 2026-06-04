using System;
using System.Collections.Generic;
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
    /// IoT 标签服务 - 提供标签数据的增删改查操作
    /// </summary>
    public class IotTagService : Repository<iot_tag>, IIotTagService
    {
        public iot_tag GetByTagId(string Id)
        {
            var db = base.AsSugarClient();
            var one = db.Queryable<iot_tag>()
                .Where(t => t.id == Id && (t.address != null || t.address != ""))
                .First();
            return one;
        }
        /// <summary>
        /// 根据DeviceId获取对应的tag标签---关联iot_group，意义不大
        /// </summary>
        /// <param name="DeviceId"></param>
        /// <returns></returns>
        public List<iot_tag> GetTagListByDeviceId(string DeviceId)
        {
            var db = base.AsSugarClient();
            //var one = db.Queryable<iot_tag, iot_group>(
            //    (t, g) => new JoinQueryInfos(JoinType.Left, t.device_id == g.device_id && t.group_id == g.id))
            //    .Select<iot_tag>()
            //    .Where(t => t.device_id == DeviceId && t.is_active == 1 && (t.address != null || t.address != ""))
            //    .OrderBy(t => t.id)
            //    .ToList();
            var query = db.Queryable<iot_tag>()
                .Where(t => t.device_id == DeviceId && t.is_active == 1 && (t.address != null || t.address != ""))
                .OrderBy(t => t.id)
                .ToList();
            return query;
        }

        public List<iot_tag> QueryList(List<IConditionalModel> cList)
        {
            List<iot_tag> list = new List<iot_tag>();
            try
            {
                var db = base.AsSugarClient();
                list = db.Queryable<iot_tag>().Where(cList).ToList();
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotTagService.QueryList 异常：" + ex.Message, Config.LogFile);
            }
            return list;
        }
        /// <summary>
        /// 根据StationNo获取对应的tag标签
        /// </summary>
        /// <param name="StationNo"></param>
        /// <returns></returns>
        public List<iot_tag> GetTagListByStationNo(pms_public p, string StationNo, int purpose = -1)
        {
            var db = base.AsSugarClient();
            List<iot_tag> list = new List<iot_tag>();
            if (purpose == -1)
                list = db.Queryable<iot_tag, iot_device>(
                    (t, d) => new JoinQueryInfos(JoinType.Left, t.device_id == d.id))
                    .Where((t, d) => d.company_id == p.company_id && d.plant_id == p.plant_id && d.line == p.line && d.station_no == StationNo && t.is_active == 1 && (t.address != null || t.address != ""))
                    .Select<iot_tag>()
                    .OrderBy(t => t.id)
                    .ToList();
            else
                list = db.Queryable<iot_tag, iot_device>(
                    (t, d) => new JoinQueryInfos(JoinType.Left, t.device_id == d.id))
                    .Where((t, d) =>
                        d.company_id == p.company_id && d.plant_id == p.plant_id && d.line == p.line && d.station_no == StationNo && d.purpose == purpose &&
                        t.is_active == 1 && (t.address != null || t.address != "")
                        )
                    .Select<iot_tag>()
                    .OrderBy(t => t.id)
                    .ToList();
            return list;
        }

        /// <summary>
        /// 更新设备的状态--针对设备报警
        /// </summary>
        /// <param name="id"></param>
        /// <param name="value"></param>
        public void UpdateTagValue(string id, string value)
        {
            try
            {
                var db = base.AsSugarClient();
                db.Updateable<iot_tag>()
                 .SetColumns(it => new iot_tag() { value = value, updated_at = SqlFunc.GetDate() })
                 .Where(i => i.id == id)
                 .ExecuteCommand();
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotTagService.UpdateTagValue异常：" + ex.Message, Config.LogFile);
                throw;
            }
        }

        public List<iot_tag> GetAllDeviceTag(pms_public p, int purpose = -1)
        {
            List<iot_tag> list = new List<iot_tag>();
            try
            {
                const string sqlBase = @"SELECT a.* FROM
 iot_tag a
 JOIN (SELECT * FROM iot_device WHERE is_active = 1{0} AND company_id=@company_id AND plant_id=@plant_id AND line=@line) b ON a.device_id = b.id
    JOIN iot_device_type c ON b.device_type_id = c.id
WHERE
 a.is_active = 1 AND (a.address IS NOT NULL)
ORDER BY
 a.device_id, a.data_source, a.address";

                string purposeClause;
                var pars = new System.Collections.Generic.List<SugarParameter>
                {
                    new SugarParameter("@company_id", p.company_id),
                    new SugarParameter("@plant_id",   p.plant_id),
                    new SugarParameter("@line",       p.line),
                };

                if (purpose == -1)
                {
                    purposeClause = string.Empty;
                }
                else if (purpose == 999)
                {
                    purposeClause = " AND purpose IN (2, 3)";
                }
                else
                {
                    purposeClause = " AND purpose=@purpose";
                    pars.Add(new SugarParameter("@purpose", purpose));
                }

                string sql = string.Format(sqlBase, purposeClause);
                list = GetList<iot_tag>(sql, pars.ToArray());
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("iot_tag.GetAllDeviceTag异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return list;
        }

        public List<iot_tag> GetAllDeviceTagByEdgeId(string edgeId, int purpose = 1)
        {
            List<iot_tag> list = new List<iot_tag>();
            try
            {
                const string sqlBase = @"SELECT a.* FROM
 iot_tag a
 JOIN ( SELECT y.* FROM iot_edge_relation x
  JOIN (SELECT * FROM iot_device WHERE is_active = 1{0}) y ON x.edge_id = @edge_id
  AND x.device_id = y.id ) b ON a.device_id = b.id
    JOIN iot_device_type c ON b.device_type_id = c.id
WHERE
 a.is_active = 1 AND (a.address IS NOT NULL)
ORDER BY
 a.device_id, a.data_source, a.address";

                var pars = new System.Collections.Generic.List<SugarParameter>
                {
                    new SugarParameter("@edge_id", edgeId),
                };

                string purposeClause;
                if (purpose == -1)
                {
                    purposeClause = string.Empty;
                }
                else
                {
                    purposeClause = " AND purpose=@purpose";
                    pars.Add(new SugarParameter("@purpose", purpose));
                }

                string sql = string.Format(sqlBase, purposeClause);
                list = GetList<iot_tag>(sql, pars.ToArray());
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("iot_tag.GetAllDeviceTagByEdgeId异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return list;
        }

        public List<iot_tag> GetAllDeviceTagByDeviceId(string deviceId)
        {
            List<iot_tag> list = new List<iot_tag>();
            try
            {
                const string sql = @"SELECT a.* FROM
 iot_tag a
 JOIN (SELECT * FROM iot_device WHERE is_active = 1 AND id=@device_id) b ON a.device_id = b.id
    JOIN iot_device_type c ON b.device_type_id = c.id
WHERE
 a.is_active = 1 AND (a.address IS NOT NULL)
ORDER BY
 a.device_id, a.data_source, a.address";
                list = GetList<iot_tag>(sql,
                    new SugarParameter("@device_id", deviceId));
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("iot_tag.GetAllDeviceTagByDeviceId异常：" + ex.Message, Config.LogFile);
                throw;
            }
            return list;
        }
        /// <summary>
        /// 查询监控地址相关关联的配方下发地址，只取了tag_type = 'W' 且 set_type为I,P两种
        /// </summary>
        /// <param name="device_id"></param>
        /// <param name="pid"></param>
        /// <returns></returns>
        public List<IotPfDto> GetPFNullList(string device_id, string pid)
        {
            List<IotPfDto> list = new List<IotPfDto>();
            try
            {
                const string sql = @"SELECT '' AS company_id, '' AS plant_id, '' AS line,
                                    id AS tag_id, tag_name, address, data_type, list_order, exe_order,
                                    tag_type, set_type, preset, info_type,
                                    (CASE set_type WHEN 'P' THEN preset ELSE '' END) AS val
                                    FROM iot_tag
                                    WHERE device_id = @device_id
                                      AND pid = @pid
                                      AND tag_type = 'W'
                                      AND set_type IN ('I', 'P')
                                      AND is_active = 1
                                    ORDER BY list_order";
                list = GetList<IotPfDto>(sql,
                    new SugarParameter("@device_id", device_id),
                    new SugarParameter("@pid",       pid));
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("GetPFNullList函数执行错误，错误信息:" + ex.Message, Config.LogFile);
            }
            return list;
        }

        /// <summary>
        /// 查询配方参数
        /// </summary>
        /// <param name="device_id"></param>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="sn"></param>
        /// <returns></returns>
        public List<IotPfDto> GetPFBySn(string device_id, string pid, pms_public p, string sn)
        {
            List<IotPfDto> list = new List<IotPfDto>();
            try
            {
                const string sql = @"SELECT
                D.company_id,
                D.plant_id,
                D.line,
                T.id AS tag_id,
                T.tag_name,
                T.address,
                T.data_type,
                T.list_order,
                T.exe_order,
                T.tag_type,
                T.set_type,
                T.preset,
                T.info_type,
                CASE WHEN T.set_type = 'P' THEN T.preset
                     WHEN T.set_type = 'I' AND T.info_type = 'D' THEN N.sn
                     WHEN T.set_type = 'I' AND T.info_type != 'D' THEN
                        (CASE WHEN T.info_type='SN' THEN N.sn
                              WHEN T.info_type='M'  THEN N.model
                              WHEN T.info_type='O'  THEN N.order_no
                              WHEN T.info_type='SC' THEN N.short_code
                              WHEN T.info_type='C'  THEN N.catena
                              WHEN T.info_type='KY' THEN N.remark
                         END)
                END AS val
                FROM (SELECT * FROM iot_tag
                      WHERE device_id = @device_id AND pid = @pid
                        AND tag_type = 'W' AND set_type IN ('I', 'P') AND is_active = 1) T
                    JOIN iot_device D ON T.device_id = D.id
                    LEFT JOIN (
                        SELECT I.company_id, I.plant_id, I.line, I.sn, I.model,
                               M.catena, M.short_code, I.remark, I.order_no,
                               (CASE I.repair_mark WHEN '0' THEN '0' WHEN '1' THEN '1' END) AS REPAIR
                        FROM pms_plan I
                        JOIN bom_model M ON I.company_id = M.company_id AND I.plant_id = M.plant_id
                                        AND I.line = M.line AND I.model = M.model
                        WHERE I.company_id = @company_id AND I.plant_id = @plant_id
                          AND I.line = @line AND sn = @sn
                    ) N ON D.company_id = N.company_id AND D.plant_id = N.plant_id AND D.line = N.line";
                list = GetList<IotPfDto>(sql,
                    new SugarParameter("@device_id",  device_id),
                    new SugarParameter("@pid",        pid),
                    new SugarParameter("@company_id", p.company_id),
                    new SugarParameter("@plant_id",   p.plant_id),
                    new SugarParameter("@line",       p.line),
                    new SugarParameter("@sn",         sn));
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("GetPFBySn函数执行错误，错误信息:" + ex.Message, Config.LogFile);
            }
            return list;
        }

        #region 异步方法

        /// <summary>
        /// 异步根据标签ID获取标签
        /// </summary>
        public async Task<iot_tag> GetByTagIdAsync(string Id, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetByTagId(Id), cancellationToken);
        }

        /// <summary>
        /// 异步根据设备ID获取标签列表
        /// </summary>
        public async Task<List<iot_tag>> GetTagListByDeviceIdAsync(string DeviceId, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetTagListByDeviceId(DeviceId), cancellationToken);
        }

        /// <summary>
        /// 异步条件查询标签列表
        /// </summary>
        public async Task<List<iot_tag>> QueryListAsync(List<IConditionalModel> cList, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => QueryList(cList), cancellationToken);
        }

        /// <summary>
        /// 异步根据工位号获取标签列表
        /// </summary>
        public async Task<List<iot_tag>> GetTagListByStationNoAsync(pms_public p, string StationNo, int purpose = -1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetTagListByStationNo(p, StationNo, purpose), cancellationToken);
        }

        /// <summary>
        /// 异步更新标签值
        /// </summary>
        public async Task UpdateTagValueAsync(string id, string value, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => UpdateTagValue(id, value), cancellationToken);
        }

        /// <summary>
        /// 异步获取所有设备标签
        /// </summary>
        public async Task<List<iot_tag>> GetAllDeviceTagAsync(pms_public p, int purpose = -1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetAllDeviceTag(p, purpose), cancellationToken);
        }

        /// <summary>
        /// 异步根据边缘ID获取所有设备标签
        /// </summary>
        public async Task<List<iot_tag>> GetAllDeviceTagByEdgeIdAsync(string edgeId, int purpose = 1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetAllDeviceTagByEdgeId(edgeId, purpose), cancellationToken);
        }

        /// <summary>
        /// 异步根据设备ID获取所有设备标签
        /// </summary>
        public async Task<List<iot_tag>> GetAllDeviceTagByDeviceIdAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetAllDeviceTagByDeviceId(deviceId), cancellationToken);
        }

        /// <summary>
        /// 异步查询监控地址相关联的配方下发地址
        /// </summary>
        public async Task<List<IotPfDto>> GetPFNullListAsync(string device_id, string pid, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetPFNullList(device_id, pid), cancellationToken);
        }

        /// <summary>
        /// 异步根据SN查询配方参数
        /// </summary>
        public async Task<List<IotPfDto>> GetPFBySnAsync(string device_id, string pid, pms_public p, string sn, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GetPFBySn(device_id, pid, p, sn), cancellationToken);
        }

        #endregion
    }
}
