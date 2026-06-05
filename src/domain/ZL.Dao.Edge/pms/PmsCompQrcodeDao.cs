using System;
using System.Collections.Generic;
using System.Data;
using SqlSugar;
using ZL.DB.Acc;
using ZL.PFLite;

namespace ZL.Dao.Edge
{
    public class PmsCompQrcodeDao : DaoBase
    {
        public DataTable Pms_Compqrcode_PartSn_Select(pms_public p, string station_no, string sn, string type)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = $"SELECT * FROM pms_compqrcode  WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' AND station_no = '{station_no}' AND sn = '{sn}' AND type = '{type}'";
                return db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.Pms_Compqrcode_PartSn_Select函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        [Obsolete("该方法已经废弃，请使用 GetListByType")]
        public DataTable Pms_Compqrcode_PartSn_Select(pms_station_sn ps, string type)
        {
            string sql = string.Empty;
            try
            {
                return db.Queryable<pms_compqrcode>()
                    .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id)
                    .Where(i => i.line == ps.line && i.station_no == ps.station_no &&
                            i.sn == ps.sn && i.type == type).ToDataTable();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.Pms_Compqrcode_PartSn_Select函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        /// <summary>
        /// Pms_Compqrcode_PartSn_SelectList
        /// </summary>
        /// <param name="ps"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public List<pms_compqrcode> GetListByType(pms_station_sn ps, string type)
        {
            try
            {
                return db.Queryable<pms_compqrcode>()
                    .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id)
                    .Where(i => i.line == ps.line && i.station_no == ps.station_no &&
                            i.sn == ps.sn && i.type == type)
                    .OrderBy(i => i.created_at, SqlSugar.OrderByType.Desc).ToList();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.GetListByType 函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 根据流水号查询记录是否存在
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="qrcode"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public DataTable P_Select_Pms_CompQrCode_Sn(pms_station_no p, string sn, string type)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = $"SELECT * FROM pms_compqrcode WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' AND station_no = '{p.station_no}' AND sn = '{sn}' AND type = '{type}' order by created_at desc";
                return db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        public DataTable P_Select_Pms_CompQrCode_Sn(pms_station_sn ps, string type)
        {
            try
            {
                return db.Queryable<pms_compqrcode>()
                    .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id)
                    .Where(i => i.line == ps.line && i.station_no == ps.station_no &&
                            i.sn == ps.sn && i.type == type)
                    .OrderBy(i => i.created_at, SqlSugar.OrderByType.Desc).ToDataTable();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode_Sn函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }

        public DataTable P_Select_Pms_CompQrCode_QrCode(pms_public p, string station_no, string qrcode, string type)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = string.Format(@"SELECT * FROM pms_compqrcode
                                        WHERE company_id = '{0}' 
	                                  AND plant_id = '{1}' 
	                                  AND line = '{2}' 
                                      AND station_no = '{3}'
	                                  AND qrcode LIKE '%{4}%'
                                      AND type = '{5}'  order by created_at desc", p.company_id, p.plant_id, p.line, station_no, qrcode, type);
                return db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode_PartSn函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        public DataTable P_Select_Pms_CompQrCode_QrCode(pms_station_no p, string qrcode, string type)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = string.Format(@"SELECT * FROM pms_compqrcode
                                        WHERE company_id = '{0}' 
	                                  AND plant_id = '{1}' 
	                                  AND line = '{2}' 
                                      AND station_no = '{3}'
	                                  AND qrcode LIKE '%{4}%'
                                      AND type = '{5}'  order by created_at desc", p.company_id, p.plant_id, p.line, p.station_no, qrcode, type);
                return db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode_PartSn函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        public DataTable P_Select_Pms_CompQrCode_QrCode(pms_station_sn pms_Station_Sn, string qrcode, string type)
        {
            string sql = string.Empty;
            try
            {
                return db.Queryable<pms_compqrcode>()
                    .Where(i => i.company_id == pms_Station_Sn.company_id && i.plant_id == pms_Station_Sn.plant_id)
                    .Where(i => i.line == pms_Station_Sn.line && i.station_no == pms_Station_Sn.station_no &&
                            i.qrcode.Contains(qrcode) && i.type == type)
                    .OrderBy(i => i.created_at, SqlSugar.OrderByType.Desc).ToDataTable();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode_QrCode函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        public List<pms_compqrcode> P_Select_Pms_CompQrCode_QrCodeList(pms_station_sn ps, string qrcode, string type)
        {
            string sql = string.Empty;
            try
            {
                return db.Queryable<pms_compqrcode>()
                    .Where(i => i.company_id == ps.company_id && i.plant_id == ps.plant_id)
                    .Where(i => i.line == ps.line && i.station_no == ps.station_no &&
                            i.qrcode.Contains(qrcode) && i.type == type)
                    .OrderBy(i => i.created_at, SqlSugar.OrderByType.Desc).ToList();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode_QrCode函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        /// <summary>
        /// 只针对OP1001工位差速器 扫码 
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="qrcode"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public DataTable P_Select_Pms_CompQrCode_QrCode_OP1001(pms_station_no p, string sn, string qrcode, string type)
        {
            string sql = string.Empty;
            try
            {
                DataTable dt = new DataTable();
                sql = $"SELECT * FROM pms_compqrcode WHERE company_id = '{p.company_id}' AND plant_id = '{p.plant_id}' AND line = '{p.line}' AND station_no = '{p.station_no}' AND sn='{sn}' AND qrcode like '%{qrcode}%' AND type = '{type}'";
                return db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode_PartSn_OP1001函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        public DataTable P_Select_Pms_CompQrCode_QrCode_OP1001(pms_station_sn pms_Station_Sn, string qrcode, string type)
        {
            try
            {
                return db.Queryable<pms_compqrcode>()
                    .Where(i => i.company_id == pms_Station_Sn.company_id && i.plant_id == pms_Station_Sn.plant_id)
                    .Where(i => i.line == pms_Station_Sn.line && i.station_no == pms_Station_Sn.station_no &&
                            i.sn == pms_Station_Sn.sn && i.qrcode.Contains(qrcode) && i.type == type).ToDataTable();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode_QrCode_OP1001函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        public List<pms_compqrcode> P_Select_Pms_CompQrCode_QrCode_OP1001List(pms_station_sn pms_Station_Sn, string qrcode, string type)
        {
            try
            {
                return db.Queryable<pms_compqrcode>()
                    .Where(i => i.company_id == pms_Station_Sn.company_id && i.plant_id == pms_Station_Sn.plant_id)
                    .Where(i => i.line == pms_Station_Sn.line && i.station_no == pms_Station_Sn.station_no &&
                            i.sn == pms_Station_Sn.sn && i.qrcode.Contains(qrcode) && i.type == type).ToList();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.P_Select_Pms_CompQrCode_QrCode_OP1001List函数执行错误，错误信息:" + ex.Message);
                return null;
            }
        }
        /// <summary>
        /// 删除qrcode
        /// </summary>
        public void Delete_Qrcode(pms_station_sn pms_Station_Sn, string qrcode, string type)
        {
            try
            {
                db.Deleteable<pms_compqrcode>()
                    .Where(i => i.company_id == pms_Station_Sn.company_id && i.plant_id == pms_Station_Sn.plant_id)
                    .Where(i => i.line == pms_Station_Sn.line && i.station_no == pms_Station_Sn.station_no &&
                            i.sn == pms_Station_Sn.sn && i.qrcode == qrcode)
                    .Where(i => i.type == type).ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.Delete_Qrcode函数执行错误, 错误信息：" + ex.Message);
            }
        }

        /// <summary>
        /// 合箱更新批次码父子sn关联
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="part_sn"></param>
        public void update_pms_compqrcode_OP70(pms_station_no p, string sn, string part_sn)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE  pms_compqrcode
            SET     sn = '{4}' ,
                    updated_at = {6} ,
                    updated_by = 'admin'
            WHERE   company_id ='{0}'
                  AND plant_id = '{1}'
                  AND line = '{2}'
                  AND station_no IN ( 'OP60')
                  AND part_sn = '{5}';",
         p.company_id, p.plant_id, p.line, p.station_no, sn, part_sn, GetDateStr);
                db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsCompQrcodeDao.update_pms_compqrcode_OP70函数执行错误, 错误信息：" + ex.Message);
            }
        }

        public void update_pms_compqrcode_qrcode(string qrcode, string station_no, string sn, string type)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE pms_compqrcode
SET qrcode = '{0}'
WHERE
	sn = '{1}'
AND station_no = '{2}'
AND type = '{3}';",
          qrcode, sn, station_no, type);
                db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsCompQrcodeDao.update_pms_compqrcode_OP70函数执行错误, 错误信息：" + ex.Message);
            }
        }
        public void update_pms_compqrcode_OP70(pms_part_sn pms_Part_Sn)
        {
            try
            {
                db.Updateable<pms_compqrcode>()
                    .SetColumns(it => new pms_compqrcode() { sn = pms_Part_Sn.sn, updated_at = SqlFunc.GetDate(), updated_by = "admin" })
                    .Where(i => i.company_id == pms_Part_Sn.company_id && i.plant_id == pms_Part_Sn.plant_id)
                    .Where(i => i.line == pms_Part_Sn.line && i.station_no.Contains(pms_Part_Sn.station_no) &&
                            i.part_sn == pms_Part_Sn.part_sn)
                    .ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log("PmsCompQrcodeDao.update_pms_compqrcode_OP70函数执行错误, 错误信息：" + ex.Message);
            }
        }

        /// <summary>
        /// 轴系输入项更新父子级
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="part_sn"></param>
        public void update_pms_compqrcode_OP40(pms_part_sn pms_Part_Sn)
        {
            string sql = string.Empty;
            try
            {
                db.Updateable<pms_compqrcode>()
                    .SetColumns(it => new pms_compqrcode() { sn = pms_Part_Sn.sn, updated_at = SqlFunc.GetDate(), updated_by = "admin" })
                    .Where(i => i.company_id == pms_Part_Sn.company_id && i.plant_id == pms_Part_Sn.plant_id)
                    .Where(i => i.line == pms_Part_Sn.line && SqlFunc.ContainsArray(new string[] { "OP10-1-2", "OP10-3" }, i.station_no) && i.part_sn == pms_Part_Sn.part_sn)
                    .ExecuteCommand();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsCompQrcodeDao.update_pms_compqrcode_OP40函数执行错误, 错误信息：" + ex.Message);

            }
        }
        public void update_pms_compqrcode_OP40(pms_station_no p, string sn, string part_sn)
        {
            string sql = string.Empty;
            try
            {
                if (dbType == SqlSugar.DbType.SqlServer)
                    sql = string.Format(@"UPDATE  pms_compqrcode
                SET     sn = '{4}' ,
                        updated_at = {GetDateStr} ,
                        updated_by = 'admin'
                WHERE   company_id = '{0}'
                      AND plant_id = '{1}'
                      AND line = '{2}'
                      AND station_no IN ( 'OP10-1-2', 'OP10-3' )
                      AND part_sn = '{5}';",
               p.company_id, p.plant_id, p.line, p.station_no, sn, part_sn);
                if (dbType == SqlSugar.DbType.MySql)
                    sql = string.Format(@"UPDATE  pms_compqrcode
                SET     sn = '{4}' ,
                        updated_at = now() ,
                        updated_by = 'admin'
                WHERE   company_id = '{0}'
                      AND plant_id = '{1}'
                      AND line = '{2}'
                      AND station_no IN ( 'OP10-1-2', 'OP10-3' )
                      AND part_sn = '{5}';",
         p.company_id, p.plant_id, p.line, p.station_no, sn, part_sn);
                db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsCompQrcodeDao.update_pms_compqrcode_OP40函数执行错误, 错误信息：" + ex.Message);

            }
        }

        //public  void Insert_pms_compqrcode(string StationNo, string sn, string qrcode, string type)
        //{
        //    Insert_pms_compqrcode(Constants.CompanyId, Constants.PlantId, Constants.Line, StationNo, sn, qrcode, type);
        //}
        /// <summary>
        /// 更新二维码 批次码
        /// </summary>
        /// <param name="company_id">公司</param>
        /// <param name="plant_id">工厂</param>
        /// <param name="line">线号</param>
        /// <param name="station_no">工位号</param>
        /// <param name="sn">产品序列号</param>
        /// <param name="qrcode">二维码、批次码</param>
        /// <param name="type">类型</param>
        public void Insert_pms_compqrcode(pms_compqrcode obj)
        {
            string sql = string.Empty;
            try
            {
                obj.created_at = SqlFunc.GetDate();
                obj.updated_at = SqlFunc.GetDate();
                obj.created_by = "admin";
                obj.updated_by = "admin";

                db.Insertable<pms_compqrcode>(obj).ExecuteCommandIdentityIntoEntity();
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsCompQrcodeDao.Insert_pms_compqrcode函数执行错误, 错误信息：" + ex.Message);
            }
        }

        public int Add_pms_compqrcode(pms_station_no p, string type, string qrcode, string Mes_Code)
        {
            string sql = string.Empty;
            try
            {

                sql = string.Format(@"INSERT INTO pms_compqrcode (
	                                            company_id,
	                                            plant_id,
	                                            line,
	                                            station_no,
                                                sn,
                                                part_sn,
	                                            type,
	                                            qrcode,
	                                            created_at,
	                                            created_by,
	                                            updated_at,
	                                            updated_by
                                            )
                                            VALUES
	                                            ('{0}','{1}','{2}','{3}','0','{6}','{4}','{5}',GetDateStr,'lima',GetDateStr,'lima')",
                                                p.company_id, p.plant_id, p.line, p.station_no, type, qrcode, Mes_Code);
                db?.GetDataTable(sql);
                return 0;
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsCompQrcodeDao.Insert_pms_compqrcode函数执行错误, 错误信息：" + ex.Message);
                return 1;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="qrcode"></param>
        /// <param name="type"></param>
        /// <param name="remark">将tag_name记录下来</param>
        public void Insert_pms_compqrcode(pms_station_no p, string sn, string qrcode, string type, string remark = "")
        {
            string sql = string.Empty;
            try
            {

                sql = string.Format(@"INSERT  INTO pms_compqrcode
                    ( company_id ,
                      plant_id ,
                      line ,
                      station_no ,
                      sn ,
                      part_sn ,
                      qrcode ,
                      type ,
                      created_at ,
                      created_by ,
                      updated_at ,
                      updated_by,
                        remark
                    )
            VALUES  ( '{0}' ,
                      '{1}' ,
                      '{2}' ,
                      '{3}' ,
                      '{4}' ,
                      '{4}',
                      '{5}' ,
                      '{6}' ,
                      {7} ,
                      'admin' ,
                      {7},
                      'admin', '{8}'
		            );", p.company_id, p.plant_id, p.line, p.station_no, sn, qrcode, type, GetDateStr, remark);
                db?.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("PmsCompQrcodeDao.Insert_pms_compqrcode函数执行错误, 错误信息：" + ex.Message);
            }
        }
        public DataTable GetCompqrCode(string sWhere)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT
	a.[id],
	a.[company_id],
	a.[plant_id],
	a.[line],
	a.[station_no],
	a.[sn],
	b.qrcode as qr_code,
	a.[type],
	a.[qrcode],
	a.[created_at],
	a.[created_by],
	a.[updated_at],
	a.[updated_by],
    a.[remark]
FROM
	[dbo].[pms_compqrcode] a LEFT JOIN pms_compqrcode b ON a.sn=b.sn
WHERE
	1 = 1 {0}", sWhere);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }
        public DataTable select_qrcode(pms_station_no p, string qrcode)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"select * FROM pms_compqrcode where company_id='{0}' and plant_id='{1}' and line='{2}' and station_no='{3}' and qrcode='{4}'",
                    p.company_id, p.plant_id, p.line, p.station_no, qrcode);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }

        public DataTable GetQrcode(string sWhere)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT
	company_id,
	plant_id,
	line,
	station_no,
	type,
	qrcode
FROM
	pms_compqrcode
WHERE
	1 = 1 {0}", sWhere);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }
        public DataTable GetSnModel(string sWhere)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT a.line,
	a.station_no,
	b.model,
	a.sn,
	a.qrcode
FROM
	pms_compqrcode a
LEFT JOIN pms_plan b ON a.line = b.line
AND a.sn = b.sn
 WHERE 1=1  {0}", sWhere);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }

        public DataTable GetCompQrcode(string sWhere)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT
	line_name,
	station_no,
	sn,
	qrcode,
    (CASE state WHEN 1 THEN '合格' 
      ELSE '不合格'
END ) as state,
	a.created_at,
	a.updated_at
FROM
	pms_compqrcode a
LEFT JOIN line b ON a.line = b.line
WHERE 1=1 {0}", sWhere);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }

        /// <summary>
        /// 报废修改
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="qr_code"></param>
        /// <returns></returns>
        public void update_pms_compqrcode_srcap(pms_station_no p, string sn, string qr_code)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE pms_compqrcode
SET state = 2
WHERE
	company_id = '{0}'
AND plant_id = '{1}'
AND line = '{2}'
AND station_no = '{3}'
AND sn = '{4}'
AND qrcode = '{5}'", p.company_id, p.plant_id, p.line, p.station_no, sn, qr_code);

                db?.Execute(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("Pms_Plan_List.Update_plan_list_start_time函数执行错误, 错误信息：" + ex.Message);
            }
        }

        public void update_pms_compqrcode_gd(pms_station_no ps, string sn, string gd_code)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE pms_compqrcode
SET sn = '{4}'
WHERE
	company_id = '{0}'
AND plant_id = '{1}'
AND line = '{2}'
AND station_no = '{3}'
AND part_sn = '{5}';", ps.company_id, ps.plant_id, ps.line, ps.station_no, sn, gd_code);

                db?.Execute(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("Pms_Plan_List.update_pms_compqrcode函数执行错误, 错误信息：" + ex.Message);
            }
        }
        //        public  void update_pms_compqrcode_gd(pms_station_no p, string sn, string gd_code)
        //        {
        //            string sql = string.Empty;
        //            try
        //            {
        //                sql = string.Format(@"UPDATE pms_compqrcode
        //SET sn = '{4}'
        //WHERE
        //	company_id = '{0}'
        //AND plant_id = '{1}'
        //AND line = '{2}'
        //AND station_no = '{3}'
        //AND part_sn = '{5}';", p.company_id, p.plant_id, p.line, p.station_no, sn, gd_code);

        //                db?.Execute(sql);
        //            }
        //            catch (Exception ex)
        //            {
        //                Log(sql);
        //                Log("Pms_Plan_List.update_pms_compqrcode函数执行错误, 错误信息：" + ex.Message);
        //            }
        //        }

        public DataTable Get_Qrcode_Type(string company_id, string plant_id)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT DISTINCT
	(type) AS type_no,
	type AS type_name
FROM
	pms_compqrcode
WHERE
	type IN ('ET', 'CSQ_XKT', 'SKT')
AND company_id='{0}' AND plant_id='{1}';", company_id, plant_id);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }


        public void update_pms_compqrcode_cancel(pms_station_no p, string sn, string qr_code)
        {
            string sql = string.Empty;
            try
            {
                sql = string.Format(@"UPDATE pms_compqrcode
SET state = 1
WHERE
	company_id = '{0}'
AND plant_id = '{1}'
AND line = '{2}'
AND station_no = '{3}'
AND sn = '{4}'
AND qrcode = '{5}'", p.company_id, p.plant_id, p.line, p.station_no, sn, qr_code);

                db?.Execute(sql);
            }
            catch (Exception ex)
            {
                Log(sql);
                Log("update_pms_compqrcode_cancel函数执行错误, 错误信息：" + ex.Message);
            }
        }


        public void update_pms_compqrcode(pms_station_no p, string sn, string part_sn)
        {
            string sql = string.Empty;
            try
            {
                if (p.station_no == "OP20-3")
                {
                    sql = string.Format(@"UPDATE  pms_compqrcode
            SET     sn = '{4}' ,
                    updated_at = GetDateStr ,
                    updated_by = 'lima'
            WHERE   company_id = '{0}'
                    AND plant_id = '{1}'
                    AND line = '{2}'
                    AND station_no in ('OP20-1','OP20-2','OP20-3')
                    AND part_sn = '{5}';", p.company_id, p.plant_id, p.line, p.station_no, sn, part_sn);

                    db?.Execute(sql);
                }
                else if (p.station_no == "OP30")
                {
                    sql = string.Format(@"UPDATE  pms_compqrcode
                SET     sn = '{4}' ,
                        updated_at = GetDateStr ,
                        updated_by = 'lima'
                WHERE   company_id = '{0}'
                        AND plant_id = '{1}'
                        AND line = '{2}'
                        AND station_no IN ( 'OP10-1-2', 'OP10-3','JPQ' )
                        AND part_sn = '{5}';", p.company_id, p.plant_id, p.line, p.station_no, sn, part_sn);
                    db?.Execute(sql);
                }

            }
            catch (Exception ex)
            {
                Log(sql);
                Log("update_pms_compqrcode函数执行错误, 错误信息：" + ex.Message);
            }
        }


        public DataSet GetData(string factoryCode, string lineCode, string productType, string sn)
        {
            DataSet ds = new DataSet();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT
    a.id,
	a.plant_id,
	a.line,
	a.station_no,
	b.model,
	a.qrcode,
   (CASE state WHEN 1 THEN '合格' 
      ELSE '不合格'
END ) as state,
	a.remark
FROM
	pms_compqrcode a
LEFT JOIN pms_plan b ON a.sn = b.sn
WHERE
	a.plant_id = '{0}'
AND a.line = '{1}'
AND b.model = '{2}'
AND a.sn = '{3}'
AND a.type != 'ET'
AND a.type != '刻印内容'", factoryCode, lineCode, productType, sn);
                ds = db?.GetDataSet(sql);
            }
            return ds;
        }

        public DataTable Exist_Pms_CompQrCode(pms_station_no p, string qrcode)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT * FROM
	                                            pms_compqrcode 
                                                WHERE
	                                            company_id = '{0}' 
	                                            AND plant_id = '{1}' 
	                                            AND line = '{2}' 
	                                            AND station_no = '{3}' 
	                                            AND qrcode = '{4}'",
                                                p.company_id, p.plant_id, p.line, p.station_no, qrcode);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }

        public DataTable F_GetQrCode(string station_no, string type, string qrcode)
        {
            DataTable dt = new DataTable();
            string sql = string.Empty;
            {
                sql = string.Format(@"SELECT * FROM F_GetQrCode('{0}','{1}','{2}')", station_no, type, qrcode);
                dt = db?.GetDataTable(sql);
            }
            return dt;
        }

    }
}
