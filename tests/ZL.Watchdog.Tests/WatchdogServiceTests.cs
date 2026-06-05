using ZL.Watchdog;

namespace ZL.Watchdog.Tests;

public class WatchdogOptionsTests
{
    [Fact]
    public void DefaultOptions_AreValid()
    {
        var options = new WatchdogOptions();
        Assert.Empty(options.Validate());
    }

    [Fact]
    public void InvalidInterval_ReturnsError()
    {
        var options = new WatchdogOptions { CheckIntervalSeconds = 0 };
        Assert.Contains(options.Validate(), e => e.Contains("CheckIntervalSeconds"));
    }

    [Fact]
    public void WindowSmallerThanInterval_ReturnsError()
    {
        var options = new WatchdogOptions { CheckIntervalSeconds = 10, WindowSeconds = 5 };
        Assert.Contains(options.Validate(), e => e.Contains("WindowSeconds"));
    }

    [Fact]
    public void InvalidMaxRestarts_ReturnsError()
    {
        var options = new WatchdogOptions { MaxRestartsPerWindow = 0 };
        Assert.Contains(options.Validate(), e => e.Contains("MaxRestartsPerWindow"));
    }
}

public class WatchedEntryTests
{
    [Fact]
    public void Constructor_NullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WatchedEntry(null!, () => true));
    }

    [Fact]
    public void Constructor_NullHealthCheck_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WatchedEntry("test", null!));
    }

    [Fact]
    public void Constructor_WithRestart_AutoRestartEnabled()
    {
        var entry = new WatchedEntry("test", () => true, _ => true);
        Assert.True(entry.AutoRestart);
    }

    [Fact]
    public void Constructor_WithoutRestart_AutoRestartDisabled()
    {
        var entry = new WatchedEntry("test", () => true);
        Assert.False(entry.AutoRestart);
    }
}

public class WatchdogServiceTests
{
    [Fact]
    public void Constructor_InvalidOptions_Throws()
    {
        var options = new WatchdogOptions { CheckIntervalSeconds = 0 };
        Assert.Throws<ArgumentException>(() => new WatchdogService(options));
    }

    [Fact]
    public void Register_And_Unregister()
    {
        using var wd = new WatchdogService(new WatchdogOptions());
        wd.Register(new WatchedEntry("svc-1", () => true));
        Assert.Single(wd.GetStatus().Entries);

        wd.Unregister("svc-1");
        Assert.Empty(wd.GetStatus().Entries);
    }

    [Fact]
    public void Start_And_Stop_Lifecycle()
    {
        using var wd = new WatchdogService(new WatchdogOptions());
        Assert.False(wd.GetStatus().Running);

        wd.Start();
        Assert.True(wd.GetStatus().Running);

        wd.Stop();
        Assert.False(wd.GetStatus().Running);
    }

    [Fact]
    public async Task DetectsUnhealthy_AndRestarts()
    {
        bool healthy = true;
        int restartCount = 0;

        var options = new WatchdogOptions
        {
            CheckIntervalSeconds = 1,
            MaxRestartsPerWindow = 3,
            WindowSeconds = 60
        };

        using var wd = new WatchdogService(options);
        wd.Register(new WatchedEntry(
            "test-service",
            () => healthy,
            _ => { Interlocked.Increment(ref restartCount); return true; }
        ));

        wd.Start();
        await Task.Delay(1500);
        Assert.Equal(0, restartCount); // 健康，不重启

        healthy = false;
        await Task.Delay(1500);
        Assert.True(restartCount >= 1, $"应至少重启 1 次，实际 {restartCount}");

        wd.Stop();
    }

    [Fact]
    public async Task RestartLimit_PreventsExcessiveRestarts()
    {
        var options = new WatchdogOptions
        {
            CheckIntervalSeconds = 1,
            MaxRestartsPerWindow = 2,
            WindowSeconds = 60
        };

        int restartCount = 0;
        using var wd = new WatchdogService(options);
        wd.Register(new WatchedEntry(
            "flaky-service",
            () => false,
            _ => { Interlocked.Increment(ref restartCount); return true; }
        ));

        wd.Start();
        await Task.Delay(4000);
        wd.Stop();

        Assert.True(restartCount <= 2, $"窗口内最多重启 2 次，实际 {restartCount}");

        var status = wd.GetStatus();
        Assert.True(status.Entries[0].RestartLimitReached);
    }

    [Fact]
    public async Task NoRestart_WhenAutoRestartDisabled()
    {
        int alertCount = 0;
        var options = new WatchdogOptions { CheckIntervalSeconds = 1 };
        using var wd = new WatchdogService(options);
        wd.HealthCheckFailed += (_, e) =>
        {
            if (!e.RestartTriggered) Interlocked.Increment(ref alertCount);
        };

        wd.Register(new WatchedEntry("no-restart-svc", () => false));
        wd.Start();
        await Task.Delay(1500);
        wd.Stop();

        Assert.True(alertCount > 0, "应触发告警");
    }

    [Fact]
    public async Task Events_AreFired()
    {
        var failedEvents = new List<HealthCheckFailedEventArgs>();
        var restartedEvents = new List<RestartedEventArgs>();

        var options = new WatchdogOptions { CheckIntervalSeconds = 1 };
        using var wd = new WatchdogService(options);
        wd.HealthCheckFailed += (_, e) => { lock (failedEvents) failedEvents.Add(e); };
        wd.Restarted += (_, e) => { lock (restartedEvents) restartedEvents.Add(e); };

        wd.Register(new WatchedEntry("event-svc", () => false, _ => true));
        wd.Start();
        await Task.Delay(1500);
        wd.Stop();

        Assert.NotEmpty(failedEvents);
        Assert.Equal("event-svc", failedEvents[0].Name);
        Assert.True(failedEvents[0].RestartTriggered);

        Assert.NotEmpty(restartedEvents);
        Assert.True(restartedEvents[0].Success);
        Assert.Equal(1, restartedEvents[0].RestartCount);
    }

    [Fact]
    public void GetStatus_ReportsCorrectMetrics()
    {
        using var wd = new WatchdogService(new WatchdogOptions());
        wd.Register(new WatchedEntry("svc-1", () => true));
        wd.Register(new WatchedEntry("svc-2", () => true));

        var status = wd.GetStatus();
        Assert.Equal(2, status.Entries.Length);
        Assert.Equal(0, status.TotalChecks);
        Assert.Equal(0, status.TotalRestarts);
        Assert.All(status.Entries, e => Assert.True(e.Healthy));
    }

    [Fact]
    public void Dispose_StopsCleanly()
    {
        var wd = new WatchdogService(new WatchdogOptions());
        wd.Register(new WatchedEntry("svc", () => true));
        wd.Start();
        wd.Dispose();

        Assert.False(wd.GetStatus().Running);
        // 二次 Dispose 不抛异常
        wd.Dispose();
    }

    [Fact]
    public void DoubleStart_IsIdempotent()
    {
        using var wd = new WatchdogService(new WatchdogOptions());
        wd.Start();
        wd.Start(); // 不应抛异常
        Assert.True(wd.GetStatus().Running);
        wd.Stop();
    }
}
