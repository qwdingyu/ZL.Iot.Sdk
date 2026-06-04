using System;
using System.Data;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 主界面初始化数据展示
    /// </summary>
    public class IotAlarmDao : DaoBase
    {
        /// <summary>
        /// 新增报警记录
        /// </summary>
        /// <param name="company_id">公司</param>
        /// <param name="plant_id">工厂</param>
        /// <param name="line">装配线</param>
        /// <param name="station_no">工位号</param>
        /// <param name="comp_no">零件号</param>
        /// <param name="comp_name">零件名称</param>
        /// <param name="user"></param>
        public  void AddData(pms_station_no p, string model, string comp_no, string comp_name, string user)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = string.Format(@"INSERT INTO iot_alarm
                                        ( company_id ,
                                        plant_id ,
                                        line ,
                                        station_no ,
                                        alarm_type ,
                                        model,
                                        comp_no ,
                                        comp_name ,
                                        start_time ,
                                        created_at ,
                                        created_by ,
                                        updated_at ,
                                        updated_by)
                                        VALUES  ( '{0}' ,
                                        '{1}' ,
                                        '{2}' ,
                                        '{3}' ,
                                        1 ,
                                        '{7}' ,
                                        '{4}' ,
                                        '{5}' ,
                                        {8}  ,
                                        {8} ,
                                        '{6}' ,
                                        {8} ,
                                        '{6}')", p.company_id, p.plant_id, p.line, p.station_no, comp_no, comp_name, user, model, GetDateStr);
                Execute(sql);
            }
            catch (Exception ex)
            {
                Log("iot_alarm.AddData函数执行错误，错误信息:" + ex.Message);
                Log("sql :" + sql);
            }
        }

        /// <summary>
        /// 更新报警信息
        /// </summary>
        /// <param name="company_id">公司</param>
        /// <param name="plant_id">工厂</param>
        /// <param name="line">装配线</param>
        /// <param name="station_no">工位号</param>
        /// <param name="comp_no">零件号</param>
        /// <param name="comp_name">零件名称</param>
        /// <param name="user">操作者</param>
        public  void UpdateData(pms_station_no p, string model, string comp_no, string comp_name, string user)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE iot_alarm SET
                                        end_time = {7},
                                        updated_at = {7},
                                        updated_by = '{0}'
		                                WHERE  company_id = '{1}'
                                      AND plant_id = '{2}'
                                      AND line = '{3}'
                                      AND station_no = '{4}'
                                      AND alarm_type = 1
                                      AND comp_no = '{5}'
                                      AND comp_name = '{6}'", user, p.company_id, p.plant_id, p.line, p.station_no, comp_no, comp_name, GetDateStr);
                Execute(sql);
            }
            catch (Exception ex)
            {
                Log("Aiot_alarm.UpdateData函数执行错误，错误信息:" + ex.Message);
                Log("sql :" + sql);
            }
        }

        /// <summary>
        /// 获取工位报警信息
        /// </summary>
        /// <param name="station_no">工位号</param>
        /// <returns></returns>
        public  DataTable get_ifs_alarm(string station_no)
        {
            DataTable dt = new DataTable();
            string sql = string.Format(@"select * from iot_alarm where  alarm_type = '1' and (end_time = '' or end_time is NULL) and station_no ='{0}'", station_no);
            dt = db?.GetDataTable(sql);
            return dt;
        }

        public  DataTable Query(string StationNo, string start, string end)
        {
            DataTable dt = new DataTable();
            string sql = $"select * from iot_alarmlog where station_no='{StationNo}' and created_at between '{start}' and '{end}' order by created_at desc";
            dt = db?.GetDataTable(sql);
            return dt;
        }
    }
}

