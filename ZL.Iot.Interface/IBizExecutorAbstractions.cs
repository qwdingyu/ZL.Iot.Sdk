using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZL.Iot.Interface
{
    /// <summary>
    /// SQL 执行抽象接口 (Edge compatible)
    /// </summary>
    public interface ISqlExecutor
    {
        string Dialect { get; }
        Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql, Dictionary<string, object> parameters = null);
        Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object> parameters = null);
        
        /// <summary>
        /// 批量执行非查询 SQL（高性能接口）
        /// </summary>
        Task<int> ExecuteBatchNonQueryAsync(string sql, IEnumerable<Dictionary<string, object>> parameterList);
        
        Task<object> ExecuteScalarAsync(string sql, Dictionary<string, object> parameters = null);
        bool Validate(string sql, out string errorMessage);
        
        void BeginTransaction();
        void CommitTransaction();
        void RollbackTransaction();
    }

    /// <summary>
    /// 脚本引擎抽象接口 (Edge compatible)
    /// </summary>
    public interface IScriptEngine
    {
        string Render(string template, Dictionary<string, object> variables);
        IEnumerable<string> GetVariables(string template);
        bool Validate(string template, out string errorMessage);
    }

    /// <summary>
    /// 规则引擎抽象接口 (Edge compatible)
    /// </summary>
    public interface IRuleEngine
    {
        Task<RuleEvaluationResult> EvaluateAsync(string ruleJson, Dictionary<string, object> facts);
        bool Validate(string ruleJson, out string errorMessage);
    }

    public class RuleEvaluationResult
    {
        public bool IsMatch { get; set; }
        public string MatchedRuleName { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// 业务执行器抽象接口 (Edge compatible)
    /// </summary>
    public interface IBizCfgExecutor
    {
        Task<bool> ExeUpdateAsync(string tagId, Dictionary<string, object> facts, object context = null);
        Task<List<Dictionary<string, object>>> ExeSelectAsync(string tagId, string bizCode, Dictionary<string, object> facts);
    }

    /// <summary>
    /// 配置镜像同步服务 (Cloud -> Edge)
    /// </summary>
    public interface IMirrorSyncService
    {
        /// <summary>
        /// 应用从云端推送过来的配置快照
        /// </summary>
        Task<bool> ApplySnapshotAsync(string gatewayId, string version, string jsonPayload);
        
        /// <summary>
        /// 获取本地配置版本
        /// </summary>
        Task<string> GetLocalVersionAsync(string gatewayId);
    }
}
