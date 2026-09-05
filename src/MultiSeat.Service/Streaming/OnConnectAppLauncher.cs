using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Streaming;

/// <summary>
/// Launches configured apps into a seat's session when a Moonlight client connects,
/// and (optionally) kills them on disconnect.
///
/// Why this exists:
///   Apollo creates the virtual Xbox 360 controller (ViGEm) only while a client is
///   streaming. Game launchers like Steam Big Picture and EmulationStation enumerate
///   controllers at launch and do not reliably hot-plug-detect a pad that appears
///   afterwards. If such a launcher is autostarted at login it runs before any stream
///   and never sees the pad. Launching it AFTER the client connects (when the pad
///   exists) makes its startup scan pick the controller up.
///
/// Detection:
///   Apollo's per-seat log (apollo.log) emits "CLIENT CONNECTED" / "CLIENT DISCONNECTED"
///   lines. This watcher tails that log on each health-check tick, tracks the connected
///   state per seat, and fires on the transitions.
///
/// Configured via MultiSeat:LaunchOnConnect in appsettings.json. Empty = disabled.
/// </summary>
public sealed class OnConnectAppLauncher
{
    private const string ConnectedMarker = "CLIENT CONNECTED";
    private const string DisconnectedMarker = "CLIENT DISCONNECTED";

    // Longest marker we scan for; we retain this-many-minus-one chars between reads so a
    // marker straddling a tick boundary is reassembled on the next read and still detected.
    // internal so tests can compute split points from the real value instead of hardcoding it.
    internal static readonly int MaxMarkerLen =
        Math.Max(ConnectedMarker.Length, DisconnectedMarker.Length);

    private readonly ILogger<OnConnectAppLauncher> _logger;
    private readonly MultiSeatOptions _options;
    private readonly ApolloManager _apollo;
    private readonly ProcessInjector _injector;
    private readonly Func<Guid, int?>? _sessionLookup;

    private readonly ConcurrentDictionary<Guid, SeatConnState> _states = new();

    public OnConnectAppLauncher(
        ILogger<OnConnectAppLauncher> logger,
        IOptions<MultiSeatOptions> options,
        ApolloManager apollo,
        ProcessInjector injector,
        Func<Guid, int?>? sessionLookup = null)
    {
        _logger = logger;
        _options = options.Value;
        _apollo = apollo;
        _injector = injector;
        _sessionLookup = sessionLookup;
    }

    /// <summary>
    /// Inspect a seat's Apollo log for new connect/disconnect events and act on edges.
    /// Cheap and safe to call every health-check tick. No-op when the feature is off.
    /// </summary>
    public void ProcessSeat(SeatInfo seat, CancellationToken ct)
    {
        if (_options.LaunchOnConnect.Length == 0) return; // feature disabled
        if (seat.SessionId < 0) return;

        var logPath = _apollo.GetLogPath(seat.AccountName, _options.ApolloConfigDir);
        if (!File.Exists(logPath)) return;

        var state = _states.GetOrAdd(seat.Id, _ => SeedState(logPath));

        // The resolved log can change under us — Apollo restarting produces a new timestamped
        // file (see ApolloManager.ResolveLogPath). A byte offset into the old file means
        // nothing in the new one, so re-seed against the new file rather than reading from a
        // stale position. ReadLatestState's rotation guard only catches shrinkage, and a new
        // log is usually longer than the old offset, so it would not fire here.
        if (!string.Equals(state.LogPath, logPath, StringComparison.OrdinalIgnoreCase))
        {
            lock (state.Gate)
            {
                state.LogPath = logPath;
                state.Offset  = 0;
                state.Carry   = string.Empty;
            }
        }

        bool? connectedNow = ReadLatestState(logPath, state);
        if (connectedNow is null) return; // no new connect/disconnect lines since last tick

        lock (state.Gate)
        {
            if (connectedNow.Value == state.Connected) return; // no edge
            state.Connected = connectedNow.Value;

            if (connectedNow.Value)
                OnConnect(seat, state, ct);
            else
                OnDisconnect(seat, state);
        }
    }

    /// <summary>
    /// Snapshot of the process identities (PID + start time) of the apps this launcher
    /// started for a seat that are still recorded. Seat teardown calls this BEFORE
    /// <see cref="Forget"/>, so the launched apps can be terminated explicitly instead of
    /// relying on session logoff alone; after Forget the state is gone and this is empty.
    /// </summary>
    public IReadOnlyList<ProcessIdentity> GetLaunchedProcesses(Guid seatId)
    {
        if (!_states.TryGetValue(seatId, out var state)) return [];
        lock (state.Gate) return state.Launched.ToList();
    }

