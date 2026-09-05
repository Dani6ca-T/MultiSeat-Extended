using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Streaming;

/// <summary>
/// Manages per-seat Apollo (Sunshine fork) process lifecycle.
///
/// Each seat runs its own Apollo instance with isolated config,
/// port range, display target, and audio device.
///
/// Architecture:
///   - Config is generated per-seat by ApolloConfigBuilder
///   - Apollo is launched inside the seat's Windows session via ProcessInjector
///     (so it sees the session's virtual display + audio device)
///   - Health is monitored by SessionHealthCheck; crashed instances are auto-restarted
///   - On teardown, the entire process tree is killed (Apollo spawns child encoders)
///
/// Apollo (Sunshine) uses these port offsets within a seat's block
/// (see Shared/Constants for the authoritative values):
///   -5  GFE HTTPS (Moonlight serverinfo/pair/launch)
///    0  GFE HTTP  (same, plaintext — the value written to the 'port' config key)
///    1  Web UI    (Apollo HTTPS web UI)
///    9  Video   (RTP)
///   10  Control (ENet)
///   11  Audio   (RTP)
///   12  Mic     (RTP)
///   26  RTSP    (session setup)
///
/// Requires Apollo (Sunshine fork) installed:
///   https://github.com/ClassicOldSong/Apollo
/// </summary>
public sealed class ApolloManager : IStreamingProvider
{
    private readonly ILogger<ApolloManager> _logger;
    private readonly MultiSeatOptions _options;
    private readonly ApolloConfigBuilder _configBuilder;
    private readonly ProcessInjector _processInjector;
    private readonly Monitoring.ApolloServerQuery _serverQuery;
    private readonly IProcessTracker _tracker;
    private readonly IProcessMonitor _monitor;

    // Seat → Apollo instance tracking
    private readonly ConcurrentDictionary<Guid, ApolloInstance> _instances = new();

    public ApolloManager(
        ILogger<ApolloManager> logger,
        IOptions<MultiSeatOptions> options,
        ApolloConfigBuilder configBuilder,
        ProcessInjector processInjector,
        Monitoring.ApolloServerQuery serverQuery,
        IProcessTracker tracker,
        IProcessMonitor monitor)
    {
        _logger = logger;
        _options = options.Value;
        _configBuilder = configBuilder;
        _processInjector = processInjector;
        _serverQuery = serverQuery;
        _tracker = tracker;
        _monitor = monitor;
    }

    /// <summary>
    /// True if the Apollo executable exists at the configured path.
    /// </summary>
    public bool IsApolloInstalled => File.Exists(_options.ApolloExePath);

    /// <summary>
    /// Get the number of running Apollo instances.
    /// </summary>
    public int RunningInstanceCount => _instances.Count(i => i.Value.IsAlive);

    /// <summary>
    /// Start an Apollo instance for the given seat.
    /// Generates per-seat config and launches Apollo inside the seat's Windows session.
    /// Returns the Apollo process ID.
    /// </summary>
    public async Task<int> StartAsync(SeatInfo seat, CancellationToken ct)
    {
        if (seat.SessionId < 0)
            throw new InvalidOperationException(
                $"Seat {seat.Id} has no active session (SessionId={seat.SessionId}). " +
                "Launch a Windows session before starting Apollo.");

        if (!IsApolloInstalled)
        {
            _logger.LogWarning(
                "Apollo not found at {Path} — streaming will not work. " +
                "Install Apollo from https://github.com/ClassicOldSong/Apollo",
                _options.ApolloExePath);
            return -1;
        }

        // Generate per-seat configuration file
        var configPath = _configBuilder.BuildConfig(seat, _options.ApolloConfigDir);
        _logger.LogInformation(
            "Seat {Id}: Apollo config generated at {Config}", seat.Id, configPath);

        // Launch Apollo inside the seat's own Windows session.
        // Apollo (SudoMaker fork) connects to the SudoVDA IddCx driver via session-scoped
        // IPC — the SudoVDA watchdog aborts if the connection fails. The IPC works when
        // Apollo runs in the same session as the virtual display, not in the console session.
        // The seat's session was created by SessionLauncher and already has the virtual display.
        var pid = await _processInjector.LaunchApolloInSessionAsync(
            seat.SessionId, seat.AccountName,
            _options.ApolloExePath, configPath, ct);

        if (pid <= 0)
        {
            _logger.LogError("Seat {Id}: Apollo failed to start (PID={Pid})", seat.Id, pid);
            return pid;
        }

        // Obtain actual OS start time for ProcessIdentity (PID reuse protection).
        // If the start time cannot be obtained, the identity is invalid — kill the
        // orphaned process and treat the start as a failure.
        var startedAt = GetProcessStartTime(pid);
        if (startedAt is null)
        {
            _logger.LogError(
                "Seat {Id}: Apollo launched (PID {Pid}) but process start time could not be " +
                "obtained — killing orphaned process", seat.Id, pid);
            KillOrphanedProcess(pid);
            return -1;
        }

        var identity = new ProcessIdentity(pid, startedAt.Value);

        var instance = new ApolloInstance(
            SeatId: seat.Id,
            Identity: identity,
            ProcessId: pid,
            ConfigPath: configPath,
            SessionId: seat.SessionId,
            AccountName: seat.AccountName,
            StartedAt: DateTimeOffset.UtcNow,
            RestartCount: 0);

        _instances[seat.Id] = instance;

        // Register ownership and start lifecycle monitoring
        _tracker.Register(identity, seat.Id, ManagedProcessType.Provider);
        _monitor.StartMonitoring(identity, seat.Id, ManagedProcessType.Provider);

        // A PID is not readiness: the server must answer serverinfo within the startup
        // window, or this is a failed start. Clean up (same shape as the unreadable
        // start-time path above) and report -1 through the existing failure signal —
        // every StartAsync caller already branches on pid <= 0, and nothing throws, so
        // the G2 resolution commit and the health-check -1 branches keep working.
        if (!await WaitForServerReadyAsync(seat, identity, ct))
        {
            _logger.LogWarning(
                "Seat {Id}: Apollo (PID {Pid}) did not answer serverinfo within {Timeout}s — treating start as failed",
                seat.Id, pid, (int)ServerReadyTimeout.TotalSeconds);
            _tracker.Unregister(identity);
            _monitor.StopMonitoring(identity);
            _instances.TryRemove(seat.Id, out _);
            TryKillIdentifiedProcess(identity, "failed start", 3000);
            return -1;
        }

        _logger.LogInformation(
            "Seat {Id}: Apollo started (PID {Pid}) — Moonlight can connect on port {Port}",
            seat.Id, pid, seat.PortBase + 1);

        return pid;
    }

