using Xunit;

namespace ZL.ProtocolGateway.Tests.Scenarios;

/// <summary>
/// 共用串行集合：所有涉及 GatewayLog 静态状态（_customWriter）的测试类必须加入此集合。
/// GatewayLog 是静态类，SetOutput/ResetOutput 在多集合并行时会导致日志互吞。
/// 当前成员：TcpForwardingScenarioTests, GatewayLogTests
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TcpScenarioTestCollection
{
    public const string Name = "ProtocolGateway.TcpScenario";
}
