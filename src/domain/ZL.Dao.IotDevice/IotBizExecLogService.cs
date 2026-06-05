using SqlSugar;
using ZL.DB.Acc;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using ZL.Dao.IotDevice.Interfaces;
using ZL.Model;

namespace ZL.Dao.IotDevice
{
    /// <summary>
    /// 业务配置执行审计日志服务
    /// 为 BizCfgExe 提供结构化的执行记录写入能力
    /// </summary>
    public class IotBizExecLogService : Repository<iot_biz_exec_log>, IIotBizExecLogService
    {
        /// <summary>
        /// 写入一条执行审计记录
        /// </summary>
        /// <param name="tagId">触发执行的标签ID</param>
        /// <param name="bizCode">业务模式代码</param>
        /// <param name="exeOrder">执行顺序号</param>
        /// <param name="exeType">执行类型（S/U/B）</param>
        /// <param name="scriptSnapshot">脱敏后的脚本快照</param>
        /// <param name="errMsg">错误信息（无错时传 null 或空）</param>
        /// <param name="result">执行结果：OK / FAIL / SKIP</param>
        /// <param name="durationMs">执行耗时（毫秒）</param>
        /// <param name="deviceId">设备ID（可选）</param>
        /// <param name="companyId">公司ID（可选）</param>
        public void Insert(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string errMsg, string result, long durationMs,
            string deviceId = null, string companyId = null, BizExecutionContext context = null)
        {
            context = context ?? new BizExecutionContext();
            var log = new iot_biz_exec_log
            {
                tag_id = string.IsNullOrWhiteSpace(context.TagId) ? tagId : context.TagId,
                biz_code = bizCode,
                exe_order = exeOrder,
                exe_type = exeType,
                script_snapshot = TruncateSnapshot(scriptSnapshot),
                err_msg = TruncateSnapshot(errMsg),
                result = result,
                duration_ms = durationMs,
                // create_time 由 SqlSugar DataExecuting AOP 自动赋值（Insert 时触发）
                device_id = string.IsNullOrWhiteSpace(context.DeviceId) ? deviceId : context.DeviceId,
                trace_id = context.TraceId,
                trigger_source = context.TriggerSource,
                source_event_id = context.SourceEventId,
                template_version = context.TemplateVersion,
                snapshot_version = context.SnapshotVersion,
                operator_id = context.OperatorId,
                operator_name = context.OperatorName,
                input_snapshot = TruncateSnapshot(context.Inputs == null || context.Inputs.Count == 0
                    ? null
                    : JsonConvert.SerializeObject(context.Inputs)),
                company_id = companyId
            };
            Insert(log);
        }

        /// <summary>
        /// 记录成功执行
        /// </summary>
        public void LogSuccess(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, long durationMs, string deviceId = null, string companyId = null, BizExecutionContext context = null)
        {
            Insert(tagId, bizCode, exeOrder, exeType, scriptSnapshot, null, "OK", durationMs, deviceId, companyId, context);
        }

        /// <summary>
        /// 记录失败执行
        /// </summary>
        public void LogFail(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string errMsg, long durationMs, string deviceId = null, string companyId = null, BizExecutionContext context = null)
        {
            Insert(tagId, bizCode, exeOrder, exeType, scriptSnapshot, errMsg, "FAIL", durationMs, deviceId, companyId, context);
        }

        /// <summary>
        /// 记录跳过执行（判断条件不满足）
        /// </summary>
        public void LogSkip(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string reason, string deviceId = null, string companyId = null, BizExecutionContext context = null)
        {
            Insert(tagId, bizCode, exeOrder, exeType, scriptSnapshot, reason, "SKIP", 0, deviceId, companyId, context);
        }

        private static string TruncateSnapshot(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return text.Length > 2000 ? text.Substring(0, 2000) : text;
        }

        #region 异步方法

        /// <summary>
        /// 异步写入一条执行审计记录
        /// </summary>
        public async Task InsertAsync(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string errMsg, string result, long durationMs,
            string deviceId = null, string companyId = null, BizExecutionContext context = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Run(() => Insert(tagId, bizCode, exeOrder, exeType, scriptSnapshot, errMsg, result, durationMs, deviceId, companyId, context), cancellationToken);
        }

        /// <summary>
        /// 异步记录成功执行
        /// </summary>
        public async Task LogSuccessAsync(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, long durationMs, string deviceId = null, string companyId = null,
            BizExecutionContext context = null, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => LogSuccess(tagId, bizCode, exeOrder, exeType, scriptSnapshot, durationMs, deviceId, companyId, context), cancellationToken);
        }

        /// <summary>
        /// 异步记录失败执行
        /// </summary>
        public async Task LogFailAsync(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string errMsg, long durationMs, string deviceId = null, string companyId = null,
            BizExecutionContext context = null, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => LogFail(tagId, bizCode, exeOrder, exeType, scriptSnapshot, errMsg, durationMs, deviceId, companyId, context), cancellationToken);
        }

        /// <summary>
        /// 异步记录跳过执行（判断条件不满足）
        /// </summary>
        public async Task LogSkipAsync(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string reason, string deviceId = null, string companyId = null,
            BizExecutionContext context = null, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => LogSkip(tagId, bizCode, exeOrder, exeType, scriptSnapshot, reason, deviceId, companyId, context), cancellationToken);
        }

        #endregion
    }
}
