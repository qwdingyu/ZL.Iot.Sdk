using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ZL.Biz.Execute.Biz;
using ZL.Dao.IotDevice;
using ZL.Iot.Interface;

namespace ZL.Biz.Execute.Tests
{
    /// <summary>
    /// 工业级边缘执行器深度验证套件 (加固版)
    /// 侧重验证：高性能批量同步、Schema 演进自愈、版本冲突防御、SQL 安全防护、事务原子性
    /// </summary>
    public class EdgeExecutorTests
    {
        private readonly Mock<ILogger<BizCfgExecutor>> _loggerMock = new();
        private readonly Mock<IScriptEngine> _scriptEngineMock = new();
        private readonly Mock<ISqlExecutor> _sqlExecutorMock = new();
        private readonly Mock<IRuleEngine> _ruleEngineMock = new();
        private readonly Mock<IBizStorageProvider> _storageProviderMock = new();
        private readonly Mock<ILogger<MirrorSyncService>> _syncLoggerMock = new();

        public EdgeExecutorTests()
        {
            _sqlExecutorMock.Setup(x => x.Dialect).Returns("Sqlite");
            _storageProviderMock.Setup(x => x.SqlExecutor).Returns(_sqlExecutorMock.Object);
            _storageProviderMock.Setup(x => x.EngineType).Returns("Sqlite");
            // 默认 mock：返回空的 exe 和 val 列表
            _storageProviderMock.Setup(x => x.GetBizConfigAsync<iot_exe, iot_exeval>(It.IsAny<string>()))
                .ReturnsAsync((new List<iot_exe>(), new List<iot_exeval>()));
        }

        #region 1. 高性能与稳定性验证 (Performance & Stability)

        [Fact]
        public async Task MirrorSync_ShouldUseBatchExecution_ForBetterPerformance()
        {
            // 场景：验证在大批量数据同步时，系统是否调用了 Batch 接口而非逐行插入
            var service = new MirrorSyncService(_syncLoggerMock.Object, _sqlExecutorMock.Object);
            
            var rows = Enumerable.Range(1, 100).Select(i => new Dictionary<string, object> { 
                { "id", i }, { "val", "data" } 
            }).ToList();

            string payload = SerializeSnapshot("BatchTable", rows);

            // Mock Pragma to avoid schema errors
            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.Is<string>(s => s.Contains("PRAGMA")), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(new List<Dictionary<string, object>> { new Dictionary<string, object> { { "name", "id" } }, new Dictionary<string, object> { { "name", "val" } } });

            await service.ApplySnapshotAsync("G1", "V1", payload);

            // 验证：必须调用 ExecuteBatchNonQueryAsync 而不是循环调用 ExecuteNonQueryAsync
            _sqlExecutorMock.Verify(x => x.ExecuteBatchNonQueryAsync(
                It.Is<string>(s => s.Contains("INSERT INTO BatchTable")), 
                It.Is<IEnumerable<Dictionary<string, object>>>(list => list.Count() == 100)), 
                Times.Once);
        }

        [Fact]
        public async Task MirrorSync_VersionControl_ShouldPreventDowngrade()
        {
            // 场景：防止旧版本快照意外覆盖边缘侧已有的新配置
            var service = new MirrorSyncService(_syncLoggerMock.Object, _sqlExecutorMock.Object);

            // 1. 模拟本地已有版本 202604260002
            _sqlExecutorMock.Setup(x => x.ExecuteScalarAsync(It.Is<string>(s => s.Contains("edge_sync_metadata")), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync("202604260002");

            // 2. 尝试推送旧版本 202604260001
            string oldPayload = SerializeSnapshot("test", new Dictionary<string, object> { { "id", 1 } });
            bool result = await service.ApplySnapshotAsync("GW1", "202604260001", oldPayload);

            // 3. 断言：虽然返回成功（协议层已处理），但不应触发任何数据库事务
            Assert.True(result);
            _sqlExecutorMock.Verify(x => x.BeginTransaction(), Times.Never);
            _syncLoggerMock.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeastOnce);
        }

        #endregion

        #region 2. Schema 健壮性验证 (Robustness)

        [Fact]
        public async Task MirrorSync_SchemaProbing_ShouldHandleNullsInFirstRow()
        {
            // 场景：如果第一行数据的某个字段是 null，系统应通过探测后续行来确定字段类型，而不是简单丢弃
            var service = new MirrorSyncService(_syncLoggerMock.Object, _sqlExecutorMock.Object);

            var rows = new List<Dictionary<string, object>> {
                new Dictionary<string, object> { { "id", 1 }, { "ext_info", null } }, // 第一行是 null
                new Dictionary<string, object> { { "id", 2 }, { "ext_info", "Detailed Data" } } // 第二行有数据
            };

            // Mock Pragma
            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.Is<string>(s => s.Contains("PRAGMA")), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(new List<Dictionary<string, object>> { new Dictionary<string, object> { { "name", "id" } }, new Dictionary<string, object> { { "name", "ext_info" } } });

            await service.ApplySnapshotAsync("G1", "V1", SerializeSnapshot("T_PROBE", rows));

            // 验证：生成的建表或比对逻辑中必须包含 ext_info 字段
            _sqlExecutorMock.Verify(x => x.ExecuteNonQueryAsync(It.Is<string>(s => s.Contains("ext_info")), It.IsAny<Dictionary<string, object>>()), Times.AtLeastOnce);
        }

        #endregion

        #region 3. 事务与数据一致性 (Data Integrity)

        [Fact]
        public async Task MirrorSync_BatchFailure_ShouldTriggerFullRollback()
        {
            // 场景：在批量插入过程中如果发生磁盘满或约束冲突，必须确保整个快照回滚
            var service = new MirrorSyncService(_syncLoggerMock.Object, _sqlExecutorMock.Object);
            
            // 模拟批量执行异常
            _sqlExecutorMock.Setup(x => x.ExecuteBatchNonQueryAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Dictionary<string, object>>>()))
                .ThrowsAsync(new Exception("SQLite_FULL: database or disk is full"));

            var payload = SerializeSnapshot("CriticalTable", new Dictionary<string, object> { { "id", 1 } });

            // 执行同步
            bool result = await service.ApplySnapshotAsync("GW1", "V_FATAL", payload);

            // 断言：返回失败，必须执行回滚，且不能提交版本更新
            Assert.False(result);
            _sqlExecutorMock.Verify(x => x.RollbackTransaction(), Times.Once);
            _sqlExecutorMock.Verify(x => x.CommitTransaction(), Times.Never);
        }

