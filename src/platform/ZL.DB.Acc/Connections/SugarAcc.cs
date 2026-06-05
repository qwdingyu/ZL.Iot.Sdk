using System;
using System.Configuration;
using System.Linq;
using SqlSugar;
using ZL.DB.Acc.Aop;
using ZL.PFLite.Common;
using ZL.PFLite.Net;

namespace ZL.DB.Acc
{
    /// <summary>
    /// SqlSugar 连接工厂
    ///
    /// 统一连接入口，所有创建 SqlSugarScope 的路径最终都委派给 <see cref="GetScope"/>。
    ///
    /// 公开入口说明：
    ///   <list type="bullet">
    ///     <item><see cref="GetScope"/>        — 核心方法，传入连接串 + DbType 直接创建</item>
    ///     <item><see cref="GetInstance(string, SqlSugar.DbType)"/> — 显式连接串 + DbType（别名）</item>
    ///     <item><see cref="GetInstance(string)"/>    — 从 ConnectionStrings 按名称读取</item>
    ///     <item><see cref="GetSugarClient(string)"/> — 从 App.config 读取 DbKind + 连接串</item>
    ///     <item><see cref="GetSugarClient(string, string)"/> — 传入 DbKind 字符串 + 连接串</item>
    ///     <item><see cref="GetConn"/>         — 带 out msg 的安全封装，不抛异常</item>
    ///   </list>
    ///
    /// 所有路径共享同一套 AOP（SQL 日志 / 错误日志 / 审计字段），由 <see cref="SqlAopConfigurator"/> 统一注入。
    /// </summary>
    public class SugarAcc
    {
        // ─── 核心工厂方法 ──────────────────────────────────────────────

        /// <summary>
        /// 核心连接工厂：根据连接串 + DbType 创建并配置 SqlSugarScope。
        /// 所有其他 GetXxx 方法最终都委派给此方法。
        /// </summary>
        /// <param name="connectionString">数据库连接串</param>
        /// <param name="dbType">数据库类型</param>
        /// <param name="logFile">日志文件名（可选，默认 Config.LogFile）</param>
        public static SqlSugarScope GetScope(string connectionString, SqlSugar.DbType dbType, string logFile = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString), "connectionString cannot be null or whitespace.");

            ConnKit.ResourceCheck();

            try
            {
                var config = new ConnectionConfig()
                {
                    ConnectionString = connectionString,
                    DbType = dbType,
                    IsAutoCloseConnection = true,
                    InitKeyType = InitKeyType.Attribute,
                    ConfigureExternalServices = new ConfigureExternalServices
                    {
                        // 支持 int? / decimal? 等 Nullable<T> 自动映射为可空列
                        EntityService = (c, p) =>
                        {
                            if (c.PropertyType.IsGenericType &&
                                c.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                            {
                                p.IsNullable = true;
                            }
                        }
                    }
                };

                var scope = new SqlSugarScope(config, db =>
                {
                    // 统一注入 AOP：SQL 日志 + 错误日志 + 审计字段
                    SqlAopConfigurator.Configure(db, dbType, logFile);
                });

                // 注册自定义 Lambda 扩展方法
                scope.SetExpMethods();

                TraceKit.SendText($"{dbType} 数据库连接已建立：{connectionString}");
                return scope;
            }
            catch (Exception ex)
            {
                TraceKit.SendText($"{dbType} 数据库连接失败：{connectionString}，异常：{ex.Message}");
                throw;
            }
        }

        // ─── 别名入口（保持向后兼容） ──────────────────────────────────

        /// <summary>
        /// 显式传入连接串 + DbType 创建 Scope（<see cref="GetScope"/> 的别名）
        /// </summary>
        public static SqlSugarScope GetInstance(string connectionString, SqlSugar.DbType dbType)
            => GetScope(connectionString, dbType);

