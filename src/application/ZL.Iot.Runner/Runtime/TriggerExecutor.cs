// ============================================================
//  触发执行器 - 配置驱动的业务逻辑执行引擎
//  复用 ZL.Biz.Execute 的脚本引擎和规则引擎，不重复造轮子
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Interface;
using ZL.Biz.Execute.Biz;
using ZL.Biz.Execute.Conditions;

namespace ZL.Iot.Runner.Runtime
{
    /// <summary>
    /// 配置驱动的触发执行器
    /// 
    /// 工作原理：
    /// 1. 加载配置文件中的执行器列表（ExecutorProfile）
    /// 2. 当 DeviceRoot.TriggerDataChanged 事件触发时，查找匹配的触发器
    /// 3. 使用 ConditionTreeEvaluator/JudgeType 进行条件判断
    /// 4. 使用 ScribanScriptEngine 渲染脚本中的变量 {{TagId}}
    /// 5. 执行渲染后的 SQL（Phase 1 仅记录日志，Phase 2 接入 SqliteExecutor）
    /// 
    /// 复用已有组件：
    /// - IScriptEngine（ScribanScriptEngine）：变量替换
    /// - IRuleEngine（RulesEngineAdapter + ConditionTreeEvaluator）：条件判断
    /// - ISqlExecutor（SqliteExecutor）：Phase 2 SQL 执行
    /// </summary>
    public class TriggerExecutor
    {
        private readonly List<ExecutorProfile> _executors;
        private readonly DataStorageOptions? _storage;
        private readonly IScriptEngine _scriptEngine;
        private readonly IRuleEngine _ruleEngine;
        private readonly ISqlExecutor? _sqlExecutor;
        private readonly ILogger<TriggerExecutor> _logger;