        #endregion

        #region 4. 安全防护验证 (Security)

        [Fact]
        public async Task MirrorSync_TableNameValidation_ShouldBlockInjections()
        {
            var service = new MirrorSyncService(_syncLoggerMock.Object, _sqlExecutorMock.Object);
            
            // 非法表名尝试注入
            string[] badNames = { "base_recipe; drop table students;", "iot_exe'--", "system.configs", " " };

            foreach (var name in badNames)
            {
                var payload = SerializeSnapshot(name, new Dictionary<string, object> { { "id", 1 } });
                bool result = await service.ApplySnapshotAsync("GW1", "V_SEC", payload);
                
                // 断言：非法输入必须导致 ApplySnapshot 返回 false (Graceful Failure)
                Assert.False(result, $"Should have blocked injection attempt with name: {name}");
            }
        }

        #endregion

        private string SerializeSnapshot(string table, object rows)
        {
            var rList = rows is IEnumerable<Dictionary<string, object>> list ? list : new List<Dictionary<string, object>> { (Dictionary<string, object>)rows };
            var dto = new {
                Tables = new[] {
                    new { TableName = table, Strategy = 0, Rows = rList }
                }
            };
            return Newtonsoft.Json.JsonConvert.SerializeObject(dto);
        }

        [Fact]
        public async Task BizCfgExecutor_ExeSelect_ShouldHandleMissingData_Gracefully()
        {
            // 场景：业务代码尝试查询一个不存在的镜像表（同步尚未到达），应优雅报错不崩溃
            var executor = new BizCfgExecutor(_loggerMock.Object, _scriptEngineMock.Object, _ruleEngineMock.Object, _storageProviderMock.Object);
            
            // 设置 GetBizConfigAsync 返回非空列表以便执行到查询逻辑
            _storageProviderMock.Setup(x => x.GetBizConfigAsync<iot_exe, iot_exeval>(It.IsAny<string>()))
                .ReturnsAsync((new List<iot_exe> { new iot_exe { id = "1", exe_type = "S", script = "SELECT 1" } }, new List<iot_exeval>()));
            
            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .ThrowsAsync(new Exception("no such table: base_recipe_mirror"));

            var results = await executor.ExeSelectAsync("T1", "BIZ", new Dictionary<string, object>());

            Assert.Empty(results); // 返回空结果而非崩溃
            _loggerMock.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        #region 5. 工业级压测与并发验证 (Stress & Concurrency)

        [Fact]
        public async Task MirrorSync_StressTest_ShouldHandleLargePayload_Efficiently()
        {
            // 场景：模拟 10,000 行数据的同步，验证系统不会因为内存溢出或 SQL 拼接过长而崩溃
            var service = new MirrorSyncService(_syncLoggerMock.Object, _sqlExecutorMock.Object);
            
            int rowCount = 10000;
            var rows = Enumerable.Range(1, rowCount).Select(i => new Dictionary<string, object> {
                { "id", i },
                { "code", $"CODE_{i}" },
                { "value", 99.99 },
                { "ts", DateTime.Now }
            }).ToList();

            string payload = SerializeSnapshot("StressTable", rows);

            // Mock schema info
            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.Is<string>(s => s.Contains("PRAGMA")), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(new List<Dictionary<string, object>> {
                    new Dictionary<string, object> { { "name", "id" } },
                    new Dictionary<string, object> { { "name", "code" } },
                    new Dictionary<string, object> { { "name", "value" } },
                    new Dictionary<string, object> { { "name", "ts" } }
                });

            var startTime = DateTime.Now;
            _sqlExecutorMock.Setup(x => x.ExecuteScalarAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync("0");
            bool result = await service.ApplySnapshotAsync("STRESS_GW", "V_STRESS", payload);
            var duration = DateTime.Now - startTime;

            Assert.True(result);
            Assert.True(duration.TotalSeconds < 5, $"Stress sync too slow: {duration.TotalSeconds}s"); // 1万行本地同步应在 5秒内完成 (Mock 环境下理论极快)
            
            _sqlExecutorMock.Verify(x => x.ExecuteBatchNonQueryAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Dictionary<string, object>>>()), Times.Once);
        }

        [Fact]
        public async Task MirrorSync_Concurrency_ReadWriteLockProtection_Verification()
        {
            // 清除静态缓存，避免其他测试污染
            BizCfgExecutor.ClearCache();
            
            // 场景：验证在事务写入（同步）期间，并发的查询请求能够正常排队或通过 SQLite 的共享缓存工作，而不会抛出 "Database is locked"
            // 注意：由于我们使用的是 Mock，此测试更多是验证代码逻辑层面的并发处理能力
            var service = new MirrorSyncService(_syncLoggerMock.Object, _sqlExecutorMock.Object);
            var executor = new BizCfgExecutor(_loggerMock.Object, _scriptEngineMock.Object, _ruleEngineMock.Object, _storageProviderMock.Object);

            // 模拟一个耗时的写入事务
            _sqlExecutorMock.Setup(x => x.ExecuteBatchNonQueryAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Dictionary<string, object>>>()))
                .Returns(async () => {
                    await Task.Delay(500); // 模拟耗时操作
                    return 1;
                });

            _sqlExecutorMock.Setup(x => x.ExecuteScalarAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync("0"); // 模拟本地无旧版本

            // Mock schema info for SyncTablePacketAsync
            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.Is<string>(s => s.Contains("PRAGMA")), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(new List<Dictionary<string, object>> { new Dictionary<string, object> { { "name", "id" } } });

            var payload = SerializeSnapshot("SyncTable", new Dictionary<string, object> { { "id", 1 } });

            // 并行执行：一个同步任务，多个查询任务
            // 预置 Mock 返回
            _scriptEngineMock.Setup(x => x.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns((string s, Dictionary<string, object> f) => s);

            // 关键：为 BizCfgExecutor.ExeSelectAsync 设置 GetBizConfigAsync 返回有效的业务配置
            // exe_type 必须是 "S" 才会被 ExeSelectAsync 处理
            _storageProviderMock.Setup(x => x.GetBizConfigAsync<iot_exe, iot_exeval>(It.IsAny<string>()))
                .ReturnsAsync((new List<iot_exe> {
                    new iot_exe { id = "1", exe_type = "S", script = "SELECT * FROM SyncTable", exe_order = 1 }
                }, new List<iot_exeval>()));

            // 规则引擎返回匹配结果
            _ruleEngineMock.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(new RuleEvaluationResult { IsMatch = true });

            // 最终的 SQL 查询必须返回数据
            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.Is<string>(s => s.Contains("SyncTable") && !s.Contains("PRAGMA")), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(new List<Dictionary<string, object>> { new Dictionary<string, object> { { "id", 1 }, { "data", "test_result" } } });

            _sqlExecutorMock.Setup(x => x.ExecuteQueryAsync(It.Is<string>(s => s.Contains("iot_exe_mirror")), It.IsAny<Dictionary<string, object>>()))
                .ReturnsAsync(new List<Dictionary<string, object>> { new Dictionary<string, object> { { "exe_type", "S" }, { "script", "SELECT 1" }, { "exe_order", 1 } } });

            var syncTask = service.ApplySnapshotAsync("GW_CONC", "V_CONC", payload);
            var queryTasks = Enumerable.Range(1, 5).Select(_ => executor.ExeSelectAsync("T1", "SELECT * FROM SyncTable", new Dictionary<string, object>())).ToList();

            await Task.WhenAll(syncTask, Task.WhenAll(queryTasks));

            Assert.True(syncTask.Result);
            foreach(var t in queryTasks)
            {
                Assert.NotEmpty(await t);
            }

            _sqlExecutorMock.Verify(x => x.CommitTransaction(), Times.Once);
        }

        #endregion
    }
}
