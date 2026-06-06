using ZL.Retry;
using Xunit;

namespace ZL.Connection.Tests;

public class ExponentialBackoffTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var strategy = new ExponentialBackoffStrategy();
        Assert.Equal(1000, strategy.BaseDelayMs);
        Assert.Equal(60000, strategy.MaxDelayMs);
        Assert.Equal(-1, strategy.MaxRetries);
        Assert.Equal(0.2, strategy.JitterFactor);
        Assert.Equal(0, strategy.CurrentAttempt);
    }

    [Fact]
    public void Constructor_InvalidBaseDelay_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffStrategy(baseDelayMs: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffStrategy(baseDelayMs: -1));
    }

    [Fact]
    public void Constructor_MaxDelayLessThanBase_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffStrategy(baseDelayMs: 5000, maxDelayMs: 1000));
    }

    [Fact]
    public void Constructor_InvalidJitterFactor_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffStrategy(jitterFactor: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffStrategy(jitterFactor: 1.1));
    }

    [Fact]
    public void GetNextDelayMs_Sequence_ExponentialGrowth()
    {
        // Use jitter=0 for predictable results
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 1000, maxDelayMs: 60000, jitterFactor: 0);

        Assert.Equal(1000, strategy.GetNextDelayMs());  // 1000 * 2^0
        Assert.Equal(2000, strategy.GetNextDelayMs());  // 1000 * 2^1
        Assert.Equal(4000, strategy.GetNextDelayMs());  // 1000 * 2^2
        Assert.Equal(8000, strategy.GetNextDelayMs());  // 1000 * 2^3
        Assert.Equal(16000, strategy.GetNextDelayMs()); // 1000 * 2^4
        Assert.Equal(32000, strategy.GetNextDelayMs()); // 1000 * 2^5
        Assert.Equal(60000, strategy.GetNextDelayMs()); // capped at max
        Assert.Equal(60000, strategy.GetNextDelayMs()); // still capped
    }

    [Fact]
    public void GetNextDelayMs_WithJitter_WithinRange()
    {
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 1000, maxDelayMs: 60000, jitterFactor: 0.2);

        for (int i = 0; i < 100; i++)
        {
            strategy.Reset();
            var delay = strategy.GetNextDelayMs();
            // With jitter ±20%, and minimum clamped to BaseDelayMs
            Assert.InRange(delay, 1000, 1200);
        }
    }

    [Fact]
    public void GetNextDelayMs_MaxRetries_Exceeds_ReturnsNegative()
    {
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 1000, maxDelayMs: 60000, maxRetries: 3, jitterFactor: 0);

        Assert.Equal(1000, strategy.GetNextDelayMs());  // attempt 0
        Assert.Equal(2000, strategy.GetNextDelayMs());  // attempt 1
        Assert.Equal(4000, strategy.GetNextDelayMs());  // attempt 2
        Assert.Equal(-1, strategy.GetNextDelayMs());    // attempt 3 >= maxRetries
    }

    [Fact]
    public void GetNextDelayMs_InfiniteRetries_NeverReturnsNegative()
    {
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 1000, maxDelayMs: 60000, maxRetries: -1, jitterFactor: 0);

        for (int i = 0; i < 20; i++)
        {
            var delay = strategy.GetNextDelayMs();
            Assert.True(delay > 0);
        }
    }

    [Fact]
    public void GetNextDelay_ReturnsTimeSpan()
    {
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 1000, maxDelayMs: 60000, jitterFactor: 0);

        var delay = strategy.GetNextDelay();
        Assert.NotNull(delay);
        Assert.Equal(1000, delay.Value.TotalMilliseconds);
    }

    [Fact]
    public void GetNextDelay_MaxRetriesExceeded_ReturnsNull()
    {
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 1000, maxDelayMs: 60000, maxRetries: 0, jitterFactor: 0);

        var delay = strategy.GetNextDelay();
        Assert.Null(delay);
    }

    [Fact]
    public void Reset_ClearsCounter()
    {
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 1000, maxDelayMs: 60000, jitterFactor: 0);

        strategy.GetNextDelayMs(); // attempt 0
        strategy.GetNextDelayMs(); // attempt 1
        Assert.Equal(2, strategy.CurrentAttempt);

        strategy.Reset();
        Assert.Equal(0, strategy.CurrentAttempt);
        Assert.Equal(1000, strategy.GetNextDelayMs()); // starts from beginning
    }

    [Fact]
    public void CanRetry_WithinLimit_ReturnsTrue()
    {
        var strategy = new ExponentialBackoffStrategy(maxRetries: 3, jitterFactor: 0);

        Assert.True(strategy.CanRetry());
        strategy.GetNextDelayMs();
        Assert.True(strategy.CanRetry());
        strategy.GetNextDelayMs();
        Assert.True(strategy.CanRetry());
        strategy.GetNextDelayMs();
        Assert.False(strategy.CanRetry()); // attempt 3 >= maxRetries 3
    }

    [Fact]
    public void CanRetry_Infinite_ReturnsTrue()
    {
        var strategy = new ExponentialBackoffStrategy(maxRetries: -1);
        Assert.True(strategy.CanRetry());
        for (int i = 0; i < 100; i++) strategy.GetNextDelayMs();
        Assert.True(strategy.CanRetry());
    }

    [Fact]
    public void GetCurrentDelayMs_DoesNotIncrementCounter()
    {
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 1000, jitterFactor: 0);

        Assert.Equal(1000, strategy.GetCurrentDelayMs());
        Assert.Equal(0, strategy.CurrentAttempt);

        strategy.GetNextDelayMs();
        Assert.Equal(2000, strategy.GetCurrentDelayMs());
        Assert.Equal(1, strategy.CurrentAttempt);
    }

    [Fact]
    public void ThreadSafety_ConcurrentCalls_NoException()
    {
        var tasks = Enumerable.Range(0, 50).Select(_ =>
            Task.Run(() =>
            {
                var local = new ExponentialBackoffStrategy(baseDelayMs: 100, maxDelayMs: 1000, maxRetries: 10, jitterFactor: 0.1);
                for (int i = 0; i < 10 && local.CanRetry(); i++)
                {
                    local.GetNextDelayMs();
                }
            }));

        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ToString_ReturnsNonEmpty()
    {
        var strategy = new ExponentialBackoffStrategy(baseDelayMs: 500, maxDelayMs: 5000, maxRetries: 3);
        var s = strategy.ToString();
        Assert.NotEmpty(s);
        Assert.Contains("ExponentialBackoff", s);
    }
}
