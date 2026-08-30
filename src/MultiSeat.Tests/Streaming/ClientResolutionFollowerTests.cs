using MultiSeat.Service.Streaming;
using MultiSeat.Shared.Models;
using Xunit;

using FollowAction = MultiSeat.Service.Streaming.ClientResolutionFollower.FollowAction;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// Following the client's resolution means reconnecting the seat's RDP session at the requested
/// size, which briefly interrupts the stream. So the decision to act has to be right in both
/// directions, and both mistakes are ugly: act too eagerly and the seat reconnects on a loop,
/// interrupting play every tick; act too rarely and the client silently gets the wrong size.
///
/// The parsing half is covered by ApolloLogParserTests. This is the deciding half.
/// </summary>
public class ClientResolutionFollowerTests
{
    private static RequestedMode Mode(int w, int h, int? hz = 60) => new(w, h, hz);

    [Fact]
    public void SameSizeAsTheSeat_IsLeftAlone()
    {
        // The steady state, hit on every tick of a healthy stream. If this ever returned Resize
        // the seat would reconnect continuously while nothing was wrong.
        Assert.Equal(
            FollowAction.AlreadyCorrectSize,
            ClientResolutionFollower.Decide(Mode(1920, 1080), 1920, 1080, lastApplied: null));
    }

    [Fact]
    public void ANewSize_IsApplied()
    {
        Assert.Equal(
            FollowAction.Resize,
            ClientResolutionFollower.Decide(Mode(2560, 1440), 1920, 1080, lastApplied: null));
    }

    [Fact]
    public void TheSameSizeTwice_IsNotRetried()
    {
        // The loop guard. We asked for 2560x1440, the seat is still 1920x1080, so Windows or mstsc
        // refused it. Trying again on the next tick — and every tick after — would interrupt the
        // stream repeatedly and never succeed.
        Assert.Equal(
            FollowAction.AlreadyAttempted,
            ClientResolutionFollower.Decide(Mode(2560, 1440), 1920, 1080, lastApplied: Mode(2560, 1440)));
    }

    [Fact]
    public void ADifferentSizeAfterARefusedOne_IsStillApplied()
    {
        // The other half of the loop guard, and the easier one to get wrong: remembering the last
        // attempt must not freeze the seat's resolution forever. A client that asks for something
        // new — or a different client entirely — has to get through.
        Assert.Equal(
            FollowAction.Resize,
            ClientResolutionFollower.Decide(Mode(1280, 720), 1920, 1080, lastApplied: Mode(2560, 1440)));
    }

    [Fact]
    public void RefreshRateAloneDoesNotCountAsANewRequest()
    {
        // Only width and height are compared, because only they are what a reconnect can change.
        // Treating 60Hz -> 120Hz at the same size as new would reconnect the session for a value
        // this mechanism cannot affect.
        Assert.Equal(
            FollowAction.AlreadyAttempted,
            ClientResolutionFollower.Decide(Mode(2560, 1440, 120), 1920, 1080, lastApplied: Mode(2560, 1440, 60)));
    }

    [Theory]
    [InlineData(320, 240)]      // below mstsc's floor
    [InlineData(640, 100)]      // height below the floor
    [InlineData(9000, 4000)]    // beyond the ceiling
    public void AGeometryMstscWouldIgnore_IsNotAttempted(int w, int h)
    {
        // Reconnecting for a size mstsc discards costs an interruption and changes nothing, so it
        // is refused before it is tried rather than after it fails.
        Assert.Equal(
            FollowAction.GeometryRejected,
            ClientResolutionFollower.Decide(Mode(w, h), 1920, 1080, lastApplied: null));
    }

    [Fact]
    public void AlreadyCorrectSize_WinsOverEverythingElse()
    {
        // Ordering: a seat that is already the requested size must report AlreadyCorrectSize even
        // if that size was also the last thing attempted — otherwise a successful resize reads as
        // a failed one forever after.
        Assert.Equal(
            FollowAction.AlreadyCorrectSize,
            ClientResolutionFollower.Decide(Mode(2560, 1440), 2560, 1440, lastApplied: Mode(2560, 1440)));
    }

    [Fact]
    public void AnOddButLegalSize_IsApplied()
    {
        // 1920x1200 (Ally X) and other non-16:9 sizes are perfectly valid; the geometry check is
        // about mstsc's limits, not about tidy aspect ratios.
        Assert.Equal(
            FollowAction.Resize,
            ClientResolutionFollower.Decide(Mode(1920, 1200), 1920, 1080, lastApplied: null));
    }
}
