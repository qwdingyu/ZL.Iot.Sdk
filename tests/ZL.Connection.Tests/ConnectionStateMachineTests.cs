using Cs = ZL.ConnectionStateMachine;
using Xunit;

namespace ZL.Connection.Tests;

public class ConnectionStateMachineTests
{
    [Fact]
    public void Constructor_DefaultStartState_IsDisconnected()
    {
        var sm = new Cs.ConnectionStateMachine();
        Assert.Equal(Cs.ConnectionState.Disconnected, sm.CurrentState);
    }

    [Fact]
    public void Constructor_CustomStartState_UsesProvidedState()
    {
        var sm = new Cs.ConnectionStateMachine(Cs.ConnectionState.Connecting);
        Assert.Equal(Cs.ConnectionState.Connecting, sm.CurrentState);
    }

    [Theory]
    [InlineData(Cs.ConnectionState.Disconnected, Cs.ConnectionState.Connecting, true)]
    [InlineData(Cs.ConnectionState.Connecting, Cs.ConnectionState.Connected, true)]
    [InlineData(Cs.ConnectionState.Connecting, Cs.ConnectionState.Error, true)]
    [InlineData(Cs.ConnectionState.Connecting, Cs.ConnectionState.Disconnected, true)]
    [InlineData(Cs.ConnectionState.Connected, Cs.ConnectionState.Disconnected, true)]
    [InlineData(Cs.ConnectionState.Connected, Cs.ConnectionState.Reconnecting, true)]
    [InlineData(Cs.ConnectionState.Connected, Cs.ConnectionState.Error, true)]
    [InlineData(Cs.ConnectionState.Reconnecting, Cs.ConnectionState.Connected, true)]
    [InlineData(Cs.ConnectionState.Reconnecting, Cs.ConnectionState.Error, true)]
    [InlineData(Cs.ConnectionState.Reconnecting, Cs.ConnectionState.Disconnected, true)]
    [InlineData(Cs.ConnectionState.Error, Cs.ConnectionState.Connecting, true)]
    [InlineData(Cs.ConnectionState.Error, Cs.ConnectionState.Disconnected, true)]
    public void TryTransition_ValidTransitions_Succeed(Cs.ConnectionState from, Cs.ConnectionState to, bool expected)
    {
        var sm = new Cs.ConnectionStateMachine(from);
        var result = sm.TryTransition(to);
        Assert.Equal(expected, result);
        if (result)
            Assert.Equal(to, sm.CurrentState);
    }

    [Theory]
    [InlineData(Cs.ConnectionState.Disconnected, Cs.ConnectionState.Connected)]
    [InlineData(Cs.ConnectionState.Disconnected, Cs.ConnectionState.Reconnecting)]
    [InlineData(Cs.ConnectionState.Connecting, Cs.ConnectionState.Reconnecting)]
    [InlineData(Cs.ConnectionState.Connected, Cs.ConnectionState.Connecting)]
    [InlineData(Cs.ConnectionState.Reconnecting, Cs.ConnectionState.Connecting)]
    [InlineData(Cs.ConnectionState.Error, Cs.ConnectionState.Connected)]
    [InlineData(Cs.ConnectionState.Error, Cs.ConnectionState.Reconnecting)]
    public void TryTransition_InvalidTransitions_Fail(Cs.ConnectionState from, Cs.ConnectionState to)
    {
        var sm = new Cs.ConnectionStateMachine(from);
        var result = sm.TryTransition(to);
        Assert.False(result);
        Assert.Equal(from, sm.CurrentState);
    }

    [Fact]
    public void TryTransition_Chained_TransitionsUpdateState()
    {
        var sm = new Cs.ConnectionStateMachine();

        Assert.True(sm.TryTransition(Cs.ConnectionState.Connecting));
        Assert.Equal(Cs.ConnectionState.Connecting, sm.CurrentState);

        Assert.True(sm.TryTransition(Cs.ConnectionState.Connected));
        Assert.Equal(Cs.ConnectionState.Connected, sm.CurrentState);

        Assert.True(sm.TryTransition(Cs.ConnectionState.Disconnected));
        Assert.Equal(Cs.ConnectionState.Disconnected, sm.CurrentState);
    }