        /// <summary>
        /// 触发执行器构造方法（支持依赖注入）
        /// </summary>
        /// <param name="executors">执行器配置列表</param>
        /// <param name="storage">数据存储配置（Phase 1 传入 null）</param>
        /// <param name="scriptEngine">脚本引擎（从 DI 容器获取 ScribanScriptEngine）</param>
        /// <param name="ruleEngine">规则引擎（从 DI 容器获取 RulesEngineAdapter）</param>
        /// <param name="sqlExecutor">SQL 执行器（从 DI 容器获取 SqliteExecutor）</param>
        /// <param name="logger">日志记录器</param>
        public TriggerExecutor(
            List<ExecutorProfile> executors,
            DataStorageOptions storage,
            IScriptEngine scriptEngine,
            IRuleEngine ruleEngine,
            ISqlExecutor sqlExecutor,
            ILogger<TriggerExecutor> logger)
        {
            _executors = executors ?? new List<ExecutorProfile>();
            _storage = storage;
            _scriptEngine = scriptEngine ?? throw new ArgumentNullException(nameof(scriptEngine));
            _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
            _sqlExecutor = sqlExecutor ?? throw new ArgumentNullException(nameof(sqlExecutor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 触发执行器构造方法（Phase 1 简化构造，无需 SQL 执行器）
        /// 仅记录日志，不连接数据库
        /// </summary>
        /// <param name="executors">执行器配置列表</param>
        /// <param name="logger">日志记录器（由调用方通过共享 LoggerFactory 提供）</param>
        /// <param name="scriptEngine">可选的脚本引擎，未提供时内部创建默认 ScribanScriptEngine</param>
        public TriggerExecutor(List<ExecutorProfile> executors, ILogger<TriggerExecutor> logger, IScriptEngine? scriptEngine = null)
        {
            _executors = executors ?? new List<ExecutorProfile>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scriptEngine = scriptEngine ?? new ScribanScriptEngine(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ScribanScriptEngine>.Instance
            );
            _ruleEngine = new RulesEngineAdapter(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RulesEngineAdapter>.Instance
            );
        }

        /// <summary>
        /// 当标签值变化时调用，查找匹配的触发器并执行
        /// 此方法被 SingleDeviceRunner.OnTriggerDataChanged 回调调用
        /// </summary>
        /// <param name="tagId">触发标签 Id（业务标签名）</param>
        /// <param name="value">当前值</param>
        /// <param name="quality">数据质量（0=Good，其他=Bad）</param>
        public void OnTagChanged(string tagId, object value, byte quality = 0)
        {
            if (quality != 0)
            {
                _logger.LogDebug("[{TagId}] 数据质量为 Bad，跳过触发判断", tagId);
                return;
            }

            var matchedExecutors = _executors
                .Where(e => e.Enable && e.TagId == tagId)
                .OrderBy(e => e.ExeOrder);

            foreach (var exe in matchedExecutors)
            {
                try
                {
                    // 1. 条件判断（JudgeType 0-8 或 ConditionTree JSON）
                    if (!EvaluateCondition(exe, value))
                    {
                        _logger.LogDebug("[{BizCode}] 条件判断未通过，跳过执行", exe.BizCode);
                        continue;
                    }

                    // 2. 脚本渲染（变量替换 {{TagId}} → actual value）
                    var renderedScript = RenderScript(exe.Script, tagId, value);
                    if (string.IsNullOrWhiteSpace(renderedScript))
                    {
                        _logger.LogDebug("[{BizCode}] 渲染结果为空，跳过执行", exe.BizCode);
                        continue;
                    }

                    // 3. 执行 SQL（Phase 1 仅记录日志，Phase 2 接入 SqlExecutor）
                    ExecuteSql(exe, renderedScript);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{BizCode}] 执行异常", exe.BizCode);
                }
            }
        }

        /// <summary>
        /// 条件判断：JudgeType 0-8 + 兼容 ConditionTree JSON 格式
        /// </summary>
        private bool EvaluateCondition(ExecutorProfile exe, object value)
        {
            // JudgeType 为数字字符串（0-8），使用遗留兼容判断
            if (int.TryParse(exe.JudgeType, out var judgeType))
            {
                return EvaluateLegacyJudgeType(judgeType, exe.JudgeExp, value);
            }

            // JudgeType 不是数字，尝试作为 ConditionTree JSON 解析
            return EvaluateRuleEngine(exe.JudgeType, exe.JudgeExp, value);
        }

        /// <summary>
        /// 遗留 JudgeType 判断（0-8）
        /// 0: 任意（无条件触发）
        /// 1: 值==1
        /// 2: 值==0
        /// 3: 值变化（任意变化）
        /// 4: 值>Threshold
        /// 5: 值<Threshold
        /// 6: 值>=Threshold
        /// 7: 值<=Threshold
        /// 8: 值!=Threshold
        /// </summary>
        private bool EvaluateLegacyJudgeType(int judgeType, string judgeExp, object value)
        {
            // 值任意（无条件触发）
            if (judgeType == 0) return true;

            // 值==1（bool 型 true）
            if (judgeType == 1)
            {
                return Convert.ToBoolean(value) == true;
            }

            // 值==0（bool 型 false）
            if (judgeType == 2)
            {
                return Convert.ToBoolean(value) == false;
            }

            // 值变化（任意变化）—— 这里假设已经是变化触发的回调
            if (judgeType == 3) return true;

            // 数值比较：解析 Threshold
            if (!double.TryParse(judgeExp, out var threshold))
            {
                _logger.LogWarning("[JudgeType {Type}] Threshold 解析失败: {Exp}", judgeType, judgeExp);
                return false;
            }

            if (!double.TryParse(value?.ToString(), out var actualValue))
            {
                _logger.LogWarning("[JudgeType {Type}] 值无法转换为数值比较: {Value}", judgeType, value);
                return false;
            }

            return judgeType switch
            {
                4 => actualValue > threshold,
                5 => actualValue < threshold,
                6 => actualValue >= threshold,
                7 => actualValue <= threshold,
                8 => Math.Abs(actualValue - threshold) > 0.0001, // 浮点不相等
                _ => false
            };
        }

        /// <summary>
        /// 使用规则引擎评估条件（支持 ConditionTree JSON）
        /// </summary>
        private bool EvaluateRuleEngine(string judgeType, string judgeExp, object value)
        {
            var facts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "Value", value },
                { "TagId", judgeType } // judgeType 在这里是 JSON 时作为 ruleJson 传入
            };

            // judgeExp 作为 ruleJson 传入（ConditionTree JSON 格式）
            var result = _ruleEngine.EvaluateAsync(judgeExp, facts).GetAwaiter().GetResult();
            return result.IsMatch;
        }

