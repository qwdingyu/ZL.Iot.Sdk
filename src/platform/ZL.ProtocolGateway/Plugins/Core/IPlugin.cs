using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 输入插件接口 - 定义数据输入的标准行为
    /// 实现类：SerialInputPlugin, MqttInputPlugin, TcpInputPlugin, UdpInputPlugin
    /// </summary>
    public interface IInputPlugin : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 插件名称（唯一标识）
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 协议类型
        /// </summary>
        string ProtocolType { get; }

        /// <summary>
        /// 插件版本号（语义化版本，如 "1.0.0"）。默认 "1.0.0"。
        /// 用于未来热插拔场景中的版本兼容性校验。
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 启动插件，接收数据
        /// </summary>
        /// <param name="messageHandler">消息处理回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task StartAsync(Func<Message, Task> messageHandler, CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止插件
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 插件状态
        /// </summary>
        PluginStatus Status { get; }

        /// <summary>
        /// 连接状态改变事件
        /// </summary>
        event Action<string, bool> ConnectionChanged;

        /// <summary>
        /// 详细状态变更事件（推荐实现，提供更丰富的状态信息）
        /// </summary>
        event Action<InputPluginStatusArgs>? DetailedStatusChanged;
    }

    /// <summary>
    /// 输入插件状态变更事件参数
    /// </summary>
    public class InputPluginStatusArgs : EventArgs
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string PluginName { get; set; } = string.Empty;

        /// <summary>
        /// 插件状态
        /// </summary>
        public PluginStatus Status { get; set; }

        /// <summary>
        /// 原始技术消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 统一错误码
        /// </summary>
        public string ErrorCode { get; set; } = GatewayErrorCodes.None;

        /// <summary>
        /// 用户可读描述
        /// </summary>
        public string UserMessage { get; set; } = string.Empty;

        /// <summary>
        /// 建议处置动作
        /// </summary>
        public string Advice { get; set; } = string.Empty;

        /// <summary>
        /// 健康级别
        /// </summary>
        public InputPluginHealthLevel HealthLevel { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 最后一次异常（如果有）
        /// </summary>
        public Exception? LastException { get; set; }
    }

    /// <summary>
    /// 输入插件健康级别枚举
    /// </summary>
    public enum InputPluginHealthLevel
    {
        /// <summary>
        /// 健康 - 连接正常
        /// </summary>
        Healthy,

        /// <summary>
        /// 降级 - 性能下降但可恢复
        /// </summary>
        Degraded,

        /// <summary>
        /// 警告 - 连接断开但正在重连
        /// </summary>
        Warning,

        /// <summary>
        /// 错误 - 超过重试阈值
        /// </summary>
        Error,

        /// <summary>
        /// 致命 - 配置错误等不可恢复
        /// </summary>
        Fatal
    }

    /// <summary>
    /// 输出插件健康级别枚举
    /// </summary>
    public enum OutputPluginHealthLevel
    {
        /// <summary>
        /// 健康 - 连接正常
        /// </summary>
        Healthy,

        /// <summary>
        /// 降级 - 性能下降但可恢复
        /// </summary>
        Degraded,

        /// <summary>
        /// 警告 - 连接断开但正在重连
        /// </summary>
        Warning,

        /// <summary>
        /// 错误 - 超过重试阈值
        /// </summary>
        Error,

        /// <summary>
        /// 致命 - 配置错误等不可恢复
        /// </summary>
        Fatal
    }

    /// <summary>
    /// 输出插件状态变更事件参数
    /// </summary>
    public class OutputPluginStatusArgs : EventArgs
    {
        /// <summary>
        /// 插件名称
        /// </summary>
        public string PluginName { get; set; } = string.Empty;

        /// <summary>
        /// 插件状态
        /// </summary>
        public PluginStatus Status { get; set; }

        /// <summary>
        /// 原始技术消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 统一错误码
        /// </summary>
        public string ErrorCode { get; set; } = GatewayErrorCodes.None;

        /// <summary>
        /// 用户可读描述
        /// </summary>
        public string UserMessage { get; set; } = string.Empty;

        /// <summary>
        /// 建议处置动作
        /// </summary>
        public string Advice { get; set; } = string.Empty;

        /// <summary>
        /// 链路追踪 ID
        /// </summary>
        public string TraceId { get; set; } = string.Empty;

        /// <summary>
        /// 健康级别
        /// </summary>
        public OutputPluginHealthLevel HealthLevel { get; set; }

        /// <summary>
        /// 连续失败次数
        /// </summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 最后一次异常（如果有）
        /// </summary>
        public Exception? LastException { get; set; }
    }

    /// <summary>
    /// 输出插件接口 - 定义数据输出的标准行为
    /// 实现类：HttpOutputPlugin, MqttOutputPlugin, TcpOutputPlugin, UdpOutputPlugin, SerialOutputPlugin
    /// </summary>
    public interface IOutputPlugin : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 插件名称（唯一标识）
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 协议类型
        /// </summary>
        string ProtocolType { get; }

        /// <summary>
        /// 插件版本号（语义化版本，如 "1.0.0"）。默认 "1.0.0"。
        /// 用于未来热插拔场景中的版本兼容性校验。
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 启动插件
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送消息
        /// </summary>
        Task SendAsync(Message message, CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止插件
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 插件状态
        /// </summary>
        PluginStatus Status { get; }

        /// <summary>
        /// 连接状态改变事件（基础事件，仅传递连接/断开）
        /// </summary>
        event Action<string, bool>? ConnectionChanged;

        /// <summary>
        /// 详细状态变更事件（推荐实现，提供更丰富的状态信息）
        /// </summary>
        event Action<OutputPluginStatusArgs>? DetailedStatusChanged;
    }

    /// <summary>
    /// 插件状态枚举
    /// </summary>
    public enum PluginStatus
    {
        /// <summary>
        /// 未启动
        /// </summary>
        Stopped,

        /// <summary>
        /// 启动中
        /// </summary>
        Starting,

        /// <summary>
        /// 运行中
        /// </summary>
        Running,

        /// <summary>
        /// 运行中但有性能下降（如延迟增加、部分功能降级）
        /// </summary>
        Degraded,

        /// <summary>
        /// 连接断开但正在自动重连
        /// </summary>
        Recovering,

        /// <summary>
        /// 停止中
        /// </summary>
        Stopping,

        /// <summary>
        /// 错误状态（可恢复）
        /// </summary>
        Error,

        /// <summary>
        /// 致命错误（不可恢复，如配置错误）
        /// </summary>
        Fatal
    }
}
