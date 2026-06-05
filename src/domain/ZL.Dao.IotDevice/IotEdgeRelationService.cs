using System.Collections.Generic;
using ZL.Dao.IotDevice;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.IotDevice
{
    public class IotEdgeRelationService : Repository<iot_edge_relation>
    {

        public List<iot_edge_relation> GetDeviceListByEdgeId(string edgeId)
        {
            List<iot_edge_relation> list = new List<iot_edge_relation>();
            list = db.Queryable<iot_edge_relation>().Where(it => it.edge_id == edgeId).ToList();
            return list;
        }

        public string GetEdgeIdByDeviceId(string deviceId)
        {
            string edgeId = "";
            var one = db.Queryable<iot_edge_relation>().Where(it => it.device_id == deviceId)
                .First();
            edgeId = one != null ? one.edge_id : "";
            return edgeId;
        }
    }
}