        /// <summary>
        /// 使用脚本引擎渲染变量
        /// 支持格式：
        /// - Scriban: {{TagId}} 或 {{Value}}
        /// - 遗留: ?TagId? / #TagId# / @TagId@
        /// </summary>
        private string RenderScript(string script, string tagId, object value)
        {
            if (string.IsNullOrWhiteSpace(script))
                return script;

            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { tagId, value },
                { "Value", value },
                { "TagId", tagId },
                { "Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            return _scriptEngine.Render(script, variables);
        }

        /// <summary>
        /// 执行 SQL
        /// Phase 1: 仅记录日志（不连接数据库）
        /// Phase 2: 接入 SqliteExecutor 或 MySqlExecutor
        /// </summary>
        private void ExecuteSql(ExecutorProfile exe, string renderedScript)
        {
            _logger.LogInformation(
                "[{BizCode}] 执行 {ExeType} SQL | TagId={TagId} | SQL: {Sql}",
                exe.BizCode, exe.ExeType, exe.TagId, renderedScript);

            // Phase 2: 接入真实的 SQL 执行器
            // if (_sqlExecutor != null && _storage?.Type != "None")
            // {
            //     var affected = await _sqlExecutor.ExecuteNonQueryAsync(renderedScript);
            //     _logger.LogInformation("[{BizCode}] 执行完成，影响行数: {Rows}", exe.BizCode, affected);
            // }
        }

        /// <summary>
        /// 异步执行 SQL（Phase 2 使用）
        /// </summary>
        public async Task ExecuteSqlAsync(ExecutorProfile exe, string renderedScript)
        {
            if (_sqlExecutor == null || _storage?.Type == "None")
            {
                ExecuteSql(exe, renderedScript);
                return;
            }

            _logger.LogInformation(
                "[{BizCode}] 执行 {ExeType} SQL | TagId={TagId} | SQL: {Sql}",
                exe.BizCode, exe.ExeType, exe.TagId, renderedScript);

            try
            {
                int affected = exe.ExeType?.ToUpperInvariant() switch
                {
                    "S" or "Q" => 0, // Select/Query 不影响行数
                    _ => await _sqlExecutor.ExecuteNonQueryAsync(renderedScript)
                };

                _logger.LogInformation("[{BizCode}] 执行完成，影响行数: {Rows}", exe.BizCode, affected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{BizCode}] SQL 执行异常", exe.BizCode);
            }
        }

        /// <summary>
        /// 写入并回读校验（吸收自老项目 PlcDevice.WriteAndVerifyWithRetries 的设计）
        ///
        /// 工业现场必备能力：网络/PLC 抖动时，写入后立刻回读，能 99% 抓住假写。
        /// 失败重试到 maxAttempts 后放弃，避免无限重试卡死。
        ///
        /// 调用方传入驱动实例（DeviceRoot 子类，本项目用 HslUnifiedDriver），
        /// 通过 dynamic 反射调用非泛型 Read(string)/Write(string, object) 方法。
        /// 不用泛型是因为 dynamic 对泛型方法支持不好，会导致 RuntimeBinderException。
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="driver">设备驱动实例（必须具备非泛型 Read/Write 方法）</param>
        /// <param name="tagId">业务标签 Id</param>
        /// <param name="valueToWrite">要写入的值</param>
        /// <param name="maxAttempts">最大尝试次数（默认 5）</param>
        /// <param name="retryDelayMs">重试延迟（毫秒，默认 200）</param>
        /// <returns>true = 写入且回读校验通过；false = 多次重试后仍失败</returns>
        public bool WriteAndVerify<T>(
            object driver,
            string tagId,
            T valueToWrite,
            int maxAttempts = 5,
            int retryDelayMs = 200) where T : notnull
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrWhiteSpace(tagId)) throw new ArgumentException("tagId 不能为空", nameof(tagId));
            if (valueToWrite == null) throw new ArgumentNullException(nameof(valueToWrite));

            dynamic d = driver;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    d.Write(tagId, (object)valueToWrite!);
                    object readValue = d.Read(tagId);

                    if (SafeEquals(valueToWrite, readValue))
                    {
                        _logger.LogInformation(
                            "[WriteVerify] 第 {Attempt} 次写入成功 | tagId={TagId} value={Value}",
                            attempt, tagId, valueToWrite);
                        return true;
                    }

                    _logger.LogWarning(
                        "[WriteVerify] 第 {Attempt} 次回读不一致 | tagId={TagId} 写入={Wrote} 读回={Read}",
                        attempt, tagId, valueToWrite, readValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[WriteVerify] 第 {Attempt} 次异常 | tagId={TagId} err={Err}",
                        attempt, tagId, ex.Message);
                }

                if (attempt < maxAttempts)
                {
                    System.Threading.Thread.Sleep(retryDelayMs);
                }
            }

            _logger.LogError(
                "[WriteVerify] 连续 {Max} 次写入失败 | tagId={TagId} value={Value}",
                maxAttempts, tagId, valueToWrite);
            return false;
        }

        /// <summary>
        /// 安全的值相等比较（参考老项目 SafeCompareHelper.SafeEquals）
        /// 处理：null、字符串、数值、bool 的常见比较场景
        /// </summary>
        private static bool SafeEquals<T>(T a, T b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            // 字符串：忽略大小写
            if (a is string sa && b is string sb)
                return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);

            // 数值：转 double 比较（处理 float/int/decimal 之间的隐式转换）
            if (a is IConvertible && b is IConvertible)
            {
                try
                {
                    double da = Convert.ToDouble(a);
                    double db = Convert.ToDouble(b);
                    return Math.Abs(da - db) < 0.0001;
                }
                catch
                {
                    // 转换失败走 ToString 兜底
                }
            }

            // 兜底：ToString 后比较
            return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}