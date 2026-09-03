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
    private readonly ApolloManager _apolloManager;
    private readonly SeatManager _seatManager;
    private readonly OnConnectAppLauncher _onConnectApps;
    private readonly ClientResolutionFollower _resolutionFollower;

    public SessionHealthCheck(
        ILogger<SessionHealthCheck> logger,
        ISessionLauncher sessionLauncher,
        ApolloManager apolloManager,
        SeatManager seatManager,
        OnConnectAppLauncher onConnectApps,
        ClientResolutionFollower resolutionFollower)
    {
        _logger = logger;
        _sessionLauncher = sessionLauncher;
        _apolloManager = apolloManager;
        _seatManager = seatManager;
        _onConnectApps = onConnectApps;
        _resolutionFollower = resolutionFollower;
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
            seat.Status = SeatStatus.Error;
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
            try
            {
                // Kill the existing Apollo first — it survived sleep but with a broken
                // display pipeline (DXGI/QueryDisplayConfig fail on Disconnected sessions).
                // Without this, RestartAsync launches a second Apollo alongside the first,
                // causing a port conflict. KillForReconnect also resets RestartCount so
                // sleep cycles don't exhaust the crash-restart limit.
                _apolloManager.KillForReconnect(seat);

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
                    seat.Status = SeatStatus.Error;
                    seat.ErrorMessage = "RDP session did not become active after reconnect";
                    return true;
                }

                // Give the display pipeline a moment to reinitialize after the session
                // transitions to Active — SudoVDA and DXGI need a beat to be ready.
                await Task.Delay(2000, ct);

                _logger.LogInformation(
                    "Seat {Id}: session reconnected — restarting Apollo",
                    seat.Id);
                var newPid = await _apolloManager.RestartAsync(seat, ct);
                if (newPid > 0)
                {
                    seat.ApolloProcessId = newPid;
                    _logger.LogInformation(
                        "Seat {Id}: Apollo restarted after reconnect (PID {Pid})",
                        seat.Id, newPid);

                    // The session disconnect/reconnect wiped display-isolation state
                    // (SudoVDA is no longer primary; the RDP adapter has come back at
                    // its 1024×768 wake default). Without this, Apollo's mode change
                    // ends up on the wrong display and the stream stays at 1024×768.
                    await _seatManager.ApplyDisplayIsolationAsync(seat, ct);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Seat {Id}: failed to reconnect session after sleep", seat.Id);
            }
            return false;
        }

        // ── Check 2: Is Apollo still running? ─────────────────────
        var apolloAlive = IsProcessAlive(seat.ApolloProcessId);

        if (!apolloAlive && seat.ApolloProcessId > 0 &&
            seat.Status is SeatStatus.Ready or SeatStatus.Streaming)
        {
            _logger.LogWarning(
                "Seat {Id}: Apollo (PID {Pid}) crashed — attempting restart",
                seat.Id, seat.ApolloProcessId);

            // Try auto-restart
            var newPid = await _apolloManager.RestartAsync(seat, ct);

            if (newPid > 0)
            {
                seat.ApolloProcessId = newPid;
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
                // Restart failed — give up
                try { _sessionLauncher.DisconnectSession(seat.SessionId); } catch { /* best effort */ }
                seat.Status = SeatStatus.Error;
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
}
