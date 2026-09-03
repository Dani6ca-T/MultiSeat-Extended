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
    [InlineData(SeatStatus.Ready, SeatStatus.Connecting, true)]    // auto-reconnect can interrupt a Ready seat
    [InlineData(SeatStatus.Streaming, SeatStatus.Connecting, true)] // auto-reconnect can interrupt a Streaming seat
    [InlineData(SeatStatus.Connecting, SeatStatus.Ready, true)]    // successful recovery returns to Ready
    [InlineData(SeatStatus.Connecting, SeatStatus.Streaming, true)] // successful recovery returns to Streaming
    [InlineData(SeatStatus.Connecting, SeatStatus.Error, true)]    // failed recovery falls to Error
    [InlineData(SeatStatus.Connecting, SeatStatus.TearingDown, true)] // teardown is allowed from any non-terminal state
    [InlineData(SeatStatus.Idle, SeatStatus.Connecting, false)]    // Connecting is not a manual state — never entered directly
    public void CanTransitionTo_ValidatesCorrectly(SeatStatus from, SeatStatus to, bool expected)
    {
        Assert.Equal(expected, from.CanTransitionTo(to));
    }
}
