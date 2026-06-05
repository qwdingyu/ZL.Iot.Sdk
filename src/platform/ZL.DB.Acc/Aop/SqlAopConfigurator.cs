using SqlSugar;
using System;
using System.Collections.Generic;
using System.Linq;
using ZL.PFLite.Common;
using ZL.PFLite.Net;

namespace ZL.DB.Acc.Aop
{
    /// <summary>
    /// SqlSugar AOP 配置器
    ///
    /// 职责单一，只负责向 SqlSugarScope 注入三类 AOP：
    ///   1. OnLogExecuting  — SQL 执行前日志（Trace 输出 + 结构化参数展开）
    ///   2. OnError         — SQL 执行出错日志（结构化记录 + 保留堆栈）
    ///   3. DataExecuting   — 审计字段自动填充（主键雪花 ID、创建时间、更新时间）
    ///
    /// 使用方式（在 SqlSugarScope 构造 lambda 中调用）：
    ///   var scope = new SqlSugarScope(config, db => SqlAopConfigurator.Configure(db, dbType));
    ///
    /// 审计字段名约定（INSERT 自动填充创建时间 + 更新时间；UPDATE 自动填充更新时间）：
    ///   创建时间：CreatedTime | CreateTime | created_at | create_time
    ///   更新时间：UpdatedTime | UpdateTime | updated_at | update_at
    /// </summary>
    public static class SqlAopConfigurator
    {
        /// <summary>
        /// 配置全部 AOP（日志 + 错误 + 审计）
        /// </summary>
        public static void Configure(SqlSugarClient db, SqlSugar.DbType dbType, string logFile = null)
        {
            ConfigureSqlLog(db, dbType);
            ConfigureError(db, logFile);
            ConfigureAuditFields(db);
        }

        /// <summary>
        /// SQL 执行前日志（TraceKit 输出展开后的 SQL 字符串）
        /// </summary>
        public static void ConfigureSqlLog(SqlSugarClient db, SqlSugar.DbType dbType)
        {
            db.Aop.OnLogExecuting = (sql, pars) =>
            {
                try
                {
                    var fullSql = UtilMethods.GetSqlString(dbType, sql, pars);
                    TraceKit.SendText($"DbType={dbType} {fullSql}");
                }
                catch
                {
                    // 日志失败不应影响主流程
                }
            };
        }

        /// <summary>
        /// SQL 执行出错日志（记录 SQL、参数、来源方法、堆栈）
        /// </summary>
        public static void ConfigureError(SqlSugarClient db, string logFile = null)
        {
            db.Aop.OnError = exp =>
            {
                try
                {
                    var parametersString = BuildParameterString(exp.Parametres);
                    var targetMethod = exp.TargetSite;
                    var sourceInfo = targetMethod != null
                        ? $"Class: {targetMethod.DeclaringType?.FullName}, Method: {targetMethod.Name}"
                        : "Unknown";

                    var logMessage = $"Error Message: {exp.Message}\n" +
                                     $"SQL: {exp.Sql}\n" +
                                     $"Parameters: {parametersString}\n" +
                                     $"SourceInfo: {sourceInfo}\n" +
                                     $"StackTrace: {exp.StackTrace}";

                    LogKit.Error(logMessage, logFile ?? Config.LogFile);
                }
                catch
                {
                    // 日志失败不应影响主流程
                }
            };
        }

        /// <summary>
        /// 审计字段自动填充：
        ///   INSERT：主键雪花 ID（long 类型 + 值为空）、创建时间、更新时间
        ///   UPDATE：更新时间
        /// </summary>
        public static void ConfigureAuditFields(SqlSugarClient db)
        {
            db.Aop.DataExecuting = (oldValue, entityInfo) =>
            {
                try
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        // 主键(long)-赋值雪花 Id
                        if (entityInfo.EntityColumnInfo.IsPrimarykey &&
                            entityInfo.EntityColumnInfo.PropertyInfo.PropertyType == typeof(long))
                        {
                            var id = ((dynamic)entityInfo.EntityValue).Id;
                            if (id == null || id == 0)
                                entityInfo.SetValue(SnowFlakeSingle.Instance.NextId());
                        }

                        // 创建时间
                        if (IsCreateTimeField(entityInfo.PropertyName))
                            entityInfo.SetValue(DateTime.Now);

                        // Insert 时同步填充更新时间（保证非空字段有值）
                        if (IsUpdateTimeField(entityInfo.PropertyName))
                            entityInfo.SetValue(DateTime.Now);
                    }

                    if (entityInfo.OperationType == DataFilterType.UpdateByObject)
                    {
                        if (IsUpdateTimeField(entityInfo.PropertyName))
                            entityInfo.SetValue(DateTime.Now);
                    }
                }
                catch
                {
                    // 审计失败不应影响主流程
                }
            };
        }

        // ─── 私有辅助方法 ─────────────────────────────────────────

        private static bool IsCreateTimeField(string propertyName)
        {
            return propertyName == "CreatedTime"
                || propertyName == "CreateTime"
                || propertyName == "CreatedAt"
                || propertyName == "CreatedAt"
                || propertyName == "created_at"
                || propertyName == "create_time";
        }

        private static bool IsUpdateTimeField(string propertyName)
        {
            return propertyName == "UpdatedTime"
                || propertyName == "UpdateTime"
                || propertyName == "UpdatedAt"
                || propertyName == "UpdateAt"
                || propertyName == "updated_at"
                || propertyName == "update_at";
        }

        private static string BuildParameterString(object parametres)
        {
            try
            {
                if (parametres is SugarParameter[] arr)
                    return string.Join(", ", arr.Select(p => $"{p.ParameterName}={p.Value ?? "NULL"}"));

                if (parametres is List<SugarParameter> list)
                    return string.Join(", ", list.Select(p => $"{p.ParameterName}={p.Value ?? "NULL"}"));

                return parametres?.ToString() ?? "";
            }
            catch
            {
                return "(unable to serialize parameters)";
            }
        }
    }
}
