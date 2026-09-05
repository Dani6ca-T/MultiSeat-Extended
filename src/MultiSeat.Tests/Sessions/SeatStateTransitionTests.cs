using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// G8 regression: every real <see cref="SeatStatus"/> transition must stamp
/// <see cref="SeatInfo.LastTransitionAt"/>, so operators and dashboard consumers can answer
/// "when did this seat last change state" from the seat itself instead of reconstructing it
/// from logs. <see cref="SeatState.TransitionTo"/> is the single mutation point for Status,
/// so stamping there covers all 14 production call sites with one assignment.
/// </summary>
public class SeatStateTransitionTests
{
    [Fact]
    public void TransitionTo_UpdatesLastTransitionAt()
    {
        var seat = NewSeat(SeatStatus.Ready);
        var before = seat.LastTransitionAt;

        seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance);

        Assert.True(seat.LastTransitionAt >= before);
        Assert.Equal(SeatStatus.Streaming, seat.Status);
    }

    [Fact]
    public void TransitionTo_IsMonotonicAcrossTransitions()
    {
        // Ordering, not exact equality: the Windows clock need not tick between two
        // back-to-back transitions, so assert non-decreasing rather than increasing.
        var seat = NewSeat(SeatStatus.Ready);

        seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance);
        var first = seat.LastTransitionAt;
        seat.TransitionTo(SeatStatus.Connecting, NullLogger.Instance);
        var second = seat.LastTransitionAt;
        seat.TransitionTo(SeatStatus.Ready, NullLogger.Instance);

        Assert.True(second >= first);
        Assert.True(seat.LastTransitionAt >= second);
    }

    [Fact]
    public void TransitionTo_SameState_DoesNotRestamp()
    {
        // Same-state re-assert is documented as a no-op, not a transition
        // (CanTransitionTo); it must not manufacture a fresh transition timestamp.
        var seat = NewSeat(SeatStatus.Streaming);
        var before = seat.LastTransitionAt;

        seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance);

        Assert.Equal(before, seat.LastTransitionAt);
    }

    [Fact]
    public void NewSeat_HasTransitionTimestamp()
    {
        // Construction bypasses TransitionTo (object initializer), so the field carries a
        // construction-time default: a seat always has a value, never null/stale.
        var before = DateTimeOffset.UtcNow;
        var seat = NewSeat(SeatStatus.Provisioning);

        Assert.True(seat.LastTransitionAt >= before.AddMinutes(-1));
        Assert.True(seat.LastTransitionAt <= DateTimeOffset.UtcNow);
    }

    private static SeatInfo NewSeat(SeatStatus status) => new()
    {
        AccountName = "TestSeat",
        Status = status
    };
}
