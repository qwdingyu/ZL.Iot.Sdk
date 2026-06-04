using System;
using System.Data;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class IotQtyDal : DaoBase
    {
        public DataTable GetStationList(string device_id)
        {
            string sql = string.Format(@"SELECT station_no FROM
                                        (
                                            SELECT station_no,
                                                   MIN(plc_add) AS start_add
                                            FROM iot_qty_def
                                            WHERE device_id = '{0}'
                                                AND
                                                  (
                                                      val_field IS NOT NULL
                                                    AND LEN(val_field) <> 0
                                                  )
                                            GROUP BY station_no
                                        ) t
                                        ORDER BY t.start_add", device_id);
            DataTable dt = GetDataTable(sql);
            return dt;
        }
        public DataTable GetStationList(pms_station_no p)
        {
            string sql = string.Format(@"SELECT station_no FROM
                                        (
                                            SELECT station_no,
                                                   MIN(plc_add) AS start_add
                                            FROM iot_qty_def
                                            WHERE company_id = '{0}'
                                                AND plant_id = '{1}'
                                                AND line = '{2}'
                                                AND station_no = '{3}'
                                                AND
                                                  (
                                                      val_field IS NOT NULL
                                                    AND LEN(val_field) <> 0
                                                  )
                                            GROUP BY station_no
                                        ) t
                                        ORDER BY t.start_add", p.company_id, p.plant_id, p.line, p.station_no);
            DataTable dt = GetDataTable(sql);
            return dt;
        }
        public int GetAllQtyDefMinAdr(string tag_id)
        {
            int min = 0;
            try
            {
                string sql = string.Format(@"SELECT COALESCE(MIN(plc_add),0) AS min_add FROM iot_qty_def  WHERE tag_id = '{0}'", tag_id);
                min = db.GetInt(sql);
            }
            catch (Exception ex)
            {
                throw;
            }
            return min;
        }
        public int GetQtyDefMinAdr(string tag_id)
        {
            int min = 0;
            try
            {
                string sql = string.Format(@"SELECT COALESCE(MIN(plc_add),0) AS min_add FROM iot_qty_def  WHERE tag_id = '{0}' and enable=1", tag_id);
                min = db.GetInt(sql);
            }
            catch (Exception ex)
            {
                throw;
            }
            return min;
        }

        public DataTable GetQtyDef(string tag_id, int start_idx)
        {
            DataTable dt = new DataTable();
            try
            {
                string sql = string.Format(@"SELECT a.region_no,
                                       a.station_no,
                                       a.val_field,
                                       a.val_des,
                                       a.data_type,
                                       a.qty_type,
                                        MIN(a.plc_add) AS start_add,
                                        MAX(a.plc_add) AS end_add,
                                        (MIN(a.plc_add) - {1}) AS idx_add,
                                       a.null_check,
                                       a.valid_start,
                                       a.valid_end,
                                       a.size,
                                       a.bit_index
                                FROM iot_qty_def a
                                WHERE a.tag_id = '{0}' and enable=1
                                GROUP BY a.region_no,
                                         a.station_no,
                                         a.val_field,
                                         a.val_des,
                                         a.data_type,
                                         a.qty_type,
                                         a.null_check,
                                         a.valid_start,
                                         a.valid_end,
                                           a.size,
                                           a.bit_index
                                ORDER BY start_add,
                                         a.val_field", tag_id, start_idx);
                dt = GetDataTable(sql);
            }
            catch (Exception ex)
            {
                throw;
            }
            return dt;
        }
        public DataTable GetQtyDef(string tag_id, int exe_order, int start_idx)
        {
            DataTable dt = new DataTable();
            try
            {
                string sql = string.Format(@"SELECT a.region_no,
                                       a.station_no,
                                       a.val_field,
                                       a.val_des,
                                       a.data_type,
                                       a.qty_type,
                                        MIN(a.plc_add) AS start_add,
                                        MAX(a.plc_add) AS end_add,
                                        (MIN(a.plc_add) - {2}) AS idx_add,
                                       a.null_check,
                                       a.valid_start,
                                       a.valid_end,
                                       a.size,
                                       a.bit_index
                                FROM iot_qty_def a
                                WHERE a.tag_id = '{0}' and a.exe_order= {1}  and enable=1
                                GROUP BY a.region_no,
                                         a.station_no,
                                         a.val_field,
                                         a.val_des,
                                         a.data_type,
                                         a.qty_type,
                                         a.null_check,
                                         a.valid_start,
                                         a.valid_end,
                                       a.size,
                                       a.bit_index
                                ORDER BY start_add,
                                         a.val_field", tag_id, exe_order, start_idx);
                dt = GetDataTable(sql);
            }
            catch (Exception ex)
            {
                throw;
            }
            return dt;
        }
        /// <summary>
        /// 根据设备编号，查找设备相关的信息
        /// </summary>
        /// <param name="device_id"></param>
        /// <returns></returns>
        public DataRow GetDeviceInfoById(string device_id)
        {
            DataRow deviceInfo = null;
            DataTable dt = null;
            try
            {
                string sql = string.Format(@"SELECT company_id, plant_id, line, region_no, station_no, driver_id, 
id as device_id, device_type_id, device_name, class_name as device_class_name, address as ip, time_out 
from iot_device WHERE id = '{0}'", device_id);
                dt = GetDataTable(sql);
                if (dt.Rows.Count == 1)
                    deviceInfo = dt.Rows[0];
            }
            catch (Exception ex)
            {
                throw;
            }
            return deviceInfo;
        }
        /// <summary>
        /// 获取质量数据定义
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station"></param>
        /// <param name="start_idx"></param>
        /// <returns></returns>
        public DataTable GetQtyDef(pms_station_no p, int start_idx)
        {
            DataTable dt = null;
            try
            {
                string sql = string.Format(@"SELECT a.region_no,
                                       a.station_no,
                                       a.val_field,
                                       a.val_des,
                                       a.data_type,
                                       a.qty_type,
                                        MIN(a.plc_add) AS start_add,
                                        MAX(a.plc_add) AS end_add,
                                        (MIN(a.plc_add) - {4}) AS idx_add,
                                       a.null_check,
                                       a.valid_start,
                                       a.valid_end,
                                       a.size,
                                       a.bit_index
                                FROM iot_qty_def a
                                WHERE a.company_id = '{0}'
                                    AND a.plant_id = '{1}'
                                    AND a.line = '{2}'
                                    AND station_no = '{3}'  and enable=1
                                GROUP BY a.region_no,
                                         a.station_no,
                                         a.val_field,
                                         a.val_des,
                                         a.data_type,
                                         a.qty_type,
                                         a.null_check,
                                         a.valid_start,
                                         a.valid_end,
                                       a.size,
                                       a.bit_index
                                ORDER BY start_add,
                                         a.val_field", p.company_id, p.plant_id, p.line, p.station_no, start_idx);
                dt = GetDataTable(sql);
            }
            catch (Exception ex)
            {
                throw;
            }
            return dt;
        }
    }
}