    /// <summary>Drop tracked state for a seat that has been torn down.</summary>
    public void Forget(Guid seatId)
    {
        // Cancel the per-seat CTS FIRST, so any in-flight launch that was waiting on
        // the settle delay observes the cancellation before it can read the launched
        // state (and so the final sessionId check below — when the lookup is supplied —
        // sees the post-teardown session id). Then remove the state.
        if (_states.TryRemove(seatId, out var state))
        {
            try { state.LifecycleCts.Cancel(); } catch { /* already disposed */ }
            state.LifecycleCts.Dispose();
        }
    }

    // ── Edge handlers (called under state.Gate) ──────────────────────────

    private void OnConnect(SeatInfo seat, SeatConnState state, CancellationToken ct)
    {
        // Avoid duplicate launches: skip if a launch is already in flight, or if the
        // apps we launched on a previous connect are still running (KillOnDisconnect=false).
        if (state.Launching)
        {
            _logger.LogDebug("Seat {Id}: launch-on-connect already in progress — skipping", seat.Id);
            return;
        }
        if (AnyTrackedAppAlive(state))
        {
            _logger.LogDebug(
                "Seat {Id}: launch-on-connect apps still running — reusing, not relaunching", seat.Id);
            return;
        }

        state.Launching = true;
        var sessionId = seat.SessionId;
        var account = seat.AccountName;
        var seatId = seat.Id;

        // Link the per-seat lifecycle CTS with the caller-supplied CT so either source
        // (Forget from SeatManager, or service shutdown) cancels the in-flight launch.
        // CTS linked in the parent — disposing per-seat CTS in Forget releases the link.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            state.LifecycleCts.Token, ct);
        var linkedCt = linkedCts.Token;

        // Run detached so the per-launch settle delay never stalls the health-check loop.
        _ = Task.Run(async () =>
        {
            try
            {
                if (_options.LaunchOnConnectDelayMs > 0)
                    await Task.Delay(_options.LaunchOnConnectDelayMs, linkedCt);

                // FINAL VALIDATION before LaunchInSessionAsync: the captured sessionId
                // must still be the seat's current sessionId. Forget has already cancelled
                // the per-seat CTS, so this is the second line of defense for the case
                // where a sessionId-replacement path (SetResolutionAsync, /session-reconnect)
                // did NOT cancel the CTS — the captured id is no longer valid even though
                // the seat is still "live". Skip the launch silently: the next connect
                // edge from the new session will schedule its own launch.
                if (linkedCt.IsCancellationRequested) return;
                if (_sessionLookup is not null)
                {
                    var current = _sessionLookup(seatId);
                    if (current is null || current.Value != sessionId) return;
                }

                foreach (var app in _options.LaunchOnConnect)
                {
                    if (string.IsNullOrWhiteSpace(app.Path)) continue;
                    if (linkedCt.IsCancellationRequested) return;
                    try
                    {
                        var pid = await _injector.LaunchInSessionAsync(
                            sessionId, account, app.Path, app.Arguments, app.WorkingDirectory, linkedCt);
                        if (pid > 0)
                        {
                            // Capture PID + start time so seat teardown can terminate the app
                            // safely against PID reuse (a raw PID could later name an unrelated
                            // process). A start time that cannot be read means the process
                            // already exited — nothing left to track or clean up.
                            var startedAt = ApolloManager.GetProcessStartTime(pid);
                            if (startedAt is not null)
                            {
                                lock (state.Gate)
                                    state.Launched.Add(new ProcessIdentity(pid, startedAt.Value));
                            }
                            _logger.LogInformation(
                                "Seat {Id}: launched on-connect app '{Exe}' (PID {Pid})",
                                seatId, app.Path, pid);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Seat {Id}: failed to launch on-connect app '{Exe}'", seatId, app.Path);
                    }
                }
            }
            catch (OperationCanceledException) { /* shutting down or seat forgotten */ }
            finally
            {
                lock (state.Gate) state.Launching = false;
                linkedCts.Dispose();
            }
        }, linkedCt);
    }

    private void OnDisconnect(SeatInfo seat, SeatConnState state)
    {
        if (!_options.KillLaunchOnConnectAppsOnDisconnect)
        {
            _logger.LogDebug("Seat {Id}: client disconnected — leaving on-connect apps running", seat.Id);
            return;
        }

        foreach (var identity in state.Launched)
        {
            // PID-REUSE SAFETY: kill only while this PID still denotes the exact process
            // instance that was launched (PID + start time match, same check as seat
            // teardown's LaunchedProcessCleanup). A recycled PID names an unrelated
            // process and must never be touched; an exited process needs no kill.
            if (!LaunchedProcessCleanup.IsAliveAndSameProcess(identity))
                continue;
            var pid = identity.ProcessId;
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
                _logger.LogInformation(
                    "Seat {Id}: killed on-connect app (PID {Pid}) after client disconnect", seat.Id, pid);
            }
            catch (ArgumentException) { /* already gone */ }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Seat {Id}: failed to kill on-connect app PID {Pid}", seat.Id, pid);
            }
        }
        state.Launched.Clear();
    }

    // ── Log tailing ──────────────────────────────────────────────────────

    /// <summary>
    /// Seed per-seat state from the existing log so we don't replay historical
    /// connect/disconnect events: start reading at end-of-file, and infer the current
    /// connected state from the last marker already present in the file.
    /// </summary>
    internal static SeatConnState SeedState(string logPath)
    {
        var state = new SeatConnState { LogPath = logPath };
        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var text = ReadAllText(fs);
            state.Offset = fs.Length;
            state.Connected = LastMarkerIsConnected(text) ?? false;
        }
        catch (IOException) { /* leave defaults: offset 0, disconnected */ }
        return state;
    }

    /// <summary>
    /// Read log bytes appended since the last tick and return the seat's connected
    /// state if a connect/disconnect line appeared, otherwise null (no change to report).
    /// </summary>
    internal static bool? ReadLatestState(string logPath, SeatConnState state)
    {
        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = fs.Length;
            if (state.Offset > len) { state.Offset = 0; state.Carry = string.Empty; } // rotated/truncated
            if (len == state.Offset) return null;     // nothing new

            fs.Seek(state.Offset, SeekOrigin.Begin);
            var buffer = new byte[len - state.Offset];
            fs.ReadExactly(buffer, 0, buffer.Length); // exactly len-Offset bytes exist — no partial read
            state.Offset = len;

            // Prepend the carry from the previous tick so a marker split across the boundary is
            // reassembled, then retain a fresh tail for next time.
            var text = state.Carry + Encoding.UTF8.GetString(buffer, 0, buffer.Length);
            state.Carry = text.Length > MaxMarkerLen - 1 ? text[^(MaxMarkerLen - 1)..] : text;

            return LastMarkerIsConnected(text);
        }
        catch (IOException)
        {
            return null; // transient — try again next tick
        }
    }

    /// <summary>
    /// True if the last connect/disconnect marker in <paramref name="text"/> is a
    /// connect, false if a disconnect, null if neither marker is present.
    /// </summary>
    internal static bool? LastMarkerIsConnected(string text)
    {
        var c = text.LastIndexOf(ConnectedMarker, StringComparison.Ordinal);
        var d = text.LastIndexOf(DisconnectedMarker, StringComparison.Ordinal);
        if (c < 0 && d < 0) return null;
        return c > d; // "CLIENT DISCONNECTED" does not contain "CLIENT CONNECTED", so these are distinct
    }

    private static string ReadAllText(FileStream fs)
    {
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return sr.ReadToEnd();
    }

    private static bool AnyTrackedAppAlive(SeatConnState state)
    {
        foreach (var identity in state.Launched)
        {
            try
            {
                using var proc = Process.GetProcessById(identity.ProcessId);
                if (!proc.HasExited) return true;
            }
            catch (ArgumentException) { /* gone */ }
        }
        return false;
    }

    internal sealed class SeatConnState
    {
        public readonly object Gate = new();
        public long Offset;
        public bool Connected;
        public bool Launching;
        // The log file Offset refers to. Reset the offset when this changes.
        public string LogPath = string.Empty;
        // Tail of the previous read, retained so a marker split across a tick boundary is
        // still detected when the next chunk arrives.
        public string Carry = string.Empty;
        // Process identities (PID + start time) of the apps this launcher successfully
        // started for the seat. Seat teardown reads these via GetLaunchedProcesses BEFORE
        // Forget drops the state, so launched apps are terminated explicitly instead of
        // relying on session logoff alone.
        public readonly List<ProcessIdentity> Launched = [];

        // Per-seat cancellation token. Forget() cancels this BEFORE removing the state
        // from the dictionary, so a fire-and-forget Task.Run waiting on the settle delay
        // observes the cancellation before it ever reaches LaunchInSessionAsync.
        // Created in SeedState; disposed in Forget().
        public readonly CancellationTokenSource LifecycleCts = new();
    }
}
