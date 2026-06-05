using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SqlSugar;

namespace ZL.DB.Acc
{
    /// <summary>
    /// 数据库表结构初始化器（SqlSugar CodeFirst 最佳实践）
    /// 
    /// 设计原则：
    /// 1. 显式注册模型类型数组，避免旧版反射扫描带来的不确定性和性能开销
    /// 2. 仅创建不存在的表（IsAnyTable 判断），不影响已有数据
    /// 3. 通过 SetStringDefaultLength(200) 统一字符串字段默认长度
    /// 4. 所有新增模型统一通过本初始化器注册，无需修改扫描路径
    /// 5. 支持差异化初始化（仅初始化指定模型类型）
    /// 
    /// 使用方式：
    /// // 初始化所有注册模型
    /// DbSchemaInitializer.Initialize();
    /// 
    /// // 仅初始化指定模型
    /// DbSchemaInitializer.Initialize(typeof(iot_tag_snapshot), typeof(iot_device_runtime));
    /// 
    /// // 检查特定表是否存在
    /// bool exists = DbSchemaInitializer.TableExists<iot_tag_snapshot>();
    /// </summary>
    public static class DbSchemaInitializer
    {
        /// <summary>
        /// 已注册待初始化的模型类型列表
        /// 所有新模型必须在此注册，以便 InitTables 正确创建表结构
        /// </summary>
        private static readonly List<Type> _registeredTypes = new List<Type>();

        /// <summary>
        /// 初始化锁，防止并发初始化
        /// </summary>
        private static readonly object _initLock = new object();

        /// <summary>
        /// 是否已完成初始化标志
        /// </summary>
        private static bool _initialized = false;

        /// <summary>
        /// 日志输出委托（可替换为 Log4Net/NLog/Console 等）
        /// </summary>
        public static Action<string> LogWriter { get; set; } = msg => Console.WriteLine($"[DbSchemaInit] {msg}");

        #region 模型注册

        /// <summary>
        /// 注册模型类型到初始化器
        /// </summary>
        /// <typeparam name="T">模型类型</typeparam>
        public static void Register<T>() where T : class, new()
        {
            Register(typeof(T));
        }

        /// <summary>
        /// 注册模型类型到初始化器
        /// </summary>
        /// <param name="type">模型类型</param>
        public static void Register(Type type)
        {
            if (!type.IsClass || type.IsAbstract)
                throw new ArgumentException($"类型 {type.FullName} 必须是具体类", nameof(type));

            if (!_registeredTypes.Contains(type))
            {
                _registeredTypes.Add(type);
                LogWriter($"已注册模型: {type.Name}");
            }
        }

        /// <summary>
        /// 批量注册模型类型
        /// </summary>
        public static void RegisterRange(params Type[] types)
        {
            foreach (var type in types)
                Register(type);
        }

        /// <summary>
        /// 获取已注册的全部模型类型
        /// </summary>
        public static IReadOnlyList<Type> GetRegisteredTypes() => _registeredTypes.AsReadOnly();

        #endregion

        #region 初始化核心

        /// <summary>
        /// 执行数据库表初始化（创建所有已注册的不存在表）
        /// 线程安全，支持多次调用（仅首次执行实际初始化）
        /// </summary>
        /// <param name="connectionName">连接配置名称（默认 "Sql"）</param>
        public static void Initialize(string connectionName = "Sql")
        {
            lock (_initLock)
            {
                if (_initialized)
                {
                    LogWriter("初始化已完成，跳过");
                    return;
                }

                LogWriter("========== 开始数据库表结构初始化 ==========");

                try
                {
                    UseConnection(connectionName, InitializeCore);
                }
                catch (Exception ex)
                {
                    LogWriter($"初始化失败: {ex.Message}");
                    throw;
                }

                _initialized = true;
                LogWriter("========== 数据库表结构初始化完成 ==========");
            }
        }

