using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Sessions;
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
public sealed class ApolloManager
{
    private readonly ILogger<ApolloManager> _logger;
    private readonly MultiSeatOptions _options;
    private readonly ApolloConfigBuilder _configBuilder;
    private readonly ProcessInjector _processInjector;

    // Seat → Apollo instance tracking
    private readonly ConcurrentDictionary<Guid, ApolloInstance> _instances = new();

    public ApolloManager(
        ILogger<ApolloManager> logger,
        IOptions<MultiSeatOptions> options,
        ApolloConfigBuilder configBuilder,
        ProcessInjector processInjector)
    {
        _logger = logger;
        _options = options.Value;
        _configBuilder = configBuilder;
        _processInjector = processInjector;
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

        var instance = new ApolloInstance(
            SeatId: seat.Id,
            ProcessId: pid,
            ConfigPath: configPath,
            SessionId: seat.SessionId,
            AccountName: seat.AccountName,
            StartedAt: DateTimeOffset.UtcNow,
            RestartCount: 0);

        _instances[seat.Id] = instance;

        _logger.LogInformation(
            "Seat {Id}: Apollo started (PID {Pid}) — Moonlight can connect on port {Port}",
            seat.Id, pid, seat.PortBase + 1);

        return pid;
    }

    /// <summary>
    /// Kill the Apollo process if running, but preserve the instance record
    /// (config path, seat ID) so <see cref="RestartAsync"/> can reuse it.
    /// Resets RestartCount to 0 — a sleep/reconnect is not a crash.
    /// </summary>
    public void KillForReconnect(SeatInfo seat)
    {
        if (!_instances.TryGetValue(seat.Id, out var instance))
            return;

        if (instance.ProcessId > 0)
        {
            try
            {
                var proc = Process.GetProcessById(instance.ProcessId);
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
                _logger.LogInformation(
                    "Seat {Id}: Apollo killed before reconnect (PID {Pid})",
                    seat.Id, instance.ProcessId);
            }
            catch (ArgumentException) { } // already exited
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Seat {Id}: error killing Apollo before reconnect", seat.Id);
            }
        }

        // Reset restart count — a sleep reconnect is not a crash
        _instances[seat.Id] = instance with { ProcessId = 0, RestartCount = 0 };
    }

    /// <summary>
    /// Stop the Apollo instance for a seat. Kills the entire process tree
    /// (Apollo spawns encoder sub-processes).
    /// </summary>
    public void Stop(SeatInfo seat)
    {
        _instances.TryRemove(seat.Id, out _);

        if (seat.ApolloProcessId <= 0) return;

        try
        {
            var proc = Process.GetProcessById(seat.ApolloProcessId);
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(5000);
            }
            _logger.LogInformation("Seat {Id}: Apollo stopped (PID {Pid})",
                seat.Id, seat.ApolloProcessId);
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Seat {Id}: error stopping Apollo", seat.Id);
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

        // Re-use existing config — restart in the seat's own session (same as initial start)
        var pid = await _processInjector.LaunchApolloInSessionAsync(
            seat.SessionId, seat.AccountName,
            _options.ApolloExePath, prev.ConfigPath, ct);

        if (pid > 0)
        {
            _instances[seat.Id] = prev with
            {
                ProcessId = pid,
                StartedAt = DateTimeOffset.UtcNow,
                RestartCount = prev.RestartCount + 1,
                SessionId = seat.SessionId,
                AccountName = seat.AccountName
            };

            seat.ApolloProcessId = pid;
            _logger.LogInformation(
                "Seat {Id}: Apollo restarted (PID {Pid})", seat.Id, pid);
        }

        return pid;
    }

    /// <summary>
    /// Check if Apollo is running for a seat.
    /// </summary>
    public bool IsAlive(Guid seatId)
    {
        if (!_instances.TryGetValue(seatId, out var instance))
            return false;

        return instance.IsAlive;
    }

    /// <summary>
    /// Get the Apollo web UI URL for a seat (HTTPS).
    /// Used by the dashboard for seat management links.
    /// </summary>
    public string? GetWebUiUrl(SeatInfo seat)
    {
        if (seat.ApolloProcessId <= 0 || seat.PortBase <= 0)
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
                    if (candidate.Length == 0) continue;
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

            var marker = "Currently available display devices:";
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;

            var jsonStart = text.IndexOf('[', start);
            if (jsonStart < 0) return null;

            // Apollo writes the closing "]" on its own line
            var jsonEnd = text.IndexOf("\n]", jsonStart);
            if (jsonEnd < 0) return null;

            var json = text[jsonStart..(jsonEnd + 2)];

            // Walk through each display entry and find the SudoVDA one.
            // JSON field order: device_id → display_name → edid → friendly_name → info (numerator)
            //
            // Primary match: friendly_name contains "VDD", "SudoVDA", or "SudoMaker".
            // Fallback: SudoVDA registers at 1000Hz by default. When running inside an RDP
            // session, libdisplaydevice returns friendly_name="" for the virtual display
            // (no EDID, no SetupDi description available in the session context). In that
            // case we fall back to the first display whose refresh rate numerator is 1000.
            string? currentDeviceId = null;
            int currentNumerator = 0;
            string? hz1000Candidate = null;

            foreach (var line in json.Split('\n'))
            {
                var trimmed = line.Trim().TrimEnd(',');

                // New display object — finalize previous 1000Hz candidate if applicable
                var deviceIdMatch = Regex.Match(trimmed,
                    @"""device_id""\s*:\s*""([^""]+)""");
                if (deviceIdMatch.Success)
                {
                    if (currentDeviceId != null && currentNumerator == 1000)
                        hz1000Candidate ??= currentDeviceId;

                    currentDeviceId = deviceIdMatch.Groups[1].Value;
                    currentNumerator = 0;
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
                    {
                        _logger.LogInformation(
                            "Found SudoVDA display in Apollo log: {DeviceId} ({Name})",
                            currentDeviceId, friendlyName);
                        return currentDeviceId;
                    }
                    continue;
                }

                // Track refresh-rate numerator for 1000Hz fallback
                var numeratorMatch = Regex.Match(trimmed, @"""numerator""\s*:\s*(\d+)");
                if (numeratorMatch.Success &&
                    int.TryParse(numeratorMatch.Groups[1].Value, out var num))
                {
                    currentNumerator = Math.Max(currentNumerator, num);
                }
            }

            // Finalize last display entry
            if (currentDeviceId != null && currentNumerator == 1000)
                hz1000Candidate ??= currentDeviceId;

            if (hz1000Candidate != null)
            {
                _logger.LogInformation(
                    "Found SudoVDA display by 1000Hz refresh rate (friendly_name was empty): {DeviceId}",
                    hz1000Candidate);
                return hz1000Candidate;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing Apollo log for SudoVDA display at {Path}", logPath);
        }

        return null;
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
    int ProcessId,
    string ConfigPath,
    int SessionId,
    string AccountName,
    DateTimeOffset StartedAt,
    int RestartCount)
{
    /// <summary>
    /// Check if the Apollo process is still running.
    /// </summary>
    public bool IsAlive
    {
        get
        {
            if (ProcessId <= 0) return false;
            try
            {
                using var proc = Process.GetProcessById(ProcessId);
                return !proc.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