    /// <summary>
    /// Kill the Apollo process if running, but preserve the instance record
    /// (config path, seat ID) so <see cref="RestartAsync"/> can reuse it.
    /// Resets RestartCount to 0 — a sleep/reconnect is not a crash.
    /// Termination is identity-safe (PID + start time): a recycled PID naming an
    /// unrelated process is left alone.
    /// </summary>
    public void KillForReconnect(SeatInfo seat)
    {
        if (!_instances.TryGetValue(seat.Id, out var instance))
            return;

        // Mark expected exit before killing (required by IProcessMonitor contract)
        if (instance.Identity is { } identity)
            _monitor.MarkExpectedExit(identity);

        if (instance.ProcessId > 0)
        {
            // Kill only while this PID still names the recorded Apollo instance. A
            // recycled PID names an unrelated process and is left alone; the stale
            // registration is still cleaned up below so recovery relaunches Apollo.
            TryKillIdentifiedProcess(instance.Identity, "before reconnect", 3000);
        }

        // Clean up ProcessTracking (process is dead)
        if (identity is { } tid)
        {
            _tracker.Unregister(tid);
            _monitor.StopMonitoring(tid);
        }

        // Reset restart count — a sleep reconnect is not a crash
        _instances[seat.Id] = instance with { ProcessId = 0, RestartCount = 0 };
    }

    /// <summary>
    /// Stop the Apollo instance for a seat. Kills the entire process tree
    /// (Apollo spawns encoder sub-processes).
    /// Termination is identity-safe (PID + start time): without an attributable
    /// instance record the PID is left alone — killing a bare PID is PID-reuse
    /// roulette, and teardown's session logoff remains the backstop.
    /// </summary>
    public void Stop(SeatInfo seat)
    {
        _instances.TryGetValue(seat.Id, out var instance);
        var identity = instance?.Identity;

        // Mark expected exit BEFORE killing (required by IProcessMonitor contract)
        if (identity is { } id)
            _monitor.MarkExpectedExit(id);

        _instances.TryRemove(seat.Id, out _);

        if (instance is not null && instance.ProcessId > 0)
        {
            // Kill the recorded identity — not the seat field, which may be stale —
            // and only while the PID still names it.
            TryKillIdentifiedProcess(instance.Identity, "stop", 5000);
        }
        else if (seat.StreamingProcessId > 0)
        {
            _logger.LogWarning(
                "Seat {Id}: no Apollo instance record for PID {Pid} — leaving it alone (PID reuse safety)",
                seat.Id, seat.StreamingProcessId);
        }

        // Clean up ProcessTracking
        if (identity is { } tid)
        {
            _tracker.Unregister(tid);
            _monitor.StopMonitoring(tid);
        }
    }

