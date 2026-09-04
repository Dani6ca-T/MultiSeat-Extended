using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// F3-class regression for SetResolutionAsync: it captures the seat BEFORE waiting for the
/// per-seat lifecycle gate, then runs session/process-creating side effects
/// (DisconnectSession, LaunchSessionAsync, config rebuild, Apollo start). A concurrent
/// DELETE that completes while it waits leaves the captured object reading TearingDown
/// (H2 ordering: removal → TearingDown → teardown → gate release), and the side effects
/// must not run against it — LaunchSessionAsync would create a Windows session nothing in
/// _seats would ever tear down.
///
/// The decision is a pure seam pinned here; the full method needs the real SeatManager
/// dependency graph (repo's no-fakes rule).
/// </summary>
public class SeatManagerResolutionGuardTests
{
    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Error)]
    [InlineData(SeatStatus.Connecting)]
    public void RegisteredSeat_ResolutionChangeIsStillAllowed(SeatStatus status)
    {
        // Pre-existing semantics: SetResolutionAsync never gated on a status precondition,
        // and no reachable non-TearingDown state invalidates the change.
        Assert.True(SeatManager.ResolutionChangeStillValid(status));
    }

    [Fact]
    public void RemovedSeat_ResolutionChangeIsRejected()
    {
        // TearingDown is the H2 "removed" signal — the only state in which the session-
        // creating side effects must not run.
        Assert.False(SeatManager.ResolutionChangeStillValid(SeatStatus.TearingDown));
    }

    [Fact]
    public async Task CompletedConcurrentTeardown_IsRejectedAfterGateAcquisition()
    {
        // Drives the real ordering: teardown removes the seat from the registry under the
        // gate and writes TearingDown before releasing (H2). A SetResolutionAsync request
        // that was waiting on the gate then acquires it and must see TearingDown — the
        // post-gate guard rejects it before any side effect.
        var seats = new ConcurrentDictionary<Guid, SeatInfo>();
        var gate = new SeatLifecycleGate();
        var seat = new SeatInfo { AccountName = "AccountA", Status = SeatStatus.Ready };
        seats[seat.Id] = seat;

        // Concurrent DELETE wins the gate: removes the seat, tears it down (TearingDown),
        // releases.
        var (removed, teardownLease) = await SeatManager.TryBeginTeardownAsync(
            seats, gate, seat.Id, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Same(seat, removed);
        seat.TransitionTo(SeatStatus.TearingDown, NullLogger.Instance);
        teardownLease!.Dispose();

        // The stale SetResolutionAsync request now acquires the gate with its captured seat.
        using var lease = await gate.AcquireAsync(seat.Id, CancellationToken.None);
        Assert.False(SeatManager.ResolutionChangeStillValid(seat.Status));
    }
}
