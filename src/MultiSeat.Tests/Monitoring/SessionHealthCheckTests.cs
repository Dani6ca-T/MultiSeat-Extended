using Microsoft.Extensions.Logging.Abstractions;
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

    // ── F1: recovery decides from post-gate status, never the pre-gate snapshot ────────
    //
    // TryReconnectAsync captures previousStatus BEFORE waiting for the per-seat lifecycle
    // gate; provisioning may complete (Configuring → Ready) while it waits. The restore
    // target is therefore re-derived from the seat's status AFTER the gate (currentStatus),
    // and the decision is a pure function pinned here the same way ResolveRecoveryStatus is.

    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Configuring)]
    public void RecoveryIsStillAllowed_ForStatesItOwns(SeatStatus status)
    {
        Assert.True(SessionHealthCheck.CanStillRecover(status));
    }

    [Theory]
    [InlineData(SeatStatus.Error)]
    [InlineData(SeatStatus.TearingDown)]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    public void RecoveryIsSkipped_ForSeatsThatLeftItsOwnedStates(SeatStatus status)
    {
        // Error = provisioning failed while recovery waited on the gate; TearingDown = the
        // seat was removed by teardown. Both already ran their cleanup — recovery must not
        // resurrect resources for them.
        Assert.False(SessionHealthCheck.CanStillRecover(status));
    }

    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    public void NormalRecovery_StillRestoresPreviousStatus(SeatStatus previous)
    {
        // Unchanged semantics: the caller moved a Ready/Streaming seat to Connecting before
        // recovery, so a successful reconnect restores exactly that operational state.
        Assert.Equal(previous,
            SessionHealthCheck.ResolvePostGateRecoveryStatus(
                SeatStatus.Connecting, previous));
    }

    [Fact]
    public void SeatThatBecameReadyWhileRecoveryWaited_IsNotRegressedToConfiguring()
    {
        // F1: recovery observed the seat mid-provision (Configuring), waited for the gate,
        // and provisioning completed (Ready) in the meantime. A successful reconnect must
        // leave the seat Ready — never restore the stale Configuring snapshot.
        Assert.Equal(SeatStatus.Ready,
            SessionHealthCheck.ResolvePostGateRecoveryStatus(
                SeatStatus.Ready, SeatStatus.Configuring));
    }

    [Fact]
    public void SeatThatBecameStreamingWhileRecoveryWaited_IsNotRegressed()
    {
        Assert.Equal(SeatStatus.Streaming,
            SessionHealthCheck.ResolvePostGateRecoveryStatus(
                SeatStatus.Streaming, SeatStatus.Configuring));
    }

    [Fact]
    public void SeatStillConfiguringAfterGate_RecoveryFallsToError()
    {
        // Defensive: with the fix a Configuring seat is never observed behind the gate (the
        // provision that owns it ends in Ready or Error first) — this only covers a seat
        // stranded in Configuring by the pre-F1 bug. Per the ResolveRecoveryStatus contract,
        // a state automatic recovery does not return to parks in Error.
        Assert.Equal(SeatStatus.Error,
            SessionHealthCheck.ResolvePostGateRecoveryStatus(
                SeatStatus.Configuring, SeatStatus.Configuring));
    }

    // ── F2: launched app exit returns the seat to Ready (Check 3) ───────
    //
    // LaunchAppInSeatAsync tracks the ROOT PID of the dashboard-launched app on SeatInfo;
    // Check 3 polls it and, once the process has exited, clears the launch state and returns
    // the seat to Ready. The decision and the state writes are pinned here (the launch itself
    // needs a real Windows session, so it stays integration-only).

    private static SeatInfo StreamingSeatWithApp(int pid) => new()
    {
        AccountName = "MultiSeatSeat01",
        Status = SeatStatus.Streaming,
        LaunchApp = @"C:\Games\game.exe",
        LaunchedProcessId = pid,
    };

    [Fact]
    public void RunningLaunchedApp_StaysStreaming()
    {
        // The tracked root process is still alive — nothing changes.
        Assert.False(SessionHealthCheck.LaunchedAppHasExited(
            StreamingSeatWithApp(pid: 42), _ => true));
    }

    [Fact]
    public void ExitedLaunchedApp_ReturnsSeatToReady_AndClearsLaunchState()
    {
        var seat = StreamingSeatWithApp(pid: 42);

        Assert.True(SessionHealthCheck.LaunchedAppHasExited(
            seat, _ => false)); // root process reported dead

        SessionHealthCheck.FinishLaunchedAppExit(seat, NullLogger.Instance);

        Assert.Equal(SeatStatus.Ready, seat.Status);   // Streaming → Ready
        Assert.Null(seat.LaunchApp);                   // launch state cleared
        Assert.Equal(0, seat.LaunchedProcessId);       // tracking state cleared
    }

    [Fact]
    public void MissingTrackedPid_NeverTriggersAnExit()
    {
        // A Streaming seat with LaunchApp set but PID 0 predates PID tracking (or the launch
        // failed to record one) — it cannot be told apart from an app that is still running,
        // so it must not be transitioned.
        Assert.False(SessionHealthCheck.LaunchedAppHasExited(
            StreamingSeatWithApp(pid: 0), _ => false));
    }

    [Fact]
    public void StreamingWithoutLaunchApp_IsNotTouched()
    {
        var seat = StreamingSeatWithApp(pid: 42);
        seat.LaunchApp = null;

        Assert.False(SessionHealthCheck.LaunchedAppHasExited(seat, _ => false));
    }

    [Fact]
    public void NonStreamingSeat_WithLaunchState_IsNotTouched()
    {
        // A Ready seat may carry LaunchApp from provisioning (SeatRequest) but is not in the
        // launched state — Check 3 must not act on it.
        var seat = StreamingSeatWithApp(pid: 42);
        seat.Status = SeatStatus.Ready;

        Assert.False(SessionHealthCheck.LaunchedAppHasExited(seat, _ => false));
    }

    [Fact]
    public void RecycledPidThatIsAlive_StaysStreaming()
    {
        // PID-reuse guard: the original app exited and Windows handed the PID to another
        // live process. The alive check cannot distinguish it from our app, and it must
        // NOT falsely report an exit — staying Streaming is the conservative direction.
        // (A reused PID that is itself dead can only follow the original's exit, so
        // reporting Ready then is correct, never premature.)
        Assert.False(SessionHealthCheck.LaunchedAppHasExited(
            StreamingSeatWithApp(pid: 42), _ => true));
    }

    [Fact]
    public void DeadProcessReported_AppliesCleanly_WithoutThrowing()
    {
        // The real checker (IsProcessAlive) swallows ArgumentException for a PID that no
        // longer exists; the decision + apply path must handle a reported-dead process
        // without an exception.
        var seat = StreamingSeatWithApp(pid: 999999);

        Assert.True(SessionHealthCheck.LaunchedAppHasExited(seat, _ => false));

        SessionHealthCheck.FinishLaunchedAppExit(seat, NullLogger.Instance);
        Assert.Equal(SeatStatus.Ready, seat.Status);
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
