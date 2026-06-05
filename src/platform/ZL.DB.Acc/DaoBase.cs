using System;
using System.Collections.Generic;
using System.Data;
using SqlSugar;
using ZL.PFLite.Common;

namespace ZL.DB.Acc
{
    /// <summary>
    /// 非泛型 DAO 基类（原生 SQL / 报表 DAO）。
    ///
    /// 推荐定位：
    /// 1. 主要用于动态表名、复杂报表 SQL、DataTable 兼容场景。
    /// 2. 若是常规实体 CRUD / Queryable 查询，请优先使用 <see cref="Repository{T}"/>。
    /// 3. 本类不再承担“主仓储入口”职责，而是作为原生 SQL 能力补充层保留。
    /// </summary>
    public class DaoBase
    {
        public SqlSugarScope db;
        public string SqlErrLog;
        public SqlSugar.DbType dbType;
        public string ConnectionString;

        public static DaoBase instance;
        private static readonly object _lock = new object();

        public DaoBase()
        {
            Init();
        }

        public DaoBase(string dbKindStr = "")
        {
            Init(dbKindStr);
        }

        public static DaoBase GetInstance(string dbKindStr = "")
        {
            lock (_lock)
            {
                instance = new DaoBase(dbKindStr);
            }
            return instance;
        }

        public void Init(string dbKindStr = "")
        {
            try
            {
                db = SugarAcc.GetSugarClient(dbKindStr);
                dbType = db.CurrentConnectionConfig.DbType;
                ConnectionString = db.CurrentConnectionConfig.ConnectionString;
                SqlErrLog = "SqlErr";
            }
            catch (Exception ex)
            {
                Log($"DaoBase.Init 异常：{ex.Message}");
            }
        }

        public List<T> GetList<T>(string sql)
        {
            return GetList<T>(sql, null);
        }

        public List<T> GetList<T>(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return parameters == null || parameters.Length == 0
                    ? db.Ado.SqlQuery<T>(sql)
                    : db.Ado.SqlQuery<T>(sql, parameters);
            }
            catch (Exception ex)
            {
                Log($"DaoBase.GetList 异常：{ex.Message}，SQL：{sql}");
                throw;
            }
        }

        public T GetSingle<T>(string sql)
        {
            return GetSingle<T>(sql, null);
        }

        public T GetSingle<T>(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return parameters == null || parameters.Length == 0
                    ? db.Ado.SqlQuerySingle<T>(sql)
                    : db.Ado.SqlQuerySingle<T>(sql, parameters);
            }
            catch (Exception ex)
            {
                Log($"DaoBase.GetSingle 异常：{ex.Message}，SQL：{sql}");
                throw;
            }
        }

        public T GetScalar<T>(string sql)
        {
            return GetScalar<T>(sql, null);
        }

        public T GetScalar<T>(string sql, params SugarParameter[] parameters)
        {
            try
            {
                var tmp = parameters == null || parameters.Length == 0
                    ? db.Ado.GetScalar(sql)
                    : db.Ado.GetScalar(sql, parameters);
                if (tmp == null)
                    return default;

                return (T)Convert.ChangeType(tmp, typeof(T));
            }
            catch (Exception ex)
            {
                Log($"DaoBase.GetScalar 异常：{ex.Message}，SQL：{sql}");
                throw;
            }
        }

        public int Execute(string sql)
        {
            return Execute(sql, null);
        }

        public int Execute(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return parameters == null || parameters.Length == 0
                    ? db.Ado.ExecuteCommand(sql)
                    : db.Ado.ExecuteCommand(sql, parameters);
            }
            catch (Exception ex)
            {
                Log($"DaoBase.Execute 异常：{ex.Message}，SQL：{sql}");
                throw;
            }
        }

        public DataTable GetDataTable(string sql)
        {
            return GetDataTable(sql, null);
        }

        public DataTable GetDataTable(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return parameters == null || parameters.Length == 0
                    ? db.Ado.GetDataTable(sql)
                    : db.Ado.GetDataTable(sql, parameters);
            }
            catch (Exception ex)
            {
                Log($"DaoBase.GetDataTable 异常：{ex.Message}，SQL：{sql}");
                throw;
            }
        }

        public DateTime GetDate => db.GetDate();

        public string GetDateStr => SqlDialectKit.GetDateExpression(dbType);

        public string DateVal(int type = 0)
        {
            var fmt = type switch
            {
                0 => "yyyy-MM-dd HH:mm:ss",
                1 => "yyyy-MM-dd HH:mm:ss:fff",
                2 => "yyyy-MM-dd",
                3 => "HH:mm:ss",
                4 => "yy-MM-dd HH:mm:ss",
                _ => "yyyy-MM-dd HH:mm:ss"
            };
            return db.GetDate().ToString(fmt);
        }

        public Tuple<string, string> GetDateStartEnd(string StartDay = "", string EndDay = "")
        {
            var res = new Tuple<string, string>(StartDay, EndDay);
            try
            {
                var startTime = string.IsNullOrEmpty(StartDay) ? GetDate : Convert.ToDateTime(StartDay);
                var endTime = string.IsNullOrEmpty(EndDay) ? GetDate : Convert.ToDateTime(EndDay);

                StartDay = startTime.ToString("yyyy-MM-dd");
                EndDay = endTime.ToString("yyyy-MM-dd");
                if (StartDay == EndDay)
                    EndDay = endTime.AddDays(1).ToString("yyyy-MM-dd");

                res = new Tuple<string, string>(StartDay, EndDay);
            }
            catch (Exception ex)
            {
                Log($"DaoBase.GetDateStartEnd 异常：{ex.Message}");
            }
            return res;
        }

        public string GetDuration(string start, string interval = "SECOND", string end = "")
        {
            return SqlDialectKit.GetDurationExpression(dbType, start, interval, end);
        }

        public void Log(string msg, string logFile = "")
        {
            logFile = string.IsNullOrEmpty(logFile) ? SqlErrLog : logFile;
            LogKit.WriteLogs(msg, logFile);
        }

        public List<T> ToEntitys<T>(DataTable dt) where T : new()
        {
            if (dt == null || dt.Rows.Count == 0)
                return null;

            try
            {
                return Utils.EntityKit.ConvertToEntity<T>(dt);
            }
            catch (Exception ex)
            {
                Log($"DaoBase.ToEntitys 异常：{ex.Message}");
                throw;
            }
        }

        public T ToEntity<T>(DataTable dt, int index) where T : new()
        {
            if (dt == null || dt.Rows.Count == 0 || index >= dt.Rows.Count)
                return new T();

            try
            {
                return Utils.EntityKit.ConvertToEntity<T>(dt, index);
            }
            catch (Exception ex)
            {
                Log($"DaoBase.ToEntity(dt, index) 异常：{ex.Message}");
                throw;
            }
        }

        public T ToEntity<T>(DataRow dr) where T : new()
        {
            try
            {
                return Utils.EntityKit.ConvertToEntity<T>(dr);
            }
            catch (Exception ex)
            {
                Log($"DaoBase.ToEntity(dr) 异常：{ex.Message}");
                throw;
            }
        }
    }
}
