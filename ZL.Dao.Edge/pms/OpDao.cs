using System;
using System.Data;
using SqlSugar;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class OpDao : DaoBase
    {

        /// <summary>
        /// 线号变化加载工位
        /// </summary>
        /// <returns></returns>
        public  DataTable GetOp(string linecode)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"select OPCode as 工位编号 ,OPName as 工位名称 from op  where LineCode = '{0}';", linecode);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }

        #region 通用方法
        /// <summary>
        /// 通用方法
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="tablename"></param>
        /// <returns></returns>
        public  DataTable GetOP(string linecode)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT OPCode 工位号, OPName 工位名 FROM ScadaList WHERE   LineCode = '{0}'
              AND ISNULL(Enabled, 0) = 1 ORDER BY OPCode", linecode);
                dt = db?.GetDataTable(sql);

            }
            return dt;
        }
        #endregion
        public  DataTable GetStationList(pms_public p)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();

                {

                    sql = string.Format(@"select station_no '工位号' ,station_name '工位名称' from op
                                        where company_id = '{0}'
                                       AND plant_id = '{1}'
                                       AND line='{2}' order by id", p.company_id, p.plant_id, p.line);
                    dt = db?.GetDataTable(sql);

                }
                return dt;
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("Basic.Business.OP.GetStationList函数执行错误, 错误信息：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        ///  获取所有工位信息
        /// </summary>
        /// <returns></returns>
        public  DataTable GetOPCode()
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"select * from OP order by OPCode");
                dt = db?.GetDataTable(sql);

            }
            return dt;
        }


        #region 获取工位数量

        /// <summary>
        ///  获取工位数量
        /// </summary>
        /// <returns></returns>
        public  DataTable GetOPCodeCount()
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"select distinct OPCode from OP");
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }

        #endregion  获取工位数量
        //public  void update_op(string StationNo, string sn, string updated_by)
        //{
        //    update_op(Constants.CompanyId, Constants.PlantId, Constants.Line, StationNo, sn, DateTime.Now.ToString(), updated_by);
        //}

        public  void update_op(pms_station_no p, string sn, string updated_by)
        {
            string sql = string.Empty;
            try
            {
                sql = $"UPDATE op SET sn='{sn}' ,updated_at={GetDateStr},updated_by='{updated_by}' where company_id='{p.company_id}' and plant_id='{p.plant_id}' and line='{p.line}'AND station_no='{p.station_no}'";
                db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log("OpDao.update_op函数执行错误, 错误信息：" + ex.Message);
            }
        }
        public  void update_op(pms_station_sn OP)
        {
            try
            {
                db.Updateable<op>(OP)
                    .SetColumns(it => new op { sn = OP.sn, updated_at = SqlFunc.GetDate(), updated_by = "admin" })
                    .Where(i => i.company_id == OP.company_id && i.plant_id == OP.plant_id)
                    .Where(it => it.line == OP.line && it.station_no == OP.station_no).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log("OpDao.update_op函数执行错误, 错误信息：" + ex.Message);
            }
        }
    }
}
