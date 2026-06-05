using System;

namespace ZL.Watchdog
{
    /// <summary>
    /// 单个被监控组件的状态快照。
    /// </summary>
    public class EntryStatus
    {
        /// <summary>组件名称</summary>
        public string Name { get; set; }

        /// <summary>当前是否健康</summary>
        public bool Healthy { get; set; }

        /// <summary>窗口内重启次数</summary>
        public int RestartCount { get; set; }

        /// <summary>是否已达到重启上限</summary>
        public bool RestartLimitReached { get; set; }

        /// <summary>上次重启时间（null 表示从未重启）</summary>
        public DateTime? LastRestartAt { get; set; }
    }

    /// <summary>
    /// Watchdog 整体状态快照。
    /// </summary>
    public class WatchdogStatus
    {
        /// <summary>是否正在运行</summary>
        public bool Running { get; set; }

        /// <summary>总检查次数</summary>
        public long TotalChecks { get; set; }

        /// <summary>总重启次数</summary>
        public long TotalRestarts { get; set; }

        /// <summary>总告警次数（健康检查失败但未重启）</summary>
        public long TotalAlerts { get; set; }

        /// <summary>各组件状态</summary>
        public EntryStatus[] Entries { get; set; } = Array.Empty<EntryStatus>();
    }
}
