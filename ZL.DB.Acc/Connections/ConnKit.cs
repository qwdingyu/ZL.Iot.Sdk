using System;
using System.ComponentModel;
using SqlSugar;
using ZL.DB.Acc.Utils;
using ZL.PFLite.Common;
// 注意：不再全局引入 ZL.PFLite.Net 以避免 TraceKit 命名空间冲突

namespace ZL.DB.Acc
{
    /// <summary>
    /// SQL 执行日志辅助工具：将参数化 SQL 展开为可读形式输出到 TraceKit
    /// </summary>
    public static class SelfKit
    {
        /// <summary>
        /// 将参数化 SQL + 参数列表展开为可读 SQL，并输出到 TraceKit
        /// </summary>
        /// <param name="sql">参数化 SQL 语句</param>
        /// <param name="pList">SQL 参数数组</param>
        /// <param name="dbType">数据库类型</param>
        /// <returns>展开后的 SQL 语句</returns>
        public static string GetSqlLog(string sql, SugarParameter[] pList, string dbType)
        {
            try
            {
                if (pList != null && pList.Length > 0)
                {
                    foreach (var p in pList)
                    {
                        string val;
                        if (p.Value != null)
                        {
                            switch (p.Value.GetType().Name)
                            {
                                case "Byte":
                                case "Int16":
                                case "Int32":
                                case "Int64":
                                    val = $"{p.Value}";
                                    break;
                                default:
                                    val = $"'{p.Value}'";
                                    break;
                            }
                        }
                        else
                        {
                            switch (p.DbType.ToString())
                            {
                                case "Byte":
                                case "Int16":
                                case "Int32":
                                case "Int64":
                                    val = "0";
                                    break;
                                default:
                                    val = "''";
                                    break;
                            }
                        }
                        sql = sql.Replace(p.ParameterName, val);
                    }
                }
                // 使用完全限定名解决命名空间冲突：ZL.DB.Acc.Utils.TraceKit vs ZL.PFLite.Net.TraceKit
                Utils.TraceKit.SendText($"dbtype={dbType} {sql}");
            }
            catch (Exception ex)
            {
                // 日志展开失败不应中断主流程，仅记录异常信息
                Utils.TraceKit.SendText($"[SelfKit.GetSqlLog] 参数展开异常：{ex.Message}");
            }
            return sql;
        }
    }

    /// <summary>
    /// 数据库连接配置解析工具
    ///
    /// 从 App.config 的 AppSettings / ConnectionStrings 读取数据库类型和连接串。
    /// 适用于传统 App.config 配置场景（Desktop/WinForms/Console 应用）。
    ///
    /// 新项目若使用 ASP.NET Core DI，推荐直接调用 <see cref="SugarAcc.GetScope"/> 传入参数。
    /// </summary>
    public class ConnKit
    {
        private static readonly System.Collections.Specialized.NameValueCollection _appSettings
            = System.Configuration.ConfigurationManager.AppSettings;

        // ─── DbType 解析 ────────────────────────────────────────────────

        /// <summary>
        /// 从 AppSettings["DbKind"] 读取数据库类型字符串
        /// </summary>
        public static string GetDbTypeStr()
        {
            var dbKind = _appSettings["DbKind"];
            if (string.IsNullOrWhiteSpace(dbKind))
                throw new InvalidOperationException("AppSettings 中未配置 DbKind，请添加 <add key=\"DbKind\" value=\"MySql\"/> 等配置。");
            return dbKind.Trim();
        }

        /// <summary>
        /// 解析 DbType 枚举，dbKindStr 为空时从 AppSettings["DbKind"] 取默认值
        /// </summary>
        public static DbType GetDbType(string dbKindStr = "")
        {
            if (string.IsNullOrWhiteSpace(dbKindStr))
                dbKindStr = GetDbTypeStr();

            return (DbType)new EnumConverter(typeof(DbType)).ConvertFromString(dbKindStr);
        }

        // ─── 连接串解析 ──────────────────────────────────────────────────

        /// <summary>
        /// 从 ConnectionStrings[dbKindStr] 读取连接串，dbKindStr 为空时使用 AppSettings["DbKind"]
        /// </summary>
        public static string GetConnStr(string dbKindStr = "")
        {
            if (string.IsNullOrWhiteSpace(dbKindStr))
                dbKindStr = GetDbTypeStr();
            return GetConnStrByKey(dbKindStr);
        }

        /// <summary>
        /// 从 ConnectionStrings[dbType 枚举名] 读取连接串
        /// </summary>
        public static string GetConnStr(DbType dbType)
        {
            return GetConnStrByKey(dbType.ToString());
        }

        /// <summary>
        /// 从 ConnectionStrings 按 key 名称读取连接串
        /// </summary>
        internal static string GetConnStrByKey(string key)
        {
            try
            {
                var entry = System.Configuration.ConfigurationManager.ConnectionStrings[key];
                if (entry == null)
                    throw new InvalidOperationException($"ConnectionStrings 中未找到名为 [{key}] 的配置项。");
                if (string.IsNullOrWhiteSpace(entry.ConnectionString))
                    throw new InvalidOperationException($"ConnectionStrings[{key}] 的连接串为空，请检查配置。");
                return entry.ConnectionString;
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs($"ConnKit.GetConnStrByKey({key}) 异常：{ex.Message}");
                throw;
            }
        }

        // ─── 组合入口 ────────────────────────────────────────────────────

        /// <summary>
        /// 同时返回连接串和 DbType
        /// </summary>
        public static Tuple<string, DbType> GetConnInfo(string dbKindStr = "")
        {
            var dbType = GetDbType(dbKindStr);
            var connStr = GetConnStr(dbType);
            return new Tuple<string, DbType>(connStr, dbType);
        }

        /// <summary>
        /// 资源预检（当前保留为空实现，可扩展为连通性预检）
        /// </summary>
        public static void ResourceCheck() { }
    }
}
