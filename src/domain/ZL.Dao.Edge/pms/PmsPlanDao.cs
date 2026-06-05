using System;
using System.Collections.Generic;
using System.Data;
using SqlSugar;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class PmsPlanDao : Repository<pms_plan>
    {
        PmsPlanListDao pmsPlanListDao = new PmsPlanListDao();   
        public bool isExist(pms_plan one)
        {
            try
            {
                return db.Queryable<pms_plan>()
                    .Any(it => it.company_id == one.company_id && it.plant_id == one.plant_id && it.line == one.line && it.sn == one.sn);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public pms_plan Get(pms_plan one)
        {
            try
            {
                return db.Queryable<pms_plan>()
                    .Where(it => it.company_id == one.company_id && it.plant_id == one.plant_id && it.line == one.line && it.sn == one.sn)
                    .First();
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public pms_plan GetMax(pms_plan one)
        {
            try
            {
                return db.Queryable<pms_plan>()
                    .Where(it => it.company_id == one.company_id && it.plant_id == one.plant_id
                    && it.line == one.line && it.plan_date == one.plan_date)
                    .OrderBy(it => it.list_order, OrderByType.Desc)
                    .First();
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public bool InsertPlanAndList(pms_plan obj, string[] stationArr)
        {
            bool ok = false;
            try
            {
                base.AsTenant().BeginTran();
                var old = Get(obj);
                if (old != null)
                {
                    old.on_mark = "";
                    old.updated_at = DateTime.Now;
                    db.Updateable<pms_plan>(old).ExecuteCommand();
                }
                else
                {
                    var maxPlan = GetMax(obj);
                    if (maxPlan == null) obj.list_order = 1;
                    else
                    {
                        obj.list_order = maxPlan.list_order + 1;
                    }
                    db.Insertable<pms_plan>(obj).ExecuteCommand();
                }


                for (int i = 0; i < stationArr.Length; i++)
                {
                    string station = stationArr[i];
                    var planListObj = new pms_plan_list
                    {
                        company_id = obj.company_id,
                        plant_id = obj.plant_id,
                        line = obj.line,
                        station_no = station,
                        sn = obj.sn,
                        remark = obj.model,
                        can_work_mark = 0,
                        job_completed = 0,
                        repaire_mark = 0,
                        waste_mark = 0,
                        created_at = DateTime.Now,
                        updated_at = DateTime.Now,
                    };
                    pmsPlanListDao.Upsert(planListObj);
                }
                base.AsTenant().CommitTran();
                ok = true;
            }
            catch (Exception ex)
            {
                base.AsTenant().RollbackTran();
                string err = ex.Message;
            }
            return ok;
        }
        /// <summary>
        /// 更新计划的站点
        /// </summary>
        /// <param name="planList"></param>
        /// <param name="station"></param>
        /// <returns></returns>
        public bool UpdatePlanStation(List<pms_plan> planList, string station)
        {
            bool ok = false;
            try
            {
                base.AsTenant().BeginTran();
                foreach (var it in planList)
                {
                    //插入绑定标记
                    it.on_mark = station;
                    it.station_no = station;
                    it.updated_at = DateTime.Now;
                    db.Updateable<pms_plan>(it).ExecuteCommand();
                }
                base.AsTenant().CommitTran();
                ok = true;
            }
            catch (Exception ex)
            {
                base.AsTenant().RollbackTran();
                string err = ex.Message;
            }
            return ok;
        }


        public PfValDto GetPFBySn(pms_public p, string sn)
        {
            PfValDto obj = new PfValDto();
            try
            {
                obj = GetPFBySn(new pms_sn { company_id = p.company_id, plant_id = p.plant_id, line = p.line, sn = sn });
            }
            catch (Exception ex)
            {
                Log("GetPFBySn函数执行错误，错误信息:" + ex.Message);
            }
            return obj;
        }
        public PfValDto GetPFBySn(pms_sn p)
        {
            string sql = string.Empty;
            PfValDto obj = new PfValDto();
            try
            {
                sql = string.Format(@"SELECT I.sn as Sn, I.model as Model, M.catena as Catena, M.short_code as ShortCode, I.order_no as OrderNo, I.plan_date as PlanDate, '' as Now
                                FROM pms_plan I LEFT JOIN bom_model M ON I.company_id = M.company_id AND I.plant_id = M.plant_id AND I.line = M.line AND I.model = M.model
								WHERE  I.company_id = '{0}' AND I.plant_id = '{1}' AND I.line = '{2}' AND sn = '{3}'",
                                p.company_id, p.plant_id, p.line, p.sn);
                obj = db.SqlQueryable<PfValDto>(sql).First();
            }
            catch (Exception ex)
            {
                Log("GetPFBySn函数执行错误，错误信息:" + ex.Message);
            }
            return obj;
        }
        /// <summary>
        /// 获取指定产品序列号的计划信息
        /// </summary>
        public pms_plan getBySn(pms_sn p)
        {
            try
            {
                return db.Queryable<pms_plan>()
                    .Where(i => i.company_id == p.company_id && i.plant_id == p.plant_id &&
                            i.line == p.line && i.sn == p.sn).First();
            }
            catch (Exception ex)
            {
                Log("getBySn函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        public pms_plan GetPmsPlanOnLine(pms_public p)
        {
            try
            {
                return db.Queryable<pms_plan>()
                    .Where(i => i.company_id == p.company_id && i.plant_id == p.plant_id &&
                            i.line == p.line && (i.on_mark == null || i.on_mark == "" || i.on_mark == "0"))
                    .OrderBy(i => new { i.plan_date, i.list_order })
                    .First();
            }
            catch (Exception ex)
            {
                Log("PmsPlanDao.GetPms_Plan函数执行错误, 错误信息：" + ex.Message);
                return null;
            }
        }

        [Obsolete("该方法已被弃用，请使用getBySn代替")]
        public DataTable GetPms_Plan(pms_public p, string sn)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            try
            {
                sql = $"SELECT * FROM pms_plan WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' AND sn = '{sn}'";
                return GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanDao.GetPms_Plan函数执行错误, 错误信息：" + ex.Message);
                return null;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="pms_Plan"></param>
        /// <returns></returns>
        [Obsolete("该方法已被弃用，请使用getBySn代替")]
        public DataTable GetPms_Plan(pms_sn pms_Plan)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            try
            {
                dt = db.Queryable<pms_plan>()
                    .Where(i => i.company_id == pms_Plan.company_id && i.plant_id == pms_Plan.plant_id &&
                            i.line == pms_Plan.line && i.sn == pms_Plan.sn).ToDataTable();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanDao.GetPms_Plan函数执行错误, 错误信息：" + ex.Message);
            }
            return dt;
        }

        /// <summary>
        /// 更新主线上线标记
        /// </summary>
        /// <param name="p"></param>
        public void update_pms_plan_on_mark(pms_station_sn p)
        {
            string sql = string.Empty;
            try
            {
                db.Updateable<pms_plan>(p)
                    .SetColumns(i => new pms_plan
                    {
                        station_no = p.station_no,
                        start_time = SqlFunc.GetDate(),
                        on_mark = "1",
                        updated_at = SqlFunc.GetDate(),
                        updated_by = "admin"
                    })
                    .Where(i => i.company_id == p.company_id && i.plant_id == p.plant_id && i.line == p.line && i.sn == p.sn).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanDao.update_pms_plan_on_mark函数执行错误, 错误信息：" + ex.Message);
            }
        }
        /// <summary>
        /// 根据type（ON, OFF）来更新pms_plan中不同的字段
        /// 目前未同pms_plan_seq进行关联
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="type"></param>
        public void update_pms_plan_mark(pms_station_no p, string sn, string type)
        {
            string sql = string.Empty;
            try
            {
                string planMode = "";
                //未做约束及提醒
                //例如：已经上线，重复更新；
                //未上线，进行下线或下修操作
                if (type == "ON")
                    sql = $"UPDATE pms_plan SET station_no='{p.station_no}',start_time={GetDateStr},on_time={GetDateStr},on_mark=1,updated_at={GetDateStr},updated_by='admin' WHERE company_id='{p.company_id}' AND plant_id = '{p.plant_id}'AND line = '{p.line}'AND sn = '{sn}'";
                if (type == "OFF")
                    sql = $"UPDATE pms_plan SET station_no='{p.station_no}',end_time={GetDateStr},off_time={GetDateStr},off_mark=1,updated_at={GetDateStr},updated_by='admin' WHERE company_id='{p.company_id}' AND plant_id = '{p.plant_id}'AND line = '{p.line}'AND sn = '{sn}' AND on_mark='1'";
                if (type == "RP")
                    sql = $"UPDATE pms_plan SET station_no='{p.station_no}',repair_time={GetDateStr},repair_mark=1,updated_at={GetDateStr},updated_by='admin' WHERE company_id='{p.company_id}' AND plant_id = '{p.plant_id}'AND line = '{p.line}'AND sn = '{sn}' AND om_mark='1'";
                Execute(sql);
                if (planMode == "SEQ")
                {
                    sql = "";
                    Execute(sql);
                }
                Log("工位:" + p.station_no + " 【SQL】" + sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanDao.update_pms_plan_on_mark函数执行错误, 错误信息：" + ex.Message);
            }
        }

        /// <summary>
        /// 更新计划开始时间
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        public void update_pms_plan_start_time(pms_station_no p, string sn)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE  pms_plan
                    SET     station_no = '{3}' ,
                            start_time = {5} ,
                            updated_at = {5} ,
                            updated_by = 'lima'
                    WHERE   company_id = '{0}'
                            AND plant_id = '{1}'
                            AND line = '{2}'
                            AND sn = '{4}';",
                   p.company_id, p.plant_id, p.line, p.station_no, sn, GetDateStr);
                Execute(sql);

                Log("工位:" + p.station_no + " 【SQL】" + sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanDao.update_pms_plan_start_time函数执行错误, 错误信息：" + ex.Message);

            }
        }

        /// <summary>
        /// 更新差速器上线标记
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        public void update_pms_plan_csq_on_mark(pms_station_no p, string sn)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE  pms_plan
                                            SET     station_no = '{3}' ,
                                                    csq_on_mark = 1 ,
                                                    updated_at = {5} ,
                                                    updated_by = 'admin'
                                            WHERE   company_id = '{0}'
                                                  AND plant_id = '{1}'
                                                  AND line = '{2}'
                                                  AND sn = '{4}';",
                p.company_id, p.plant_id, p.line, p.station_no, sn, GetDateStr);
                GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanDao.update_pms_plan_csq_on_mark函数执行错误, 错误信息：" + ex.Message);
            }
        }
        public void update_pms_plan_csq_on_mark(pms_station_sn pms_Plan)
        {
            try
            {
                //db.Updateable<pms_plan>(pms_Plan)
                //    .SetColumns(i => new pms_plan { station_no = pms_Plan.station_no, csq_on_mark = "1", updated_at = SqlFunc.GetDate(), updated_by = "admin" })
                //    .Where(i => i.company_id == pms_Plan.company_id && i.plant_id == pms_Plan.plant_id && i.line == pms_Plan.line && i.sn == pms_Plan.sn).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log("PmsPlanDao.update_pms_plan_csq_on_mark函数执行错误, 错误信息：" + ex.Message);
            }
        }
        //public   void update_pms_plan_station_no(string StationNo, string sn)
        //{
        //    update_pms_plan_station_no(Constants.CompanyId, Constants.PlantId, Constants.Line, StationNo, sn);
        //}

        /// <summary>
        /// 更新plan表当前工位
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        public void update_pms_plan_station_no(pms_station_no p, string sn)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE pms_plan
                                            SET station_no = '{3}',
                                             -- start_time = {5},
                                             updated_at = {5},
                                             updated_by = 'admin'
                                            WHERE company_id = '{0}'
                                          AND plant_id = '{1}'
                                          AND line = '{2}'
                                          AND sn = '{4}';",
               p.company_id, p.plant_id, p.line, p.station_no, sn, GetDateStr);
                GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanDao.update_pms_plan_station_no函数执行错误, 错误信息：" + ex.Message);
            }
        }
        public void update_pms_plan_station_no(pms_station_sn pms_Plan)
        {
            string sql = string.Empty;
            try
            {
                db.Updateable<pms_plan>(pms_Plan)
                    .SetColumns(i => new pms_plan { station_no = pms_Plan.station_no, updated_at = SqlFunc.GetDate(), updated_by = "admin" })
                    .Where(i => i.company_id == pms_Plan.company_id && i.plant_id == pms_Plan.plant_id &&
                            i.line == pms_Plan.line && i.sn == pms_Plan.sn).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsPlanDao.update_pms_plan_station_no函数执行错误, 错误信息：" + ex.Message);
            }
        }

        public DataTable GetPms_Plan_OP10(pms_station_no pms_Plan)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            try
            {
                var query = db.Queryable<pms_plan>()
                    .Where(i => i.line == pms_Plan.line && i.company_id == pms_Plan.company_id && i.on_mark == "0" && i.priority_mark == 0)
                    .Where(a => SqlFunc.Subqueryable<pms_plan_list>().Where(s => s.company_id == a.company_id && s.plant_id == a.plant_id &&
                             s.line == a.line && s.station_no == a.station_no && s.sn == a.sn && s.waste_mark != 1 && s.can_work_mark == 0).NotAny()).Min<string>(i => i.sn);
                dt = db.Queryable<pms_plan, bom_model>((a, b) => new SqlSugar.JoinQueryInfos(SqlSugar.JoinType.Left, a.model == b.model))
                         .Where(a => a.line == pms_Plan.line && a.plant_id == pms_Plan.plant_id && SqlFunc.Contains(query, a.sn))
                         .OrderBy(a => a.id)
                         .Select((a, b) => new
                         {
                             id = a.id.SelectAll(),
                             short_code = b.short_code,
                             model = b.model,
                             model_name = b.model_name

                         }).Take(1).ToDataTable();
            }
            catch (Exception ex)
            {
                Log("GetPms_Plan_OP10函数执行错误, 错误信息：" + ex.Message);
            }
            return dt;
        }

        public void update_op(pms_part_sn op)
        {
            string sql = string.Empty;
            try
            {
                db.Updateable<op>(op)
                    .SetColumns(i => new op
                    {
                        sn = op.sn,
                        updated_at = SqlFunc.GetDate(),
                        updated_by = "admin"
                    })
                    .Where(i => i.company_id == op.company_id && i.plant_id == op.plant_id && i.line == op.line && i.station_no == op.station_no)
                    .ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log(" OpDao.update_op函数执行错误, 错误信息：" + ex.Message);
            }
        }
    }
}
