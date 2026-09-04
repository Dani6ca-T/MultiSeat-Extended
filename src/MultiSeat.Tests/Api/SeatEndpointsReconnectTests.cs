using MultiSeat.Service.Api;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Api;

/// <summary>
/// F3 regression: /session-reconnect captures the seat BEFORE waiting for the per-seat
/// lifecycle gate, then creates a NEW Windows session via LaunchSessionAsync. If a
/// concurrent teardown removed the seat (H2 ordering: removal → TearingDown → teardown →
/// gate release) or another reconnect already healed it (Error → Ready) while the request
/// waited, launching would orphan that session — nothing in _seats would ever tear it down.
///
/// The handler therefore re-checks, right after the gate and strictly before the launch,
/// that the seat is still Error. That decision is the pure seam pinned here (the endpoint
/// itself cannot be driven without a Windows session, matching the repo's no-fakes rule).
/// </summary>
public class SeatEndpointsReconnectTests
{
    [Fact]
    public void ErrorSeat_ReconnectIsStillAllowed()
    {
        Assert.True(SeatEndpoints.IsReconnectStillValid(SeatStatus.Error));
    }

    [Theory]
    [InlineData(SeatStatus.TearingDown)] // removed from _seats by a concurrent teardown
    [InlineData(SeatStatus.Ready)]       // already healed by another reconnect
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Connecting)]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    public void NonErrorSeat_ReconnectIsRejected(SeatStatus status)
    {
        // LaunchSessionAsync must not be reached for any of these — the guard runs before
        // the session-creating side effect.
        Assert.False(SeatEndpoints.IsReconnectStillValid(status));
    }
}