    [Fact]
    public void TryTransition_ErrorState_CapturesErrorMessage()
    {
        var sm = new Cs.ConnectionStateMachine(Cs.ConnectionState.Connecting);
        var error = new InvalidOperationException("test error");
        var raised = false;

        sm.StateChanged += (sender, e) =>
        {
            raised = true;
            Assert.Equal(Cs.ConnectionState.Error, e.CurrentState);
            Assert.Equal("timeout", e.ErrorMessage);
            Assert.Same(error, e.Exception);
        };

        sm.TryTransition(Cs.ConnectionState.Error, "timeout", error);
        Assert.True(raised);
        Assert.Equal(Cs.ConnectionState.Error, sm.CurrentState);
    }

    [Fact]
    public void ForceTransition_SkipsGuard_StillTriggersEvent()
    {
        var sm = new Cs.ConnectionStateMachine(Cs.ConnectionState.Disconnected);
        var raised = false;

        sm.StateChanged += (sender, e) =>
        {
            raised = true;
            Assert.Equal(Cs.ConnectionState.Connected, e.CurrentState);
        };

        sm.ForceTransition(Cs.ConnectionState.Connected);
        Assert.True(raised);
        Assert.Equal(Cs.ConnectionState.Connected, sm.CurrentState);
    }

    [Fact]
    public void CanTransition_Valid_ReturnsTrue()
    {
        var sm = new Cs.ConnectionStateMachine(Cs.ConnectionState.Disconnected);
        Assert.True(sm.CanTransition(Cs.ConnectionState.Connecting));
        Assert.False(sm.CanTransition(Cs.ConnectionState.Connected));
    }

    [Fact]
    public void GetAvailableTransitions_FromDisconnected_ReturnsConnectingAndError()
    {
        var sm = new Cs.ConnectionStateMachine(Cs.ConnectionState.Disconnected);
        var available = sm.GetAvailableTransitions();
        Assert.Contains(Cs.ConnectionState.Connecting, available);
        Assert.Contains(Cs.ConnectionState.Error, available);
        Assert.DoesNotContain(Cs.ConnectionState.Connected, available);
        Assert.DoesNotContain(Cs.ConnectionState.Reconnecting, available);
    }

    [Fact]
    public void Reset_ReturnsToDisconnected_AndTriggersEvent()
    {
        var sm = new Cs.ConnectionStateMachine(Cs.ConnectionState.Connected);
        var raised = false;

        sm.StateChanged += (sender, e) =>
        {
            raised = true;
            Assert.Equal(Cs.ConnectionState.Disconnected, e.CurrentState);
        };

        sm.Reset();
        Assert.True(raised);
        Assert.Equal(Cs.ConnectionState.Disconnected, sm.CurrentState);
    }

    [Fact]
    public void StateChanged_Event_RaisesForEveryTransition()
    {
        var sm = new Cs.ConnectionStateMachine();
        var events = new List<Cs.ConnectionState>();

        sm.StateChanged += (sender, e) => events.Add(e.CurrentState);

        sm.TryTransition(Cs.ConnectionState.Connecting);
        sm.TryTransition(Cs.ConnectionState.Connected);
        sm.TryTransition(Cs.ConnectionState.Error, "fail");
        sm.TryTransition(Cs.ConnectionState.Connecting);
        sm.TryTransition(Cs.ConnectionState.Connected);

        Assert.Equal(
            new[]
            {
                Cs.ConnectionState.Connecting,
                Cs.ConnectionState.Connected,
                Cs.ConnectionState.Error,
                Cs.ConnectionState.Connecting,
                Cs.ConnectionState.Connected
            },
            events);
    }

    [Fact]
    public void ThreadSafety_ConcurrentTransitions_NoException()
    {
        var sm = new Cs.ConnectionStateMachine();

        var tasks = Enumerable.Range(0, 50).Select(_ =>
            Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    sm.TryTransition(Cs.ConnectionState.Connecting);
                    sm.TryTransition(Cs.ConnectionState.Connected);
                    sm.TryTransition(Cs.ConnectionState.Disconnected);
                }
            }));

        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
    }
}
