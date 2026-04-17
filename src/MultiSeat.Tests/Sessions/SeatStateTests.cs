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
    public void CanTransitionTo_ValidatesCorrectly(SeatStatus from, SeatStatus to, bool expected)
    {
        Assert.Equal(expected, from.CanTransitionTo(to));
    }
}
