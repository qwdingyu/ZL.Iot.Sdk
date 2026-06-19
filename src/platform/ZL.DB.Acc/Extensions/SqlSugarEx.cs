using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Xml;
using SqlSugar;

namespace ZL.DB.Acc
{
    public static class SqlSugarEx
    {
        public static SqlSugar.DbType GetDbType(this SqlSugarScope scope)
        {
            return scope.CurrentConnectionConfig.DbType;
        }
        /// <summary>
        /// 扩展方法，自带方法不能满足的时候可以添加新方法
        /// </summary>
        /// <returns></returns>
        //public static List<T> CommQuery<T>(this SqlSugarScope scope, string json)
        //{
        //    T t = scope.Utilities.DeserializeObject<T>(json);
        //    var list = scope.Queryable<T>().WhereClass(t).ToList<T>();
        //    return list;
        //}
        /// <summary>
        /// 执行增删改的sql
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static int Execute(this SqlSugarScope scope, string sql)
        {
            int effect = 0;
            try
            {
                effect = scope.Ado.ExecuteCommand(sql);
            }
            catch (Exception)
            {
                throw;
            }
            return effect;
        }
        public static void Execute(this SqlSugarScope scope, string[] sqls)
        {
            try
            {
                foreach (string sql in sqls) scope.Ado.ExecuteCommand(sql);
            }
            catch (Exception)
            {
                throw;
            }
        }
        public static int Execute(this SqlSugarScope scope, string sql, params SugarParameter[] parameters)
        {
            int effect = 0;
            try
            {
                effect = scope.Ado.ExecuteCommand(sql, parameters);
            }
            catch (Exception)
            {
                throw;
            }
            return effect;
        }
        public static int Execute(this SqlSugarScope scope, string sql, IDbTransaction tran)
        {
            int effect = 0;
            try
            {
                //effect = scope.Ado.ExecuteCommand(tran, sql);
                effect = scope.Ado.ExecuteCommand(sql);
            }
            catch (Exception)
            {
                throw;
            }
            return effect;
        }
        /// <summary>
        /// List&lt;iot_device&gt; list = SugarAcc.GetList&lt;iot_device&gt;("select * from iot_device");
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static List<T> GetList<T>(this SqlSugarScope scope, string sql)
        {
            List<T> list = scope.Ado.SqlQuery<T>(sql);
            return list;
        }
        /// <summary>
        /// 查询一行记录
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static T GetSingle<T>(this SqlSugarScope scope, string sql)
        {
            T list = scope.Ado.SqlQuerySingle<T>(sql);
            return list;
        }
        /// <summary>
        /// 查询首行首列
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static object GetScalar(this SqlSugarScope scope, string sql)
        {
            object obj = scope.Ado.GetScalar(sql);
            return obj;
        }
        public static object GetScalar(this SqlSugarClient client, string sql)
        {
            object obj = client.Ado.GetScalar(sql);
            return obj;
        }
        public static int GetInt(this SqlSugarScope scope, string sql)
        {
            int obj = scope.Ado.GetInt(sql);
            return obj;
        }
        public static string GetString(this SqlSugarScope scope, string sql)
        {
            string obj = scope.Ado.GetString(sql);
            return obj;
        }
        /// <summary>
        /// 查询首行首列
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        [Obsolete("统一方法名，请使用GetScalar代替")]
        public static object ExecuteScalar(this SqlSugarScope scope, string sql)
        {
            object obj = scope.Ado.GetScalar(sql);
            return obj;
        }

        /// <summary>
        /// 查询DataSet
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static DataSet GetDataSet(this SqlSugarScope scope, string sql)
        {
            DataSet ds = new DataSet();
            ds = scope.Ado.GetDataSetAll(sql);
            return ds;
        }
        public static DataSet GetDataSet(this SqlSugarScope scope, string[] sqls)
        {
            DataSet ds = new DataSet();

            try
            {
                sqls = sqls.Select(sql => sql.TrimEnd(' ').EndsWith(";") ? sql : sql + ";").ToArray();
                string _sql = string.Join("", sqls);
                ds = scope.Ado.GetDataSetAll(_sql);
            }
            catch (Exception)
            {
                ds = new DataSet();
                foreach (var sql in sqls)
                {
                    DataTable dt = scope.Ado.GetDataTable(sql);
                    ds.Tables.Add(dt);
                }
            }
            return ds;
        }

