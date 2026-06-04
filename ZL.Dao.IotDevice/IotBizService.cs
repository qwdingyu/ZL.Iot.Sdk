using System.Data;
using ZL.DB.Acc;

namespace ZL.Dao.IotDevice
{
    public class IotBizService : DaoBase
    {
        public DataRow GetTagDeviceInfoByTagId(string tagId)
        {
            string sql = string.Empty;
            DataRow dr = null;
            DataTable dt = new DataTable();
            sql = $"SELECT a.*, b.company_id,b.plant_id, b.line,b.station_no FROM (SELECT device_id, address, description FROM iot_tag WHERE  id = '{tagId}') a LEFT JOIN iot_device b ON a.device_id =b.id";
            dt = db.GetDataTable(sql);
            if (dt.Rows.Count == 0) return dr;
            dr = dt.Rows[0];
            return dr;
        }

        /// <summary>
        /// 试验成功，不用单独建立每个cs类来出来
        /// 也就是除了Repository不具备的方法才用单独建立cs类进行编写
        /// </summary>
        public void Test()
        {
            var list = BaseDao.iot_deviceDb.AsQueryable().ToList();
            foreach (var item in list)
            {
                string id = item.id;
            }
        }
    }
}
