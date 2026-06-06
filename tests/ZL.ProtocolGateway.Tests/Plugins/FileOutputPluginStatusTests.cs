#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Plugins;

public class FileOutputPluginStatusTests
{
    [Fact]
    public async Task StartAndStop_ShouldRaiseDetailedStatusChangedWithStandardFields()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"protocol-gateway-file-output-{Guid.NewGuid():N}.log");
        var plugin = new FileOutputPlugin(new FileOutputConfig
        {
            Name = "FileStatusTest",
            FilePath = tempFile,
            EncodingName = "UTF-8"
        });

        var events = new List<OutputPluginStatusArgs>();
        plugin.DetailedStatusChanged += args => events.Add(args);

        try
        {
            await plugin.StartAsync();
            await plugin.StopAsync();
        }
        finally
        {
            plugin.Dispose();
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        Assert.Contains(events, args =>
            args.PluginName == "FileStatusTest"
            && args.Status == PluginStatus.Starting
            && args.HealthLevel == OutputPluginHealthLevel.Healthy
            && args.Message.Contains("ready", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(events, args =>
            args.PluginName == "FileStatusTest"
            && args.Status == PluginStatus.Stopped
            && args.HealthLevel == OutputPluginHealthLevel.Healthy
            && args.Message.Contains("stopped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvalidEncoding_ShouldRaiseFatalDetailedStatusChanged()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"protocol-gateway-file-output-{Guid.NewGuid():N}.log");
        var plugin = new FileOutputPlugin(new FileOutputConfig
        {
            Name = "BrokenFileOutput",
            FilePath = tempFile,
            EncodingName = "__invalid_encoding__"
        });

        OutputPluginStatusArgs? capturedFatal = null;
        plugin.DetailedStatusChanged += args =>
        {
            if (args.HealthLevel == OutputPluginHealthLevel.Fatal)
            {
                capturedFatal = args;
            }
        };

        try
        {
            await plugin.StartAsync();
        }
        finally
        {
            plugin.Dispose();
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        Assert.NotNull(capturedFatal);
        Assert.Equal("BrokenFileOutput", capturedFatal!.PluginName);
        Assert.Equal(PluginStatus.Fatal, capturedFatal.Status);
        Assert.Equal(OutputPluginHealthLevel.Fatal, capturedFatal.HealthLevel);
        Assert.NotNull(capturedFatal.LastException);
        Assert.Contains("Start failed", capturedFatal.Message, StringComparison.OrdinalIgnoreCase);
    }
}
