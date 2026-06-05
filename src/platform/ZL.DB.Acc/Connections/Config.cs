using SqlSugar;

namespace ZL.DB.Acc
{
    /// <summary>
    /// ZL.DB.Acc 内部常量配置
    ///
    /// 历史遗留说明：
    ///   原先此类包含 GetInstance() 单例 + ConnInfo 连接信息，
    ///   已随 P0 重构移除。连接创建现统一由 <see cref="SugarAcc.GetScope"/> 负责。
    ///   此类保留仅作为内部常量持有者，外部项目无需引用。
    /// </summary>
    internal static class Config
    {
        /// <summary>
        /// 默认日志文件名（传入 LogKit.WriteLogs 的 logFile 参数）
        /// </summary>
        public static string LogFile { get; set; } = "DB_ACC";
    }
}