        /// <summary>
        /// 根据 ConnectionStrings 节点名称创建 Scope。
        ///
        /// App.config 配置规范：
        /// <code>
        /// <connectionStrings>
        ///   <add name="Sql" connectionString="..." />
        ///   <add name="MySql" connectionString="..." />
        /// </connectionStrings>
        /// <appSettings>
        ///   <add key="SqlDbType" value="SqlServer" />
        ///   <add key="MySqlDbType" value="MySql" />
        /// </appSettings>
        /// </code>
        /// </summary>
        /// <param name="connectionName">ConnectionStrings 中的节点名称</param>
        public static SqlSugarScope GetInstance(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName), "connectionName cannot be null or whitespace.");

            var connConfig = ConfigurationManager.ConnectionStrings[connectionName]
                ?? throw new ConfigurationErrorsException($"未找到名为 [{connectionName}] 的连接配置，请检查 ConnectionStrings 节点。");

            var connectionString = connConfig.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ConfigurationErrorsException($"[{connectionName}] 的连接串为空，请检查配置。");

            var dbTypeStr = ConfigurationManager.AppSettings[connectionName + "DbType"];
            if (string.IsNullOrWhiteSpace(dbTypeStr))
                throw new ConfigurationErrorsException($"未找到 AppSettings 中的 [{connectionName}DbType]，请补充数据库类型配置。");

            if (!Enum.TryParse<SqlSugar.DbType>(dbTypeStr, ignoreCase: true, out var dbType))
                throw new ArgumentException($"无法识别的 DbType 值：{dbTypeStr}，请参考 SqlSugar.DbType 枚举。");

            return GetScope(connectionString, dbType);
        }

        // ─── App.config 读取快捷入口（兼容旧版 ConnKit 配置方式） ──────

        /// <summary>
        /// 从 App.config 的 AppSettings[DbKind] + ConnectionStrings[DbKind] 读取连接信息并创建 Scope。
        /// 当 dbKindStr 为空时使用 AppSettings["DbKind"] 默认值。
        /// </summary>
        /// <param name="dbKindStr">数据库类型键名，如 "MySql"、"SqlServer"（可空，取 AppSettings["DbKind"]）</param>
        public static SqlSugarScope GetSugarClient(string dbKindStr = "")
        {
            var connInfo = ConnKit.GetConnInfo(dbKindStr);
            return GetScope(connInfo.Item1, connInfo.Item2);
        }

        /// <summary>
        /// 传入 DbKind 字符串 + 连接串直接创建 Scope（不依赖配置文件）
        /// </summary>
        /// <param name="dbKindStr">数据库类型字符串，如 "MySql"</param>
        /// <param name="connStr">连接串</param>
        public static SqlSugarScope GetSugarClient(string dbKindStr, string connStr)
        {
            var dbType = ConnKit.GetDbType(dbKindStr);
            return GetScope(connStr, dbType);
        }

        // ─── 安全封装（不抛异常，通过 msg 返回错误） ────────────────────

        /// <summary>
        /// 带错误信息输出的安全连接创建（适用于 UI 场景测试连接）。
        /// 失败时返回 null，并通过 msg 返回错误描述。
        /// </summary>
        /// <param name="dataBaseType">数据库类型字符串</param>
        /// <param name="dbConnStr">连接串</param>
        /// <param name="msg">错误信息输出</param>
        public static SqlSugarScope GetConn(string dataBaseType, string dbConnStr, out string msg)
        {
            msg = string.Empty;

            if (string.IsNullOrWhiteSpace(dataBaseType))
            {
                msg = "请选择数据库类型！";
                return null;
            }
            if (string.IsNullOrWhiteSpace(dbConnStr))
            {
                msg = "数据库连接串不能为空！";
                return null;
            }

            try
            {
                return GetSugarClient(dataBaseType, dbConnStr);
            }
            catch (Exception ex)
            {
                msg = ex.Message;
                return null;
            }
        }
    }
}
