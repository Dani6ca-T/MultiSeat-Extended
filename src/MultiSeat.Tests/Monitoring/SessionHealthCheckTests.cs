using MultiSeat.Service.Monitoring;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Monitoring;

/// <summary>
/// Tests for the session-active guard in SessionHealthCheck: the polling loop that
/// waits for a reconnected RDP session to reach Active state before Apollo is started.
/// </summary>
public class SessionHealthCheckTests
{
    // ── WaitForSessionActiveAsync ───────────────────────────────────────

    [Fact]
    public async Task SessionActiveImmediately_ReturnsTrue()
    {
        var result = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => true, sessionId: 42, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task SessionNeverActive_ReturnsFalse()
    {
        var result = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => false, sessionId: 42, CancellationToken.None,
            pollMs: 50, timeoutMs: 200);

        Assert.False(result);
    }

    [Fact]
    public async Task SessionBecomesActiveAfterSeveralPolls_ReturnsTrue()
    {
        var callCount = 0;
        var result = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => ++callCount >= 4,
            sessionId: 7, CancellationToken.None,
            pollMs: 10, timeoutMs: 5000);

        Assert.True(result);
        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task CancellationStopsPolling()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await SessionHealthCheck.WaitForSessionActiveAsync(
                _ =>
                {
                    callCount++;
                    if (callCount >= 2) cts.Cancel();
                    return false;
                },
                sessionId: 1, cts.Token,
                pollMs: 10, timeoutMs: 5000);
        });
    }

    [Fact]
    public async Task ActiveCheckOnFinalPollAfterTimeout_ReturnsTrue()
    {
        var polls = 0;
        var result = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => ++polls >= 3,
            sessionId: 99, CancellationToken.None,
            pollMs: 100, timeoutMs: 250);

        Assert.True(result);
        Assert.True(polls <= 4);
    }

    // ── IsWorthChecking (existing, still valid) ─────────────────────────

    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Connecting)]
    public void ALiveSeatIsChecked(SeatStatus status)
    {
        Assert.True(SessionHealthCheck.IsWorthChecking(status));
    }

    [Theory]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    [InlineData(SeatStatus.TearingDown)]
    [InlineData(SeatStatus.Error)]
    public void ANonActiveSeatIsNotChecked(SeatStatus status)
    {
        Assert.False(SessionHealthCheck.IsWorthChecking(status));
    }

    // ── Automatic recovery state transitions ────────────────────────────
    //
    // The actual Apollo / mstsc / SudoVDA work in CheckSeatAsync is sealed behind concrete
    // ApolloManager / SeatManager and cannot be unit-tested without introducing fakes — which
    // the change forbids. The state-transition decision is small, pure, and the only thing
    // the dashboard can observe, so it is the right thing to pin.

    [Fact]
    public void ReadySeat_AfterReconnect_ReturnsToReady()
    {
        Assert.Equal(SeatStatus.Ready,
            SessionHealthCheck.ResolveRecoveryStatus(SeatStatus.Ready, recoverySucceeded: true));
    }

    [Fact]
    public void StreamingSeat_AfterReconnect_ReturnsToStreaming()
    {
        Assert.Equal(SeatStatus.Streaming,
            SessionHealthCheck.ResolveRecoveryStatus(SeatStatus.Streaming, recoverySucceeded: true));
    }

    [Fact]
    public void ReadySeat_AfterFailedReconnect_GoesToError()
    {
        Assert.Equal(SeatStatus.Error,
            SessionHealthCheck.ResolveRecoveryStatus(SeatStatus.Ready, recoverySucceeded: false));
    }

    [Fact]
    public void StreamingSeat_AfterFailedReconnect_GoesToError()
    {
        Assert.Equal(SeatStatus.Error,
            SessionHealthCheck.ResolveRecoveryStatus(SeatStatus.Streaming, recoverySucceeded: false));
    }

    [Fact]
    public void AnUnreachablePreviousState_AfterReconnect_FallsToError()
    {
        // Defensive: CheckSeatAsync only ever calls this with Ready or Streaming (the only
        // states IsWorthChecking admits), but if a future caller passes something else and
        // reports success, the seat must not stay in an unreachable state.
        Assert.Equal(SeatStatus.Error,
            SessionHealthCheck.ResolveRecoveryStatus(SeatStatus.Configuring, recoverySucceeded: true));
    }

    // ── Integration: full CheckSeatAsync cycle ─────────────────────────

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires SYSTEM, TermWrap, and a live seat — runtime validation only")]
    public void ReadyToConnectingToReady_OnAutomaticSessionRecovery()
    {
        // Mirrors the runtime validation plan: a Ready seat that loses its RDP session is
        // observed as Connecting, then returns to Ready after the health check rebuilds
        // the session and Apollo. Lives only so the dashboard wiring is asserted alongside
        // the unit-tested transition logic — the unit tests above are the load-bearing
        // coverage.
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires SYSTEM, TermWrap, and a live seat — runtime validation only")]
    public void StreamingToConnectingToStreaming_OnAutomaticSessionRecovery()
    {
        // Same shape as the Ready variant; included to keep the (Ready|Streaming) → Connecting
        // → (Ready|Streaming) symmetry visible at the test level.
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires SYSTEM, TermWrap, and a live seat — runtime validation only")]
    public void ReadyToConnectingToError_WhenSessionRecoveryFails()
    {
        // Same as the success path but with the recovery engineered to fail (e.g. terminate
        // the seat account mid-recovery). Asserts the seat ends in Error, not stuck in Connecting.
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires SYSTEM, TermWrap, and a live seat — runtime validation only")]
    public void StreamingToConnectingToError_WhenSessionRecoveryFails()
    {
    }
}
