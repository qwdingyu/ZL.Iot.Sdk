using ZL.Connection;
using Xunit;

namespace ZL.Connection.Tests;

public class ConnectionStateMachineTests
{
    [Fact]
    public void Constructor_DefaultStartState_IsDisconnected()
    {
        var sm = new ConnectionStateMachine();
        Assert.Equal(ConnectionState.Disconnected, sm.CurrentState);
    }

    [Fact]
    public void Constructor_CustomStartState_UsesProvidedState()
    {
        var sm = new ConnectionStateMachine(ConnectionState.Connecting);
        Assert.Equal(ConnectionState.Connecting, sm.CurrentState);
    }

    [Theory]
    [InlineData(ConnectionState.Disconnected, ConnectionState.Connecting, true)]
    [InlineData(ConnectionState.Connecting, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.Connecting, ConnectionState.Error, true)]
    [InlineData(ConnectionState.Connecting, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.Connected, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.Connected, ConnectionState.Reconnecting, true)]
    [InlineData(ConnectionState.Connected, ConnectionState.Error, true)]
    [InlineData(ConnectionState.Reconnecting, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.Reconnecting, ConnectionState.Error, true)]
    [InlineData(ConnectionState.Reconnecting, ConnectionState.Disconnected, true)]
    [InlineData(ConnectionState.Error, ConnectionState.Connecting, true)]
    [InlineData(ConnectionState.Error, ConnectionState.Disconnected, true)]
    public void TryTransition_ValidTransitions_Succeed(ConnectionState from, ConnectionState to, bool expected)
    {
        var sm = new ConnectionStateMachine(from);
        var result = sm.TryTransition(to);
        Assert.Equal(expected, result);
        if (result)
            Assert.Equal(to, sm.CurrentState);
    }

    [Theory]
    [InlineData(ConnectionState.Disconnected, ConnectionState.Connected)]
    [InlineData(ConnectionState.Disconnected, ConnectionState.Reconnecting)]
    [InlineData(ConnectionState.Connecting, ConnectionState.Reconnecting)]
    [InlineData(ConnectionState.Connected, ConnectionState.Connecting)]
    [InlineData(ConnectionState.Reconnecting, ConnectionState.Connecting)]
    [InlineData(ConnectionState.Error, ConnectionState.Connected)]
    [InlineData(ConnectionState.Error, ConnectionState.Reconnecting)]
    public void TryTransition_InvalidTransitions_Fail(ConnectionState from, ConnectionState to)
    {
        var sm = new ConnectionStateMachine(from);
        var result = sm.TryTransition(to);
        Assert.False(result);
        Assert.Equal(from, sm.CurrentState);
    }

    [Fact]
    public void TryTransition_Chained_TransitionsUpdateState()
    {
        var sm = new ConnectionStateMachine();

        Assert.True(sm.TryTransition(ConnectionState.Connecting));
        Assert.Equal(ConnectionState.Connecting, sm.CurrentState);

        Assert.True(sm.TryTransition(ConnectionState.Connected));
        Assert.Equal(ConnectionState.Connected, sm.CurrentState);

        Assert.True(sm.TryTransition(ConnectionState.Disconnected));
        Assert.Equal(ConnectionState.Disconnected, sm.CurrentState);
    }

    [Fact]
    public void TryTransition_ErrorState_CapturesErrorMessage()
    {
        var sm = new ConnectionStateMachine(ConnectionState.Connecting);
        var error = new InvalidOperationException("test error");
        var raised = false;

        sm.StateChanged += (sender, e) =>
        {
            raised = true;
            Assert.Equal(ConnectionState.Error, e.CurrentState);
            Assert.Equal("timeout", e.ErrorMessage);
            Assert.Same(error, e.Exception);
        };

        sm.TryTransition(ConnectionState.Error, "timeout", error);
        Assert.True(raised);
        Assert.Equal(ConnectionState.Error, sm.CurrentState);
    }

    [Fact]
    public void ForceTransition_SkipsGuard_StillTriggersEvent()
    {
        var sm = new ConnectionStateMachine(ConnectionState.Disconnected);
        var raised = false;

        sm.StateChanged += (sender, e) =>
        {
            raised = true;
            Assert.Equal(ConnectionState.Connected, e.CurrentState);
        };

        sm.ForceTransition(ConnectionState.Connected);
        Assert.True(raised);
        Assert.Equal(ConnectionState.Connected, sm.CurrentState);
    }

    [Fact]
    public void CanTransition_Valid_ReturnsTrue()
    {
        var sm = new ConnectionStateMachine(ConnectionState.Disconnected);
        Assert.True(sm.CanTransition(ConnectionState.Connecting));
        Assert.False(sm.CanTransition(ConnectionState.Connected));
    }

    [Fact]
    public void GetAvailableTransitions_FromDisconnected_ReturnsConnectingAndError()
    {
        var sm = new ConnectionStateMachine(ConnectionState.Disconnected);
        var available = sm.GetAvailableTransitions();
        Assert.Contains(ConnectionState.Connecting, available);
        Assert.Contains(ConnectionState.Error, available);
        Assert.DoesNotContain(ConnectionState.Connected, available);
        Assert.DoesNotContain(ConnectionState.Reconnecting, available);
    }

    [Fact]
    public void Reset_ReturnsToDisconnected_AndTriggersEvent()
    {
        var sm = new ConnectionStateMachine(ConnectionState.Connected);
        var raised = false;

        sm.StateChanged += (sender, e) =>
        {
            raised = true;
            Assert.Equal(ConnectionState.Disconnected, e.CurrentState);
        };

        sm.Reset();
        Assert.True(raised);
        Assert.Equal(ConnectionState.Disconnected, sm.CurrentState);
    }

    [Fact]
    public void StateChanged_Event_RaisesForEveryTransition()
    {
        var sm = new ConnectionStateMachine();
        var events = new List<ConnectionState>();

        sm.StateChanged += (sender, e) => events.Add(e.CurrentState);

        sm.TryTransition(ConnectionState.Connecting);
        sm.TryTransition(ConnectionState.Connected);
        sm.TryTransition(ConnectionState.Error, "fail");
        sm.TryTransition(ConnectionState.Connecting);
        sm.TryTransition(ConnectionState.Connected);

        Assert.Equal(
            new[]
            {
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Error,
                ConnectionState.Connecting,
                ConnectionState.Connected
            },
            events);
    }

    [Fact]
    public void ThreadSafety_ConcurrentTransitions_NoException()
    {
        var sm = new ConnectionStateMachine();

        var tasks = Enumerable.Range(0, 50).Select(_ =>
            Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    sm.TryTransition(ConnectionState.Connecting);
                    sm.TryTransition(ConnectionState.Connected);
                    sm.TryTransition(ConnectionState.Disconnected);
                }
            }));

        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
    }
}