    /// <summary>
    /// Restart a crashed Apollo instance for a seat.
    /// Called by SessionHealthCheck when it detects Apollo is no longer running.
    /// </summary>
    public async Task<int> RestartAsync(SeatInfo seat, CancellationToken ct)
    {
        if (!_instances.TryGetValue(seat.Id, out var prev))
        {
            _logger.LogWarning(
                "Seat {Id}: no previous Apollo instance to restart", seat.Id);
            return await StartAsync(seat, ct);
        }

        if (prev.RestartCount >= MaxRestartAttempts)
        {
            _logger.LogError(
                "Seat {Id}: Apollo has crashed {Count} times — giving up. " +
                "Check {LogPath} for errors.",
                seat.Id, prev.RestartCount,
                ResolveLogPath(Path.GetDirectoryName(prev.ConfigPath)!));
            return -1;
        }

        _logger.LogWarning(
            "Seat {Id}: restarting Apollo (attempt {N}/{Max})",
            seat.Id, prev.RestartCount + 1, MaxRestartAttempts);

        // Unregister old identity (process already dead — detected by SessionHealthCheck)
        if (prev.Identity is { } oldIdentity)
        {
            _tracker.Unregister(oldIdentity);
            _monitor.StopMonitoring(oldIdentity);
        }

        // Re-use existing config — restart in the seat's own session (same as initial start)
        var pid = await _processInjector.LaunchApolloInSessionAsync(
            seat.SessionId, seat.AccountName,
            _options.ApolloExePath, prev.ConfigPath, ct);

        if (pid > 0)
        {
            // Obtain actual OS start time for new ProcessIdentity.
            // If the start time cannot be obtained, kill the orphaned process.
            var startedAt = GetProcessStartTime(pid);
            if (startedAt is null)
            {
                _logger.LogError(
                    "Seat {Id}: Apollo relaunched (PID {Pid}) but process start time could not be " +
                    "obtained — killing orphaned process", seat.Id, pid);
                KillOrphanedProcess(pid);
                return -1;
            }

            var newIdentity = new ProcessIdentity(pid, startedAt.Value);

            _instances[seat.Id] = prev with
            {
                Identity = newIdentity,
                ProcessId = pid,
                StartedAt = DateTimeOffset.UtcNow,
                RestartCount = prev.RestartCount + 1,
                SessionId = seat.SessionId,
                AccountName = seat.AccountName
            };

            // Register and monitor the new process
            _tracker.Register(newIdentity, seat.Id, ManagedProcessType.Provider);
            _monitor.StartMonitoring(newIdentity, seat.Id, ManagedProcessType.Provider);

            // Same readiness gate as StartAsync: a PID is not readiness. On failure the
            // new process is abandoned and the previous record is restored with an
            // incremented count, so the crash loop stays bounded by MaxRestartAttempts
            // and still parks in Error. Callers already map -1 to their Error branches.
            if (!await WaitForServerReadyAsync(seat, newIdentity, ct))
            {
                _logger.LogWarning(
                    "Seat {Id}: restarted Apollo (PID {Pid}) did not answer serverinfo within {Timeout}s — treating restart as failed",
                    seat.Id, pid, (int)ServerReadyTimeout.TotalSeconds);
                _tracker.Unregister(newIdentity);
                _monitor.StopMonitoring(newIdentity);
                TryKillIdentifiedProcess(newIdentity, "failed restart", 3000);
                _instances[seat.Id] = prev with { RestartCount = prev.RestartCount + 1 };
                return -1;
            }

            seat.StreamingProcessId = pid;
            _logger.LogInformation(
                "Seat {Id}: Apollo restarted (PID {Pid})", seat.Id, pid);
        }

        return pid;
    }

    /// <summary>
    /// Check if Apollo is running for a seat — identity-aware.
    ///
    /// Compares the registered <see cref="ProcessIdentity"/> (PID + start time) against the OS
    /// via the tracker rather than only checking whether the PID exists, so a PID Windows
    /// recycled onto a different process reads as "Apollo is dead" and the health check
    /// restarts it. A raw <c>Process.GetProcessById</c> lookup would answer "alive" for that
    /// unrelated process and the seat would never recover. The record always carries a real
    /// OS identity (a launch whose start time is unreadable fails without registering), so
    /// there is no PID-only fallback — and none is wanted. Read-only: never probes server
    /// readiness (G4 owns that).
    /// </summary>
    public bool IsAlive(Guid seatId)
    {
        if (!_instances.TryGetValue(seatId, out var instance))
            return false;

        // KillForReconnect parks the record at ProcessId 0 while preserving the old identity;
        // a record with no live PID is not alive regardless of what the identity says.
        if (instance.ProcessId <= 0)
            return false;

        return _tracker.IsAlive(instance.Identity);
    }

    /// <summary>
    /// Get the Apollo web UI URL for a seat (HTTPS).
    /// Used by the dashboard for seat management links.
    /// </summary>
    public string? GetWebUiUrl(SeatInfo seat)
    {
        if (seat.StreamingProcessId <= 0 || seat.PortBase <= 0)
            return null;

        // Apollo's 'port' config key = HTTP; HTTPS web UI = port + 1
        var httpsPort = seat.PortBase + 1;
        return $"https://localhost:{httpsPort}";
    }

