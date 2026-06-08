namespace ZL.DataSync.Config;

/// <summary>
/// 远程目标配置（一个目标 = 一个数据库/HTTP API 端点）。
/// </summary>
public sealed class RemoteTargetConfig
{
    /// <summary>目标名称（用于日志标识，如 "MES-MySQL"）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>目标类型</summary>
    public TargetType Type { get; init; } = TargetType.MySql;

    /// <summary>连接字符串</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// 表映射配置。Key=本地表名，Value=远程表名。
    /// 为空时默认表名相同。
    /// </summary>
    public Dictionary<string, string> TableMappings { get; init; } = new();

    /// <summary>
    /// 数据上传模式（当 Type=Http 时使用）。
    /// </summary>
    public HttpUploadConfig? HttpConfig { get; init; }
}

/// <summary>远程目标类型</summary>
public enum TargetType
{
    /// <summary>MySQL 数据库</summary>
    MySql,
    /// <summary>SQL Server 数据库</summary>
    SqlServer,
    /// <summary>PostgreSQL 数据库</summary>
    PostgreSql,
    /// <summary>Oracle 数据库</summary>
    Oracle,
    /// <summary>HTTP API（JSON 格式推送）</summary>
    Http
}

/// <summary>
/// HTTP 上传配置（仅 Type=Http 时使用）。
/// </summary>
public sealed class HttpUploadConfig
{
    /// <summary>API 端点 URL</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// 每个表对应的 API 端点。
    /// Key=本地表名，Value=API URL。
    /// 为空时使用上面的 Endpoint 作为默认值。
    /// </summary>
    public Dictionary<string, string> TableEndpoints { get; init; } = new();

    /// <summary>请求超时（秒）。默认 30</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// 自定义请求头（如认证 Token）。
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = new();

    /// <summary>
    /// 自定义请求体模板。
    /// 支持变量：{deviceName}, {barCode}, {timestamp}, {table}, {data}
    /// 为空时自动生成标准 JSON 结构。
    /// </summary>
    public string? BodyTemplate { get; init; }

    /// <summary>设备标识（用于请求体中的 deviceName 字段）</summary>
    public string? DeviceName { get; init; }

    /// <summary>数据分类标识（用于请求体中的 type 字段）</summary>
    public string? Type { get; init; }
}