        /// <summary>
        /// 初始化指定的模型类型（可调用多次逐步初始化）
        /// </summary>
        /// <param name="connectionName">连接配置名称</param>
        /// <param name="types">要初始化的模型类型数组</param>
        public static void Initialize(string connectionName, params Type[] types)
        {
            if (types == null || types.Length == 0)
            {
                Initialize(connectionName);
                return;
            }

            LogWriter($"========== 开始初始化指定模型 ({types.Length} 个) ==========");

            try
            {
                UseConnection(connectionName, db =>
                {
                    db.DbMaintenance.CreateDatabase();

                    foreach (var type in types)
                    {
                        InitializeSingleTable(db, type);
                    }
                });
                LogWriter($"========== 指定模型初始化完成 ==========");
            }
            catch (Exception ex)
            {
                LogWriter($"初始化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 初始化核心逻辑
        /// </summary>
        private static void InitializeCore(ISqlSugarClient db)
        {
            // 确保数据库存在
            db.DbMaintenance.CreateDatabase();
            LogWriter($"数据库检查完成: {db.CurrentConnectionConfig.ConnectionString}");

            if (_registeredTypes.Count == 0)
            {
                LogWriter("警告: 没有注册任何模型类型，请先调用 Register<T>() 或 RegisterRange()");
                return;
            }

            // 统一设置字符串默认长度为 200（SqlSugar 最佳实践）
            // 对于特定字段超过 200 字符的，在模型 [SugarColumn(Length = N)] 中单独指定
            db.CodeFirst.SetStringDefaultLength(200);

            // 逐个检查并初始化表
            var created = 0;
            var skipped = 0;

            foreach (var type in _registeredTypes.ToList())
            {
                var tableName = GetTableName(type);
                if (db.DbMaintenance.IsAnyTable(tableName))
                {
                    LogWriter($"[跳过] 表已存在: {tableName}");
                    skipped++;
                }
                else
                {
                    try
                    {
                        db.CodeFirst.InitTables(type);
                        LogWriter($"[创建] 表已创建: {tableName}");
                        created++;
                    }
                    catch (Exception ex)
                    {
                        LogWriter($"[错误] 创建表 {tableName} 失败: {ex.Message}");
                        throw;
                    }
                }
            }

            LogWriter($"汇总: 新建 {created} 个表，跳过 {skipped} 个已有表");
        }

        /// <summary>
        /// 初始化单个表
        /// </summary>
        private static void InitializeSingleTable(ISqlSugarClient db, Type type)
        {
            var tableName = GetTableName(type);

            // 设置字符串默认长度
            db.CodeFirst.SetStringDefaultLength(200);

            if (db.DbMaintenance.IsAnyTable(tableName))
            {
                LogWriter($"[跳过] 表已存在: {tableName}");
                return;
            }

            db.CodeFirst.InitTables(type);
            LogWriter($"[创建] 表已创建: {tableName}");
        }

        /// <summary>
        /// 从模型类型推断表名（与 SqlSugar 推断逻辑一致）
        /// 表名 = 类名（去掉 "iot_" 前缀用于别名），实际使用类名本身
        /// 如需自定义表名，在模型类上使用 [SugarTable("xxx")]
        /// </summary>
        private static string GetTableName(Type type)
        {
            // 先检查 [SugarTable("xxx")]
            var attr = type.GetCustomAttribute<SugarTable>();
            if (attr != null && !string.IsNullOrEmpty(attr.TableName))
                return attr.TableName;

            // 默认：类名（SqlSugar 默认行为）
            return type.Name;
        }

        private static void UseConnection(string connectionName, Action<ISqlSugarClient> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            using (var db = SugarAcc.GetInstance(connectionName))
            {
                action(db);
            }
        }

        private static TResult UseConnection<TResult>(string connectionName, Func<ISqlSugarClient, TResult> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            using (var db = SugarAcc.GetInstance(connectionName))
            {
                return func(db);
            }
        }

        #endregion

        #region 查询接口

        /// <summary>
        /// 检查指定模型对应的表是否存在
        /// </summary>
        /// <typeparam name="T">模型类型</typeparam>
        /// <param name="connectionName">连接配置名称</param>
        /// <returns>表是否存在</returns>
        public static bool TableExists<T>(string connectionName = "Sql") where T : class, new()
        {
            return UseConnection(connectionName, db =>
            {
                var tableName = GetTableName(typeof(T));
                return db.DbMaintenance.IsAnyTable(tableName);
            });
        }

        /// <summary>
        /// 检查指定表名的表是否存在
        /// </summary>
        /// <param name="tableName">数据库表名</param>
        /// <param name="connectionName">连接配置名称</param>
        /// <returns>表是否存在</returns>
        public static bool TableExists(string tableName, string connectionName = "Sql")
        {
            return UseConnection(connectionName, db => db.DbMaintenance.IsAnyTable(tableName));
        }

        /// <summary>
        /// 获取指定表的所有列信息
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="connectionName">连接配置名称</param>
        /// <returns>列信息列表</returns>
        public static List<DbColumnInfo> GetTableColumns(string tableName, string connectionName = "Sql")
        {
            return UseConnection(connectionName, db => db.DbMaintenance.GetColumnInfosByTableName(tableName));
        }

        /// <summary>
        /// 获取当前数据库的所有表名
        /// </summary>
        /// <param name="connectionName">连接配置名称</param>
        /// <returns>表名列表</returns>
        public static List<string> GetAllTableNames(string connectionName = "Sql")
        {
            return UseConnection(connectionName, db => db.DbMaintenance.GetTableInfoList().Select(x => x.Name).ToList());
        }

        #endregion

        #region 结构差异查询

        /// <summary>
        /// 获取模型与数据库表之间的结构差异（仅对比列，不含索引/约束）
        /// 可用于检测模型变更并生成 ALTER TABLE 脚本
        /// </summary>
        /// <typeparam name="T">模型类型</typeparam>
        /// <param name="connectionName">连接配置名称</param>
        /// <returns>差异 SQL 脚本列表</returns>
        public static List<string> GetDiffSql<T>(string connectionName = "Sql") where T : class, new()
        {
            return UseConnection(connectionName, db =>
            {
                var types = new[] { typeof(T) };
                var diffTables = db.CodeFirst.GetDifferenceTables(types);
                var diffList = diffTables.ToDiffList();
                return diffList.Select(x => x.ToString()).ToList();
            });
        }

        /// <summary>
        /// 获取所有注册模型与数据库之间的结构差异 SQL
        /// </summary>
        /// <param name="connectionName">连接配置名称</param>
        /// <returns>差异 SQL 脚本列表</returns>
        public static List<string> GetAllDiffSql(string connectionName = "Sql")
        {
            return UseConnection(connectionName, db =>
            {
                var types = _registeredTypes.ToArray();
                var diffTables = db.CodeFirst.GetDifferenceTables(types);
                return diffTables.ToDiffList().Select(x => x.ToString()).ToList();
            });
        }

        #endregion

        /// <summary>
        /// 重置初始化状态（仅用于测试场景）
        /// </summary>
        public static void Reset()
        {
            lock (_initLock)
            {
                _initialized = false;
                _registeredTypes.Clear();
            }
        }
    }
}
