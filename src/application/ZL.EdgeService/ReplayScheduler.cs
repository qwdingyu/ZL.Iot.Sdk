using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ZL.Iot.Interface;
using System.Linq;

namespace ZL.EdgeService
{
    /// <summary>
    /// 边缘侧回放调度器 (工业加固版)
    /// 负责在网络恢复后，将 edge_offline_commands 中的离线执行流水批量回放给云端对账中心
    /// 优化点：批量回放、指数退避重试、毒丸隔离
    /// </summary>
    public class ReplayScheduler : IDisposable
    {
        private readonly ILogger<ReplayScheduler> _logger;
        private readonly ISqlExecutor _localSqlExecutor;
        private readonly string _gatewayId;
        private readonly HttpClient _httpClient;
        private readonly CancellationTokenSource _cts = new();
        private bool _isOnline = false;
        private int _consecutiveFailures = 0;

        /// <summary>
        /// 云端 API 基础地址（可通过配置注入）
        /// P1: 从硬编码改为配置化
        /// </summary>
        public string CloudApiBaseUrl { get; set; } = "http://cloud-api.tmom.com";
        
        /// <summary>
        /// 是否启用真实 HTTP 回放（生产环境应为 true，测试环境可设为 false 使用 Mock）
        /// </summary>
        public bool EnableRealHttpReplay { get; set; } = true;
        
        /// <summary>
        /// 连通性检查超时时间（毫秒）
        /// </summary>
        public int ConnectivityTimeoutMs { get; set; } = 5000;

        // 用于单元测试注入，拦截真实网络请求
        public Func<List<Dictionary<string, object>>, Task<bool>> BatchReplayProxy { get; set; }

        public ReplayScheduler(
            ILogger<ReplayScheduler> logger,
            ISqlExecutor localSqlExecutor,
            string gatewayId)
        {
            _logger = logger;
            _localSqlExecutor = localSqlExecutor;
            _gatewayId = gatewayId;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            BatchReplayProxy = ReplayBatchToCloudAsync;
        }

        public void Start()
        {
            Task.Run(() => WorkerLoop(_cts.Token));
            _logger.LogInformation("Replay Scheduler started for Gateway: {Gateway}", _gatewayId);
        }

        private async Task WorkerLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _isOnline = await CheckConnectivityAsync();

                    if (_isOnline)
                    {
                        await ProcessPendingCommandsAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Replay Scheduler worker loop error.");
                }

