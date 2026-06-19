// ============================================================
//  触发执行器 - 配置驱动的业务逻辑执行引擎
//  复用 ZL.Biz.Execute 的脚本引擎和规则引擎，不重复造轮子
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
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
    /// 5. 执行渲染后的 SQL（无执行器时记录日志/JSONL，有执行器时通过统一 ISqlExecutor 执行）
    ///
    /// 复用已有组件：
    /// - IScriptEngine（ScribanScriptEngine）：变量替换
    /// - IRuleEngine（RulesEngineAdapter + ConditionTreeEvaluator）：条件判断
    /// - ISqlExecutor（SqlSugarExecutor）：统一数据库 SQL 执行
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
        /// 标签值提供者（用于 F 分支读取各标签值和写回反馈信号）
        /// </summary>
        private readonly ITagValueProvider? _tagValueProvider;

        /// <summary>
        /// FieldMapping 数据写入器（有 ISqlExecutor 时使用，否则用 JSONL 降级）
        /// </summary>
        private readonly IFieldMappingSink? _fieldMappingSink;

        /// <summary>
        /// P1-1 统一写入队列。非空时，规则 SQL 与 FieldMapping 改为投递到单消费者
        /// 队列异步落库，采集线程不再阻塞在 DB I/O；为空时回退到原同步落库/JSONL 行为
        /// （供无队列的单元测试与简化构造使用）。
        /// </summary>
        private readonly RunnerWriteQueue? _writeQueue;

        /// <summary>
        /// 触发执行器构造方法（支持依赖注入）
        /// </summary>
        public TriggerExecutor(
            List<ExecutorProfile> executors,
            DataStorageOptions storage,
            IScriptEngine scriptEngine,
            IRuleEngine ruleEngine,
            ISqlExecutor sqlExecutor,
            ILogger<TriggerExecutor> logger,
            ITagValueProvider? tagValueProvider = null,
            RunnerWriteQueue? writeQueue = null)
        {
            _executors = executors ?? new List<ExecutorProfile>();
            _storage = storage;
            _scriptEngine = scriptEngine ?? throw new ArgumentNullException(nameof(scriptEngine));
            _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
            _sqlExecutor = sqlExecutor ?? throw new ArgumentNullException(nameof(sqlExecutor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tagValueProvider = tagValueProvider;
            _writeQueue = writeQueue;
            _fieldMappingSink = sqlExecutor != null && storage?.Type != "None"
                ? new SqlExecutorFieldMappingSink(sqlExecutor, logger)
                : null;
        }

        /// <summary>
        /// 触发执行器构造方法（Phase 1 简化构造，无需 SQL 执行器）
        /// </summary>
        public TriggerExecutor(
            List<ExecutorProfile> executors,
            ILogger<TriggerExecutor> logger,
            IScriptEngine? scriptEngine = null,
            ITagValueProvider? tagValueProvider = null)
        {
            _executors = executors ?? new List<ExecutorProfile>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scriptEngine = scriptEngine ?? new ScribanScriptEngine(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ScribanScriptEngine>.Instance
            );
            _ruleEngine = new RulesEngineAdapter(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RulesEngineAdapter>.Instance
            );
            _tagValueProvider = tagValueProvider;
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

                    // ★ FieldMapping 分支：exe_type = "F" 时执行配置驱动采集
                    if (string.Equals(exe.ExeType, "F", StringComparison.OrdinalIgnoreCase))
                    {
                        ExecuteFieldMapping(exe.Script, tagId);
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
            // JudgeType 0-8：使用遗留兼容判断
            if (exe.JudgeType is >= 0 and <= 8)
            {
                return EvaluateLegacyJudgeType(exe.JudgeType, exe.JudgeExp, value);
            }

            // JudgeType > 8：作为 ConditionTree JSON 索引解析
            return EvaluateRuleEngine(exe.JudgeType.ToString(), exe.JudgeExp, value);
        }

        /// <summary>
        /// 遗留 JudgeType 判断（0-8）
        /// 0: 任意（无条件触发）
        /// 1: 值==1
        /// 2: 值==0
        /// 3: 值变化（任意变化）
        /// 4: 值 &gt; Threshold
        /// 5: 值 &lt; Threshold
        /// 6: 值 &gt;= Threshold
        /// 7: 值 &lt;= Threshold
        /// 8: 值 != Threshold
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
            // 使用 Task.Run 避免在无 SynchronizationContext 环境下阻塞采集线程
            var result = Task.Run(() => _ruleEngine.EvaluateAsync(judgeExp, facts)).GetAwaiter().GetResult();
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
        /// 执行 FieldMapping 配置驱动采集
        /// 数据流：解析 JSON → 发现轴列表 → 展开多行 → 自动建表 → INSERT → 反馈写回
        /// </summary>
        private void ExecuteFieldMapping(string? jsonConfig, string triggerTagId)
        {
            if (string.IsNullOrWhiteSpace(jsonConfig))
            {
                _logger.LogWarning("[FieldMapping] 配置为空，跳过执行");
                return;
            }

            var config = FieldMappingConfig.FromJson(jsonConfig);
            if (config?.Columns == null || config.Columns.Count == 0)
            {
                _logger.LogWarning("[FieldMapping] JSON 解析失败或缺少 columns");
                return;
            }

            // 1. 发现轴列表
            var axes = DiscoverAxes(config.PerAxis);
            if (axes.Count == 0) axes = [""]; // 非 perAxis 模式：一行

            // 2. 为每个轴展开一行
            var rows = new List<Dictionary<string, object?>>();
            var now = DateTime.Now;

            foreach (var axis in axes)
            {
                var row = new Dictionary<string, object?>();

                foreach (var col in config.Columns)
                {
                    // 非 perAxis 字段且已在前面轴处理过，跳过
                    if (col.PerAxis && axis == "") continue;
                    if (!col.PerAxis && axis != "" && rows.Count > 0) continue;

                    row[col.Name] = ResolveFieldValue(col, axis, triggerTagId);
                }

                // 只有需要添加的行（非 perAxis 或 perAxis 有轴）
                if (row.Count > 0)
                {
                    row["ProcessTime"] = now;
                    rows.Add(row);
                }
            }

            if (rows.Count == 0) return;

            _logger.LogInformation("[FieldMapping] 展开完成: {Rows} 行, 目标表={Table}", rows.Count, config.TableName);

            // 反馈写回动作：落库成功后才执行（防丢数据——PLC 收到“采集完成”信号时数据已落库）。
            Action? feedback = (!string.IsNullOrEmpty(config.FeedbackTag) && _tagValueProvider != null)
                ? () =>
                {
                    _tagValueProvider.WriteTag(config.FeedbackTag!, 1);
                    _logger.LogInformation("[FieldMapping] 反馈已写回: Tag={FeedbackTag}, Value=1", config.FeedbackTag);
                }
                : null;

            // P1-1：有统一写入队列时，投递表插入命令异步落库，落库成功后由消费者触发反馈写回。
            if (_writeQueue != null)
            {
                _writeQueue.TryEnqueue(new TableInsertCommand
                {
                    TableName = config.TableName,
                    Columns = config.Columns,
                    Rows = rows,
                    OnCommitted = feedback
                });
                return;
            }

            // 兼容路径（无队列）：同步落库后立即反馈写回。
            if (_fieldMappingSink != null)
            {
                _fieldMappingSink.EnsureTable(config.TableName, config.Columns);
                _fieldMappingSink.InsertRows(config.TableName, rows);
            }
            else
            {
                // 降级策略：写入本地 JSON Lines 文件（保证数据不丢）
                WriteFieldMappingFallback(config, rows);
            }

            feedback?.Invoke();
        }

        /// <summary>
        /// 发现轴列表
        /// </summary>
        private static List<string> DiscoverAxes(PerAxisConfig? perAxis)
        {
            if (perAxis?.Enabled != true)
                return []; // 单行模式

            var discovery = perAxis.AxisDiscovery;
            if (discovery == null) return [];

            return discovery.Mode?.ToLowerInvariant() switch
            {
                "explicit" => discovery.Axes ?? [],
                "range" => DiscoverAxesByRange(discovery),
                _ => [] // auto 模式需要全量标签名列表，Runner 中暂不支持
            };
        }

        private static List<string> DiscoverAxesByRange(AxisDiscoveryConfig discovery)
        {
            var axes = new List<string>();
            int start = discovery.Start ?? 1;
            int end = discovery.End ?? 1;
            int step = discovery.Step > 0 ? discovery.Step : 1;

            for (int i = start; i <= end; i += step)
            {
                axes.Add(discovery.Format != null ? string.Format(discovery.Format, i) : i.ToString());
            }
            return axes;
        }

        /// <summary>
        /// 解析单个字段的值
        /// </summary>
        private object? ResolveFieldValue(FieldMappingRule col, string currentAxis, string triggerTagId)
        {
            object? rawValue = null;

            switch (col.SourceType)
            {
                case "AxisNumber":
                    rawValue = currentAxis;
                    break;

                case "AxisValue":
                    if (currentAxis != null && !string.IsNullOrEmpty(col.SourceField))
                    {
                        var tagName = col.SourceField.Replace("{0}", currentAxis);
                        rawValue = ReadTagValue(tagName);
                    }
                    else if (!string.IsNullOrEmpty(col.SourceTag))
                    {
                        rawValue = ReadTagValue(col.SourceTag);
                    }
                    break;

                case "TagValue":
                    if (!string.IsNullOrEmpty(col.SourceTag))
                        rawValue = ReadTagValue(col.SourceTag);
                    break;

                case "StationProperty":
                    rawValue = col.SourceField;
                    break;

                case "FixedValue":
                    rawValue = col.SourceField;
                    break;

                case "SystemVariable":
                    rawValue = col.SourceField?.ToLowerInvariant() switch
                    {
                        "now" => DateTime.Now,
                        "utcnow" => DateTime.UtcNow,
                        "guid" => Guid.NewGuid().ToString(),
                        _ => col.SourceField
                    };
                    break;
            }

            // 值转换
            if (rawValue != null && col.Transform != null && col.Transform.Count > 0)
            {
                var key = rawValue.ToString()?.Trim();
                if (key != null && col.Transform.TryGetValue(key, out var transformed))
                    rawValue = transformed;
            }

            // 缩放
            if (rawValue is IConvertible conv && col.ScaleFactor != 1.0)
            {
                try { rawValue = Convert.ToDouble(conv) * col.ScaleFactor; }
                catch { }
            }

            return rawValue ?? col.DefaultValue;
        }

        /// <summary>
        /// 读取标签值：优先通过 ITagValueProvider，fallback 返回 null
        /// </summary>
        private object? ReadTagValue(string tagId)
        {
            if (_tagValueProvider != null)
            {
                try { return _tagValueProvider.ReadTag(tagId); }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// 将 FieldMapping 行写入本地 JSON Lines 文件（降级策略）
        /// 当无数据库可用时，确保采集数据不丢失
        /// </summary>
        private void WriteFieldMappingFallback(FieldMappingConfig config, List<Dictionary<string, object?>> rows)
        {
            try
            {
                var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                Directory.CreateDirectory(dataDir);
                var filePath = Path.Combine(dataDir, $"{config.TableName}.jsonl");

                foreach (var row in rows)
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(row);
                    File.AppendAllText(filePath, json + Environment.NewLine);
                }

                _logger.LogInformation("[FieldMapping] 已写入 JSON Lines: {FilePath} ({Rows} 行)", filePath, rows.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FieldMapping] 降级写入失败: {TableName}", config.TableName);
            }
        }

        /// <summary>
        /// 执行 SQL — 有 ISqlExecutor 时真实执行，否则写入 JSONL 降级
        /// </summary>
        private void ExecuteSql(ExecutorProfile exe, string renderedScript)
        {
            // P1-1：有统一写入队列时，把渲染后的 SQL 投递到单消费者异步落库，
            // 采集线程立即返回，不阻塞在 DB I/O，也不再 sync-over-async。
            if (_writeQueue != null)
            {
                _writeQueue.TryEnqueue(new RawSqlCommand
                {
                    BizCode = exe.BizCode,
                    ExeType = exe.ExeType ?? string.Empty,
                    Sql = renderedScript
                });
                return;
            }

            if (_sqlExecutor != null && _storage?.Type != "None")
            {
                // 兼容路径（无队列）：直接执行。
                // 使用 Task.Run 避免阻塞采集线程，将异步工作移至线程池
                try
                {
                    int affected = Task.Run(() => _sqlExecutor.ExecuteNonQueryAsync(renderedScript)).GetAwaiter().GetResult();
                    _logger.LogInformation("[{BizCode}] 执行完成 | Type={ExeType} | 影响行数: {Rows}",
                        exe.BizCode, exe.ExeType, affected);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{BizCode}] SQL 执行异常 | SQL: {Sql}", exe.BizCode, renderedScript);
                }
            }
            else
            {
                // Phase 1 / 降级：写入 JSON Lines 文件
                try
                {
                    var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                    Directory.CreateDirectory(dataDir);
                    var filePath = Path.Combine(dataDir, $"sql_exec_{DateTime.Now:yyyyMMdd}.jsonl");
                    var entry = new { time = DateTime.Now, bizCode = exe.BizCode, exeType = exe.ExeType, sql = renderedScript };
                    var json = System.Text.Json.JsonSerializer.Serialize(entry);
                    File.AppendAllText(filePath, json + Environment.NewLine);
                }
                catch { }
                _logger.LogInformation("[{BizCode}] 模拟执行 {ExeType} | TagId={TagId} | SQL: {Sql}",
                    exe.BizCode, exe.ExeType, exe.TagId, renderedScript);
            }
        }

        /// <summary>
        /// 异步执行 SQL（Phase 1 记录日志，Phase 2 真实执行）
        /// </summary>
        public async Task ExecuteSqlAsync(ExecutorProfile exe, string renderedScript)
        {
            if (_sqlExecutor == null || _storage?.Type == "None")
            {
                // 降级：写入 JSON Lines 文件
                try
                {
                    var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                    Directory.CreateDirectory(dataDir);
                    var filePath = Path.Combine(dataDir, $"sql_exec_{DateTime.Now:yyyyMMdd}.jsonl");
                    var entry = new { time = DateTime.Now, bizCode = exe.BizCode, exeType = exe.ExeType, sql = renderedScript };
                    var json = System.Text.Json.JsonSerializer.Serialize(entry);
                    File.AppendAllText(filePath, json + Environment.NewLine);
                }
                catch { }
                _logger.LogInformation("[{BizCode}] 模拟执行 {ExeType} | TagId={TagId} | SQL: {Sql}",
                    exe.BizCode, exe.ExeType, exe.TagId, renderedScript);
                return;
            }

            _logger.LogInformation("[{BizCode}] 执行 {ExeType} | TagId={TagId} | SQL: {Sql}",
                exe.BizCode, exe.ExeType, exe.TagId, renderedScript);

            try
            {
                int affected = exe.ExeType?.ToUpperInvariant() switch
                {
                    "S" or "Q" => 0,
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
        /// 调用方传入驱动实例（DeviceRoot 子类，由 DriverFactory.Create 创建），
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
