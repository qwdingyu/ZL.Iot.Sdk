using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Plugins;

namespace ZL.ProtocolGateway.Cli;

public sealed class SyntheticInputPlugin : IInputPlugin
{
    private readonly Message _message;

    public SyntheticInputPlugin(Message message)
    {
        _message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public string Name => "SyntheticInput";
    public string ProtocolType => _message.Metadata.TryGetValue("Protocol", out var protocol) ? protocol : "CLI";
    public string Version => "1.0.0";
    public PluginStatus Status { get; private set; } = PluginStatus.Stopped;
    public event Action<string, bool>? ConnectionChanged;
    public event Action<InputPluginStatusArgs>? DetailedStatusChanged;

    public async Task StartAsync(Func<Message, Task> messageHandler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageHandler);

        Status = PluginStatus.Starting;
        ConnectionChanged?.Invoke(Name, true);
        Status = PluginStatus.Running;

        cancellationToken.ThrowIfCancellationRequested();
        await messageHandler(_message.Clone());
    }

    public Task StopAsync()
    {
        if (Status == PluginStatus.Stopped)
        {
            return Task.CompletedTask;
        }

        Status = PluginStatus.Stopped;
        ConnectionChanged?.Invoke(Name, false);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }
}
