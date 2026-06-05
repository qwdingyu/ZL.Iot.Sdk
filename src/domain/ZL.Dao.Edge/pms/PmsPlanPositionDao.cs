using System;
using System.Data;
using SqlSugar;
using ZL.DB.Acc;
using ZL.PFLite;
//using ZLApi.Reflection;

namespace ZL.Dao.Edge
{
    public class PmsPlanPositionDao : DaoBase
    {
        /// <summary>
        /// 删除过点
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        public  void Delete_position(pms_station_no p, string sn, string qrcode)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = string.Format(@"delete from pms_plan_position where company_id='{0}' and plant_id='{1}' and line='{2}' and station_no='{3}' and sn='{4}' and part_sn='{5}'", p.company_id, p.plant_id, p.line, p.station_no, sn, qrcode);
                db?.Execute(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanPositionDao.Delete_position函数执行错误, 错误信息：" + ex.Message);
            }
        }
        //[HslMqttApi(HttpMethod = "POST", Description = "添加pms_plan_position数据")]
        public  void Delete_position(pms_part_sn pms_plan_position)
        {
            try
            {
                db.Deleteable<pms_plan_position>(pms_plan_position).
                    Where(i => i.company_id == pms_plan_position.company_id && i.plant_id == pms_plan_position.plant_id).
                    Where(i => i.line == pms_plan_position.line && i.station_no == pms_plan_position.station_no).
                    Where(i => i.sn == pms_plan_position.sn && i.part_sn == pms_plan_position.part_sn).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log("PmsPlanPositionDao.Delete_position函数执行错误, 错误信息：" + ex.Message);
            }
        }

