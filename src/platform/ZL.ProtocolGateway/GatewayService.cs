using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 网关服务核心 (Gateway Host)
    /// <para>已简化为 GatewayManager 的薄封装，保留向后兼容。</para>
    /// <para>新代码应直接使用 <see cref="GatewayManager"/>。</para>
    /// </summary>
    [Obsolete("GatewayService is obsolete. Use GatewayManager directly. Will be removed in v2.0.", error: false)]
    public class GatewayService
    {
        private readonly GatewayManager _manager;

        /// <summary>
        /// 使用默认配置创建网关服务
        /// </summary>
        public GatewayService()
            : this(new GatewayManager())
        {
        }

        /// <summary>
        /// 使用指定 Pipeline 创建网关服务（向后兼容构造函数）
        /// </summary>
        public GatewayService(IPipeline pipeline)
            : this(new GatewayManager(new GatewayManagerOptions
            {
                QueueCapacity = pipeline is ResilientMessagePipeline rmp ? rmp.QueueCapacity : 10000,
                SendTimeoutMs = pipeline is ResilientMessagePipeline rmp2 ? rmp2.SendTimeoutMs : 30000
            }))
        {
            // 将传入的 pipeline 的输出插件迁移到 manager
            if (pipeline is ResilientMessagePipeline rmp3)
            {
                // 内部 pipeline 已被 GatewayManager 替换，此处仅保留兼容构造
            }
        }

        /// <summary>
        /// 使用 GatewayManager 创建网关服务
        /// </summary>
        public GatewayService(GatewayManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>
        /// 获取内部的 GatewayManager 实例
        /// </summary>
        public GatewayManager Manager => _manager;

        /// <summary>
        /// 设置全局消息速率限制（可选）。
        /// <para>防止 Input 侧洪水攻击导致 Pipeline 过载。</para>
        /// <para>默认不限流，调用此方法后生效。</para>
        /// </summary>
        /// <param name="tokensPerSecond">每秒允许的最大消息数，0 表示取消限流</param>
        public void SetRateLimit(double tokensPerSecond)
        {
            _manager.SetRateLimit(tokensPerSecond);
        }

        /// <summary>
        /// 注册输入插件
        /// </summary>
        public void AddInput(IInputPlugin input)
        {
            _manager.AddInput(input);
        }

        /// <summary>
        /// 启动网关服务
        /// </summary>
        public Task StartAsync(CancellationToken ct = default)
        {
            return _manager.StartAsync(ct);
        }

        /// <summary>
        /// 停止网关服务
        /// </summary>
        public Task StopAsync()
        {
            return _manager.StopAsync();
        }

        /// <summary>
        /// 向 Pipeline 直接发布消息（跳过 IInputPlugin，供进程内桥接调用）。
        /// 与 AddInput/StartAsync 注册的外部监听器不同，此方法允许 PlcMemory 等
        /// 进程内事件源直接将消息注入处理流水线。
        /// <para>仅在 GatewayService 已启动时生效。</para>
        /// <para>受 SetRateLimit 速率限制约束。</para>
        /// </summary>
        public Task PublishAsync(Message message, CancellationToken ct = default)
        {
            return _manager.PublishMessageAsync(message, ct);
        }
    }
}