                // 指数退避逻辑：如果连续失败，增加等待时间，最高等待 5 分钟
                int delaySeconds = Math.Min(30 * (int)Math.Pow(2, Math.Min(_consecutiveFailures, 4)), 300);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
            }
        }

        public async Task ProcessPendingCommandsAsync()
        {
            // 1. 批量读取待同步指令 (一次读取 50 条)
            string sql = "SELECT * FROM edge_offline_commands WHERE sync_status = 0 AND retry_count < 5 ORDER BY create_time ASC LIMIT 50";
            var pendingCmds = await _localSqlExecutor.ExecuteQueryAsync(sql);

            if (pendingCmds == null || pendingCmds.Count == 0)
            {
                _consecutiveFailures = 0;
                return;
            }

            _logger.LogInformation("Found {Count} pending commands to replay.", pendingCmds.Count);

            // 2. 调用批量回放代理
            bool batchOk = await BatchReplayProxy(pendingCmds);

            if (batchOk)
            {
                _consecutiveFailures = 0;
                // 3. 批量更新本地状态 (工业加固：使用 ID 列表批量更新，减少磁盘 IO)
                // 修复 SQL 注入风险：使用参数化查询替代字符串拼接
                var ids = pendingCmds.Select((c, i) => $"@id{i}").ToList();
                var parameters = new Dictionary<string, object>();
                for (int i = 0; i < pendingCmds.Count; i++)
                {
                    parameters[$"id{i}"] = pendingCmds[i]["id"];
                }
                string updateSql = $"UPDATE edge_offline_commands SET sync_status = 1, error_msg = NULL WHERE id IN ({string.Join(",", ids)})";
                await _localSqlExecutor.ExecuteNonQueryAsync(updateSql, parameters);
                
                _logger.LogInformation("Successfully replayed {Count} commands to cloud.", pendingCmds.Count);
            }
            else
            {
                _consecutiveFailures++;
                // 批量记录失败，增加重试次数
                // 修复 SQL 注入风险：使用参数化查询替代字符串拼接
                var ids = pendingCmds.Select((c, i) => $"@id{i}").ToList();
                var parameters = new Dictionary<string, object>();
                for (int i = 0; i < pendingCmds.Count; i++)
                {
                    parameters[$"id{i}"] = pendingCmds[i]["id"];
                }
                string failSql = $"UPDATE edge_offline_commands SET retry_count = retry_count + 1, error_msg = 'Batch Replay failed' WHERE id IN ({string.Join(",", ids)})";
                await _localSqlExecutor.ExecuteNonQueryAsync(failSql, parameters);
                
                _logger.LogWarning("Batch replay failed. Consecutive failures: {Count}", _consecutiveFailures);
            }
        }

        private async Task<bool> ReplayBatchToCloudAsync(List<Dictionary<string, object>> cmds)
        {
            try
            {
                // 构造批量对账数据包
                // P3.3: 云端幂等要求：
                // TMom API 应使用 trace_id 作为幂等键，实现以下逻辑：
                // 1. 接收到请求后，先查询是否已存在相同 trace_id 的记录
                // 2. 如果存在，直接返回成功（避免重复插入）
                // 3. 如果不存在，执行插入操作
                // 这样可以保证即使网络波动导致重试，也不会产生重复数据
                var packet = new
                {
                    gateway_id = _gatewayId,
                    batch_id = Guid.NewGuid().ToString("N"),
                    commands = cmds.Select(cmd => new
                    {
                        trace_id = cmd.ContainsKey("trace_id") ? cmd["trace_id"]?.ToString() : "",
                        biz_code = cmd.ContainsKey("biz_code") ? cmd["biz_code"]?.ToString() : "",
                        cmd_type = cmd.ContainsKey("cmd_type") ? cmd["cmd_type"]?.ToString() : "",
                        payload = cmd.ContainsKey("payload") ? cmd["payload"]?.ToString() : "",
                        occurred_at = cmd.ContainsKey("create_time") ? cmd["create_time"]?.ToString() : DateTime.Now.ToString("O")
                    }).ToList()
                };

                string cloudUrl = $"{CloudApiBaseUrl}/api/iot/audit/replay-batch";
                
                // P1: 启用真实 HTTP 回放（可通过 EnableRealHttpReplay 配置关闭）
                if (EnableRealHttpReplay)
                {
                    var content = new StringContent(JsonConvert.SerializeObject(packet), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(cloudUrl, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Batch replay success. Count: {Count}, Url: {Url}", cmds.Count, cloudUrl);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Batch replay failed. StatusCode: {StatusCode}, Url: {Url}", response.StatusCode, cloudUrl);
                        return false;
                    }
                }
                else
                {
                    // Mock 模式：用于测试环境
                    _logger.LogDebug("Mock Batch Replay Success. Count: {Count}", cmds.Count);
                    await Task.Delay(100);
                    return true;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Batch replay HTTP error: {Message}", ex.Message);
                return false;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Batch replay timeout");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Batch replay to cloud exception: {Message}", ex.Message);
                return false;
            }
        }

        private async Task<bool> CheckConnectivityAsync()
        {
            try
            {
                // P1: 真实连通性检测（访问云端健康检查端点）
                if (EnableRealHttpReplay)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(ConnectivityTimeoutMs));
                    var healthUrl = $"{CloudApiBaseUrl}/health";
                    var response = await _httpClient.GetAsync(healthUrl, cts.Token);
                    var isOnline = response.IsSuccessStatusCode;
                    
                    if (isOnline != _isOnline)
                    {
                        _logger.LogInformation("Connectivity changed: {Status}", isOnline ? "Online" : "Offline");
                    }
                    
                    return isOnline;
                }
                else
                {
                    // Mock 模式：用于测试环境
                    return true;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogDebug("Connectivity check failed: {Message}", ex.Message);
                return false;
            }
            catch (TaskCanceledException)
            {
                _logger.LogDebug("Connectivity check timeout");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Connectivity check error: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 停止回放调度器
        /// </summary>
        public void Stop()
        {
            _cts.Cancel();
            _logger.LogInformation("Replay Scheduler stopped for Gateway: {Gateway}", _gatewayId);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _httpClient.Dispose();
        }
    }
}
