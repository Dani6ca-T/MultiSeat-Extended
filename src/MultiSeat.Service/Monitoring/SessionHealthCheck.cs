using System.Diagnostics;
using MultiSeat.Service.Api;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Monitoring;

/// <summary>
/// Periodic liveness probe for all active seats.
///
/// Checks that the Windows session, Apollo process, and virtual display
/// are all still alive. Degraded seats trigger auto-recovery:
///   - Apollo crash → auto-restart (up to ApolloManager.MaxRestartAttempts)
///   - Session death → seat enters Error state (requires manual teardown)
///
/// Runs on the MultiSeatWorker's health check timer (default: every 5 seconds).
/// State changes are broadcast to connected WebSocket clients.
/// </summary>
public sealed class SessionHealthCheck
{
    private readonly ILogger<SessionHealthCheck> _logger;
    private readonly ISessionLauncher _sessionLauncher;
    private readonly IStreamingProvider _streaming;
    private readonly SeatManager _seatManager;
    private readonly OnConnectAppLauncher _onConnectApps;
    private readonly ClientResolutionFollower _resolutionFollower;
    private readonly SeatLifecycleGate _lifecycleGate;

    public SessionHealthCheck(
        ILogger<SessionHealthCheck> logger,
        ISessionLauncher sessionLauncher,
        IStreamingProvider streaming,
        SeatManager seatManager,
        OnConnectAppLauncher onConnectApps,
        ClientResolutionFollower resolutionFollower,
        SeatLifecycleGate lifecycleGate)
    {
        _logger = logger;
        _sessionLauncher = sessionLauncher;
        _streaming = streaming;
        _seatManager = seatManager;
        _onConnectApps = onConnectApps;
        _resolutionFollower = resolutionFollower;
        _lifecycleGate = lifecycleGate;
    }

    /// <summary>
    /// Run health checks on all active seats.
    /// Returns a list of seats that changed state (for broadcasting).
    /// </summary>
    public async Task<IReadOnlyList<SeatInfo>> CheckAllSeatsAsync(
        SeatManager seatManager, CancellationToken ct)
    {
        var changedSeats = new List<SeatInfo>();

        foreach (var seat in seatManager.GetAllSeats())
        {
            if (!IsWorthChecking(seat.Status)) continue;

            var changed = await CheckSeatAsync(seat, ct);
            if (changed)
                changedSeats.Add(seat);
        }

        // Broadcast state changes to WebSocket clients
        foreach (var seat in changedSeats)
        {
            try
            {
                await WebSocketHub.BroadcastSeatUpdateAsync(seat);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to broadcast state change for seat {Id}", seat.Id);
            }
        }

        return changedSeats;
    }

    /// <summary>
    /// Check a single seat's health. Returns true if state changed.
    /// </summary>
    /// <summary>
    /// Which seats this check looks at.
    ///
    /// A seat mid-provision or mid-teardown is expected to be in flux, so watching it would fight
    /// whatever is moving it. Error is skipped for a blunter reason: a seat parked there has no
    /// live session to check.
    ///
    /// ⚠️ That last one has a consequence worth stating, because it caused a real bug (PR #22):
    /// nothing here ever takes a seat OUT of Error, and the Apollo-restart check below only runs
    /// for a seat this method admits. So a seat that lands in Error stays broken until something
    /// outside this class hands it back - which is what POST /api/seats/{id}/session-reconnect now
    /// does. Widening this set is not the fix; it would have the check fighting a teardown.
    /// </summary>
    internal static bool IsWorthChecking(SeatStatus status) =>
        status is not (SeatStatus.Idle or SeatStatus.Provisioning
                       or SeatStatus.TearingDown or SeatStatus.Error);

    /// <summary>
    /// How long after start an Apollo death is classified as a startup failure rather than
    /// a runtime crash. A process that dies this quickly likely never finished initializing
    /// (encoder setup, FFmpeg, log file) — restarting it would hit the same wall.
    /// </summary>
    internal static readonly TimeSpan ApolloStartupWindow =
        TimeSpan.FromSeconds(30);

    /// <summary>
    /// Classify an Apollo death by its uptime: within <see cref="ApolloStartupWindow"/> of
    /// start it is a startup failure. Null uptime (no instance record) is never a startup
    /// failure — there is nothing to classify.
    /// </summary>
    internal static bool IsStartupFailure(TimeSpan? uptime)
    {
        if (!uptime.HasValue)
            return false;

        return uptime.Value <= ApolloStartupWindow;
    }