    /// <summary>
    /// Get the config path for a seat's Apollo instance.
    /// </summary>
    public string? GetConfigPath(Guid seatId)
    {
        return _instances.TryGetValue(seatId, out var instance)
            ? instance.ConfigPath
            : null;
    }

    /// <summary>
    /// Get the restart count for a seat's Apollo instance.
    /// </summary>
    public int GetRestartCount(Guid seatId)
    {
        return _instances.TryGetValue(seatId, out var instance)
            ? instance.RestartCount
            : 0;
    }

    /// <summary>
    /// How long the current Apollo instance for a seat has been running.
    /// Returns null when there is no instance record for the seat.
    /// </summary>
    public TimeSpan? GetUptime(Guid seatId)
    {
        if (!_instances.TryGetValue(seatId, out var instance))
            return null;

        return DateTimeOffset.UtcNow - instance.StartedAt;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CONFIGURATION ORCHESTRATION
    //  These methods route SeatManager's configuration requests through
    //  ApolloManager, keeping Apollo-specific details (sunshine.conf,
    //  sunshine_state.json) out of the orchestration layer.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Update the display output target in the seat's Apollo config.
    /// Called after SudoVDA UUID discovery so Apollo points at the correct virtual display.
    /// </summary>
    public void UpdateDisplayOutput(SeatInfo seat, string displayId)
    {
        var configPath = GetConfigPath(seat.Id);
        if (configPath is not null)
            _configBuilder.UpdateDisplayOutput(configPath, displayId);
    }

    /// <summary>
    /// Regenerate the seat's Apollo config (sunshine.conf) from current seat state.
    /// Called when seat properties change (e.g. resolution) and Apollo must re-read them.
    /// </summary>
    public void RebuildConfig(SeatInfo seat)
    {
        _configBuilder.BuildConfig(seat, _options.ApolloConfigDir);
    }

    /// <summary>
    /// Clean up ephemeral Apollo config files for a seat on teardown.
    /// Removes junction points; preserves sunshine_state.json and TLS credentials.
    /// </summary>
    public void CleanupSeatConfig(SeatInfo seat)
    {
        _configBuilder.CleanupConfig(seat.AccountName, _options.ApolloConfigDir);
    }

    /// <summary>
    /// List Moonlight clients currently paired to this seat.
    /// Reads from sunshine_state.json (Apollo's pairing state file).
    /// </summary>
    public IReadOnlyList<string> GetSeatPairedClients(SeatInfo seat)
    {
        return _configBuilder.GetPairedClients(seat.AccountName, _options.ApolloConfigDir);
    }

    /// <summary>
    /// Remove a single paired Moonlight client from this seat.
    /// Changes take effect after Apollo restarts.
    /// </summary>
    public bool UnpairSeatClient(SeatInfo seat, string clientName)
    {
        return _configBuilder.UnpairClient(seat.AccountName, _options.ApolloConfigDir, clientName);
    }

    /// <summary>
    /// Remove all paired Moonlight clients from this seat.
    /// The server UUID is preserved — clients just need to re-pair.
    /// </summary>
    public void UnpairAllSeatClients(SeatInfo seat)
    {
        _configBuilder.UnpairAllClients(seat.AccountName, _options.ApolloConfigDir);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HEALTH / QUERY ORCHESTRATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Query whether this seat's Apollo instance is reachable and streaming.
    /// Sends the same HTTP serverinfo request a Moonlight client would.
    /// Returns null when the instance did not answer.
    /// </summary>
    public async Task<Monitoring.ApolloServerInfo?> QueryHealthAsync(SeatInfo seat, CancellationToken ct)
    {
        if (seat.StreamingProcessId <= 0 || seat.PortBase <= 0)
            return null;

        return await _serverQuery.QueryAsync(
            seat.PortBase + Shared.Constants.OffsetGfeHttp, ct);
    }

    /// <summary>
    /// Wait until the Apollo server behind <paramref name="seat"/> answers serverinfo,
    /// or the process behind <paramref name="identity"/> dies, or <paramref name="deadline"/>
    /// elapses (default <see cref="ServerReadyTimeout"/>). A process can exist while its
    /// server never comes up (wedged init), so callers must not report startup success
    /// from the PID alone.
    ///
    /// Uses the existing serverinfo probe (same question a Moonlight client asks). The
    /// seat's StreamingProcessId is deliberately NOT consulted — StartAsync calls this
    /// before the caller records the PID — so the endpoint and port math intentionally
    /// mirror <see cref="QueryHealthAsync"/> without its "PID recorded" guard. Liveness
    /// comes from the tracked identity, so a start that dies mid-window fails fast
    /// instead of waiting out the clock. Cancellation propagates (never converted into
    /// a readiness verdict). The deadline override exists for tests; production callers
    /// use the default.
    /// </summary>
    internal async Task<bool> WaitForServerReadyAsync(
        SeatInfo seat, ProcessIdentity identity, CancellationToken ct,
        TimeSpan? deadline = null)
    {
        var effectiveDeadline = deadline ?? ServerReadyTimeout;
        var start = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - start < effectiveDeadline)
        {
            if (!_tracker.IsAlive(identity))
                return false;

            var info = await _serverQuery.QueryAsync(
                seat.PortBase + Shared.Constants.OffsetGfeHttp, ct);
            if (info is not null)
                return true;

            await Task.Delay(ServerReadyPollInterval, ct);
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PROCESS IDENTITY HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Outcome of an identity-checked Apollo termination attempt.
    /// </summary>
    internal enum ApolloKillOutcome
    {
        /// <summary>The PID still named the recorded identity; it was terminated.</summary>
        Killed,
        /// <summary>The PID is gone or the process already exited; nothing to do.</summary>
        AlreadyGone,
        /// <summary>The PID names a different process instance (PID reuse); deliberately untouched.</summary>
        IdentityMismatch,
        /// <summary>The identity matched but termination itself failed.</summary>
        KillFailed,
    }

    /// <summary>
    /// Terminate the process named by <paramref name="identity"/> — but only while that
    /// PID still denotes the exact recorded instance, via <see cref="ProcessIdentity.Matches"/>
    /// (PID + start time, the single source of truth for the comparison).
    ///
    /// A single open handle spans verification through Kill: Windows does not recycle a
    /// PID while handles to its process object are open, so a verified handle cannot
    /// name a different process. The residual race (exit between verify and kill) can
    /// only fail the kill of the RIGHT process, never kill the WRONG one. (The
    /// whole-tree kill below shares the runtime's inherent child-enumeration races
    /// with every other tree kill in the codebase; that is out of scope here.)
    ///
    /// Never throws: every failure maps to an outcome so callers keep their contracts
    /// (idempotent stop, best-effort reconnect kill).
    /// </summary>
    internal ApolloKillOutcome TryKillIdentifiedProcess(
        ProcessIdentity identity, string reason, int waitMs)
    {
        try
        {
            using var proc = Process.GetProcessById(identity.ProcessId);
            if (proc.HasExited)
                return ApolloKillOutcome.AlreadyGone;

            DateTimeOffset startedAt;
            try
            {
                startedAt = proc.StartTime.ToUniversalTime();
            }
            catch (InvalidOperationException)
            {
                // Exited mid-check: fail closed, never kill blind.
                return ApolloKillOutcome.AlreadyGone;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // Start time unreadable (denied): fail closed, never kill blind.
                _logger.LogWarning(ex,
                    "Apollo PID {Pid} start time unreadable during {Reason} — leaving it alone",
                    identity.ProcessId, reason);
                return ApolloKillOutcome.IdentityMismatch;
            }

            if (!identity.Matches(identity.ProcessId, startedAt))
            {
                _logger.LogWarning(
                    "Apollo PID {Pid} no longer names the recorded instance " +
                    "(recorded {Recorded}, current {Current}) — PID was reused; " +
                    "leaving the unrelated process alone ({Reason})",
                    identity.ProcessId, identity.StartedAt, startedAt, reason);
                return ApolloKillOutcome.IdentityMismatch;
            }

            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(waitMs);
            _logger.LogInformation(
                "Apollo PID {Pid} terminated ({Reason})", identity.ProcessId, reason);
            return ApolloKillOutcome.Killed;
        }
        catch (ArgumentException)
        {
            return ApolloKillOutcome.AlreadyGone; // PID free
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error terminating Apollo PID {Pid} ({Reason})", identity.ProcessId, reason);
            return ApolloKillOutcome.KillFailed;
        }
    }

    /// <summary>
    /// Obtain the actual OS process start time for a PID.
    /// Used to construct <see cref="ProcessIdentity"/> for PID-reuse protection.
    /// Returns null if the start time cannot be obtained (process exited, access denied, etc.).
    /// Callers must NOT register a ProcessIdentity with a fallback timestamp — if the real
    /// start time is unavailable, the launched process must be terminated and the start
    /// treated as a failure.
    /// </summary>
    internal static DateTimeOffset? GetProcessStartTime(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.StartTime.ToUniversalTime();
        }
        catch (ArgumentException)
        {
            // PID does not exist — process already exited between launch and now
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process object in invalid state
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied or other OS error
            return null;
        }
    }

    /// <summary>
    /// Kill an orphaned Apollo process that was launched but whose identity could not be
    /// constructed. Called when GetProcessStartTime fails after a successful process launch.
    /// Best-effort: if the kill fails, the process will be detected as an orphan on the
    /// next seat provisioning cycle.
    /// </summary>
    private static void KillOrphanedProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(3000);
            }
        }
        catch (ArgumentException) { } // already exited
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CONSTANTS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maximum number of automatic restart attempts before giving up.
    /// After this many crashes, the seat enters Error state and requires
    /// manual intervention (check Apollo logs).
    /// </summary>
    public const int MaxRestartAttempts = 3;

