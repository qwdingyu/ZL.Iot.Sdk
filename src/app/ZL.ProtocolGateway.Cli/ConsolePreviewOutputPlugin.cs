namespace ZL.ProtocolGateway.Cli;

public sealed class ConsolePreviewOutputPlugin : IOutputPlugin
{
    public ConsolePreviewOutputPlugin(string? name = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "ConsolePreview" : name;
    }

    public string Name { get; }
    public string ProtocolType => "Console";
    public string Version => "1.0.0";
    public PluginStatus Status { get; private set; } = PluginStatus.Stopped;
    public event Action<string, bool>? ConnectionChanged;
    public event Action<OutputPluginStatusArgs>? DetailedStatusChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Status = PluginStatus.Running;
        ConnectionChanged?.Invoke(Name, true);
        DetailedStatusChanged?.Invoke(new OutputPluginStatusArgs
        {
            PluginName = Name,
            Status = Status,
            Message = "Console preview output started.",
            HealthLevel = OutputPluginHealthLevel.Healthy,
            Timestamp = DateTime.Now
        });
        return Task.CompletedTask;
    }

    public Task SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        Console.WriteLine();
        Console.WriteLine("--- 目标输出预览 ---");
        Console.WriteLine($"MessageId   : {message.Id}");
        Console.WriteLine($"Topic       : {message.Topic}");
        Console.WriteLine($"ContentType : {message.ContentType}");
        Console.WriteLine($"Timestamp   : {message.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("Metadata    :");
        foreach (var item in message.Metadata.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  - {item.Key} = {item.Value}");
        }

        Console.WriteLine("Payload     :");
        Console.WriteLine(RenderPayload(message));
        Console.WriteLine("--------------------");
        Console.WriteLine();

        return Task.CompletedTask;
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
        // P0-3 修复：同步 Dispose 不等待 StopAsync 完成，避免死锁。
        // ConsolePreview 无需释放资源。
    }

    public ValueTask DisposeAsync()
    {
        // ConsolePreview 无需释放资源
        return ValueTask.CompletedTask;
    }

    private static string RenderPayload(Message message)
    {
        return message.ContentType switch
        {
            "json" => message.GetJsonContent() ?? string.Empty,
            "hex" => message.GetHexContent() ?? string.Empty,
            _ => message.GetTextContent() ?? string.Empty
        };
    }
}
