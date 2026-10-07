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
            // ⚠️ 兼容构造必须把传入 pipeline 上已注册的输出与路由迁移到 manager（2026-10-06 修复）：
            // 此处曾长期是空实现（只剩一行「仅保留兼容构造」注释）——传入 pipeline 成了孤岛：
            // 输出永远不会被启动（连连接循环都不跑），输入消息进入 manager 内部 pipeline 后
            // 无路由可匹配而被静默丢弃。所有经由本构造 + pipeline.RegisterOutput/AddRouter 的
            // 调用方（含 6 个场景测试与 NuGet 包消费者）的转发全部静默失效。
            // 迁移语义：输出走 GatewayManager.RegisterOutput（GatewayOutputManager 会同步注册进
            // manager 内部 pipeline，随 StartAsync 启动）；路由规则直接加到 manager 的 pipeline。
            if (pipeline is ResilientMessagePipeline legacy)
            {
                foreach (var output in legacy.RegisteredOutputs)
                {
                    _manager.RegisterOutput(output.Name, output);
                }

                foreach (var rule in legacy.RegisteredRouterRules)
                {
                    _manager.Pipeline.AddRouter(rule);
                }
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
