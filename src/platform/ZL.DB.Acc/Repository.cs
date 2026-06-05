using SqlSugar;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading.Tasks;
using ZL.PFLite.Common;

namespace ZL.DB.Acc
{
    /// <summary>
    /// 统一分页请求。
    /// </summary>
    public class SqlPageRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public int NormalizePageIndex() => PageIndex <= 0 ? 1 : PageIndex;
        public int NormalizePageSize() => PageSize <= 0 ? 20 : PageSize;
    }

    /// <summary>
    /// 统一分页结果。
    /// </summary>
    public class PagedResult<TItem>
    {
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public List<TItem> Items { get; set; } = new List<TItem>();

        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount * 1.0 / PageSize);
    }

    /// <summary>
    /// SQL 方言辅助，统一维护日期函数与时差表达式。
    /// </summary>
    public static class SqlDialectKit
    {
        public static string GetDateExpression(SqlSugar.DbType dbType)
        {
            if (dbType == SqlSugar.DbType.Oracle) return "SYSDATE";
            if (dbType == SqlSugar.DbType.SqlServer) return "GetDate()";
            if (dbType == SqlSugar.DbType.MySql) return "NOW()";
            if (dbType == SqlSugar.DbType.Sqlite) return "datetime('now', 'localtime') ";
            return null;
        }

        public static string GetDurationExpression(SqlSugar.DbType dbType, string start, string interval = "SECOND", string end = "")
        {
            if (dbType == SqlSugar.DbType.Oracle)
            {
                return "SYSDATE";
            }

            if (dbType == SqlSugar.DbType.SqlServer)
            {
                var actualEnd = string.IsNullOrEmpty(end) ? "GETDATE()" : end;
                return $"DATEDIFF({interval}, {start}, {actualEnd})";
            }

            if (dbType == SqlSugar.DbType.MySql)
            {
                var actualEnd = string.IsNullOrEmpty(end) ? "NOW()" : end;
                return $"timestampdiff({interval}, {start}, {actualEnd})";
            }

            if (dbType == SqlSugar.DbType.Sqlite)
            {
                var multiplier = 1;
                if (interval == "SECOND") multiplier = 24 * 60 * 60;
                if (interval == "MINUTE") multiplier = 24 * 60;
                if (interval == "HOUR") multiplier = 24;
                if (interval == "DAY") multiplier = 1;
                var actualEnd = string.IsNullOrEmpty(end) ? "datetime('now','localtime')" : end;
                return $"Cast((JulianDay({actualEnd}) - JulianDay({start}))*{multiplier} As Integer)";
            }

            return null;
        }
    }

    /// <summary>
    /// 兼容型仓储基类。
    ///
    /// 推荐定位：
    /// 1. 新项目默认优先继承本类。
    /// 2. 统一承接常规 CRUD、Queryable、原生 SQL、分页、事务与常用异步能力。
    /// 3. 业务需要纯原生 SQL / 报表型 DAO 时，再使用 <see cref="DaoBase"/>。
    /// 4. 旧式仓储 <c>Legacy/BaseRepository<T></c> 仅作为历史兼容层保留。
    /// </summary>
    public class Repository<T> : SimpleClient<T> where T : class, new()
    {
        public SqlSugarScope db;
        public SqlSugar.DbType dbType;
        public string SqlErrLog;

        static Repository()
        {
        }

        public Repository(ISqlSugarClient context = null) : base(context)
        {
            try
            {
                var resolvedContext = context ?? SugarAcc.GetSugarClient();
                base.Context = resolvedContext;
                db = base.AsSugarClient() as SqlSugarScope;

                if (db != null)
                {
                    dbType = db.CurrentConnectionConfig.DbType;
                    SqlErrLog = Config.LogFile;
                }
            }
            catch (Exception ex)
            {
                var err = $"Repository<{typeof(T).Name}> 构造函数异常：{ex.Message}";
                LogKit.Error(err, Config.LogFile);
                throw;
            }
        }

        #region 主推荐 Queryable 入口

        public ISugarQueryable<T> Query()
        {
            return base.Context.Queryable<T>();
        }

        public bool Any(Expression<Func<T, bool>> expression)
        {
            try
            {
                return Query().Any(expression);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public async Task<bool> AnyAsync(Expression<Func<T, bool>> expression)
        {
            try
            {
                return await Query().AnyAsync(expression);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public new int Count(Expression<Func<T, bool>> expression = null)
        {
            try
            {
                return expression == null ? Query().Count() : Query().Count(expression);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public new async Task<int> CountAsync(Expression<Func<T, bool>> expression = null)
        {
            try
            {
                return expression == null ? await Query().CountAsync() : await Query().CountAsync(expression);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public T FirstOrDefault(Expression<Func<T, bool>> expression)
        {
            try
            {
                return Query().First(expression);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public async Task<T> FirstOrDefaultAsync(Expression<Func<T, bool>> expression)
        {
            try
            {
                return await Query().FirstAsync(expression);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public T GetEntityById(object id)
        {
            try
            {
                return base.GetById(id);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public List<T> FindList(Expression<Func<T, bool>> expression)
        {
            try
            {
                return Query().Where(expression).ToList();
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public List<T> FindList(Expression<Func<T, bool>> expression, Expression<Func<T, object>> orderBy, OrderByType orderByType = OrderByType.Asc)
        {
            try
            {
                return Query().Where(expression).OrderBy(orderBy, orderByType).ToList();
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public async Task<List<T>> FindListAsync(Expression<Func<T, bool>> expression)
        {
            try
            {
                return await Query().Where(expression).ToListAsync();
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public PagedResult<T> FindPage(Expression<Func<T, bool>> expression, SqlPageRequest request, Expression<Func<T, object>> orderBy = null, OrderByType orderByType = OrderByType.Asc)
        {
            try
            {
                request ??= new SqlPageRequest();
                var pageIndex = request.NormalizePageIndex();
                var pageSize = request.NormalizePageSize();
                var totalCount = 0;

                var query = Query().Where(expression);
                if (orderBy != null)
                {
                    query = query.OrderBy(orderBy, orderByType);
                }

                var items = query.ToPageList(pageIndex, pageSize, ref totalCount);
                return new PagedResult<T>
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    Items = items
                };
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public async Task<PagedResult<T>> FindPageAsync(Expression<Func<T, bool>> expression, SqlPageRequest request, Expression<Func<T, object>> orderBy = null, OrderByType orderByType = OrderByType.Asc)
        {
            try
            {
                request ??= new SqlPageRequest();
                var pageIndex = request.NormalizePageIndex();
                var pageSize = request.NormalizePageSize();
                RefAsync<int> totalCount = 0;

                var query = Query().Where(expression);
                if (orderBy != null)
                {
                    query = query.OrderBy(orderBy, orderByType);
                }

                var items = await query.ToPageListAsync(pageIndex, pageSize, totalCount);
                return new PagedResult<T>
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    Items = items
                };
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        public PagedResult<TResult> SqlQueryPage<TResult>(string sql, SqlPageRequest request, params SugarParameter[] parameters)
        {
            try
            {
                request ??= new SqlPageRequest();
                var pageIndex = request.NormalizePageIndex();
                var pageSize = request.NormalizePageSize();
                var data = GetList<TResult>(sql, parameters);
                var totalCount = data?.Count ?? 0;
                var skip = (pageIndex - 1) * pageSize;
                var items = data == null ? new List<TResult>() : data.GetRange(Math.Min(skip, totalCount), Math.Max(0, Math.Min(pageSize, totalCount - Math.Min(skip, totalCount))));

                return new PagedResult<TResult>
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    Items = items
                };
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        #endregion

        #region Transaction 推荐快捷入口

        public DbResult<bool> ExecuteInTransaction(Action<ISqlSugarClient> action)
        {
            try
            {
                return base.AsSugarClient().Ado.UseTran(() => action(base.AsSugarClient()));
            }
            catch (Exception ex)
            {
                Log(ex);
                throw;
            }
        }

        #endregion

        #region SQL 快捷入口

        public List<TResult> GetList<TResult>(string sql)
        {
            try
            {
                return base.AsSugarClient().Ado.SqlQuery<TResult>(sql);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public List<TResult> GetList<TResult>(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return base.AsSugarClient().Ado.SqlQuery<TResult>(sql, parameters);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public async Task<List<TResult>> GetListAsync<TResult>(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return parameters == null || parameters.Length == 0
                    ? await base.AsSugarClient().Ado.SqlQueryAsync<TResult>(sql)
                    : await base.AsSugarClient().Ado.SqlQueryAsync<TResult>(sql, parameters);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public TResult GetSingle<TResult>(string sql)
        {
            try
            {
                return base.AsSugarClient().Ado.SqlQuerySingle<TResult>(sql);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public TResult GetSingle<TResult>(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return base.AsSugarClient().Ado.SqlQuerySingle<TResult>(sql, parameters);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public async Task<TResult> GetSingleAsync<TResult>(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return parameters == null || parameters.Length == 0
                    ? await base.AsSugarClient().Ado.SqlQuerySingleAsync<TResult>(sql)
                    : await base.AsSugarClient().Ado.SqlQuerySingleAsync<TResult>(sql, parameters);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public TResult GetScalar<TResult>(string sql)
        {
            try
            {
                var tmp = base.AsSugarClient().Ado.GetScalar(sql);
                if (tmp == null)
                    return default;

                return (TResult)Convert.ChangeType(tmp, typeof(TResult));
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public TResult GetScalar<TResult>(string sql, params SugarParameter[] parameters)
        {
            try
            {
                var tmp = base.AsSugarClient().Ado.GetScalar(sql, parameters);
                if (tmp == null)
                    return default;

                return (TResult)Convert.ChangeType(tmp, typeof(TResult));
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public int Execute(string sql)
        {
            try
            {
                return base.AsSugarClient().Ado.ExecuteCommand(sql);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public int Execute(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return base.AsSugarClient().Ado.ExecuteCommand(sql, parameters);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public async Task<int> ExecuteAsync(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return parameters == null || parameters.Length == 0
                    ? await base.AsSugarClient().Ado.ExecuteCommandAsync(sql)
                    : await base.AsSugarClient().Ado.ExecuteCommandAsync(sql, parameters);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public DataTable GetDataTable(string sql)
        {
            try
            {
                return base.AsSugarClient().Ado.GetDataTable(sql);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public DataTable GetDataTable(string sql, params SugarParameter[] parameters)
        {
            try
            {
                return base.AsSugarClient().Ado.GetDataTable(sql, parameters);
            }
            catch (Exception ex)
            {
                Log(ex, sql);
                throw;
            }
        }

        public List<T> CommQuery(string json)
        {
            var entity = base.Context.Utilities.DeserializeObject<T>(json);
            try
            {
                return base.Context.Queryable<T>().WhereClass(entity).ToList();
            }
            catch (Exception ex)
            {
                Log(ex, json);
                throw;
            }
        }

        #endregion

        #region Date / Dialect Helper

        public DateTime GetDate => base.AsSugarClient().GetDate();

        public string GetDateStr => SqlDialectKit.GetDateExpression(base.AsSugarClient().CurrentConnectionConfig.DbType);

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
                Log(ex);
            }
            return res;
        }

        public string GetDuration(string start, string interval = "SECOND", string end = "")
        {
            return SqlDialectKit.GetDurationExpression(base.AsSugarClient().CurrentConnectionConfig.DbType, start, interval, end);
        }

        #endregion

        #region Logging

        public void Log(string msg, string logFile = "")
        {
            logFile = string.IsNullOrEmpty(logFile) ? SqlErrLog : logFile;
            LogKit.WriteLogs(msg, logFile);
        }

        public void Log(string methodName, string sql, Exception ex, string logFile = "")
        {
            var logMessage = $"MethodName: {methodName}异常:\n" +
                             $"Error Message: {ex.Message}\n" +
                             $"SQL: {sql}\n" +
                             $"StackTrace: {ex.StackTrace}";
            logFile = string.IsNullOrEmpty(logFile) ? SqlErrLog : logFile;
            LogKit.WriteLogs(logMessage, logFile);
        }

        public void Log(Exception ex, string sql = "", string logFile = "")
        {
            var st = new StackTrace();
            var methodName = st.GetFrame(1)?.GetMethod()?.Name ?? "Unknown";
            sql = string.IsNullOrEmpty(sql) ? "" : $"SQL: {sql}\n";
            var logMessage = $"MethodName: Repository.{methodName}异常:\n" +
                             $"Error Message: {ex.Message}\n" +
                             sql +
                             $"StackTrace: {ex.StackTrace}";
            logFile = string.IsNullOrEmpty(logFile) ? SqlErrLog : logFile;
            LogKit.Error(logMessage, logFile);
        }

        #endregion
    }
}
