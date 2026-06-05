using Newtonsoft.Json;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Data;
using ZL.DB.Acc;
using ZL.PFLite;
using ZL.PFLite.Common;

namespace ZL.Dao.Edge
{
    public class PrintQueueDao : DaoBase
    {
        static BomBarCodeDao bomBarCodeDao = new BomBarCodeDao();
        static BomStationDao bomStationDao = new BomStationDao();
        static PmsPlanDao pmsPlanDao = new PmsPlanDao();
        /// <summary>
        /// 
        /// </summary>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <returns></returns>

        public string not_Print_barcode(string line, string station_no)
        {
            string sql = string.Empty;
            if (dbType == SqlSugar.DbType.MySql)
                sql = $"SELECT barcode FROM print_queue WHERE line = '{line}'AND station_no = '{station_no}'AND printed = '0' order by created_at LIMIT 0, 1";
            if (dbType == SqlSugar.DbType.SqlServer)
                sql = $"SELECT TOP 1 barcode FROM print_queue WHERE line = '{line}' AND station_no = '{station_no}' AND printed = '0'  ORDER BY created_at";

            return GetScalar<string>(sql);
        }
        public print_queue not_Print_barcode1(string line, string station_no)
        {
            return db.Queryable<print_queue>()
                .Where(i => i.line == line && i.station_no == station_no && i.printed == "0").OrderBy(i => i.created_at).Take(1).First();
        }

