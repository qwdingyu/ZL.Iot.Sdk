using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZL.Iot.Interface
{
    /// <summary>
    /// 业务存储提供者抽象，解耦业务引擎与具体数据库实现
    /// </summary>
    public interface IBizStorageProvider
    {
        string EngineType { get; } // e.g., "Sqlite", "MySQL"
        
        /// <summary>
        /// 获取业务配置镜像
        /// </summary>
        Task<(List<TExe> exes, List<TVal> vals)> GetBizConfigAsync<TExe, TVal>(string tagId) 
            where TExe : class, new() 
            where TVal : class, new();

        /// <summary>
        /// 记录离线流水（仅边缘侧需要）
        /// </summary>
        Task RecordOfflineCommandAsync(string bizCode, string cmdType, string payload, string traceId);
        
        /// <summary>
        /// 基础执行接口
        /// </summary>
        ISqlExecutor SqlExecutor { get; }
    }
}
