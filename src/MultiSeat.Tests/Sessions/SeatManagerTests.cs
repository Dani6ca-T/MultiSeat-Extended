using Xunit;
using MultiSeat.Shared.Models;

namespace MultiSeat.Tests.Sessions;

public class SeatManagerTests
{
    // SeatManager integration tests require mocked subsystems.
    // These will be fleshed out as each subsystem is implemented.

    [Fact]
    public void SeatRequest_DefaultResolution_Is1080p()
    {
        var request = new SeatRequest { AccountName = "TestUser" };
        Assert.Equal(1920, request.Width);
        Assert.Equal(1080, request.Height);
        Assert.Equal(60, request.Fps);
    }

    [Fact]
    public void SeatInfo_StartsIdle()
    {
        var seat = new SeatInfo { AccountName = "TestUser" };
        Assert.Equal(SeatStatus.Idle, seat.Status);
        Assert.Equal(-1, seat.SessionId);
    }
}
