using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// G8 contract: <see cref="SeatInfo.LastTransitionAt"/> answers "when did this seat last
/// change state" from the seat itself. <see cref="SeatState.TransitionTo"/> is the single
/// production mutation point for Status, so stamping there covers every transition with one
/// assignment — and only real transitions stamp: construction, illegal attempts and
/// same-state no-ops leave it untouched.
/// </summary>
public class SeatStateTransitionTests
{
    [Fact]
    public void NewSeat_LastTransitionAt_IsNull()
    {
        // Construction sets the initial status directly (object initializer), which is not
        // a transition — no timestamp exists yet.
        var seat = NewSeat(SeatStatus.Idle);

        Assert.Null(seat.LastTransitionAt);
    }

    [Fact]
    public void RealTransition_SetsLastTransitionAt()
    {
        var testStart = DateTimeOffset.UtcNow;
        var seat = NewSeat(SeatStatus.Idle);

        seat.TransitionTo(SeatStatus.Provisioning, NullLogger.Instance);

        Assert.NotNull(seat.LastTransitionAt);
        Assert.True(seat.LastTransitionAt >= testStart);
        Assert.True(seat.LastTransitionAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void SuccessiveTransitions_AdvanceLastTransitionAt()
    {
        // Strictly increasing across A → B → C. No sleeps and no clock abstraction: the
        // wall clock is only advanced by performing real transitions until it ticks past
        // the recorded stamp (bounded spin; each iteration is a genuine state change).
        var seat = NewSeat(SeatStatus.Ready);

        seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance);
        var first = AssertNonNullStamp(seat);

        var guard = 0;
        do
        {
            seat.TransitionTo(SeatStatus.Connecting, NullLogger.Instance);
            seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance);
            guard++;
        }
        while (seat.LastTransitionAt == first && guard < 1_000_000);

        Assert.True(seat.LastTransitionAt > first);
    }

    [Fact]
    public void IllegalTransition_PreservesBehavior_AndDoesNotStamp()
    {
        // StrictTransitions is on for the whole test assembly (SeatTransitionEnforcementTests
        // module initializer), so an illegal transition throws — that existing behavior is
        // preserved, and the failed attempt must not manufacture a transition timestamp.
        // Idle -> Streaming skips the entire pipeline; nothing should ever do it.
        var seat = NewSeat(SeatStatus.Idle);

        var ex = Assert.Throws<InvalidOperationException>(
            () => seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance));

        Assert.Contains("Idle", ex.Message);
        Assert.Equal(SeatStatus.Idle, seat.Status);
        Assert.Null(seat.LastTransitionAt);
    }

    [Fact]
    public void SameStateTransition_DoesNotRestamp()
    {
        // Same-state re-assert is legal but documented as a no-op, not a transition
        // (CanTransitionTo): it must not falsely create a new transition timestamp.
        var seat = NewSeat(SeatStatus.Ready);
        seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance);
        var first = AssertNonNullStamp(seat);

        seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance);

        Assert.Equal(first, seat.LastTransitionAt);
    }

    [Fact]
    public void LastTransitionAt_FlowsThroughSeatJson()
    {
        // HTTP and WebSocket serialize the whole SeatInfo (camelCase); no per-property
        // serialization code exists, so the signal must simply be there. The dashboard may
        // keep ignoring it — this checkpoint only makes it available.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        var seat = NewSeat(SeatStatus.Ready);
        seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance);

        var json = JsonSerializer.Serialize(seat, options);

        Assert.Contains("\"lastTransitionAt\"", json);
        Assert.Contains(seat.Status.ToString(), json);

        var back = JsonSerializer.Deserialize<SeatInfo>(json, options);
        Assert.NotNull(back);
        Assert.Equal(seat.LastTransitionAt, back!.LastTransitionAt);
    }

    private static DateTimeOffset AssertNonNullStamp(SeatInfo seat)
    {
        Assert.NotNull(seat.LastTransitionAt);
        return seat.LastTransitionAt!.Value;
    }

    private static SeatInfo NewSeat(SeatStatus status) => new()
    {
        AccountName = "TestSeat",
        Status = status
    };
}
