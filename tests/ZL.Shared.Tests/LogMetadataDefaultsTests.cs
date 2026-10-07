namespace ZL.Shared.Tests;

/// <summary>
/// 日志元数据默认值契约：Direction 的默认 "SYS" 被 StructuredLog 的输出模板消费，
/// 改默认值会静默改变所有日志行的 [Direction] 字段，故锁定。
/// </summary>
public class LogMetadataDefaultsTests
{
    [Fact]
    public void LogEventMetadata_默认Direction为SYS其余为空()
    {
        var m = new LogEventMetadata();
        Assert.Equal("SYS", m.Direction);
        Assert.Equal(string.Empty, m.Instance);
        Assert.Equal(string.Empty, m.Payload);
        Assert.Equal(string.Empty, m.SessionId);
    }

    [Fact]
    public void LogBootstrapOptions_默认值()
    {
        var o = new LogBootstrapOptions();
        Assert.False(o.EnableConsole);
        Assert.Null(o.BaseDirectory);
        Assert.Null(o.AppNameOverride);
        Assert.NotNull(LogBootstrapOptions.Default);
    }
}
