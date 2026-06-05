using System.Collections.Generic;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class SysLineParamsDao : Repository<sys_line_params>
    {
        public List<sys_line_params> GetListByLine(pms_public p)
        {
            List<sys_line_params> list = new List<sys_line_params>();
            try
            {
                var db = base.AsSugarClient();
                list = db.Queryable<sys_line_params>().Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line).ToList();
            }
            catch
            {
            }
            return list;
        }

        public sys_line_params GetByCodeName(pms_public p, string name)
        {
            sys_line_params entity = new sys_line_params();
            try
            {
                var db = base.AsSugarClient();
                entity = db.Queryable<sys_line_params>().Where(it => it.company_id == p.company_id && it.plant_id == p.plant_id && it.line == p.line && it.name == name).First();
            }
            catch
            {
            }
            return entity;
        }
    }
}
