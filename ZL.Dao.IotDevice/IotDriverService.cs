using System.Collections.Generic;
using ZL.Dao.IotDevice;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.IotDevice
{
    public class IotDriverService : Repository<iot_driver>
    {
        public List<IotDriverAssemblyDto> GetDriverList(pms_public p, string StationNo)
        {
            string sql = "";
            if (string.IsNullOrEmpty(StationNo))
                sql = string.Format(@"SELECT
	                                    DISTINCT b.id,
	                                    b.assembly_name,
	                                    b.class_full_name,
	                                    b.class_name 
                                    FROM
	                                    iot_device a
	                                    INNER JOIN iot_driver b ON a.driver_id = b.id 
                                    WHERE
	                                    a.is_active = 1 AND a.company_id='{0}' AND a.plant_id='{1}' AND a.line='{2}'",
                                        p.company_id, p.plant_id, p.line);
            else
                sql = string.Format(@"SELECT DISTINCT b.id, b.assembly_name, b.class_full_name, b.class_name 
                                    FROM iot_device a INNER JOIN iot_driver b ON a.driver_id = b.id 
                                    WHERE a.is_active = 1 AND a.company_id='{0}' AND a.plant_id='{1}' AND a.line='{2}' AND a.station_no='{3}'",
                                        p.company_id, p.plant_id, p.line, StationNo);
            return GetList<IotDriverAssemblyDto>(sql);
        }

        public List<IotDriverAssemblyDto> GetDriverListByEdgeId(string edgeId)
        {
            string sql = "";
            sql = string.Format(@"SELECT DISTINCT b.id, b.assembly_name, b.class_full_name, b.class_name 
                                    FROM iot_device a INNER JOIN iot_driver b ON a.driver_id = b.id 
                                    WHERE a.is_active = 1 AND a.id IN ( SELECT device_id FROM iot_edge_relation WHERE edge_id = '{0}' ) ",
                                   edgeId);
            return GetList<IotDriverAssemblyDto>(sql);
        }

        public List<IotDriverAssemblyDto> GetDriverListByDeviceId(string deviceId)
        {
            string sql = "";
            sql = string.Format(@"SELECT DISTINCT b.id, b.assembly_name, b.class_full_name, b.class_name 
                                    FROM iot_device a INNER JOIN iot_driver b ON a.driver_id = b.id 
                                    WHERE a.is_active = 1 AND a.id = '{0}' ", deviceId);
            return GetList<IotDriverAssemblyDto>(sql);
        }
    }
}
