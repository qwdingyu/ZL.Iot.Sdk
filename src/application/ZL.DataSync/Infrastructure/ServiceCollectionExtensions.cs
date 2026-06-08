using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZL.DataSync;
using ZL.DataSync.Config;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// SyncEngine 的 DI 扩展。
/// </summary>
public static class DataSyncServiceCollectionExtensions
{
    /// <summary>
    /// 注册数据同步引擎。
    /// </summary>
    public static IServiceCollection AddDataSync(
        this IServiceCollection services,
        Action<DataSyncConfig> configure)
    {
        var config = new DataSyncConfig();
        configure(config);
        ValidateConfig(config);
        services.AddSingleton(config);
        services.AddSingleton<SyncEngine>();
        return services;
    }

    /// <summary>
    /// 从 IConfiguration 配置节注册数据同步引擎。
    /// </summary>
    public static IServiceCollection AddDataSyncFromConfig(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "DataSync")
    {
        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            // 配置不存在时不注册（允许可选配置）
            return services;
        }

        var json = section.Value;
        if (string.IsNullOrEmpty(json))
        {
            return services;
        }

        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<DataSyncConfig>(json)
            ?? throw new ArgumentException($"配置节 '{sectionName}' 格式错误", nameof(sectionName));

        ValidateConfig(config);
        services.AddSingleton(config);
        services.AddSingleton<SyncEngine>();
        return services;
    }

    /// <summary>
    /// 从配置文件路径注册数据同步引擎（JSON 文件）。
    /// </summary>
    public static IServiceCollection AddDataSyncFromJsonFile(
        this IServiceCollection services,
        string jsonFilePath,
        string sectionName = "DataSync")
    {
        string json;
        try
        {
            json = File.ReadAllText(jsonFilePath);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"无法读取配置文件: {jsonFilePath}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"无权访问配置文件: {jsonFilePath}", ex);
        }

        DataSyncConfig? config;
        try
        {
            config = Newtonsoft.Json.JsonConvert.DeserializeObject<DataSyncConfig>(json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"JSON 解析失败 [{jsonFilePath}]: {ex.Message}", ex);
        }

        if (config == null)
            throw new InvalidOperationException($"配置文件为空或格式错误: {jsonFilePath}");

        ValidateConfig(config);
        services.AddSingleton(config);
        services.AddSingleton<SyncEngine>();
        return services;
    }

    /// <summary>
    /// 基本配置校验。
    /// </summary>
    private static void ValidateConfig(DataSyncConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.LocalDbPath))
            throw new ArgumentException("LocalDbPath 不能为空", nameof(config.LocalDbPath));

        if (config.BatchSize <= 0)
            throw new ArgumentException("BatchSize 必须大于 0", nameof(config.BatchSize));

        if (config.SyncIntervalSeconds <= 0)
            throw new ArgumentException("SyncIntervalSeconds 必须大于 0", nameof(config.SyncIntervalSeconds));

        if (config.RemoteTargets.Count > 0)
        {
            foreach (var target in config.RemoteTargets)
            {
                if (string.IsNullOrWhiteSpace(target.Name))
                    throw new ArgumentException("目标 Name 不能为空", nameof(target.Name));

                if (string.IsNullOrWhiteSpace(target.ConnectionString))
                    throw new ArgumentException($"目标 {target.Name} 的 ConnectionString 不能为空", nameof(target.ConnectionString));

                if (target.Type == TargetType.Http && target.HttpConfig == null)
                    throw new ArgumentException($"HTTP 目标 {target.Name} 必须配置 HttpConfig", nameof(target.Name));
            }
        }
    }
}
