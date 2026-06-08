using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using SqlSugar;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;
using ZL.DataSync.Pipeline;

namespace ZL.DataSync.Pipeline;

/// <summary>
/// 基于 HTTP 的数据上传实现。
/// 将本地 SQLite 的记录序列化为 JSON，批量 POST 到远程 API。
/// 上传成功后更新 _Synced 标记。
/// </summary>
public sealed class HttpSyncStrategy : ISyncStrategy
{
    private readonly HttpUploadConfig _config;
    private static readonly HttpClient s_http = CreateSharedHttpClient();
    private readonly string _targetName;
    private readonly IStructuredLogger _logger;

    private static HttpClient CreateSharedHttpClient()
    {
        return new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public HttpSyncStrategy(
        HttpUploadConfig config,
        string targetName,
        IStructuredLogger logger)
    {
        _config = config;
        _targetName = targetName;
        _logger = logger;
        // P2-2 修复：复用静态 HttpClient，避免频繁创建/销毁导致端口耗尽
    }

    public string TargetName => _targetName;

    /// <summary>
    /// 从 SQLite 读取未同步的记录，批量 POST 到 API。
    /// </summary>
    public async Task<SyncReport> SyncTableAsync(
        string tableName,
        string? remoteTable,
        int batchSize,
        SqlSugarClient localDb,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. 读未同步数据 — 直接返回 Dictionary，避免 JSON 序列化
        var rows = await localDb.Queryable<Dictionary<string, object?>>()
            .AS(tableName)
            .Where("_Synced = 0")
            .OrderBy("ProcessTime")
            .Take(batchSize)
            .ToListAsync().ConfigureAwait(false);

        if (rows == null || rows.Count == 0)
            return SyncReport.Ok(tableName, 0, 0, null, sw.Elapsed.TotalMilliseconds);

        // 2. 确定目标 URL
        string? endpoint = null;
        if (_config.TableEndpoints.TryGetValue(tableName, out var tableEp))
            endpoint = tableEp;
        endpoint ??= _config.Endpoint;

        if (string.IsNullOrEmpty(endpoint))
        {
            _logger.Warning($"HTTP 目标 {_targetName} 未配置 Endpoint，跳过表 {tableName}");
            return SyncReport.Fail(tableName, rows.Count, "未配置 HTTP Endpoint", sw.Elapsed.TotalMilliseconds);
        }

        // 3. 分批上传（每 MaxHttpBatchSize 条一批）
        const int MaxHttpBatchSize = 20; // HTTP API 通常有 payload 大小限制
        int ok = 0;
        int fail = 0;
        DateTime? maxWatermark = null;
        var successRows = new List<Dictionary<string, object?>>(rows.Count);

        for (int i = 0; i < rows.Count; i += MaxHttpBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = rows.Skip(i).Take(MaxHttpBatchSize).ToList();

            // 批量构建请求体
            var requestBatches = batch.Select(row => BuildRequestBody(tableName, row)).ToList();
            var json = JsonConvert.SerializeObject(requestBatches);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var resp = await s_http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
                var respText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (resp.IsSuccessStatusCode)
                {
                    ok += batch.Count;
                    successRows.AddRange(batch);
                }
                else
                {
                    fail += batch.Count;
                    _logger.Warning($"HTTP 上传失败 [{tableName}]: HTTP {resp.StatusCode} -> {shorten(respText, 200)}");
                }
            }
            catch (Exception ex)
            {
                fail += batch.Count;
                _logger.Warning($"HTTP 上传异常 [{tableName}]: {ex.Message}");
            }
        }

        // 4. 批量标记同步成功
        if (successRows.Count > 0)
        {
            try
            {
                await BatchMarkSyncedAsync(localDb, tableName, successRows, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"HTTP 目标 {_targetName} 批量标记同步失败 {tableName}: {ex.Message}");
            }
        }

        int totalProcessed = ok + fail;
        return fail == 0 && totalProcessed > 0
            ? SyncReport.Ok(tableName, rows.Count, ok, maxWatermark?.ToString("o"), sw.Elapsed.TotalMilliseconds)
            : SyncReport.Fail(tableName, rows.Count, $"成功 {ok}/{rows.Count}, 失败 {fail}", sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// 构建标准 HTTP 请求体（兼容 DataUploadServer 的 DataUploadReq 格式）。
    /// </summary>
    private Dictionary<string, object?> BuildRequestBody(string tableName, Dictionary<string, object?> dict)
    {
        string? deviceName = null;
        if (_config.DeviceName != null)
            deviceName = _config.DeviceName;
        else if (dict.TryGetValue("StationCode", out var sc) && sc is string scStr)
            deviceName = scStr;

        string? type = null;
        if (_config.Type != null)
            type = _config.Type;
        else if (dict.TryGetValue("UploadFlag", out var uf) && uf is string ufStr)
            type = ufStr;

        string? GetOrNull(string key) => dict.TryGetValue(key, out var v) ? v?.ToString() : null;

        return new Dictionary<string, object?>
        {
            ["deviceName"] = deviceName,
            ["type"] = type,
            ["barCode"] = GetOrNull("BarCode"),
            ["EngineNo"] = GetOrNull("EngineNo"),
            ["FinalResult"] = GetOrNull("FinalResult"),
            ["DetectItems"] = GetOrNull("DetectItems"),
            ["paramArr"] = dict.TryGetValue("ParamArr", out var pa) ? pa : new List<object>(),
            ["checkResult"] = GetOrNull("FinalResult"),
            ["detectTime"] = dict.TryGetValue("ProcessTime", out var pt) && pt is DateTime d
                ? d.ToString("yyyy-MM-dd HH:mm:ss")
                : DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ["data"] = GetOrNull("data")
        };
    }

    /// <summary>
    /// 批量标记本地记录为已同步。
    /// 使用参数化 SQL 替代 WhereColumns 以绕过 SqlSugar API 限制。
    /// </summary>
    private async Task BatchMarkSyncedAsync(SqlSugarClient localDb, string tableName, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        // 优先按 Id 批量标记
        var ids = rows
            .Select(r => r.TryGetValue("Id", out var id) && id != null ? id : null)
            .Where(id => id != null)
            .Distinct()
            .ToList();

        if (ids.Count > 0)
        {
            try
            {
                // 构建 IN 参数 SQL
                var paramNames = new List<string>();
                var parameters = new List<SugarParameter>();
                foreach (var id in ids)
                {
                    var name = $"id{parameters.Count}";
                    paramNames.Add($"@{name}");
                    parameters.Add(new SugarParameter(name, id));
                }

                var sql = $"UPDATE {QuoteIdentifier(tableName, SqlSugar.DbType.Sqlite)} SET _Synced = 1, _SyncTime = @now WHERE Id IN ({string.Join(",", paramNames)})";
                parameters.Add(new SugarParameter("now", DateTime.UtcNow));

                await localDb.Ado.ExecuteCommandAsync(sql, parameters.ToArray()).ConfigureAwait(false);
            }
            catch
            {
                // 退化为按 ProcessTime 标记
                var processTimes = rows
                    .Select(r => r.TryGetValue("ProcessTime", out var v) && v is DateTime dt ? dt : (DateTime?)null)
                    .Where(t => t.HasValue && t.Value > DateTime.MinValue)
                    .Distinct()
                    .ToList();

                if (processTimes.Count > 0)
                {
                    var paramNames = new List<string>();
                    var parameters = new List<SugarParameter>();
                    foreach (var t in processTimes)
                    {
                        var name = $"pt{parameters.Count}";
                        paramNames.Add($"@{name}");
                        parameters.Add(new SugarParameter(name, t.Value));
                    }

                    var sql = $"UPDATE {QuoteIdentifier(tableName, SqlSugar.DbType.Sqlite)} SET _Synced = 1, _SyncTime = @now WHERE ProcessTime IN ({string.Join(",", paramNames)})";
                    parameters.Add(new SugarParameter("now", DateTime.UtcNow));

                    await localDb.Ado.ExecuteCommandAsync(sql, parameters.ToArray()).ConfigureAwait(false);
                }
            }
        }
    }

    private static string QuoteIdentifier(string name, SqlSugar.DbType dbType) =>
        dbType == SqlSugar.DbType.MySql ? $"`{name}`" : $"\"{name}\"";

    private static string shorten(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    public void Dispose()
    {
        // s_http 是静态共享实例，不释放
    }
}
