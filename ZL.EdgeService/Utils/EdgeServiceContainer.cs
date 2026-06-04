using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZL.Iot.Interface;
using ZL.Biz.Execute.Biz;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.EdgeService.Utils
{
    /// <summary>
    /// 边缘服务配置选项
    /// </summary>
    public class EdgeServiceOptions
    {
        /// <summary>
        /// 网关 ID
        /// </summary>
        public string GatewayId { get; set; } =
            Environment.GetEnvironmentVariable("EDGE_GATEWAY_ID") ?? "EDGE_GATEWAY_001";

        /// <summary>
        /// 云端 API 基础地址
        /// </summary>
        public string CloudApiBaseUrl { get; set; } =
            Environment.GetEnvironmentVariable("EDGE_CLOUD_API_URL") ?? "http://cloud-api.tmom.com";

        /// <summary>
        /// 是否启用真实 HTTP 回放
        /// </summary>
        public bool EnableRealHTTPReplay { get; set; } =
            Environment.GetEnvironmentVariable("EDGE_DISABLE_HTTP_REPLAY") == null;

        /// <summary>
        /// 数据库路径（可选，默认为 Drivers/EdgeData.db）
        /// </summary>
        public string DatabasePath { get; set; }

        /// <summary>
        /// 最小日志级别
        /// </summary>
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// 从环境变量创建配置选项
        /// </summary>
        public static EdgeServiceOptions FromEnvironment()
        {
            return new EdgeServiceOptions();
        }
    }

    /// <summary>
    /// 边缘服务依赖注入容器
    /// 提供服务提供者的延迟初始化和生命周期管理
    /// </summary>
    /// <remarks>
    /// 改进说明：
    /// 1. 使用 Lazy{T} 替代双检锁，更安全简洁
    /// 2. 实现 IDisposable 模式支持资源清理
    /// 3. 分离配置类，遵循单一职责原则
    /// 4. 添加异常处理和日志记录
    /// 5. 支持外部配置注入
    /// </remarks>
    public sealed class EdgeServiceContainer : IDisposable
    {
        #region 单例模式

        private static readonly Lazy<EdgeServiceContainer> _instance =
            new Lazy<EdgeServiceContainer>(() => new EdgeServiceContainer(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 获取容器单例实例
        /// </summary>
        public static EdgeServiceContainer Instance => _instance.Value;

        #endregion

        #region 配置属性

        /// <summary>
        /// 边缘服务配置选项
        /// </summary>
        public EdgeServiceOptions Options { get; private set; }

        /// <summary>
        /// 网关 ID
        /// </summary>
        public string GatewayId => Options.GatewayId;

        /// <summary>
        /// 云端 API 基础地址
        /// </summary>
        public string CloudApiBaseUrl => Options.CloudApiBaseUrl;

        /// <summary>
        /// 是否启用真实 HTTP 回放
        /// </summary>
        public bool EnableRealHTTPReplay => Options.EnableRealHTTPReplay;

        #endregion

        #region 服务提供者

        private readonly Lazy<IServiceProvider> _serviceProvider;
        private readonly object _initializeLock = new object();
        private IServiceProvider _externalProvider;
        private bool _isExternalProvider = false;
        private bool _isDisposed = false;

        /// <summary>
        /// 获取服务提供者
        /// </summary>
        /// <exception cref="ObjectDisposedException">容器已释放</exception>
        public IServiceProvider Provider
        {
            get
            {
                ThrowIfDisposed();
                return _isExternalProvider ? _externalProvider : _serviceProvider.Value;
            }
        }

        #endregion

        #region 调度器

        private ReplayScheduler _scheduler;
        private readonly ILogger<EdgeServiceContainer> _logger;

        #endregion

        #region 构造函数

        /// <summary>
        /// 私有构造函数（单例模式）
        /// </summary>
        private EdgeServiceContainer()
        {
            Options = EdgeServiceOptions.FromEnvironment();
            _serviceProvider = new Lazy<IServiceProvider>(BuildServiceProvider, LazyThreadSafetyMode.ExecutionAndPublication);
            _logger = CreateDefaultLogger();
        }

        private static ILogger<EdgeServiceContainer> CreateDefaultLogger()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            return loggerFactory.CreateLogger<EdgeServiceContainer>();
        }

        #endregion

        #region 初始化方法

        /// <summary>
        /// 使用自定义配置初始化容器
        /// </summary>
        /// <param name="options">边缘服务配置选项</param>
        /// <exception cref="ArgumentNullException">options 为 null</exception>
        /// <exception cref="InvalidOperationException">容器已初始化</exception>
        public void Initialize(EdgeServiceOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options), "配置选项不能为 null");

            lock (_initializeLock)
            {
                if (_serviceProvider.IsValueCreated && !_isExternalProvider)
                {
                    _logger.LogWarning("容器已初始化，重复调用 Initialize 将被忽略");
                    return;
                }

                Options = options;
                _logger.LogInformation("边缘服务容器已使用自定义配置初始化，GatewayId: {GatewayId}", options.GatewayId);
            }
        }

        /// <summary>
        /// 使用外部服务提供者初始化容器（主要用于单元测试）
        /// </summary>
        /// <param name="provider">外部服务提供者</param>
        /// <exception cref="ArgumentNullException">provider 为 null</exception>
        public void Initialize(IServiceProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider), "服务提供者不能为 null");

            lock (_initializeLock)
            {
                _externalProvider = provider;
                _isExternalProvider = true;
                _logger.LogInformation("边缘服务容器已使用外部 Provider 初始化");
            }
        }

        #endregion

        #region 服务构建

        private IServiceProvider BuildServiceProvider()
        {
            try
            {
                var services = new ServiceCollection();

                // 1. 日志服务
                ConfigureServicesLogging(services);

                // 2. 注册配置选项
                services.AddSingleton(Options);

                // 3. 基础引擎 (边缘侧实现)
                services.AddSingleton<IScriptEngine, ScribanScriptEngine>();

                // 4. SQLite 执行器
                ConfigureSqliteExecutor(services);

                // 5. 存储提供者
                services.AddSingleton<IBizStorageProvider, SqliteBizStorageProvider>();

                // 6. 规则引擎
                // P4: 使用工厂方法确保 ILoggerFactory 被正确注入
                services.AddSingleton<IRuleEngine>(sp =>
                    new RulesEngineAdapter(
                        sp.GetRequiredService<ILogger<RulesEngineAdapter>>(),
                        sp.GetRequiredService<ILoggerFactory>()));

                // 7. 核心业务执行器
                services.AddSingleton<IBizCfgExecutor, BizCfgExecutor>();
                services.AddSingleton<IMirrorSyncService, MirrorSyncService>();

                var provider = services.BuildServiceProvider();

                // 8. 启动回放调度器
                InitializeScheduler(provider);

                _logger.LogInformation("边缘服务容器构建完成，GatewayId: {GatewayId}", Options.GatewayId);

                return provider;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "构建服务提供者失败");
                throw new InvalidOperationException("构建服务提供者失败，请查看内部异常", ex);
            }
        }

        private void ConfigureServicesLogging(IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.AddDebug();
                builder.AddConsole();
                builder.SetMinimumLevel(Options.MinimumLogLevel);
            });
        }

        private void ConfigureSqliteExecutor(IServiceCollection services)
        {
            string dbPath = GetDatabasePath();
            EnsureDatabaseDirectory(dbPath);

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            });

            var sqliteExecutor = new SqliteExecutor(
                loggerFactory.CreateLogger<SqliteExecutor>(),
                dbPath);

            // 同步初始化数据库表
            InitializeSqliteTablesAsync(sqliteExecutor).GetAwaiter().GetResult();

            services.AddSingleton<ISqlExecutor>(sqliteExecutor);
        }

        private string GetDatabasePath()
        {
            return Options.DatabasePath
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Drivers", "EdgeData.db");
        }

        private void EnsureDatabaseDirectory(string dbPath)
        {
            string directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("已创建数据库目录: {Directory}", directory);
            }
        }

        private async Task InitializeSqliteTablesAsync(ISqlExecutor executor)
        {
            // P1: 表结构扩展支持动作事实 JSON 存储
            // 新增字段：action_id, gateway_id, action_type, action_key, payload_json, config_version, occurred_at
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS edge_offline_commands (
                    id TEXT PRIMARY KEY,
                    trace_id TEXT,
                    action_id TEXT,
                    gateway_id TEXT,
                    biz_code TEXT,
                    action_type TEXT,
                    action_key TEXT,
                    payload_json TEXT,
                    cmd_type TEXT,
                    payload TEXT,
                    config_version TEXT,
                    occurred_at TEXT,
                    create_time TEXT,
                    sync_status INTEGER DEFAULT 0,
                    retry_count INTEGER DEFAULT 0,
                    error_msg TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_sync_status ON edge_offline_commands(sync_status);
                CREATE INDEX IF NOT EXISTS idx_gateway_id ON edge_offline_commands(gateway_id);
                CREATE INDEX IF NOT EXISTS idx_action_type ON edge_offline_commands(action_type);

                -- 基础配置镜像表预览
                -- P4.2: 扩展支持触发器精细化配置（trigger_mode, debounce_ms）
                CREATE TABLE IF NOT EXISTS iot_exe_mirror (
                    id TEXT PRIMARY KEY,
                    tag_id TEXT,
                    biz_code TEXT,
                    script TEXT,
                    judge_exp TEXT,
                    exe_order INTEGER,
                    enable INTEGER DEFAULT 1,
                    trigger_mode INTEGER DEFAULT 0,
                    debounce_ms INTEGER DEFAULT 0
                );

                -- 补充变量镜像表
                CREATE TABLE IF NOT EXISTS iot_exeval_mirror (
                    id TEXT PRIMARY KEY,
                    tag_id TEXT,
                    val_field TEXT,
                    val TEXT,
                    val_opu TEXT,
                    exe_order INTEGER
                );
            ";

            try
            {
                await executor.ExecuteNonQueryAsync(createTableSql);
                _logger.LogInformation("SQLite 数据库表初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化 SQLite 数据库表失败");
                throw;
            }
        }

        private void InitializeScheduler(IServiceProvider provider)
        {
            try
            {
                _scheduler = new ReplayScheduler(
                    provider.GetRequiredService<ILogger<ReplayScheduler>>(),
                    provider.GetRequiredService<ISqlExecutor>() as SqliteExecutor,
                    Options.GatewayId);
                _scheduler.Start();
                _logger.LogInformation("回放调度器已启动，GatewayId: {GatewayId}", Options.GatewayId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化回放调度器失败");
                // 不重新抛出异常，允许容器在没有调度器的情况下运行
            }
        }

        #endregion

        #region 服务解析辅助方法

        /// <summary>
        /// 从容器获取所需服务
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <returns>服务实例</returns>
        /// <exception cref="InvalidOperationException">无法解析服务</exception>
        public T GetRequiredService<T>() where T : class
        {
            ThrowIfDisposed();
            return Provider.GetRequiredService<T>();
        }

        /// <summary>
        /// 尝试从容器获取服务
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <returns>服务实例，如果不存在则返回 null</returns>
        public T GetService<T>() where T : class
        {
            ThrowIfDisposed();
            return Provider.GetService<T>();
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 检查容器是否已释放
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(EdgeServiceContainer),
                    "边缘服务容器已释放，无法继续使用");
            }
        }

        /// <summary>
        /// 释放容器资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            lock (_initializeLock)
            {
                if (_isDisposed)
                    return;

                _logger.LogInformation("正在释放边缘服务容器资源...");

                // 停止调度器
                _scheduler?.Stop();
                _scheduler = null;

                // 释放 ServiceProvider（如果实现了 IDisposable）
                if (_serviceProvider.IsValueCreated && _serviceProvider.Value is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                }

                _isDisposed = true;
                _logger.LogInformation("边缘服务容器资源已释放");
            }
        }

        /// <summary>
        /// 重置容器（主要用于单元测试）
        /// </summary>
        /// <remarks>
        /// 注意：此方法仅用于测试场景，生产环境不应调用
        /// </remarks>
        internal static void ResetForTesting()
        {
            if (_instance.IsValueCreated)
            {
                _instance.Value.Dispose();
            }
        }

        #endregion
    }
}
