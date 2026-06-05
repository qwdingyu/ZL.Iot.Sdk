using System;
using System.Collections.Generic;
using System.Data;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class BomStationDao : Repository<bom_station>
    {

        SysLineParamsDao sysLineParamsDao = new SysLineParamsDao();
        BomStationSubDao bomStationSubDao = new BomStationSubDao();
        public List<bom_station> QueryList()
        {
            List<bom_station> lsit = new List<bom_station>();
            try
            {
                lsit = db.Queryable<bom_station>().ToList();
            }
            catch
            {
            }
            return lsit;
        }

        public bool Add(bom_station it)
        {
            bool ok = false;
            try
            {
                ok = base.Insert(it);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return ok;
        }

        public bool Del(bom_station it)
        {
            bool ok = false;
            try
            {
                ok = base.Delete(it);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return ok;
        }

        public bool DelById(int id)
        {
            bool ok = false;
            try
            {
                ok = base.DeleteById(id);
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return ok;
        }

        public bom_station GetByStationNo(pms_station_no p)
        {
            bom_station one = new bom_station();
            try
            {
                one = base.AsQueryable()
                    .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.station_no == p.station_no)
                    .First();
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return one;
        }

        /// <summary>
        /// 根据类型获取本条线或区域的工位（例如：上下工位、下线工位、返修工位）
        /// </summary>
        /// <param name="model"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public bom_station GetByType(pms_public p, string type, string region_no = "")
        {
            bom_station one = new bom_station();
            try
            {
                one = base.AsQueryable()
                    .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.pms_type == type && (it.region_no == region_no || region_no == ""))
                    .OrderBy(i => i.list_order)
                    .First();
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return one;
        }

        public string GetStationByType(pms_public p, string type, string region_no = "")
        {
            string tmp = "";
            try
            {
                bom_station o = GetByType(p, type, region_no);
                tmp = (o != null) ? o.station_no : "";
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return tmp;
        }

        /// <summary>
        /// 获取本条线或区域的首工位
        /// </summary>
        /// <param name="model"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public string GetFirst(pms_public p, string model = "", string region_no = "")
        {
            string tmp = "";
            try
            {
                var db = base.AsSugarClient();
                int has = bomStationSubDao.GetCount(p, model, region_no);
                if (has > 0)
                {
                    // --如果子表中按照model维护的有加工工位信息
                    var sub = base.ChangeRepository<Repository<bom_station_sub>>();
                    bom_station_sub o = sub.AsQueryable()
                        .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                            && (it.region_no == region_no || region_no == "") && it.model == model)
                        .OrderBy(i => i.list_order, SqlSugar.OrderByType.Asc)
                        .First();
                    tmp = (o != null) ? o.station_no : "";
                }
                else
                {  // --如果没有维护取默认值，走该region_no下所有的加工工位
                    bom_station one = base.AsQueryable()
                        .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && (it.region_no == region_no || region_no == ""))
                        .OrderBy(i => i.list_order)
                        .First();
                    tmp = (one != null) ? one.station_no : "";
                }
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return tmp;
        }
        /// <summary>
        /// 获取本条线或区域的尾工位
        /// </summary>
        /// <param name="model"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public string GetLast(pms_public p, string model = "", string region_no = "")
        {
            string tmp = "";
            try
            {
                var db = base.AsSugarClient();
                int has = bomStationSubDao.GetCount(p, model, region_no);
                if (has > 0)
                {
                    // --如果子表中按照model维护的有加工工位信息
                    var sub = base.ChangeRepository<Repository<bom_station_sub>>();
                    bom_station_sub o = sub.AsQueryable()
                        .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                            && (it.region_no == region_no || region_no == "") && it.model == model)
                        .OrderBy(i => i.list_order, SqlSugar.OrderByType.Desc)
                        .First();
                    tmp = (o != null) ? o.station_no : "";
                }
                else
                {  // --如果没有维护取默认值，走该region_no下所有的加工工位
                    bom_station one = base.AsQueryable()
                        .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                            && (it.region_no == region_no || region_no == ""))
                        .OrderBy(i => i.list_order, SqlSugar.OrderByType.Desc)
                        .First();
                    tmp = (one != null) ? one.station_no : "";
                }
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return tmp;
        }

        /// <summary>
        /// 获取传入工位的前一个工位
        /// </summary>
        /// <param name="model"></param>
        /// <param name="station_no"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public string GetPre(pms_station_sn p, string model = "", string region_no = "")
        {
            string tmp = "";
            string sql = "";
            try
            {
                var db = base.AsSugarClient();
                int has = bomStationSubDao.GetCount(p, model, region_no);
                if (has > 0)
                {
                    // --如果子表中按照model维护的有加工工位信息
                    sql = $"SELECT * FROM bom_station_sub WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                        $"AND (region_no = '{region_no}' OR '{region_no}' = '') AND model = '{model}' AND list_order < " +
                            $"(SELECT list_order FROM bom_station_sub  WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                                $"AND (region_no = '{region_no}' OR '{region_no}' = '') AND model = '{model}' AND station_no = '{p.station_no}') ORDER BY list_order DESC; ";
                    bom_station_sub o = base.GetSingle<bom_station_sub>(sql);
                    tmp = (o != null) ? o.station_no : "";
                }
                else
                {
                    // --如果没有维护取默认值，走该region_no下所有的加工工位
                    sql = $"SELECT * FROM bom_station WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                        $"AND (region_no = '{region_no}' OR '{region_no}' = '') AND list_order < " +
                            $"(SELECT list_order FROM bom_station  WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                                $"AND (region_no = '{region_no}' OR '{region_no}' = '') AND station_no = '{p.station_no}') ORDER BY list_order DESC; ";
                    bom_station o = base.GetSingle<bom_station>(sql);
                    tmp = (o != null) ? o.station_no : "";
                }
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return tmp;
        }

        /// <summary>
        /// 获取传入工位的下一个工位
        /// </summary>
        /// <param name="model"></param>
        /// <param name="station_no"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public string GetNext(pms_station_no p, string model = "", string region_no = "")
        {
            string tmp = "";
            string sql = "";
            try
            {
                var db = base.AsSugarClient();
                int has = bomStationSubDao.GetCount(p, model, region_no);
                if (has > 0)
                {
                    // --如果子表中按照model维护的有加工工位信息
                    sql = $"SELECT * FROM bom_station_sub WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                        $"AND (region_no = '{region_no}' OR '{region_no}' = '') AND model = '{model}' AND list_order > "
                            + $"(SELECT list_order FROM bom_station_sub  WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                                "AND (region_no = '{region_no}' OR '{region_no}' = '') AND model = '{model}' AND station_no = '{p.station_no}') ORDER BY list_order ASC; ";
                    bom_station_sub o = base.GetSingle<bom_station_sub>(sql);
                    tmp = (o != null) ? o.station_no : "";
                }
                else
                {  // --如果没有维护取默认值，走该region_no下所有的加工工位
                    sql = $"SELECT * FROM bom_station WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                        $"AND (region_no = '{region_no}' OR '{region_no}' = '') AND list_order >" +
                        $" (SELECT list_order FROM bom_station  WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                            $"AND (region_no = '{region_no}' OR '{region_no}' = '') AND station_no = '{p.station_no}') ORDER BY list_order ASC; ";
                    bom_station o = base.GetSingle<bom_station>(sql);
                    tmp = (o != null) ? o.station_no : "";
                }
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return tmp;
        }
        /// <summary>
        /// 返回工位的加工状态
        /// </summary>
        /// <param name="sn"></param>
        /// <param name="station_no"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public int GetStationStatus(pms_station_sn p, string region_no = "")
        {
            int station_ok_mark = -1;//  -1未加工 , 0不可加工，1可加工
            string sql = string.Empty;
            try
            {
                var db = base.AsSugarClient();
                bom_station o = GetByStationNo(p);
                if (o != null)
                {
                    sql = $"SELECT COALESCE(SUM(ok_mark), 0) AS ok_mark FROM pms_plan_position " +
                        $"WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' " +
                        $"AND (region_no = '{region_no}' OR '{region_no}' = '') AND station_no = '{p.station_no}' AND sn = '{p.sn}' AND (del_mark is null OR del_mark = '' OR del_mark <> '1')";
                    station_ok_mark = db.Ado.GetInt(sql);
                }
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return station_ok_mark;
        }


        /// <summary>
        /// 获取指定工位是否可加工
        /// </summary>
        /// <param name="sn"></param>
        /// <param name="model"></param>
        /// <param name="station_no"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public bool GetStationCando(pms_station_sn p, string model = "", string region_no = "")
        {
            bool cando = false; // false 不可加工，true 可加工
            int stationStatus = -1;//  -1未加工 , 0不可加工，1可加工
            // 查询当前工位的状态
            stationStatus = GetStationStatus(p, region_no);

            // 目标工位不是首工位
            // --判断本工位是否合格，如果已经合格，不可加工
            if (stationStatus == 1)
            {
                return false; //如果加工合格，不可加工
            }
            else if (stationStatus == -1 || stationStatus == 0)
            {
                // 如果未加工 或者 加工不合格
                // 获取当前需要加工工位的前一个工位
                string preStationNo = GetPre(p, model, region_no);
                if (string.IsNullOrEmpty(preStationNo))
                {
                    // 如果未找到前一个工位，当期工位视为首工位；
                    cando = true;
                }
                else
                {
                    var preP = p;
                    preP.station_no = preStationNo;
                    stationStatus = GetStationStatus(preP, region_no);
                    // 如果前一个工位合格，本工位可加工
                    cando = stationStatus == 1 ? true : false;
                }
            }
            return cando;
        }

        /// <summary>
        /// 获取指定工位是否可加工
        /// </summary>
        /// <param name="sn"></param>
        /// <param name="model"></param>
        /// <param name="station_no"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public bool GetStationCando(pms_station_sn p, string region_no = "")
        {
            bool cando = false; // false 不可加工，true 可加工
            int stationStatus = -1;//  -1未加工 , 0不可加工，1可加工
            // 查询当前工位的状态
            stationStatus = GetStationStatus(p, region_no);

            var sub = base.ChangeRepository<Repository<pms_plan>>();
            pms_plan o = sub.AsQueryable()
                .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.sn == p.sn)
                .OrderBy(i => i.list_order, SqlSugar.OrderByType.Desc)
                .First();
            string model = o.model;

            cando = GetStationCando(p, model, region_no);
            return cando;
        }
        /// <summary>
        /// 无计划模式，走bom_station 和 pms_plan_postion
        /// </summary>
        /// <param name="sn"></param>
        /// <param name="station_no"></param>
        /// <param name="region_no"></param>
        /// <returns></returns>
        public bool GetStationCandoNoPlan(pms_station_sn p, string region_no = "")
        {
            bool cando = false; // false 不可加工，true 可加工
            int stationStatus = -1;//  -1未加工 , 0不可加工，1可加工
            // 查询当前工位的状态
            stationStatus = GetStationStatus(p, region_no);
            cando = GetStationCando(p, "", region_no);
            return cando;
        }
        public bool GetStationCandoNoPlan(pms_station_no p, string sn, string region_no = "")
        {
            bool cando = false; // false 不可加工，true 可加工
            int stationStatus = -1;//  -1未加工 , 0不可加工，1可加工
            // 查询当前工位的状态
            pms_station_sn ps = new pms_station_sn { company_id = p.company_id, plant_id = p.plant_id, line = p.line, station_no = p.station_no, sn = sn };
            stationStatus = GetStationStatus(ps, region_no);
            cando = GetStationCando(ps, "", region_no);
            return cando;
        }

        public bool GetStationCandoNoPlanBySysParam(pms_station_sn p, string region_no = "")
        {
            bool cando = false; // false 不可加工，true 可加工
            //int stationStatus = -1;//  -1未加工 , 0不可加工，1可加工
            bool StationCheck = false;
            var lineParam = sysLineParamsDao.GetByCodeName(new pms_public { company_id = p.company_id, plant_id = p.plant_id, line = p.line }, "station_check_mode");
            if (lineParam != null)
            {
                StationCheck = lineParam.val == "1" ? true : false;
            }
            if (StationCheck)
            {
                // 查询当前工位的状态
                //stationStatus = GetStationStatus(sn, station_no, region_no);
                cando = GetStationCando(p, "", region_no);
            }
            else
            {
                //参数设定不检查，则可加工
                cando = true;
            }
            return cando;
        }

        public DataTable Get_station_no(pms_public p)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            try
            {
                var db = base.AsSugarClient();
                sql = string.Format(@"select station_no as 工位号,station_name as 工位名称 from bom_station 
                        where Company_Id = '{0}' and plant_id = '{1}' and line ='{2}'", p.company_id, p.plant_id, p.line);
                dt = base.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                //Log("Basic.Business.Line.GetLineList函数执行错误，错误信息:" + ex.Message, "SqlErr");
                //Log("sql :" + sql, "SqlErr");
            }
            return dt;
        }
    }
}