    /// <summary>
    /// How long a freshly started Apollo has to answer serverinfo before the start
    /// counts as failed. Matches <see cref="Monitoring.SessionHealthCheck.ApolloStartupWindow"/>:
    /// a start that is not serving within the codebase's own startup horizon is a
    /// startup failure by the health check's own classification. The success path
    /// returns on the first answering poll, so only broken starts pay the deadline.
    /// </summary>
    internal static readonly TimeSpan ServerReadyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Delay between serverinfo readiness polls. Local to the G4 readiness wait —
    /// not a general polling primitive (see WaitForServerReadyAsync).
    /// </summary>
    private static readonly TimeSpan ServerReadyPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Get the log file path for a seat's Apollo instance.
    /// </summary>
    public string GetLogPath(string accountName, string configDir)
    {
        var seatDir = Path.Combine(configDir, accountName);
        return ResolveLogPath(seatDir);
    }

    /// <summary>
    /// Resolve the log file a seat's streaming binary is actually writing.
    ///
    /// We ask for <c>&lt;seatDir&gt;/apollo.log</c> via the <c>log_path</c> config key
    /// (see ApolloConfigBuilder), but not every build honours it: Vibepollo ignores
    /// <c>log_path</c> and writes timestamped files to <c>&lt;seatDir&gt;\logs\apollo-&lt;stamp&gt;.log</c>
    /// instead. Hardcoding the requested name meant we read a file that never existed —
    /// which silently disabled SudoVDA display detection (so display isolation was always
    /// skipped) and launch-on-connect.
    ///
    /// So resolve by inspection rather than by assumption: take the newest non-empty
    /// <c>apollo*.log</c> from the seat root or its <c>logs\</c> subdirectory. That covers
    /// both layouts, and follows the current file across restarts and log rotation.
    /// Empty files are skipped deliberately — Vibepollo leaves a 0-byte file in the seat
    /// root while writing the real log under <c>logs\</c>.
    ///
    /// "Non-empty" is decided by <see cref="HasContent"/>, not by the directory entry —
    /// see there for why. Ordering still uses the entry's timestamp, which is safe: a log
    /// created later always sorts above one a previous run finished writing.
    ///
    /// Falls back to the requested path when nothing matches, so callers keep their
    /// existing "log not there yet" behaviour.
    /// </summary>
    public static string ResolveLogPath(string seatDir)
    {
        var requested = Path.Combine(seatDir, "apollo.log");

        try
        {
            FileInfo? newest = null;
            foreach (var dir in new[] { seatDir, Path.Combine(seatDir, "logs") })
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var candidate in new DirectoryInfo(dir).EnumerateFiles("apollo*.log"))
                {
                    if (!HasContent(candidate)) continue;
                    if (newest is null || candidate.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                        newest = candidate;
                }
            }

            if (newest is not null) return newest.FullName;
        }
        catch (IOException) { /* fall through to the requested path */ }
        catch (UnauthorizedAccessException) { /* fall through to the requested path */ }

