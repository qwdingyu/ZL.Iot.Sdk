using System;
using System.Data;
using ZL.DB.Acc;

namespace ZL.Dao.Edge
{
    public class PmsStationDao : DaoBase
    {
        public  DataTable GetAllOPCode(string name)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = "Select station_no From pms_station where station_duty='电动扳手' or station_duty='料架' order by station_no";
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }
        public  DataTable GetStation()
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT
	                                        a.id,
	                                        b.line,
	                                        station_no,
	                                        station_name,
	                                        ip_address,
	                                        plc_address,
	                                        station_duty,
                                        CASE
		                                        Is_downLoad 
		                                        WHEN 0 THEN
		                                        '否' ELSE '是' 
	                                        END Is_downLoad,
                                        CASE
	                                        Is_repair 
	                                        WHEN 0 THEN
	                                        '否' ELSE '是' 
	                                        END Is_repair,
                                        CASE
	                                        Is_opc 
	                                        WHEN 0 THEN
	                                        '否' ELSE '是' 
	                                        END Is_opc,
	                                        station_mark,
	                                        station_beat,
	                                        remark 
                                        FROM
	                                        pms_station a
	                                        LEFT JOIN bom_line b ON a.line = b.line");
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }


        public  bool CheckOPIsExit(string station_no)
        {
            bool result;
            string sql;
            try
            {
                sql = string.Format($"select count(*) from pms_station where station_no='{station_no}'");
                int count = db.GetInt(sql);
                if (count == 0)
                {
                    result = false;
                }
                else
                {
                    result = true;
                }
            }
            catch (Exception ex)
            {
                result = false;
            }
            return result;
        }
        public  bool AddstationInfo(string formName, pms_station obj)
        {
            bool result = false;
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"Insert into pms_station( 
                                                station_no,
                                                ip_address,
                                                plc_address,
                                                station_name,
                                                station_duty,
                                                station_beat,
                                                Is_downLoad,
                                                Is_repair,
                                                Is_opc,
                                                station_mark,
                                                remark,
                                                line) Values
                                      ('{0}','{1}','{2}','{3}','{4}','{5}','{6}','{7}','{8}','{9}','{10}','{11}');",
                                            obj.station_no,
                                            obj.ip_address,
                                            obj.plc_address,
                                            obj.station_name,
                                            obj.station_duty,
                                            obj.station_beat,
                                            obj.Is_downLoad,
                                            obj.Is_repair,
                                            obj.Is_opc,
                                            obj.station_mark,
                                            obj.remark,
                                            obj.line);
                db.Execute(sql);
                result = true;
            }
            catch (Exception ex)
            {
                result = false;
            }
            return result;
        }
        public  bool UpdatelineInfo(string formName, bom_line objline)
        {
            bool result = false;
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"update bom_line set line='{0}',
                                        line_name='{1}' where id='{2}'",
                                            objline.line,
                                            objline.line_name,
                                            objline.id);
                db.Execute(sql);
                result = true;
            }
            catch (Exception ex)
            {
                result = false;
            }
            return result;
        }
        public  bool DellineInfo(string formName, string id)
        {
            bool result = false;
            string sql = string.Empty;
            try
            {
                sql = string.Format($"Delete From bom_line where id='{id}'");
                db.Execute(sql);
                result = true;
            }
            catch (Exception ex)
            {
                result = false;
            }
            return result;
        }
    }
}
