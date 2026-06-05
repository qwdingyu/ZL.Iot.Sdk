using System;
using System.Collections.Generic;
using System.Data;
using SqlSugar;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class PmsPlanListDao : Repository<pms_plan_list>
    {

        public bool isExist(pms_station_sn ps, int can_work_mark = -1)
        {
            try
            {
                if (can_work_mark == -1)
                    return db.Queryable<pms_plan_list>()
                    .Any(it => it.company_id == ps.company_id && it.plant_id == ps.plant_id && it.line == ps.line && it.sn == ps.sn && it.station_no == ps.station_no);
                else
                    return db.Queryable<pms_plan_list>()
                    .Any(it => it.company_id == ps.company_id && it.plant_id == ps.plant_id && it.line == ps.line 
                        && it.sn == ps.sn && it.station_no == ps.station_no && it.can_work_mark == can_work_mark);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public bool isExist(pms_plan_list one)
        {
            try
            {
                return db.Queryable<pms_plan_list>()
                    .Any(it => it.company_id == one.company_id && it.plant_id == one.plant_id && it.line == one.line && it.sn == one.sn && it.station_no == one.station_no);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public bool Upsert(pms_plan_list obj)
        {
            bool ok = false;
            try
            {
                var oldPlanListObj = Get(obj);
                if (oldPlanListObj != null)
                {
                    //如果之前数据库有该记录，则直接更新该字段未初始状态
                    oldPlanListObj.can_work_mark = 0;
                    oldPlanListObj.job_completed = 0;
                    oldPlanListObj.updated_at = DateTime.Now;
                    db.Updateable<pms_plan_list>(oldPlanListObj).ExecuteCommand();
                }
                else
                    db.Insertable<pms_plan_list>(obj).ExecuteCommand();
                ok = true;
            }
            catch (Exception ex)
            {
                throw;
            }
            return ok;
        }
        public List<pms_plan_list> Get(pms_station_sn ps, int can_work_mark = -1)
        {
            try
            {
                if (can_work_mark == -1)
                    return db.Queryable<pms_plan_list>()
                        .Where(it => it.company_id == ps.company_id && it.plant_id == ps.plant_id && it.line == ps.line
                            && it.sn == ps.sn && it.station_no == ps.station_no)
                        .ToList();
                else
                    return db.Queryable<pms_plan_list>()
                        .Where(it => it.company_id == ps.company_id && it.plant_id == ps.plant_id && it.line == ps.line
                            && it.sn == ps.sn && it.station_no == ps.station_no && it.can_work_mark == can_work_mark)
                        .ToList();
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public pms_plan_list Get(pms_station_no ps, int can_work_mark = -1)
        {
            try
            {
                if (can_work_mark == -1)
                    return db.Queryable<pms_plan_list>()
                        .Where(it => it.company_id == ps.company_id && it.plant_id == ps.plant_id && it.line == ps.line
                            && it.station_no == ps.station_no)
                        .First();
                else
                    return db.Queryable<pms_plan_list>()
                        .Where(it => it.company_id == ps.company_id && it.plant_id == ps.plant_id && it.line == ps.line
                            && it.station_no == ps.station_no && it.can_work_mark == can_work_mark)
                        .First();
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public pms_plan_list Get(pms_plan_list one, int can_work_mark = -1)
        {
            try
            {
                if (can_work_mark == -1)
                    return db.Queryable<pms_plan_list>()
                        .Where(it => it.company_id == one.company_id && it.plant_id == one.plant_id && it.line == one.line
                            && it.sn == one.sn && it.station_no == one.station_no)
                        .First();
                else
                    return db.Queryable<pms_plan_list>()
                        .Where(it => it.company_id == one.company_id && it.plant_id == one.plant_id && it.line == one.line
                            && it.sn == one.sn && it.station_no == one.station_no && it.can_work_mark == can_work_mark)
                        .First();
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public void Update(int id, int can_work_mark)
        {
            string sql = string.Empty;
            try
            {
                var obj = db.Queryable<pms_plan_list>().First(i => i.id == id);
                if (obj == null) return;
                obj.can_work_mark = (byte)can_work_mark;
                obj.end_time = SqlFunc.GetDate();
                obj.updated_at = SqlFunc.GetDate();
                db.Updateable<pms_plan_list>(obj).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("Pms_Plan_List.Update 函数执行错误, 错误信息：" + ex.Message);
            }
        }
        public void Update(pms_station_no p, string sn, int can_work_mark)
        {
            string sql = string.Empty;
            try
            {
                var obj = db.Queryable<pms_plan_list>().First(i => i.company_id == p.company_id && i.plant_id == p.plant_id && i.line == p.line && i.sn == sn);
                if (obj == null) return;
                obj.can_work_mark = (byte)can_work_mark;
                obj.end_time = SqlFunc.GetDate();
                obj.updated_at = SqlFunc.GetDate();
                db.Updateable<pms_plan_list>(obj).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("Pms_Plan_List.Update 函数执行错误, 错误信息：" + ex.Message);
            }
        }

        public void Update(pms_station_sn p, int can_work_mark)
        {
            string sql = string.Empty;
            try
            {
                var obj = db.Queryable<pms_plan_list>().First(i => i.company_id == p.company_id && i.plant_id == p.plant_id && i.line == p.line && i.sn == p.sn);
                if (obj == null) return;
                obj.can_work_mark = (byte)can_work_mark;
                obj.end_time = SqlFunc.GetDate();
                obj.updated_at = SqlFunc.GetDate();
                db.Updateable<pms_plan_list>(obj).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("Pms_Plan_List.Update 函数执行错误, 错误信息：" + ex.Message);
            }
        }

        public void Insert_pms_plan_list_OP80(pms_plan_list pms_Plan_List)
        {
            try
            {
                pms_Plan_List.station_no = "OP80";
                pms_Plan_List.start_time = SqlFunc.GetDate();
                pms_Plan_List.Is_first_station_no = 1;
                pms_Plan_List.created_at = SqlFunc.GetDate();
                pms_Plan_List.can_work_mark = 0;
                db.Insertable<pms_plan_list>(pms_Plan_List).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log("Insert_pms_plan_list_OP800函数执行错误, 错误信息：" + ex.Message);
            }
        }

        /// <summary>
        /// 更新离壳丶差速器can_work_mark标记和完成时间
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="qrcode"></param>
        public void Update_plan_list_on_line(pms_station_no p, string sn, string qrcode)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = $"UPDATE  pms_plan_list SET     can_work_mark = 1 , end_time = {GetDateStr} , updated_at = {GetDateStr} WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}'  AND sn = '{sn}' AND station_no = '{p.station_no}' AND part_sn='{qrcode}';";
                db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("Pms_Plan_List.Update_plan_list_on_line函数执行错误, 错误信息：" + ex.Message);
            }
        }
        public void Update_plan_list_on_line(pms_part_sn ps)
        {
            db.Updateable<pms_plan_list>(ps)
                .SetColumns(it => new pms_plan_list { can_work_mark = 1, end_time = SqlFunc.GetDate(), updated_at = SqlFunc.GetDate() })
                .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id && i.line == ps.line)
                .Where(i => i.station_no == ps.station_no && i.part_sn == ps.part_sn).ExecuteCommand();
        }

        /// <summary>
        /// 更新变壳can_work_mark标记和完成时间
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="qrcode"></param>
        /// 
        public void Update_plan_list_on_line_can(pms_station_no p, string sn, string qrcode)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"select count(*) from pms_plan_list 
                           WHERE   company_id = '{0}'
                                                  AND plant_id = '{1}'
                                                  AND line = '{2}'
                                                  AND sn = '{3}'
                                                  AND station_no = '{4}'", p.company_id, p.plant_id, p.line, sn, p.station_no);
                int temp = int.Parse(db?.GetScalar(sql).ToString());
                if (temp == 0)
                {
                    sql = string.Format(@" INSERT  INTO pms_plan_list
                                ( company_id , plant_id , line ,  sn , station_no ,
                                  Is_first_station_no , created_at , can_work_mark
                                )
                        VALUES  ( '{0}' ,
                                  '{1}' ,
                                  '{2}' ,
                                  '{3}' ,
                                  '{5}' ,
                                  1 ,
                                  {4} ,
                                  0
                                );", p.company_id, p.plant_id, p.line, sn, GetDateStr, p.station_no);
                }
                else
                {

                    sql = string.Format(@"UPDATE  pms_plan_list
                                            SET     can_work_mark = 1 ,
                                                    part_sn = '{5}' ,
                                                    end_time = {6} ,
                                                    updated_at = {6}
                                            WHERE   company_id = '{0}'
                                                  AND plant_id = '{1}'
                                                  AND line = '{2}'
                                                  AND sn = '{3}'
                                                  AND station_no = '{4}';", p.company_id, p.plant_id, p.line, sn, p.station_no, qrcode, GetDateStr);
                }
                db?.Execute(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("Pms_Plan_List.Update_plan_list_on_line_can函数执行错误, 错误信息：" + ex.Message);
            }
        }
        public void Update_plan_list_on_line_can(pms_part_sn ps)
        {
            db.Updateable<pms_plan_list>(ps)
                .SetColumns(it => new pms_plan_list { can_work_mark = 1, end_time = SqlFunc.GetDate(), updated_at = SqlFunc.GetDate(), part_sn = ps.part_sn })
                .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id && i.line == ps.line)
                .Where(i => i.station_no == ps.station_no && i.part_sn == ps.part_sn)
                .ExecuteCommand();
        }

        public void UpdateStartTime(pms_station_sn ps)
        {
            db.Updateable<pms_plan_list>(ps)
                .SetColumns(it => new pms_plan_list { updated_at = SqlFunc.GetDate(), start_time = SqlFunc.GetDate() })
                .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id && i.line == ps.line)
                .Where(i => i.station_no == ps.station_no && i.sn == ps.sn)
                .ExecuteCommand();
        }

        public void UpdateStartTime(pms_station_no ps, string sn)
        {
            db.Updateable<pms_plan_list>(ps)
                .SetColumns(it => new pms_plan_list { updated_at = SqlFunc.GetDate(), start_time = SqlFunc.GetDate() })
                .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id && i.line == ps.line)
                .Where(i => i.station_no == ps.station_no && i.sn == sn)
                .ExecuteCommand();
        }

        public void UpdateStartTime(pms_station_no ps)
        {
            db.Updateable<pms_plan_list>(ps)
                .SetColumns(it => new pms_plan_list { updated_at = SqlFunc.GetDate(), start_time = SqlFunc.GetDate() })
                .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id && i.line == ps.line)
                .Where(i => i.station_no == ps.station_no)
                .ExecuteCommand();
        }


        public void UpdateForRepaire(pms_station_sn ps)
        {
            db.Updateable<pms_plan_list>(ps)
                .SetColumns(it => new pms_plan_list { can_work_mark = 0, part_sn = null, end_time = null, updated_at = SqlFunc.GetDate() })
                .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id && i.line == ps.line && i.sn == ps.sn && i.station_no == ps.station_no)
                .ExecuteCommand();

        }

        public pms_plan_list getCanOnLine(pms_station_no p)
        {
            try
            {
                pms_plan_list one = new pms_plan_list();
                one = db.Queryable<pms_plan_list>()
                    .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                        && it.station_no == p.station_no && it.job_completed == 0)
                    .OrderBy(it => it.created_at)
                    .First();
                return one;
            }
            catch (Exception ex)
            {
                Log("PmsPlanListDao.getCanOnLine函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 是否已经完成
        /// </summary>
        /// <param name="sn"></param>
        /// <param name="station"></param>
        /// <returns></returns>
        public bool IsJobCompleted(pms_station_sn p)
        {
            try
            {
                int count = db.Queryable<pms_plan_list>()
                    .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                        && it.station_no == p.station_no && it.sn == p.sn && it.job_completed == 0)
                    .Count();
                return count == 1 ? true : false;
            }
            catch (Exception ex)
            {
                Log("函数执行错误，错误信息: " + ex.Message);
                return false;
            }
        }
        public bool IsJobCompleted(pms_station_no p, string sn)
        {
            try
            {
                int count = db.Queryable<pms_plan_list>()
                    .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                        && it.station_no == p.station_no && it.sn == sn && it.job_completed == 0)
                    .Count();
                return count == 1 ? true : false;
            }
            catch (Exception ex)
            {
                Log("函数执行错误，错误信息: " + ex.Message);
                return false;
            }
        }
        public bool IsJobCompleted(pms_public p, string sn, string station_no)
        {
            try
            {
                int count = db.Queryable<pms_plan_list>()
                    .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                        && it.station_no == station_no && it.sn == sn && it.job_completed == 0)
                    .Count();
                return count == 1 ? true : false;
            }
            catch (Exception ex)
            {
                Log("函数执行错误，错误信息: " + ex.Message);
                return false;
            }
        }

    }
}