        return requested;
    }

    /// <summary>
    /// True when a file genuinely holds bytes.
    ///
    /// <see cref="FileInfo.Length"/> reports the cached directory entry, and Windows does
    /// not refresh that on every write to an open file — a log being actively written can
    /// read 0 while holding thousands of bytes. Seen on the host: a live seat log read 0 at
    /// the exact moment the seat was provisioned and 21,144 two minutes later. Trusting it
    /// made <see cref="ResolveLogPath"/> skip the live log and hand callers a stale one from
    /// a previous run, so display detection and launch-on-connect read the wrong file.
    ///
    /// Ask the handle instead — it reports the true size. Share ReadWrite (and Delete)
    /// because the streaming binary holds the log open for writing the whole time.
    /// A file we cannot open is one we could not read later either, so it counts as empty.
    /// </summary>
    private static bool HasContent(FileInfo file)
    {
        try
        {
            using var fs = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return fs.Length > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Parse Apollo's startup log to find the SudoVDA virtual display device UUID.
    ///
    /// Apollo enumerates all displays at startup and writes a JSON block to the log:
    ///   "Currently available display devices:\n[{...}, {...}]"
    ///
    /// Each entry has a "device_id" (UUID like {f0cfefd7-...}) and
    /// "friendly_name". SudoVDA shows up as "VDD by MTT".
    ///
    /// We return device_id (UUID) for output_name. The UUID works reliably at
    /// both startup and stream LAUNCH time in the console session.
    /// The GDI display_name (\\.\DISPLAY37) does NOT work — Apollo falls back
    /// to the primary monitor when given a GDI path as output_name.
    ///
    /// Returns the UUID (e.g. "{f0cfefd7-be89-5733-a759-8fe046803517}") or null if not found.
    ///
    /// The 1000Hz fallback below is deliberately narrow. Inside an RDP-loopback seat the
    /// Microsoft RDP indirect display (RdpIdd) ALSO reports 1000Hz with edid=null and
    /// friendly_name="" — it is indistinguishable from SudoVDA on those fields alone. A
    /// naive "first 1000Hz display" match therefore returns the RDP surface and we hand it
    /// to Apollo as output_name, so the seat streams the host/RDP desktop at its own size
    /// (e.g. 3440x1440) while reporting success. Worse, on a host with no SudoVDA at all
    /// the fallback still "finds" something, masking the real fault.
    ///
    /// What separates them: SudoVDA is an ADDITIONAL display attached alongside the
    /// session's existing desktop, so at parse time it is never the only display and never
    /// the primary one (MultiSeat's display isolation makes it primary later, after this
    /// runs). The RDP surface is the session's primary. So the fallback requires a
    /// non-primary 1000Hz display AND more than one display present; otherwise we return
    /// null and let the caller's "no virtual display" path report the truth.
    /// </summary>
    public string? ParseSudoVdaDisplayId(string logPath)
    {
        if (!File.Exists(logPath)) return null;

        try
        {
            string text;
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();

            var result = ParseSudoVdaDisplayIdFromLogText(text);

            if (result.DeviceId != null)
            {
                if (result.FriendlyName != null)
                    _logger.LogInformation(
                        "Found SudoVDA display in Apollo log: {DeviceId} ({Name})",
                        result.DeviceId, result.FriendlyName);
                else
                    _logger.LogInformation(
                        "Found SudoVDA display by 1000Hz refresh rate (friendly_name was empty): {DeviceId}",
                        result.DeviceId);
                return result.DeviceId;
            }

            if (result.RejectedPrimaryOnly)
                _logger.LogWarning(
                    "No SudoVDA display in Apollo log: {Count} display(s) enumerated and the only " +
                    "1000Hz match was the session's primary display — that is the RDP surface, not " +
                    "a virtual display. Apollo created no virtual display for this seat.",
                    result.DisplayCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing Apollo log for SudoVDA display at {Path}", logPath);
        }

        return null;
    }

    /// <summary>Outcome of parsing Apollo's display-list JSON.</summary>
    /// <param name="DeviceId">The SudoVDA device UUID, or null when none was identified.</param>
    /// <param name="FriendlyName">Set when matched by name; null when matched by the 1000Hz fallback.</param>
    /// <param name="DisplayCount">How many displays Apollo enumerated.</param>
    /// <param name="RejectedPrimaryOnly">
    /// True when the only 1000Hz display was the session primary (the RDP surface) or was the
    /// lone display — i.e. we deliberately declined a match the old code would have accepted.
    /// </param>
    public readonly record struct SudoVdaParseResult(
        string? DeviceId,
        string? FriendlyName,
        int DisplayCount,
        bool RejectedPrimaryOnly);

    /// <summary>
    /// Parse Apollo log text for a SudoVDA display using the <b>last</b> display-enumeration
    /// block. Apollo enumerates displays at startup (first block) and again when a client
    /// connects and creates the virtual display (later blocks). Late detection must read the
    /// most recent block to find a display that did not exist at startup.
    ///
    /// When only the first block is needed (provisioning), use
    /// <see cref="ParseSudoVdaDisplayId"/> or <see cref="ParseSudoVdaDisplayIdFromLogText"/>.
    /// </summary>
    public static SudoVdaParseResult ParseLatestSudoVdaDisplayIdFromLogText(string text)
    {
        const string marker = "Currently available display devices:";
        var last = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (last < 0) return new SudoVdaParseResult(null, null, 0, false);
        return ParseSudoVdaDisplayIdFromLogText(text[last..]);
    }

    /// <summary>
    /// Pure parse of Apollo's "Currently available display devices:" JSON block.
    /// Public and static so the RdpIdd-vs-SudoVDA discrimination can be tested directly —
    /// same rationale as <see cref="ResolveLogPath"/>.
    /// </summary>
    public static SudoVdaParseResult ParseSudoVdaDisplayIdFromLogText(string text)
    {
        var none = new SudoVdaParseResult(null, null, 0, false);
        {
            var marker = "Currently available display devices:";
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return none;

            var jsonStart = text.IndexOf('[', start);
            if (jsonStart < 0) return none;

            // Apollo writes the closing "]" on its own line
            var jsonEnd = text.IndexOf("\n]", jsonStart);
            if (jsonEnd < 0) return none;

            var json = text[jsonStart..(jsonEnd + 2)];

            // Walk through each display entry and find the SudoVDA one.
            // JSON field order: device_id → display_name → edid → friendly_name →
            //                   info { hdr_state, origin_point, primary, refresh_rate,
            //                          resolution, resolution_scale }
            //
            // Primary match: friendly_name contains "VDD", "SudoVDA", or "SudoMaker".
            // Fallback (see the remarks above): a NON-PRIMARY display at 1000Hz, and only
            // when more than one display is present.
            string? currentDeviceId = null;
            int currentRefreshNumerator = 0;
            bool currentIsPrimary = false;
            // "numerator" appears under both refresh_rate and resolution_scale; only the
            // one immediately following a "refresh_rate" key is the refresh rate.
            bool expectRefreshNumerator = false;

            var displayCount = 0;
            string? hz1000Candidate = null;
            var sawPrimaryHz1000 = false;

            // Close out the display entry we just finished parsing.
            void FinalizeEntry()
            {
                if (currentDeviceId == null) return;
                displayCount++;
                if (currentRefreshNumerator != 1000) return;
                if (currentIsPrimary)
                    sawPrimaryHz1000 = true;      // the RDP surface — never SudoVDA
                else
                    hz1000Candidate ??= currentDeviceId;
            }

            foreach (var line in json.Split('\n'))
            {
                var trimmed = line.Trim().TrimEnd(',');

                // New display object — finalize the previous entry first
                var deviceIdMatch = Regex.Match(trimmed,
                    @"""device_id""\s*:\s*""([^""]+)""");
                if (deviceIdMatch.Success)
                {
                    FinalizeEntry();

                    currentDeviceId = deviceIdMatch.Groups[1].Value;
                    currentRefreshNumerator = 0;
                    currentIsPrimary = false;
                    expectRefreshNumerator = false;
                    continue;
                }

                if (currentDeviceId == null) continue;

                // Check friendly_name — allow empty string ([^"]* not [^"]+)
                var nameMatch = Regex.Match(trimmed,
                    @"""friendly_name""\s*:\s*""([^""]*)""");
                if (nameMatch.Success)
                {
                    var friendlyName = nameMatch.Groups[1].Value;
                    if (IsSudoVdaFriendlyName(friendlyName))
                        return new SudoVdaParseResult(
                            currentDeviceId, friendlyName, displayCount + 1, false);
                    continue;
                }

                if (Regex.IsMatch(trimmed, @"""primary""\s*:\s*true"))
                {
                    currentIsPrimary = true;
                    continue;
                }

                if (trimmed.Contains("\"refresh_rate\"", StringComparison.Ordinal))
                {
                    expectRefreshNumerator = true;
                    continue;
                }

                var numeratorMatch = Regex.Match(trimmed, @"""numerator""\s*:\s*(\d+)");
                if (numeratorMatch.Success && expectRefreshNumerator &&
                    int.TryParse(numeratorMatch.Groups[1].Value, out var num))
                {
                    currentRefreshNumerator = num;
                    expectRefreshNumerator = false;
                }
            }

            FinalizeEntry();

            // Require a second display: SudoVDA is attached ALONGSIDE the session desktop,
            // so a lone display can only be that desktop.
            if (hz1000Candidate != null && displayCount > 1)
                return new SudoVdaParseResult(hz1000Candidate, null, displayCount, false);

            // Nothing usable. Flag the case where the old code would have returned the
            // RDP surface, so the caller can say so instead of silently finding nothing.
            return new SudoVdaParseResult(
                null, null, displayCount,
                RejectedPrimaryOnly: sawPrimaryHz1000 || hz1000Candidate != null);
        }
    }

    private static bool IsSudoVdaFriendlyName(string name) =>
        name.Contains("VDD", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SudoVDA", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SudoMaker", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Tracks a running Apollo instance for a seat.
/// </summary>
internal sealed record ApolloInstance(
    Guid SeatId,
    ProcessIdentity Identity,
    int ProcessId,
    string ConfigPath,
    int SessionId,
    string AccountName,
    DateTimeOffset StartedAt,
    int RestartCount)
{
    /// <summary>
    /// Check if the Apollo process is still running — identity-aware.
    ///
    /// Compares the recorded <see cref="ProcessIdentity"/> (PID + start time) against the OS
    /// via <see cref="ProcessIdentity.Matches"/>, the single source of truth for the
    /// comparison (same exact-match discipline as <see cref="IProcessTracker.IsAlive"/>).
    /// A recycled PID naming an unrelated process reads as dead. Read-only: never kills,
    /// never probes server readiness (G4 owns that). Fail-closed and never throws, so
    /// <see cref="ApolloManager.RunningInstanceCount"/> enumeration stays safe.
    /// </summary>
    public bool IsAlive
    {
        get
        {
            if (ProcessId <= 0) return false;
            try
            {
                using var proc = Process.GetProcessById(ProcessId);
                if (proc.HasExited) return false;
                return Identity.Matches(ProcessId, proc.StartTime.ToUniversalTime());
            }
            catch (ArgumentException)
            {
                return false; // PID free
            }
            catch (InvalidOperationException)
            {
                return false; // exited mid-check
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false; // start time unreadable — fail closed, never claim alive
            }
        }
    }
}
