using Xunit;

namespace ZL.ProtocolGateway.Tests.Cli;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GatewayCliTestCollection
{
    public const string Name = "ProtocolGateway.Cli.Serial";
}
