using System.Threading;
using System.Threading.Tasks;
using ZL.Model;

namespace ZL.Dao.IotDevice.Interfaces
{
    /// <summary>
    /// IoT 业务执行日志服务接口 - 提供执行审计日志的写入操作
    /// </summary>
    public interface IIotBizExecLogService
    {
        #region 同步方法

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
        /// <param name="context">业务执行上下文（可选）</param>
        void Insert(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string errMsg, string result, long durationMs,
            string deviceId = null, string companyId = null, BizExecutionContext context = null);

        /// <summary>
        /// 记录成功执行
        /// </summary>
        void LogSuccess(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, long durationMs, string deviceId = null, string companyId = null, BizExecutionContext context = null);

        /// <summary>
        /// 记录失败执行
        /// </summary>
        void LogFail(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string errMsg, long durationMs, string deviceId = null, string companyId = null, BizExecutionContext context = null);

        /// <summary>
        /// 记录跳过执行（判断条件不满足）
        /// </summary>
        void LogSkip(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string reason, string deviceId = null, string companyId = null, BizExecutionContext context = null);

        #endregion

        #region 异步方法

        /// <summary>
        /// 异步写入一条执行审计记录
        /// </summary>
        Task InsertAsync(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string errMsg, string result, long durationMs,
            string deviceId = null, string companyId = null, BizExecutionContext context = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步记录成功执行
        /// </summary>
        Task LogSuccessAsync(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, long durationMs, string deviceId = null, string companyId = null,
            BizExecutionContext context = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步记录失败执行
        /// </summary>
        Task LogFailAsync(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string errMsg, long durationMs, string deviceId = null, string companyId = null,
            BizExecutionContext context = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步记录跳过执行（判断条件不满足）
        /// </summary>
        Task LogSkipAsync(string tagId, string bizCode, string exeOrder, string exeType,
            string scriptSnapshot, string reason, string deviceId = null, string companyId = null,
            BizExecutionContext context = null, CancellationToken cancellationToken = default);

        #endregion
    }
}
