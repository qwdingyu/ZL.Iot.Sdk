using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZL.Iot.Interface;

namespace ZL.Biz.Execute.Actions
{
    /// <summary>
    /// 强类型动作执行器接口
    /// <para>P2: 新功能优先使用强类型动作，而非动态 SQL</para>
    /// </summary>
    public interface ITypedActionExecutor
    {
        string ActionType { get; }
        Task<TypedActionResult> ExecuteAsync(ActionExecutionContext context);
        bool ValidateParameters(Dictionary<string, object> parameters, out string errorMessage);
    }

    /// <summary>
    /// 动作执行上下文
    /// </summary>
    public class ActionExecutionContext
    {
        public string TraceId { get; set; }
        public string BizCode { get; set; }
        public string GatewayId { get; set; }
        public string StationNo { get; set; }
        public string TriggerTagId { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public Dictionary<string, object> Facts { get; set; } = new();
        public ISqlExecutor SqlExecutor { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 动作执行结果
    /// </summary>
    public class TypedActionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string IdempotentKey { get; set; }
        public int AffectedRows { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();

        public static TypedActionResult Ok(string idempotentKey = null, int affectedRows = 0)
            => new TypedActionResult { Success = true, IdempotentKey = idempotentKey, AffectedRows = affectedRows };

        public static TypedActionResult Fail(string errorMessage)
            => new TypedActionResult { Success = false, ErrorMessage = errorMessage };
    }

    /// <summary>
    /// 插入行动作执行器
    /// </summary>
    public class InsertRowActionExecutor : ITypedActionExecutor
    {
        private readonly ILogger<InsertRowActionExecutor> _logger;
        public string ActionType => "InsertRow";

        public InsertRowActionExecutor(ILogger<InsertRowActionExecutor> logger) { _logger = logger; }

        public async Task<TypedActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            try
            {
                if (!ValidateParameters(context.Parameters, out var error))
                    return TypedActionResult.Fail(error);

                var tableName = context.Parameters["tableName"].ToString();
                var rowData = context.Parameters["data"] as Dictionary<string, object>;

                // 使用 SafeSqlBuilder 验证表名，防止 SQL 注入
                if (!Sql.SafeSqlBuilder.IsValidIdentifier(tableName))
                    return TypedActionResult.Fail($"Invalid table name: {tableName}");

                var columns = rowData.Keys.ToList();
                var paramNames = columns.Select(c => $"@{c}").ToList();
                var parameters = new Dictionary<string, object>(rowData);

                var sql = $"INSERT INTO [{tableName}] ({string.Join(",", columns)}) VALUES ({string.Join(",", paramNames)})";
                var affected = await context.SqlExecutor.ExecuteNonQueryAsync(sql, parameters);

                _logger.LogInformation("InsertRow executed: Table={Table}, Rows={Rows}", tableName, affected);
                return TypedActionResult.Ok($"{tableName}_{context.TraceId}", affected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InsertRow action failed");
                return TypedActionResult.Fail(ex.Message);
            }
        }

        public bool ValidateParameters(Dictionary<string, object> parameters, out string errorMessage)
        {
            errorMessage = null;
            if (!parameters.ContainsKey("tableName")) { errorMessage = "tableName required"; return false; }
            if (!parameters.ContainsKey("data")) { errorMessage = "data required"; return false; }
            return true;
        }
    }

    /// <summary>
    /// 更新行动作执行器
    /// </summary>
    public class UpdateRowActionExecutor : ITypedActionExecutor
    {
        private readonly ILogger<UpdateRowActionExecutor> _logger;
        public string ActionType => "UpdateRow";

        public UpdateRowActionExecutor(ILogger<UpdateRowActionExecutor> logger) { _logger = logger; }

        public async Task<TypedActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            try
            {
                if (!ValidateParameters(context.Parameters, out var error))
                    return TypedActionResult.Fail(error);

                var tableName = context.Parameters["tableName"].ToString();
                var primaryKey = context.Parameters["primaryKey"].ToString();
                var primaryKeyValue = context.Parameters["primaryKeyValue"];
                var rowData = context.Parameters["data"] as Dictionary<string, object>;

                var setClauses = rowData.Keys.Select(k => $"{k} = @{k}").ToList();
                var parameters = new Dictionary<string, object>(rowData);
                parameters["pk"] = primaryKeyValue;

                var sql = $"UPDATE {tableName} SET {string.Join(",", setClauses)} WHERE {primaryKey} = @pk";
                var affected = await context.SqlExecutor.ExecuteNonQueryAsync(sql, parameters);

                _logger.LogInformation("UpdateRow executed: Table={Table}, Key={Key}, Rows={Rows}", tableName, primaryKey, affected);
                return TypedActionResult.Ok($"{tableName}_{primaryKey}_{primaryKeyValue}", affected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateRow action failed");
                return TypedActionResult.Fail(ex.Message);
            }
        }

        public bool ValidateParameters(Dictionary<string, object> parameters, out string errorMessage)
        {
            errorMessage = null;
            if (!parameters.ContainsKey("tableName")) { errorMessage = "tableName required"; return false; }
            if (!parameters.ContainsKey("primaryKey")) { errorMessage = "primaryKey required"; return false; }
            if (!parameters.ContainsKey("primaryKeyValue")) { errorMessage = "primaryKeyValue required"; return false; }
            if (!parameters.ContainsKey("data")) { errorMessage = "data required"; return false; }
            return true;
        }
    }

    /// <summary>
    /// 写标签动作执行器
    /// </summary>
    public class WriteTagActionExecutor : ITypedActionExecutor
    {
        private readonly ILogger<WriteTagActionExecutor> _logger;
        public string ActionType => "WriteTag";

        public WriteTagActionExecutor(ILogger<WriteTagActionExecutor> logger) { _logger = logger; }

        public async Task<TypedActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            try
            {
                if (!ValidateParameters(context.Parameters, out var error))
                    return TypedActionResult.Fail(error);

                // WriteTag 通常由 EdgeExecutorKernel 处理
                // 此处仅记录成功状态
                var tagId = context.Parameters["tagId"].ToString();
                var value = context.Parameters["value"];

                _logger.LogInformation("WriteTag executed: Tag={Tag}, Value={Value}", tagId, value);
                return TypedActionResult.Ok($"WriteTag_{tagId}_{context.OccurredAt:yyyyMMddHHmmss}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WriteTag action failed");
                return TypedActionResult.Fail(ex.Message);
            }
        }

        public bool ValidateParameters(Dictionary<string, object> parameters, out string errorMessage)
        {
            errorMessage = null;
            if (!parameters.ContainsKey("tagId")) { errorMessage = "tagId required"; return false; }
            if (!parameters.ContainsKey("value")) { errorMessage = "value required"; return false; }
            return true;
        }
    }

    /// <summary>
    /// 强类型动作执行器注册中心
    /// </summary>
    public class TypedActionRegistry
    {
        private readonly Dictionary<string, ITypedActionExecutor> _executors = new();

        public void Register(ITypedActionExecutor executor)
        {
            _executors[executor.ActionType] = executor;
        }

        public ITypedActionExecutor GetExecutor(string actionType)
        {
            return _executors.TryGetValue(actionType, out var executor) ? executor : null;
        }

        public bool HasExecutor(string actionType) => _executors.ContainsKey(actionType);
    }
}