        public DataTable GetPrintLog(string line, string station_no, string barcode)
        {
            return db.Queryable<print_queue>().Where(i => i.line == line && i.station_no == station_no && i.barcode == barcode).ToDataTable();
        }
        public DataTable GetPrintLog(pms_station_no p, string barcode)
        {
            return db.Queryable<print_queue>()
                .Where(i => i.company_id == p.company_id && i.plant_id == p.plant_id && i.line == p.line
                    && i.station_no == p.station_no && i.barcode == barcode)
                .ToDataTable();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="comp_no"></param>
        /// <param name="print_vals"></param>
        /// <param name="barcode"></param>
        /// <param name="list_order"></param>
        /// <param name="operater"></param>
        /// <returns></returns>
        public int AddPrintQueue(pms_station_no p, string barcode, string print_def_id, string print_vals, string operater)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"INSERT INTO print_queue (company_id, plant_id, line, station_no, barcode, print_def_id, print_vals, list_order, printed, created_at, created_by )
                        SELECT '{0}', '{1}', '{2}', '{3}','{4}', '{5}',  '{6}', 
                            COALESCE ((SELECT MAX(list_order ) + 1 FROM print_queue WHERE company_id = '{0}' AND plant_id = '{1}' AND line = '{2}' AND station_no = '{3}'), 1 ) as list_order, 
                            '0',  {7}, '{8}'", p.company_id, p.plant_id, p.line, p.station_no, barcode, print_def_id, print_vals, GetDateStr, operater);
                Execute(sql);
                return 0;
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                return 1;
            }
        }
        public int AddPrintQueue(pms_station_no p, string sn, string operater = "admin")
        {
            string sql = string.Empty;
            try
            {
                var StationInfo = bomStationDao.GetByStationNo(p);
                string print_def_id = StationInfo.bom_barcodeid;
                return AddPrintQueue(p, sn, print_def_id, operater);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                return 1;
            }
        }
        public int AddPrintQueue(pms_station_no p, string sn, string print_def_id, string operater = "admin")
        {
            string sql = string.Empty;
            try
            {
                string print_vals = "";
                bom_barcode bomBarCode = bomBarCodeDao.GetById(print_def_id);
                print_vals = bomBarCode.print_vals;
                if (!string.IsNullOrEmpty(print_vals))
                {
                    try
                    {
                        //JavaScriptSerializer serializer = new JavaScriptSerializer();
                        //Dictionary<string, object> jsonDic = (Dictionary<string, object>)serializer.DeserializeObject(print_vals);
                        Dictionary<string, object> jsonDic = JsonConvert.DeserializeObject<Dictionary<string, object>>(print_vals);
                        var pfInfo = pmsPlanDao.GetPFBySn(p, sn);
                        Dictionary<string, string> valDic = DicKit.ObjectToDic(pfInfo);

                        Dictionary<string, string> jsonValDic = new Dictionary<string, string>();
                        foreach (string key in jsonDic.Keys)
                        {
                            string val = "";
                            valDic.TryGetValue(key, out val);
                            if (string.IsNullOrEmpty(val))
                                valDic.TryGetValue(key.ToUpper(), out val);
                            //如果未获取到值，使用???代替，避免出现JsonConvert的 "" 转换为 null的情况
                            if (string.IsNullOrEmpty(val))
                                val = "???";
                            if (!jsonValDic.ContainsKey(key))
                            {
                                jsonValDic.Add(key, val);
                            }
                        }
                        print_vals = JsonConvert.SerializeObject(jsonValDic);
                    }
                    catch (Exception ex1)
                    {

                    }
                    //根据sn组装相关的打印数据--解析方式跟配方参数保持一致
                }
                sql = string.Format(@"INSERT INTO print_queue (company_id, plant_id, line, station_no, barcode, print_def_id, print_vals, list_order, printed, created_at, created_by )
                        SELECT '{0}', '{1}', '{2}', '{3}','{4}', '{5}',  '{6}', 
                            COALESCE ((SELECT MAX(list_order ) + 1 FROM print_queue WHERE company_id = '{0}' AND plant_id = '{1}' AND line = '{2}' AND station_no = '{3}'), 1 ) as list_order, 
                            '0',  {7}, '{8}'", p.company_id, p.plant_id, p.line, p.station_no, sn, print_def_id, print_vals, GetDateStr, operater);
                Execute(sql);
                return 0;
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                return 1;
            }
        }

        public int AddPrintLog(pms_station_no p, string comp_no, string print_vals, string barcode, int list_order, string sn, string user)
        {
            string sql = string.Empty;
            try
            {
                sql = $"INSERT INTO print_queue (company_id, plant_id, line, station_no, barcode, comp_no, print_vals, list_order, print_time, printed, print_def_id, created_at, created_by,sn ) VALUES ({p.company_id}','{p.plant_id}','{p.line}', '{p.station_no}', '{barcode}', '{comp_no}', '{print_vals}', '{list_order}', {GetDateStr}, '0', '6', {GetDateStr}, '{user}','{sn}')";
                Execute(sql);
                return 0;
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                return 1;
            }
        }
        public int AddPrintLog(pms_station_no p, string comp_no, string print_vals, string barcode, int list_order, string user)
        {
            string sql = string.Empty;
            try
            {
                sql = $"INSERT INTO print_queue (company_id, plant_id, line, station_no, barcode, comp_no, print_vals, list_order, print_time, printed, print_def_id, created_at, created_by ) VALUES ('{p.company_id}','{p.plant_id}','{p.line}', '{p.station_no}', '{barcode}', '{comp_no}', '{print_vals}', '{list_order}', '{user}', '0', '6', {GetDateStr}, '{user}')";
                Execute(sql);
                return 0;
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                return 1;
            }
        }
        public bool AddPrintLog(print_queue print_Queue)
        {
            try
            {
                print_Queue.print_time = SqlFunc.GetDate();
                print_Queue.printed = "0";
                print_Queue.print_def_id = 6;
                print_Queue.created_at = SqlFunc.GetDate();
                return db.Insertable<print_queue>(print_Queue).ExecuteCommandIdentityIntoEntity();
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                return false;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="comp_no"></param>
        /// <returns></returns>

        public DataTable select_GetPrintLog(string line, string station_no, string comp_no)
        {
            string sql = string.Empty;
            Tuple<string, string> dayStartEnd = GetDateStartEnd();
            DataTable dt = new DataTable();
            sql = $"SELECT * FROM print_queue where line='{line}' AND station_no='{station_no}' AND comp_no='{comp_no}' AND  created_at between '{dayStartEnd.Item1}' and '{dayStartEnd.Item2}'  ORDER BY created_at DESC";
            dt = db?.GetDataTable(sql);
            return dt;
        }

        public DataTable select_GetPrintLog1(string line, string station_no, string comp_no)
        {
            try
            {
                DateTime time = SqlFunc.GetDate();
                return db.Queryable<print_queue>()
                    .Where(i => i.line == line && i.station_no == station_no && i.comp_no == comp_no && i.created_at == time)
                    .OrderBy(i => i.created_at, SqlSugar.OrderByType.Desc).ToDataTable();
            }
            catch (SqlSugarException ex)
            {
                Log("select_GetPrintLog函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        public void updatePrintLog(string line, string station_no, string barcode)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"update  print_queue set printed='1',  updated_at={3} where line='{0}'   AND station_no='{1}' and barcode='{2}'", line, station_no, barcode, GetDateStr);
                Execute(sql);
            }
            catch (Exception)
            {
            }
        }
        public void updatePrintLog1(string line, string station_no, string barcode)
        {
            try
            {
                db.Updateable<print_queue>()
                    .SetColumns(i => new print_queue { printed = "1", updated_at = SqlFunc.GetDate() })
                    .Where(i => i.station_no == station_no && i.barcode == barcode).ExecuteCommand();
            }
            catch (SqlSugarException ex)
            {
                Log("updatePrintLog函数执行错误，错误信息:" + ex.Message);
            }
        }
        public DataTable Select_print_bd(string where)
        {

            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT
	id,
	line,
	station_no,
	barcode,
	print_vals,
	(CASE printed WHEN '1' THEN '已打印'
 WHEN '0' THEN '未打印' END) printed
FROM
	print_queue
WHERE 1=1 {0}", where);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }

        public void print_queue_update(string station_no, string barcode)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE print_queue
SET printed = 0
WHERE
	station_no = '{0}'
AND barcode = '{1}'", station_no, barcode, GetDateStr);

                db?.Execute(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("print_queue_update函数执行错误, 错误信息：" + ex.Message);
            }
        }

        public DataTable select_print_queue(string line, string station_no, string part_sn)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"SELECT
	*
FROM
	print_queue
WHERE
line='{0}'
AND	station_no = '{1}'
AND sn = '{2}'", line, station_no, part_sn);
                dt = db?.GetDataTable(sql);
                return dt;
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("select_print_queue函数执行错误, 错误信息：" + ex.Message);
                return null;
            }

        }
    }
}
