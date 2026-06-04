using System;
using System.Collections.Generic;
using SqlSugar;
using ZL.DB.Acc;
using ZL.PFLite.Common;

namespace ZL.Dao.IotDevice
{
    public class IotITagService : Repository<iot_itag>
    {
        /// <summary>
        /// 根据DeviceId获取对应的tag标签---关联iot_group，意义不大
        /// </summary>
        /// <param name="DeviceId"></param>
        /// <returns></returns>
        public List<iot_itag> GetTagList(string DeviceId)
        {
            var db = base.AsSugarClient();
            var query = db.Queryable<iot_itag, iot_group>(
                (t, g) => new JoinQueryInfos(JoinType.Left, t.device_id == g.device_id && t.group_id == g.id))
                .Select<iot_itag>()
                .Where(t => t.device_id == DeviceId && t.is_active == 1 && (t.address != null || t.address != ""))
                .OrderBy(t => t.id)
                .ToList();
            return query;
        }
        /// <summary>
        /// 根据StationNo获取对应的tag标签
        /// </summary>
        /// <param name="StationNo"></param>
        /// <returns></returns>
        public List<iot_itag> GetTagListByStationNo(string StationNo, int purpose = -1)
        {
            var db = base.AsSugarClient();
            List<iot_itag> list = new List<iot_itag>();
            if (purpose == -1)
                list = db.Queryable<iot_itag, iot_device>(
                    (t, d) => new JoinQueryInfos(JoinType.Left, t.device_id == d.id))
                    .Where((t, d) => d.company_id == Config.CompanyId && d.plant_id == Config.PlantId && d.line == Config.Line && d.station_no == StationNo && t.is_active == 1 && (t.address != null || t.address != ""))
                    .Select<iot_itag>()
                    .OrderBy(t => t.id)
                    .ToList();
            else
                list = db.Queryable<iot_itag, iot_device>(
                    (t, d) => new JoinQueryInfos(JoinType.Left, t.device_id == d.id))
                    .Where((t, d) =>
                        d.company_id == Config.CompanyId && d.plant_id == Config.PlantId && d.line == Config.Line && d.station_no == StationNo && d.purpose == purpose &&
                        t.is_active == 1 && (t.address != null || t.address != "")
                        )
                    .Select<iot_itag>()
                    .OrderBy(t => t.id)
                    .ToList();
            return list;
        }

        public List<iot_itag> GetTagListByDeviceId(string device_id)
        {
            var db = base.AsSugarClient();
            var query = db.Queryable<iot_itag>()
                .Where(t => t.device_id == device_id && t.is_active == 1 && (t.address != null || t.address != ""))
                .OrderBy(t => t.id)
                .ToList();
            return query;
        }
        /// <summary>
        /// 更新设备的状态--针对设备报警
        /// </summary>
        /// <param name="id"></param>
        /// <param name="value"></param>
        public void UpdateTagValue(string id, string value)
        {
            string sql = string.Empty;
            try
            {
                var db = base.AsSugarClient();
                db.Updateable<iot_itag>()
                 .SetColumns(it => new iot_itag() { value = value, updated_at = SqlFunc.GetDate() })
                 .Where(i => i.id == id)
                 .ExecuteCommand();
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("IotTagService.UpdateTagValue异常：" + ex.Message, Config.LogFile);
                throw ex;
            }
        }

        public List<iot_itag> GetAllDeviceTag(int purpose = 1)
        {
            string sql = string.Empty;
            if (purpose == -1)
                sql = string.Format(@"SELECT a.* FROM
	iot_itag a
	JOIN (SELECT * FROM iot_device WHERE is_active = 1 AND company_id='{0}' AND plant_id='{1}' AND line='{2}' ) b  ON a.device_id = b.id 
    JOIN iot_device_type c ON b.device_type_id = c.id
WHERE
	a.is_active = 1  AND (a.address is not null)
ORDER BY
	a.data_source, a.id", Config.CompanyId, Config.PlantId, Config.Line);
            else
                sql = string.Format(@"SELECT a.* FROM
	iot_itag a
	JOIN (SELECT * FROM iot_device WHERE is_active = 1 AND purpose={0} AND company_id='{1}' AND plant_id='{2}' AND line='{3}' ) b  ON a.device_id = b.id 
    JOIN iot_device_type c ON b.device_type_id = c.id
WHERE
	a.is_active = 1  AND (a.address is not null)
ORDER BY
	a.data_source, a.id", purpose, Config.CompanyId, Config.PlantId, Config.Line);
            List<iot_itag> list = new List<iot_itag>();
            try
            {
                list = GetList<iot_itag>(sql);
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("iot_itag.GetAllDeviceTag异常：" + ex.Message, Config.LogFile);
                throw ex;
            }
            return list;
        }

        public List<iot_itag> GetAllDeviceTagByEdgeId(int purpose = 1)
        {
            string sql = string.Empty;
            if (purpose == -1)
                sql = string.Format(@"SELECT a.* FROM
	iot_itag a
	JOIN ( SELECT y.* FROM iot_edge_relation x
		JOIN (SELECT * FROM iot_device WHERE is_active = 1 ) y ON x.edge_id = '{0}' 
		AND x.device_id = y.id ) b  ON a.device_id = b.id 
    JOIN iot_device_type c ON b.device_type_id = c.id
WHERE
	a.is_active = 1  AND (a.address is not null)
ORDER BY
	a.data_source, a.id",
      purpose, Config.EdgeId);
            else
                sql = string.Format(@"SELECT a.* FROM
	iot_itag a
	JOIN ( SELECT y.* FROM iot_edge_relation x
		JOIN (SELECT * FROM iot_device WHERE is_active = 1 AND purpose={0} ) y ON x.edge_id = '{1}' 
		AND x.device_id = y.id ) b  ON a.device_id = b.id 
    JOIN iot_device_type c ON b.device_type_id = c.id
WHERE
	a.is_active = 1  AND (a.address is not null)
ORDER BY
	a.data_source, a.id",
         purpose, Config.EdgeId);
            List<iot_itag> list = new List<iot_itag>();
            try
            {
                list = GetList<iot_itag>(sql);
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("iot_itag.GetAllDeviceTag异常：" + ex.Message, Config.LogFile);
                throw ex;
            }
            return list;
        }

    }
}