    private async Task<bool> CheckSeatAsync(SeatInfo seat, CancellationToken ct)
    {
        // ── Check 1: Is the Windows session still alive? ──────────
        var sessionAlive = _sessionLauncher.IsSessionAlive(seat.SessionId);

        if (!sessionAlive)
        {
            _logger.LogWarning(
                "Seat {Id}: Windows session {Sid} no longer active",
                seat.Id, seat.SessionId);
            try { _sessionLauncher.DisconnectSession(seat.SessionId); } catch { /* best effort */ }
            seat.TransitionTo(SeatStatus.Error, _logger);
            seat.ErrorMessage = "Windows session terminated unexpectedly";
            return true;
        }

        // ── Check 1b: Is the session Active (not just Disconnected)? ──
        // Sessions go Disconnected when the PC sleeps (mstsc drops). A Disconnected
        // session breaks QueryDisplayConfig / DXGI, so Apollo cannot stream.
        // Reconnect via mstsc to restore Active state, then restart Apollo.
        if (!_sessionLauncher.IsSessionActive(seat.SessionId))
        {
            _logger.LogWarning(
                "Seat {Id}: session {Sid} is Disconnected (PC may have slept) — reconnecting",
                seat.Id, seat.SessionId);

            // Surface the reconnect as its own state so the dashboard does not claim the
            // seat is Ready/Streaming while the session, Apollo, and display pipeline are
            // being torn down and rebuilt. Capture the previous operational state so a
            // successful recovery returns to exactly where the seat was — a Ready seat
            // reconnects to Ready, a Streaming seat reconnects to Streaming. Failures
            // still fall to Error, the same way they did before this state existed.
            var previousStatus = seat.Status;
            if (previousStatus is SeatStatus.Ready or SeatStatus.Streaming)
            {
                seat.TransitionTo(SeatStatus.Connecting, _logger);
                // Broadcast the Connecting state before the recovery work begins. CheckAllSeatsAsync
                // only broadcasts once, after every seat has been processed in turn, so without
                // this the dashboard would skip past Connecting and only see the post-recovery
                // state — which is exactly the misleading behaviour this state exists to fix.
                try { await WebSocketHub.BroadcastSeatUpdateAsync(seat); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to broadcast Connecting state for seat {Id}", seat.Id); }
            }
            return await TryReconnectAsync(seat, previousStatus, ct);
        }

        // ── Check 2: Is Apollo still running? ─────────────────────
        var apolloAlive = IsProcessAlive(seat.StreamingProcessId);

        if (!apolloAlive && seat.StreamingProcessId > 0 &&
            seat.Status is SeatStatus.Ready or SeatStatus.Streaming)
        {
            _logger.LogWarning(
                "Seat {Id}: Apollo (PID {Pid}) crashed — attempting restart",
                seat.Id, seat.StreamingProcessId);

            // Diagnostic only: a death this close to start is usually a startup failure
            // (encoder/FFmpeg init, log-write problems) rather than a runtime crash —
            // say so before the restart masks the evidence.
            LogStartupFailureDiagnostic(seat);

            // Per-seat lifecycle gate. Apollo restart mutates StreamingProcessId and the
            // ApolloManager instance record; serializing against TryReconnectAsync and the
            // Apollo endpoints prevents the interleavings that leave orphaned processes.
            // The dead-session branch (Check 1) above is intentionally outside the gate:
            // it only writes Status, never touches the lifecycle state.
            using var lease = await _lifecycleGate.AcquireAsync(seat.Id, ct);

            // Try auto-restart
            var newPid = await _streaming.RestartAsync(seat, ct);

            if (newPid > 0)
            {
                seat.StreamingProcessId = newPid;
                _logger.LogInformation(
                    "Seat {Id}: Apollo restarted successfully (PID {Pid})",
                    seat.Id, newPid);

                // Apollo restart re-creates the SudoVDA monitor — the in-session
                // display-isolation state (SudoVDA-as-primary, RDP shrunk to 640×480)
                // doesn't survive that, so reapply it.
                await _seatManager.ApplyDisplayIsolationAsync(seat, ct);
                return true; // state metadata changed (PID)
            }
            else
            {
                // Restart failed — give up. Diagnose a startup failure first so the user
                // knows where to look before the seat is parked in Error.
                LogStartupFailureDiagnostic(seat);

                try { _sessionLauncher.DisconnectSession(seat.SessionId); } catch { /* best effort */ }
                seat.TransitionTo(SeatStatus.Error, _logger);
                seat.ErrorMessage = "Apollo streaming server crashed and could not be restarted";
                return true;
            }
        }

        // ── Check 3: Is a launched app still running? ─────────────
        // If a game was launched and has exited, transition back to Ready
        if (seat.Status == SeatStatus.Streaming && !string.IsNullOrEmpty(seat.LaunchApp))
        {
            // We don't track the game PID separately (it could spawn children),
            // so we only flag if Apollo itself died (handled above).
            // In the future, we could track the game PID for auto-restart.
        }

        // ── Launch-on-connect: tail Apollo's log for client connect/disconnect ──
        // and launch (or kill) the configured per-seat apps on the edges. No-op when
        // MultiSeat:LaunchOnConnect is empty. Cheap: reads only the bytes appended
        // since the previous tick. Does not change seat state here.
        _onConnectApps.ProcessSeat(seat, ct);

        // ── Follow the client's requested resolution ──────────────
        // Apollo cannot apply it itself inside an RDP seat, so resize by reconnecting the
        // session. No-op unless MultiSeat:FollowClientResolution is on.
        if (await _resolutionFollower.ProcessSeatAsync(seat, ct))
            return true; // seat geometry changed — worth broadcasting

        // ── Late SudoVDA detection ────────────────────────────────
        // Apollo creates the seat's virtual display when a client connects, not at startup,
        // so provisioning's one-shot lookup always misses it and display isolation gets
        // skipped for the seat's whole life. Retry here until it appears. No-op once
        // DisplayDevicePath is set, and silent while there is still nothing to find.
        if (string.IsNullOrEmpty(seat.DisplayDevicePath))
        {
            try
            {
                if (await _seatManager.TryLateDisplayDetectionAsync(seat, ct))
                    return true; // DisplayDevicePath changed — worth broadcasting
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Seat {Id}: late display detection failed", seat.Id);
            }
        }

        return false; // no state change
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Poll <paramref name="isSessionActive"/> until the session reports Active
    /// or the 10-second timeout expires.
    /// </summary>
    /// <returns>true if the session became Active within the timeout.</returns>
    internal static async Task<bool> WaitForSessionActiveAsync(
        Func<int, bool> isSessionActive,
        int sessionId,
        CancellationToken ct,
        int pollMs = 500,
        int timeoutMs = 10_000)
    {
        var waited = 0;
        while (waited < timeoutMs && !ct.IsCancellationRequested)
        {
            if (isSessionActive(sessionId))
                return true;
            await Task.Delay(pollMs, ct);
            waited += pollMs;
        }
        return isSessionActive(sessionId);
    }

    /// <summary>
    /// Diagnostic-only: when Apollo died shortly after start, warn that this looks like a
    /// startup failure and point at verbose logging for the underlying cause. Purely additive
    /// — recovery behavior is untouched. Uses the provider abstraction so the classification
    /// stays provider-neutral.
    /// </summary>
    private void LogStartupFailureDiagnostic(SeatInfo seat)
    {
        var uptime = _streaming.GetUptime(seat.Id);
        if (!IsStartupFailure(uptime))
            return;

        _logger.LogWarning(
            "Seat {Id}: Apollo appears to have failed during startup (died {Uptime} after start). " +
            "Check the Apollo log for the underlying error — e.g. encoder initialization / FFmpeg " +
            "failures, or problems writing the Apollo log. For more detailed diagnostics, set " +
            "MultiSeat:ApolloLogLevel=verbose.",
            seat.Id, uptime);
    }

    /// <summary>
    /// Automatic session-recovery path, extracted from <see cref="CheckSeatAsync"/> only so the
    /// state-transition outcomes are testable in isolation. The Apollo / mstsc / SudoVDA work is
    /// unchanged from the version that lived inline; only the state writes are now grouped.
    ///
    /// Caller has already transitioned the seat to <c>SeatStatus.Connecting</c> when <paramref name="previousStatus"/>
    /// is <see cref="SeatStatus.Ready"/> or <see cref="SeatStatus.Streaming"/>. On success we restore
    /// exactly that state, so a Ready seat reconnects to Ready and a Streaming seat reconnects to
    /// Streaming. On any failure we transition to <see cref="SeatStatus.Error"/>, matching the
    /// behaviour of the 10-second-active-timeout branch and the dead-session branch above.
    ///
    /// The entire body runs under the per-seat lifecycle gate, so concurrent operations on the
    /// same seat (POST /session-reconnect, SetResolutionAsync, StopApollo, etc.) wait instead of
    /// interleaving. Different seats remain parallel.
    /// </summary>
    private async Task<bool> TryReconnectAsync(SeatInfo seat, SeatStatus previousStatus, CancellationToken ct)
    {
        try
        {
            // Per-seat lifecycle gate. Acquired first so a parallel /session-reconnect or
            // SetResolutionAsync for the same seat waits for the recovery to finish (success or
            // Error) before acting. Released by the `using` regardless of how the inner code
            // exits — including OperationCanceledException, which the catch below converts to
            // Error so the seat cannot remain stuck in Connecting.
            using var lease = await _lifecycleGate.AcquireAsync(seat.Id, ct);

            // The caller captured previousStatus BEFORE waiting for the gate; while we waited,
            // the gate holder may have finished provisioning (Configuring → Ready), failed it
            // (→ Error), or torn the seat down (→ TearingDown, removed from the registry).
            // Every decision from here on is made from the seat's CURRENT state — never the
            // pre-gate snapshot — or a successful recovery could regress a seat that
            // legitimately advanced (e.g. to Ready) back to a stale Configuring value.
            var currentStatus = seat.Status;

            // A seat that left the states automatic recovery owns while we waited already ran
            // its own failure/teardown cleanup — do not resurrect resources for it here.
            // Error seats are recovered by the explicit POST /api/seats/{id}/session-reconnect.
            if (!CanStillRecover(currentStatus))
                return false;

            // Kill the existing Apollo first — it survived sleep but with a broken
            // display pipeline (DXGI/QueryDisplayConfig fail on Disconnected sessions).
            // Without this, RestartAsync launches a second Apollo alongside the first,
            // causing a port conflict. KillForReconnect also resets RestartCount so
            // sleep cycles don't exhaust the crash-restart limit.
            _streaming.KillForReconnect(seat);

            // Pass the geometry: if the stale session has to be logged off and recreated,
            // the replacement must come back at the seat's own size rather than inheriting
            // the console desktop's.
            //
            // Keep the id it answers with: that path returns a NEW session, and the
            // Apollo restart and display isolation just below both act on SessionId.
            seat.SessionId = await _sessionLauncher.LaunchSessionAsync(
                seat.AccountName, ct, RdpGeometry.ForClient(seat.Width, seat.Height));

            // Wait for the session to become ACTIVE before starting Apollo.
            // Apollo needs a live session for QueryDisplayConfig / DXGI; starting it
            // against a DISCONNECTED session triggers a restart feedback loop.
            if (!await WaitForSessionActiveAsync(
                    id => _sessionLauncher.IsSessionActive(id),
                    seat.SessionId, ct))
            {
                _logger.LogWarning(
                    "Seat {Id}: session {Sid} did not become ACTIVE within 10s after reconnect — aborting",
                    seat.Id, seat.SessionId);
                try { _sessionLauncher.DisconnectSession(seat.SessionId); } catch { /* best effort */ }
                seat.TransitionTo(SeatStatus.Error, _logger);
                seat.ErrorMessage = "RDP session did not become active after reconnect";
                return true;
            }

            // Give the display pipeline a moment to reinitialize after the session
            // transitions to Active — SudoVDA and DXGI need a beat to be ready.
            await Task.Delay(2000, ct);

            _logger.LogInformation(
                "Seat {Id}: session reconnected — restarting Apollo",
                seat.Id);
            var newPid = await _streaming.RestartAsync(seat, ct);
            if (newPid > 0)
            {
                seat.StreamingProcessId = newPid;
                _logger.LogInformation(
                    "Seat {Id}: Apollo restarted after reconnect (PID {Pid})",
                    seat.Id, newPid);

                // The session disconnect/reconnect wiped display-isolation state
                // (SudoVDA is no longer primary; the RDP adapter has come back at
                // its 1024×768 wake default). Without this, Apollo's mode change
                // ends up on the wrong display and the stream stays at 1024×768.
                await _seatManager.ApplyDisplayIsolationAsync(seat, ct);

                // Successful recovery — return the seat to exactly the state it legitimately
                // holds, decided from its CURRENT status (the gate is held, so nothing moved
                // since the re-read above). A seat that finished provisioning while we waited
                // stays Ready/Streaming instead of being regressed to a stale Configuring.
                seat.TransitionTo(
                    ResolvePostGateRecoveryStatus(seat.Status, previousStatus), _logger);
                return true;
            }

            // Apollo restart came back with -1 (start failed or restart-limit hit).
            // The previous shape of the function silently returned false here, leaving
            // the seat in Connecting forever — fix that by parking it in Error.
            _logger.LogError(
                "Seat {Id}: Apollo failed to restart after session reconnect", seat.Id);
            seat.TransitionTo(SeatStatus.Error, _logger);
            seat.ErrorMessage = "Apollo failed to restart after session reconnect";
            return true;
        }
        catch (OperationCanceledException)
        {
            // Without this catch the seat would stay in Connecting when the worker stops:
            // WaitAsync / LaunchSessionAsync / RestartAsync throw OCE, the generic catch below
            // does not catch it, and the seat was set to Connecting before TryReconnectAsync ran.
            // Mirror the generic failure path: log, park in Error. The semaphore is released
            // by the `using` above regardless of how this block exits.
            _logger.LogWarning(
                "Seat {Id}: session reconnect canceled", seat.Id);
            seat.TransitionTo(SeatStatus.Error, _logger);
            seat.ErrorMessage = "Session reconnect canceled";
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Seat {Id}: failed to reconnect session after sleep", seat.Id);
            seat.TransitionTo(SeatStatus.Error, _logger);
            seat.ErrorMessage = "Failed to reconnect session after sleep";
            return true;
        }
    }

    /// <summary>
    /// Whether automatic recovery should still run after the lifecycle gate was acquired.
    ///
    /// The seat is in a worth-checking state when recovery starts, but while it waited for the
    /// gate the holder may have finished the job: provisioning failure parks the seat in Error
    /// and teardown removes it (TearingDown) — both already ran their cleanup, so running
    /// KillForReconnect + session relaunch here would resurrect resources for a seat that is no
    /// longer recovering. Configuring is admitted so a seat stranded there by the pre-F1 stale
    /// restore is still pulled to a terminal state (see <see cref="ResolvePostGateRecoveryStatus"/>).
    /// </summary>
    internal static bool CanStillRecover(SeatStatus currentStatus) =>
        currentStatus is SeatStatus.Connecting or SeatStatus.Ready
            or SeatStatus.Streaming or SeatStatus.Configuring;

    /// <summary>
    /// Terminal status for a SUCCESSFUL reconnect, decided from the seat's status AFTER the
    /// lifecycle gate was acquired — never from the pre-gate snapshot the caller captured
    /// while waiting (F1).
    ///
    ///   Connecting — the normal path: the caller moved a Ready/Streaming seat here before
    ///                recovery, so restore previousStatus (Ready stays Ready, Streaming stays
    ///                Streaming).
    ///   Ready / Streaming — provisioning completed while recovery waited on the gate; the
    ///                seat legitimately advanced, and recovery must NOT regress it to the stale
    ///                Configuring value captured before the wait.
    ///   anything else (Configuring) — not a state automatic recovery returns to; park in
    ///                Error per <see cref="ResolveRecoveryStatus"/> so the user takes an
    ///                explicit action (session-reconnect, re-provision, teardown).
    /// </summary>
    internal static SeatStatus ResolvePostGateRecoveryStatus(
        SeatStatus currentStatus, SeatStatus previousStatus) =>
        currentStatus == SeatStatus.Connecting ? previousStatus
        : currentStatus is SeatStatus.Ready or SeatStatus.Streaming ? currentStatus
        : ResolveRecoveryStatus(currentStatus, recoverySucceeded: true);

    /// <summary>
    /// The state-transition rule for the automatic session-recovery path. Pure function: given
    /// the state the seat was in before recovery and whether recovery succeeded, return the
    /// state it should be in after recovery.
    ///
    /// Success restores the previous operational state (Ready/Streaming) so the dashboard returns
    /// to what it showed before the disconnect. Failure parks the seat in Error exactly like the
    /// 10-second-active-timeout branch. A previous state outside Ready/Streaming has no
    /// operational state to return to, so it falls to Error — the caller only passes Ready,
    /// Streaming or Configuring (the Configuring case is a mid-provision seat that was pulled
    /// into recovery; see <see cref="ResolvePostGateRecoveryStatus"/>).
    ///
    /// This helper is the spec; TryReconnectAsync applies it directly at the relevant branches
    /// (the inline log lines and DisconnectSession calls differ per failure mode, so the branches
    /// stay inline rather than collapsing through here). Keeping it as a separate function lets
    /// the transition rule be pinned by unit tests without standing up ApolloManager/SeatManager.
    /// </summary>
    internal static SeatStatus ResolveRecoveryStatus(SeatStatus previousStatus, bool recoverySucceeded) =>
        recoverySucceeded
            ? (previousStatus is SeatStatus.Ready or SeatStatus.Streaming
                ? previousStatus
                : SeatStatus.Error)
            : SeatStatus.Error;
}
