using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ZL.Biz.Execute.Biz;
using ZL.Iot.Interface;
using ZL.EdgeService;

namespace ZL.Biz.Execute.Tests
{
    /// <summary>
    /// 边缘离线命令回放调度器单元测试
    /// 
    /// 测试数据构建说明：
    /// - 不使用 SQL 文件作为参照
    /// - 根据 EdgeServiceContainer 中定义的 edge_offline_commands 表结构构建测试数据
    /// - 表字段：id, trace_id, action_id, gateway_id, biz_code, action_type, action_key, 
    ///           payload_json, cmd_type, payload, config_version, occurred_at, create_time, 
    ///           sync_status, retry_count, error_msg
    /// </summary>
    public class OfflineReplayTests
    {
        private readonly Mock<ILogger<ReplayScheduler>> _loggerMock = new();
        private readonly Mock<ISqlExecutor> _sqlExecutorMock = new();

        /// <summary>
        /// 构建符合 edge_offline_commands 表结构的测试数据
        /// </summary>
        private List<Dictionary<string, object>> BuildTestCommands(string gatewayId, int count)
        {
            var commands = new List<Dictionary<string, object>>();
            for (int i = 0; i < count; i++)
            {
                var cmd = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "id", $"CMD_{gatewayId}_{i:D3}" },
                    { "trace_id", $"TRACE_{gatewayId}_{i:D3}" },
                    { "action_id", $"ACTION_{i:D3}" },
                    { "gateway_id", gatewayId },
                    { "biz_code", $"BIZ_{i:D3}" },
                    { "action_type", "SQL" },
                    { "action_key", $"KEY_{i:D3}" },
                    { "payload_json", $"{{\"table\":\"iot_device\",\"id\":{i}}}" },
                    { "cmd_type", "SQL" },
                    { "payload", $"UPDATE iot_device SET status=1 WHERE id={i}" },
                    { "config_version", "v1.0.0" },
                    { "occurred_at", DateTime.Now.AddMinutes(-i).ToString("O") },
                    { "create_time", DateTime.Now.AddMinutes(-i).ToString("yyyy-MM-dd HH:mm:ss") },
                    { "sync_status", 0 },
                    { "retry_count", 0 },
                    { "error_msg", null }
                };
                commands.Add(cmd);
            }
            return commands;
        }

        /// <summary>
        /// 构建符合 edge_offline_commands 表结构的失败命令测试数据
        /// </summary>
        private List<Dictionary<string, object>> BuildFailedTestCommands(string gatewayId)
        {
            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "id", $"BAD_CMD_{gatewayId}" },
                    { "trace_id", $"TRACE_ERR_{gatewayId}" },
                    { "action_id", "ACTION_ERR" },
                    { "gateway_id", gatewayId },
                    { "biz_code", "BIZ_ERR" },
                    { "action_type", "SQL" },
                    { "action_key", "KEY_ERR" },
                    { "payload_json", "{\"table\":\"error_table\"}" },
                    { "cmd_type", "SQL" },
                    { "payload", "SELECT * FROM non_existent_table" },
                    { "config_version", "v1.0.0" },
                    { "occurred_at", DateTime.Now.ToString("O") },
                    { "create_time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "sync_status", 0 },
                    { "retry_count", 0 },
                    { "error_msg", null }
                }
            };
        }

        [Fact]
        public async Task ReplayScheduler_ShouldProcessPendingCommands_AndMarkSuccess()
        {
            // 场景：验证回放调度器能从数据库读取待同步指令，批量发送成功后正确更新本地状态
            var gatewayId = "GW_TEST_001";
            
            // 根据 edge_offline_commands 表结构构建测试数据
            var pendingCmds = BuildTestCommands(gatewayId, 2);

            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.Is<string>(s => s.Contains("edge_offline_commands")), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(pendingCmds);

            var scheduler = new ReplayScheduler(_loggerMock.Object, _sqlExecutorMock.Object, gatewayId);

            // 拦截批量回放代理，直接返回成功
            scheduler.BatchReplayProxy = (cmds) => Task.FromResult(true);
            
            await scheduler.ProcessPendingCommandsAsync();

            // 验证：调用了批量更新 SQL（参数化查询）
            // 验证 sync_status = 1 更新
            _sqlExecutorMock.Verify(x => x.ExecuteNonQueryAsync(
                It.Is<string>(s => s.Contains("UPDATE edge_offline_commands SET sync_status = 1")), 
                It.Is<Dictionary<string, object>>(p => 
                    p.ContainsKey("id0") && p["id0"].ToString() == "CMD_GW_TEST_001_000" && 
                    p.ContainsKey("id1") && p["id1"].ToString() == "CMD_GW_TEST_001_001")), 
                Times.Once);
        }

        [Fact]
        public async Task ReplayScheduler_OnFailure_ShouldIncrementRetryCount()
        {
            // 场景：模拟批量发送云端失败，验证系统记录重试次数而非误删数据
            var gatewayId = "GW_FAIL";
            var pendingCmds = BuildFailedTestCommands(gatewayId);

            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(pendingCmds);

            var scheduler = new ReplayScheduler(_loggerMock.Object, _sqlExecutorMock.Object, gatewayId);
            
            // 拦截批量回放代理，模拟失败
            scheduler.BatchReplayProxy = (cmds) => Task.FromResult(false);

            await scheduler.ProcessPendingCommandsAsync();

            // 验证：调用了重试计数批量更新 SQL（参数化查询）
            _sqlExecutorMock.Verify(x => x.ExecuteNonQueryAsync(
                It.Is<string>(s => s.Contains("retry_count = retry_count + 1")), 
                It.Is<Dictionary<string, object>>(p => p.ContainsKey("id0") && p["id0"].ToString() == "BAD_CMD_GW_FAIL")), 
                Times.Once);
            
            // 验证：绝不能调用标记成功的 SQL
            _sqlExecutorMock.Verify(x => x.ExecuteNonQueryAsync(
                It.Is<string>(s => s.Contains("sync_status = 1")), 
                It.IsAny<Dictionary<string, object>>()), 
                Times.Never);
        }

        [Fact]
        public async Task ReplayScheduler_RetryLimit_ShouldFilterPoisonPill()
        {
            // 场景：验证重试超过 5 次的"毒丸"指令不再被读取
            var gatewayId = "GW_POISON";
            
            var scheduler = new ReplayScheduler(_loggerMock.Object, _sqlExecutorMock.Object, gatewayId);

            await scheduler.ProcessPendingCommandsAsync();

            // 验证：查询 SQL 必须包含 retry_count < 5 的过滤条件
            _sqlExecutorMock.Verify(x => x.ExecuteQueryAsync(
                It.Is<string>(s => s.Contains("retry_count < 5")), 
                It.IsAny<Dictionary<string, object>>()), 
                Times.Once);
        }
    }
}
