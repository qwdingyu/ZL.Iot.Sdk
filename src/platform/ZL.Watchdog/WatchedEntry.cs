using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Watchdog
{
    /// <summary>
    /// 健康检查委托。返回 true 表示组件健康，false 表示不健康。
    /// </summary>
    public delegate bool HealthCheckFunc();

    /// <summary>
    /// 重启操作委托。接收组件名称，执行重启逻辑。
    /// 返回 true 表示重启成功，false 表示重启失败。
    /// </summary>
    public delegate bool RestartAction(string name);

    /// <summary>
    /// 被监控的组件条目。
    /// </summary>
    public class WatchedEntry
    {
        /// <summary>组件唯一名称</summary>
        public string Name { get; set; }

        /// <summary>健康检查函数</summary>
        public HealthCheckFunc HealthCheck { get; set; }

        /// <summary>重启操作（可选，为 null 时仅报警不重启）</summary>
        public RestartAction Restart { get; set; }

        /// <summary>是否启用自动重启（默认 true）</summary>
        public bool AutoRestart { get; set; } = true;

        /// <summary>
        /// 创建被监控条目。
        /// </summary>
        /// <param name="name">组件唯一名称</param>
        /// <param name="healthCheck">健康检查函数</param>
        /// <param name="restart">重启操作（可选）</param>
        public WatchedEntry(string name, HealthCheckFunc healthCheck, RestartAction restart = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            HealthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
            Restart = restart;
            AutoRestart = restart != null;
        }
    }
}
