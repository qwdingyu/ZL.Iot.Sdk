using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.Iot.Interface;
using ZL.Dao.IotDevice;

namespace ZL.Biz.Execute.Biz
{
    /// <summary>
    /// 通用业务配置执行器实现 (Industrial Grade)
    /// 深度集成 IBizStorageProvider 实现数据库无关性
    /// 增加脚本预热 (Pre-compilation) 能力以提升执行响应速度
    /// </summary>
    public class BizCfgExecutor : IBizCfgExecutor
    {
        private readonly ILogger<BizCfgExecutor> _logger;
        private readonly IScriptEngine _scriptEngine;
        private readonly IRuleEngine _ruleEngine;
        private readonly IBizStorageProvider _storageProvider;
        
        // 工业级加固：二级缓存，Key 为 TagId。
        private static readonly ConcurrentDictionary<string, (List<iot_exe> exes, List<iot_exeval> vals)> _configCache = new();
        
        // 预热状态：Key 为 TagId
        private static readonly ConcurrentDictionary<string, bool> _preheatedTags = new();

        public BizCfgExecutor(
            ILogger<BizCfgExecutor> logger,
            IScriptEngine scriptEngine,
            IRuleEngine ruleEngine,
            IBizStorageProvider storageProvider)
        {
            _logger = logger;
            _scriptEngine = scriptEngine;
            _ruleEngine = ruleEngine;
            _storageProvider = storageProvider;
        }

        public static void ClearCache()
        {
            _configCache.Clear();
            _preheatedTags.Clear();
        }

        private async Task<(List<iot_exe> exeList, List<iot_exeval> valList)> LoadConfigAsync(string tagId)
        {
            if (_configCache.TryGetValue(tagId, out var cached))
            {
                // 如果已缓存但未预热，则在后台触发异步预热
                if (!_preheatedTags.ContainsKey(tagId))
                {
                    _ = PreheatInternalAsync(tagId, cached.exes);
                }
                return cached;
            }

            var result = await _storageProvider.GetBizConfigAsync<iot_exe, iot_exeval>(tagId);
            _configCache.TryAdd(tagId, result);
            
            // 首次加载后触发预热
            _ = PreheatInternalAsync(tagId, result.exes);
            
            return result;
        }

        /// <summary>
        /// 内部预热逻辑：触发 ScriptEngine 和 RuleEngine 的编译缓存
        /// </summary>
        private async Task PreheatInternalAsync(string tagId, List<iot_exe> exes)
        {
            if (!_preheatedTags.TryAdd(tagId, true)) return;

            _logger.LogDebug("Preheating scripts/rules for Tag: {TagId}", tagId);
            foreach (var exe in exes)
            {
                try
                {
                    // 预热规则引擎
                    if (!string.IsNullOrWhiteSpace(exe.judge_exp))
                    {
                        _ruleEngine.Validate(exe.judge_exp, out _);
                    }
                    
                    // 预热脚本引擎（Render 内部会触发缓存）
                    if (!string.IsNullOrWhiteSpace(exe.script))
                    {
                        _scriptEngine.Validate(exe.script, out _);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Preheat failed for EXE {ExeId} on Tag {TagId}", exe.id, tagId);
                }
            }
        }

        public async Task<bool> ExeUpdateAsync(string tagId, Dictionary<string, object> facts, object context = null)
        {
            string traceId = context?.ToString() ?? Guid.NewGuid().ToString("N");
            _logger.LogInformation("[{TraceId}] Executing Update for Tag: {TagId}", traceId, tagId);
            
            try
            {
                var (exeList, valList) = await LoadConfigAsync(tagId);
                if (!exeList.Any()) return false;

                var allFacts = new Dictionary<string, object>(facts, StringComparer.OrdinalIgnoreCase);
                string bizCode = allFacts.TryGetValue("BizCode", out var bc) ? bc?.ToString() : "";
                
                // 注入初始变量
                foreach (var v in valList.Where(x => x.val_opu == "F" || x.val_opu == "P"))
                {
                    if (!allFacts.ContainsKey(v.val_field)) allFacts[v.val_field] = v.val;
                }

                var sqlExecutor = _storageProvider.SqlExecutor;
                sqlExecutor.BeginTransaction();

                try
                {
                    foreach (var exe in exeList)
                    {
                        // 1. 准入校验
                        if (!string.IsNullOrWhiteSpace(exe.judge_exp))
                        {
                            var ruleResult = await _ruleEngine.EvaluateAsync(exe.judge_exp, allFacts);
                            if (!ruleResult.IsMatch)
                            {
                                if (exe.exe_order < 100) return false; // 强准入点
                                continue;
                            }
                        }

                        // 2. 脚本渲染
                        string renderedSql = _scriptEngine.Render(exe.script, allFacts);

                        // 3. 执行
                        if (exe.exe_type == "S") // Select 模式：回填变量
                        {
                            var queryRes = await sqlExecutor.ExecuteQueryAsync(renderedSql);
                            if (queryRes.Any())
                            {
                                foreach (var kv in queryRes.First()) allFacts[kv.Key] = kv.Value;
                            }
                        }
                        else // Update/Insert 模式
                        {
                            await sqlExecutor.ExecuteNonQueryAsync(renderedSql);
                            
                            // 边缘侧存证
                            if (_storageProvider.EngineType == "Sqlite")
                            {
                                await _storageProvider.RecordOfflineCommandAsync(bizCode, "SQL", renderedSql, traceId);
                            }
                        }
                    }

                    sqlExecutor.CommitTransaction();
                    return true;
                }
                catch (Exception ex)
                {
                    sqlExecutor.RollbackTransaction();
                    _logger.LogError(ex, "[{TraceId}] Transaction failed", traceId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{TraceId}] Execution error", traceId);
                return false;
            }
        }

        public async Task<List<Dictionary<string, object>>> ExeSelectAsync(string tagId, string bizCode, Dictionary<string, object> facts)
        {
            var results = new List<Dictionary<string, object>>();
            try
            {
                var (exeList, valList) = await LoadConfigAsync(tagId);
                var allFacts = new Dictionary<string, object>(facts, StringComparer.OrdinalIgnoreCase);
                
                foreach (var v in valList.Where(x => !allFacts.ContainsKey(x.val_field))) 
                    allFacts[v.val_field] = v.val;

                foreach (var exe in exeList.Where(e => e.exe_type == "S"))
                {
                    if (!string.IsNullOrWhiteSpace(exe.judge_exp))
                    {
                        var ruleResult = await _ruleEngine.EvaluateAsync(exe.judge_exp, allFacts);
                        if (!ruleResult.IsMatch) continue;
                    }

                    string renderedSql = _scriptEngine.Render(exe.script, allFacts);
                    var queryRes = await _storageProvider.SqlExecutor.ExecuteQueryAsync(renderedSql);
                    if (queryRes != null) results.AddRange(queryRes);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExeSelectAsync failed for Tag: {TagId}", tagId);
            }
            return results;
        }
    }
}
