using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

public class SeatStateTests
{
    [Theory]
    [InlineData(SeatStatus.Idle, SeatStatus.Provisioning, true)]
    [InlineData(SeatStatus.Provisioning, SeatStatus.Configuring, true)]
    [InlineData(SeatStatus.Configuring, SeatStatus.Ready, true)]
    [InlineData(SeatStatus.Ready, SeatStatus.Streaming, true)]
    [InlineData(SeatStatus.Streaming, SeatStatus.TearingDown, true)]
    [InlineData(SeatStatus.TearingDown, SeatStatus.Idle, true)]
    [InlineData(SeatStatus.Idle, SeatStatus.Streaming, false)]     // invalid skip
    [InlineData(SeatStatus.Ready, SeatStatus.Provisioning, false)] // can't go backward
    [InlineData(SeatStatus.Error, SeatStatus.TearingDown, true)]   // error recovery
    // Sleep recovery: a live seat drops into Connecting and comes back to whichever
    // operational state it held, or fails to Error.
    [InlineData(SeatStatus.Ready, SeatStatus.Connecting, true)]
    [InlineData(SeatStatus.Streaming, SeatStatus.Connecting, true)]
    [InlineData(SeatStatus.Connecting, SeatStatus.Ready, true)]
    [InlineData(SeatStatus.Connecting, SeatStatus.Streaming, true)]
    [InlineData(SeatStatus.Connecting, SeatStatus.Error, true)]
    [InlineData(SeatStatus.Connecting, SeatStatus.TearingDown, true)]
    [InlineData(SeatStatus.Connecting, SeatStatus.Provisioning, false)]
    [InlineData(SeatStatus.Idle, SeatStatus.Connecting, false)]     // nothing to reconnect
    public void CanTransitionTo_ValidatesCorrectly(SeatStatus from, SeatStatus to, bool expected)
    {
        Assert.Equal(expected, from.CanTransitionTo(to));
    }

    [Fact]
    public void HealthCheck_StillWatches_AConnectingSeat()
    {
        // A seat mid-recovery must stay in the health check's scope, or a recovery interrupted
        // by a service restart would leave it in Connecting with nothing looking at it.
        Assert.True(MultiSeat.Service.Monitoring.SessionHealthCheck.IsWorthChecking(SeatStatus.Connecting));
    }

    [Fact]
    public void EveryStatusHasATransitionRule()
    {
        // Adding a SeatStatus without a row here leaves it a dead end: CanTransitionTo returns
        // false for everything, so the state can be entered and never legally left.
        foreach (var status in Enum.GetValues<SeatStatus>())
        {
            if (status == SeatStatus.Idle) continue;   // start state, covered above
            var hasOutbound = Enum.GetValues<SeatStatus>().Any(t => status.CanTransitionTo(t));
            Assert.True(hasOutbound, $"{status} has no outbound transitions");
        }
    }
}
