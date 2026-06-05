using SqlSugar;
using System;
using System.Data;
using ZL.PFLite.Common;

namespace ZL.DB.Acc.Utils
{
    public class DbMaintenanceKit
    {
        public static SqlSugarScope db;

        static DbMaintenanceKit()
        {
            db = SugarAcc.GetSugarClient();
        }
        public static DataTable GenTabByTableName(string TableName)
        {
            DataTable dt = new DataTable();
            if (string.IsNullOrEmpty(TableName)) return dt;
            string sql = $"select * from {TableName} where 1=2";
            try
            {
                dt = db.Ado.GetDataTable(sql);
                dt.TableName = TableName;
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs($"DbMaintenanceKit.GenTabByTableName 异常：{ex.Message}!，对应的SQL为：{sql}");
                throw ex;
            }
            return dt;
        }
    }
}
