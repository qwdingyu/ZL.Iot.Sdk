using System.Collections.Generic;
using ZL.Dao.IotDevice;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.IotDevice
{
    public class IotDeviceArgService : Repository<iot_device_arg>
    {
        public List<iot_device_arg> GetArgList(pms_public p)
        {
            string sql = string.Format(@"SELECT id, device_type_id, pro_name, pro_value, description FROM iot_device_arg
                                        WHERE device_type_id in ( SELECT DISTINCT device_type_id FROM iot_device 
                                            WHERE is_active = 1 AND company_id='{0}' AND plant_id='{1}' AND line='{2}') 
                                        order by device_type_id", p.company_id, p.plant_id, p.line);
            return GetList<iot_device_arg>(sql);
        }

        public List<iot_device_arg> GetArgListByEdgeId(string edgeId)
        {
            string sql = string.Format(@"SELECT id, device_type_id, pro_name, pro_value, description FROM iot_device_arg a
    WHERE EXISTS ( SELECT 1 FROM iot_device b 
	    WHERE b.is_active = 1 
		    AND b.id IN ( SELECT device_id FROM iot_edge_relation WHERE edge_id = '{0}' ) 
		    AND a.device_type_id = b.device_type_id 
	    ) 
    ORDER BY a.device_type_id", edgeId);
            return GetList<iot_device_arg>(sql);
        }

        public List<iot_device_arg> GetArgListByDeviceId(string device_id)
        {
            string sql = string.Format(@"SELECT id, device_type_id, pro_name, pro_value, description FROM iot_device_arg a
    WHERE EXISTS ( SELECT 1 FROM iot_device b 
	    WHERE b.is_active = 1 
		    AND b.id = '{0}' 
		    AND a.device_type_id = b.device_type_id 
	    ) 
    ORDER BY a.device_type_id", device_id);
            return GetList<iot_device_arg>(sql);
        }
        /// <summary>
        /// </summary>
        /// <param name="device_id"></param>
        /// <returns></returns>
        public List<IotDeviceArgDto> GetArgByDeviceId(string device_id)
        {
            string sql = string.Format(@"SELECT
	a.id AS device_id,
	a.address as ip,
	c.id as device_type_id,
	c.type as device_type,
	b.pro_name,
	b.pro_value 
FROM
	( SELECT * FROM iot_device WHERE id = '{0}' ) a
	LEFT JOIN iot_device_arg b ON a.device_type_id= b.device_type_id
	LEFT JOIN iot_device_type c on a.device_type_id=c.id", device_id);
            return GetList<IotDeviceArgDto>(sql);
        }
    }
}