        /// <summary>
        /// 添加过点信息
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="code"></param>
        /// <param name="work_time"></param>
        /// <param name="operater"></param>
        public  void Insert_pms_plan_position(pms_station_no p, string sn, string code, string operater)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"INSERT  INTO pms_plan_position
                    ( company_id ,
                      plant_id , 
                      line ,
                      station_no ,
                      sn ,
                      part_sn ,
                      start_time ,
                      created_at ,
                      created_by
                    )
            VALUES  ( '{0}' ,
                      '{1}' ,
                      '{2}' ,
                      '{3}' ,
                      '{4}' ,
                      '{5}' ,
                      {6} ,
                      {6} ,
                      '{7}'
                    );", p.company_id, p.plant_id, p.line, p.station_no, sn, code, GetDateStr, operater);
                db?.Execute(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanPositionDao.Insert_pms_print_code函数执行错误, 错误信息：" + ex.Message);
            }
        }

        //[HslMqttApi(HttpMethod = "POST", Description = "添加pms_plan_position表")]
        public  pms_plan_position Insert_pms_plan_position(pms_plan_position entity)
        {
            try
            {
                entity.created_at = SqlFunc.GetDate();
                long max = db.Queryable<pms_plan_position>().Max(it => it.id);
                max = max + 1;
                entity.id = max;
                return db.Insertable<pms_plan_position>(entity).ExecuteReturnEntity();
            }
            catch (Exception ex)
            {
                Log("PmsPlanPositionDao.Insert_pms_print_code函数执行错误, 错误信息：" + ex.Message);
                return null;
            }
        }

        public  DataTable select_pms_plan_position(pms_station_no p, string sn)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = string.Format(@"select * from pms_plan_position 
                            WHERE   company_id = '{0}'
                          AND plant_id = '{1}'
                          AND line = '{2}'
                          AND sn = '{4}'
                          AND station_no = '{3}';", p.company_id, p.plant_id, p.line, p.station_no, sn);
                dt = db?.GetDataTable(sql);
                return dt;
            }
            catch (Exception ex)
            {
                Log("PmsPlanPositionDao.select_pms_plan_position_OP20函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        /// <summary>
        /// 修改过点信息
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="ok_mark"></param>
        /// <param name="work_time"></param>
        /// <param name="operater"></param>
        public  void update_pms_plan_position(pms_station_no p, string sn, string Part_Sn, int ok_mark, string operater)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"select count(*) from pms_plan_position 
                            WHERE   company_id = '{0}'
                          AND plant_id = '{1}'
                          AND line = '{2}'
                          AND sn = '{4}'
                          AND station_no = '{3}';", p.company_id, p.plant_id, p.line, p.station_no, sn);
                int temp = int.Parse(db?.GetScalar(sql).ToString());
                if (temp == 0)
                {
                    sql = string.Format(@"INSERT  INTO pms_plan_position
                    ( company_id ,
                      plant_id ,
                      sn ,
                      line ,
                      part_sn ,
                      station_no ,
                      ok_mark ,
                      start_time ,
                      created_at ,
                      created_by
                    )
            VALUES  ( '{0}' ,
                      '{1}' ,
                      '{4}' ,
                      '{2}' ,
                      '{5}' ,
                      '{3}' ,
                      '{8}' ,
                      {6} ,
                      {6} ,
                      '{7}'
                    );", p.company_id, p.plant_id, p.line, p.station_no, sn, Part_Sn, GetDateStr, operater, ok_mark);

                }
                else
                {

                    sql = string.Format(@" UPDATE  pms_plan_position
            SET ok_mark = '{5}', end_time = {6}, updated_at = {6}, updated_by = '{7}'
            WHERE   company_id = '{0}'
                  AND plant_id = '{1}'
                  AND line = '{2}'
                  AND sn = '{4}'
                  AND station_no = '{3}';",
                        p.company_id, p.plant_id, p.line, p.station_no, sn, ok_mark, GetDateStr, operater);
                }
                db?.Execute(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanPositionDao.update_pms_plan_position函数执行错误, 错误信息：" + ex.Message);
            }
        }

        //[HslMqttApi(HttpMethod = "POST", Description = "更新pms_plan_position表")]
        public  void update_pms_plan_position(pms_station_sn pms_plan_position, int ok_mark, string work_time, string updated_by)
        {
            if (pms_plan_position == null)
            {
                Log("PmsPlanPositionDao.update_pms_plan_position函数执行时pms_plan_position参数为空");
                return;
            }
            try
            {
                db.Updateable<pms_plan_position>()
                    .SetColumns(it => new pms_plan_position { end_time = SqlFunc.GetDate(), ok_mark = ok_mark, updated_at = SqlFunc.GetDate(), updated_by = updated_by })
                    .Where(i => i.company_id == pms_plan_position.company_id && i.plant_id == pms_plan_position.plant_id && i.line == pms_plan_position.line &&
                            i.station_no == pms_plan_position.station_no).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log("PmsPlanPositionDao.update_pms_plan_position函数执行错误, 错误信息：" + ex.Message);
            }
        }

        /// <summary>
        /// 查询最新的合格产品
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <returns></returns>

        public  DataTable select_pms_plan_position_OP20(pms_station_no p)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                if (dbType == SqlSugar.DbType.MySql)
                    sql = string.Format(@"SELECT
	                                station_no,
	                                sn,
	                                part_sn,
	                                start_time,
	                                end_time,
	                                ok_mark
                                FROM
	                                pms_plan_position
                                WHERE
	                                company_id = '{0}'
                              AND plant_id = '{1}'
                              AND line = '{2}'
                              AND station_no = '{3}'
                              AND ok_mark = 1
                              AND start_time IS NOT NULL
                              AND end_time IS NOT NULL
                                ORDER BY
	                                created_at DESC  limit 1;", p.company_id, p.plant_id, p.line, p.station_no);
                else if (dbType == SqlSugar.DbType.SqlServer)
                    sql = string.Format(@"SELECT
                                     top 1
	                                station_no,
	                                sn,
	                                part_sn,
	                                start_time,
	                                end_time,
	                                ok_mark
                                FROM
	                                pms_plan_position
                                WHERE
	                                company_id = '{0}'
                              AND plant_id = '{1}'
                              AND line = '{2}'
                              AND station_no = '{3}'
                              AND ok_mark = 1
                              AND start_time IS NOT NULL
                              AND end_time IS NOT NULL
                                ORDER BY
	                                created_at DESC  ;", p.company_id, p.plant_id, p.line, p.station_no);
                dt = db?.GetDataTable(sql);
                return dt;
            }
            catch (Exception ex)
            {
                Log("PmsPlanPositionDao.select_pms_plan_position_OP20函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }

        public  DataTable select_pms_plan_position_OP20(pms_station_sn pms_plan_position)
        {
            return db.Queryable<pms_plan_position>().
                 Where(i => i.company_id == pms_plan_position.company_id).
                 Where(i => i.plant_id == pms_plan_position.plant_id && i.line == pms_plan_position.line && i.station_no == pms_plan_position.station_no).
                 Where(i => i.ok_mark == 1 && i.start_time != null && i.end_time != null).
                 OrderBy(i => i.created_at, SqlSugar.OrderByType.Desc).
                 Take(1).ToDataTable();
        }

        public  DataTable pms_postion_qrcode(pms_station_no p)
        {
            try
            {
                DataTable dt = new DataTable();
                string sql = string.Empty;
                {
                    sql = string.Format(@"SELECT
	TOP 1 *
FROM
	pms_plan_position
WHERE
  company_id='{0}'
AND plant_id='{1}'
AND line='{2}'
AND	station_no = '{3}'
AND ok_mark = 0
AND start_time IS NOT NULL
AND end_time IS NULL
ORDER BY
	created_at DESC", p.company_id, p.plant_id, p.line, p.station_no);
                    dt = db?.GetDataTable(sql);
                }
                return dt;
            }
            catch (Exception ex)
            {
                Log("pms_postion_qrcode函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
    }
}
