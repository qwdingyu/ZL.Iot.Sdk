using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class BomStationSubDao : Repository<bom_station_sub>
    {

        SysLineParamsDao sysLineParamsDao = new SysLineParamsDao();
        public List<bom_station_sub> QueryList()
        {
            List<bom_station_sub> lsit = new List<bom_station_sub>();
            try
            {
                lsit = db.Queryable<bom_station_sub>().ToList();
            }
            catch
            {
            }
            return lsit;
        }

        public bool Add(bom_station_sub it)
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

        public bool Del(bom_station_sub it)
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

        public List<bom_station_sub> GetByStationNo(pms_station_no p)
        {
            List<bom_station_sub> list = new List<bom_station_sub>();
            try
            {
                list = base.AsQueryable()
                    .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.station_no == p.station_no)
                    .ToList();
            }
            catch (Exception ex)
            {
                string err = ex.Message;
            }
            return list;
        }

        public int GetCount(pms_public p, string model = "", string region_no = "")
        {
            int count = 0;
            if (!string.IsNullOrEmpty(model))
            {
                count = db.Queryable<bom_station_sub>()
                    .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                    && (it.region_no == region_no || region_no == "") && it.model == model)
                    .Count();
                //sql = $"SELECT count(*) FROM bom_station_sub  WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' AND (region_no = '{region_no}' OR '{region_no}' = '') AND model = '{model}';";
                //has = db.Ado.GetInt(sql);
            }
            return count;
        }

        public bool IsExists(pms_public p, string model = "", string region_no = "")
        {
            bool exists = false;
            //exists = db.Queryable<bom_station_sub>()
            //        .Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
            //        && (it.region_no == region_no || region_no == "") && it.model == model)
            //        .Any();
            exists = db.Queryable<bom_station_sub>().Any(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line
                    && (it.region_no == region_no || region_no == "") && it.model == model); //上面语法的简化

            return exists;
        }
    }
}