        /// <summary>
        /// 返回DataTable
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static DataTable GetDataTable(this SqlSugarScope scope, string sql)
        {
            DataTable dt = new DataTable();
            try
            {
                dt = scope.Ado.GetDataTable(sql);
            }
            catch (Exception)
            {
                throw;
            }
            return dt;
        }
        public static DataTable GetDataTable(this SqlSugarScope scope, string sql, int startRecord, int maxRecords)
        {
            DataTable newDt = new DataTable();
            DataTable dt = new DataTable();
            try
            {
                // 直接使用SqlSugar的分页功能
                dt = scope.Ado.GetDataTable(sql);
                if (dt.Rows.Count > startRecord)
                {
                    for (int i = startRecord; i < Math.Min(startRecord + maxRecords, dt.Rows.Count); i++)
                    {
                        newDt.ImportRow(dt.Rows[i]);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
            return newDt;
        }
        public static DataTable GetDBTable(this SqlSugarScope scope, string tableName)
        {
            DataTable dt = new DataTable();
            try
            {
                string sql = $"select * from {tableName}";
                dt = scope.Ado.GetDataTable(sql);
            }
            catch (Exception)
            {
                throw;
            }
            return dt;
        }

        public static bool DoUpdate(this SqlSugarScope scope, DataTable dt, string TableName)
        {
            bool ok = false;
            try
            {
                if (!string.IsNullOrEmpty(TableName))
                    dt.TableName = TableName;
                var updateable = scope.Updateable(dt);
                // 执行更新操作
                updateable.ExecuteCommand();

                scope.Ado.CommitTran(); // 提交事务
                ok = true;
            }
            catch (Exception)
            {
                scope.Ado.RollbackTran(); // 回滚事务
                throw;
            }
            return ok;
        }

        /// <summary>
        /// 通过表名字查询出 表下面所有的数据
        /// </summary>
        /// <returns></returns>
        public static List<dynamic> GetListByTableName(this SqlSugarScope scope, string TableName)
        {
            var list = scope.Queryable<dynamic>().AS(TableName).ToList();
            return list;
        }
        /// <summary>
        /// 通过表名字查询出 表下面所有的数据
        /// </summary>
        /// <returns></returns>
        public static DataTable GetDataTableByTableName(this SqlSugarScope scope, string TableName)
        {
            var list = scope.Queryable<dynamic>().AS(TableName).ToDataTable();
            return list;
        }
        /// <summary>
        ///  根据条件查询所有的下的记录  主要供修改的展示界面使用
        /// </summary>
        /// <param name="number">筛选的数值</param>
        /// <param name="tableName">表名</param>
        /// <param name="condition">刷选的条件</param>
        /// <returns></returns>
        public static List<T> GetListById<T>(this SqlSugarScope scope, string number, string tableName, string condition)
        {
            List<T> list = scope.Queryable<T>().AS(tableName)
                .Where($"{condition}=@id", new { id = number }).ToList();
            return list;
        }
        /// <summary>
        ///  根据条件查询所有的下的记录  主要供修改的展示界面使用
        /// </summary>
        /// <param name="number">筛选的数值</param>
        /// <param name="tableName">表名</param>
        /// <param name="condition">刷选的条件</param>
        /// <returns></returns>
        public static DataTable GetDataTableById(this SqlSugarScope scope, string number, string tableName, string condition)
        {
            var dt = scope.Queryable<dynamic>().AS(tableName)
                .Where($"{condition}=@id", new { id = number }).ToDataTable();
            return dt;
        }


        /// <summary>
        /// 计算 起始时间 和 结束时间 字段 之间相距的秒数
        /// </summary>
        /// <param name="start">开始字段</param>
        /// <param name="interval">时间计算类型</param>
        /// <param name="end"> 结束字段</param>
        /// <returns></returns>
        public static string GetDuration(this SqlSugarScope scope, string start, string interval = "SECOND", string end = "")
        {
            string _end = string.Empty;
            string tmp = null;
            SqlSugar.DbType DBType = scope.CurrentConnectionConfig.DbType;
            if (DBType == SqlSugar.DbType.Oracle)
            {
                _end = (string.IsNullOrEmpty(end)) ? "SYSDATE" : end;
                // 未实现
                tmp = "SYSDATE";
            }
            else if (DBType == SqlSugar.DbType.SqlServer)
            {
                _end = (string.IsNullOrEmpty(end)) ? "GETDATE()" : end;
                tmp = $"DATEDIFF({interval}, {start}, {_end})";
            }
            else if (DBType == SqlSugar.DbType.MySql)
            {
                _end = (string.IsNullOrEmpty(end)) ? "NOW()" : end;
                tmp = $"timestampdiff({interval}, {start}, {_end})";
                //# 所有格式
                //SELECT TIMESTAMPDIFF(FRAC_SECOND,'2012-10-01', '2013-01-13'); # 暂不支持
                //SELECT TIMESTAMPDIFF(SECOND,'2012-10-01', '2013-01-13'); # 8985600
                //SELECT TIMESTAMPDIFF(MINUTE,'2012-10-01', '2013-01-13'); # 149760
                //SELECT TIMESTAMPDIFF(HOUR,'2012-10-01', '2013-01-13'); # 2496
                //SELECT TIMESTAMPDIFF(DAY,'2012-10-01', '2013-01-13'); # 104
                //SELECT TIMESTAMPDIFF(WEEK,'2012-10-01', '2013-01-13'); # 14
                //SELECT TIMESTAMPDIFF(MONTH,'2012-10-01', '2013-01-13'); # 3
                //SELECT TIMESTAMPDIFF(QUARTER,'2012-10-01', '2013-01-13'); # 1
                //SELECT TIMESTAMPDIFF(YEAR,'2012-10-01', '2013-01-13'); # 0
            }
            else if (DBType == SqlSugar.DbType.Sqlite)
            {
                int _interval = 1;
                if (interval == "SECOND") _interval = 24 * 60 * 60;
                if (interval == "MINUTE") _interval = 24 * 60;
                if (interval == "HOUR") _interval = 24;
                if (interval == "DAY") _interval = 1;// DAY及 以下未做测试
                _end = (string.IsNullOrEmpty(end)) ? "datetime('now','localtime')" : end;
                tmp = $"Cast((JulianDay({_end}) - JulianDay({start}))*{_interval} As Integer)";
            }
            return tmp;
        }

        /// <summary>
        /// https://www.donet5.com/Doc/1/1207
        /// 用法如下：
        ///  //C# MODEL cs文件生成后存放的路径
        ///  string ModelPath = Path.Combine(Constants.ROOT_DIR, "Entity");
        ///  //每个model （cs文件）中的命名空间
        ///  string NameSpace = "Iot.Entity";
        ///  it.tab2Model(ModelPath, NameSpace);
        /// </summary>
        /// <param name="DirPath">生成实体存储路径</param>
        /// <param name="NameSpace">实体命名空间</param>
        public static void tab2Model(this SqlSugarScope scope, string DirPath, string NameSpace = "Models")
        {
            if (!Directory.Exists(DirPath)) Directory.CreateDirectory(DirPath);
            scope.DbFirst.IsCreateDefaultValue().CreateClassFile(DirPath, NameSpace);
        }
        public static void tab2Model(this SqlSugarScope scope, string DirPath, string TableName, string NameSpace = "Models")
        {
            if (string.IsNullOrEmpty(TableName)) return;
            if (!Directory.Exists(DirPath)) Directory.CreateDirectory(DirPath);
            scope.DbFirst.Where(TableName).CreateClassFile(DirPath, NameSpace);
        }
        /// <summary>
        /// 此方法需要完善
        /// https://www.donet5.com/Doc/1/1206
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="NameSpace"></param>
        public static void model2Tab<T>(this SqlSugarScope scope)
        {
            //如果不存在创建数据库
            scope.DbMaintenance.CreateDatabase();
            //2、创建表
            //scope.CodeFirst.SetStringDefaultLength(200).InitTables(typeof(CodeFirstTable1));
            scope.CodeFirst.SetStringDefaultLength(200).InitTables(typeof(T));
            //这样一个表就能成功创建了
        }

        //SqlSugar通用的执行存储过程方法（以out方式返回参数)K
        public static void ExecuteProcedure(this SqlSugarScope scope, string sprocName, Dictionary<string, object> intParams, Dictionary<string, object> outParams)
        {
            List<SugarParameter> pList = new List<SugarParameter>();
            if (intParams != null)
            {
                pList = intParams.Select(
                    obj => new SugarParameter($@"@{obj.Key}", obj.Value))
                    .ToList();
            }
            if (outParams != null)
                pList.AddRange(outParams.Select(obj => new SugarParameter($@"@{obj.Key}", obj.Value) { Direction = ParameterDirection.Output }));
            scope.Ado.UseStoredProcedure().ExecuteCommand(sprocName, pList);
            foreach (var p in pList.Where(r => r.Direction == ParameterDirection.Output))
            {
                var pName = p.ParameterName.Substring(1);
                if (outParams != null && outParams.ContainsKey(pName))
                {
                    outParams[pName] = p.Value;
                }
            }
        }
    }

    //class CodeFirstTable1
    //{
    //    [SugarColumn(IsIdentity = true, IsPrimaryKey = true)]
    //    public int Id { get; set; }
    //    public string Name { get; set; }
    //    [SugarColumn(ColumnDataType = "Nvarchar(255)")]//自定格式的情况 length不要设置
    //    public string Text { get; set; }
    //    [SugarColumn(IsNullable = true)]
    //    public DateTime CreateTime { get; set; }
    //}
}
