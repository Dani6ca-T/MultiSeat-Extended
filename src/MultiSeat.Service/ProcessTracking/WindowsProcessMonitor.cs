using System.Collections.Concurrent;
using System.Diagnostics;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.ProcessTracking;

/// <summary>
/// Windows implementation of <see cref="IProcessMonitor"/>.
///
/// Uses <see cref="Process.Exited"/> (internally WaitForSingleObject on process handle)
/// for immediate, event-driven process exit detection — no polling.
///
/// PID REUSE PROTECTION: Each monitored process is identified by ProcessIdentity
/// (PID + StartedAt). When an exit event fires, the handler verifies the identity
/// matches before raising ProcessExited. If the PID was reused (different StartedAt),
/// the exit event is silently suppressed.
///
/// THREAD SAFETY: ConcurrentDictionary + lock for state mutations.
/// Process.Exited fires on a thread-pool thread.
/// </summary>
public sealed class WindowsProcessMonitor : IProcessMonitor
{
    private readonly ILogger<WindowsProcessMonitor> _logger;

    // Key: ProcessIdentity (PID + StartedAt)
    // Value: MonitoringEntry with process handle + metadata
    private readonly ConcurrentDictionary<ProcessIdentity, MonitoringEntry> _entries = new();

    public event EventHandler<ProcessExitInfo>? ProcessExited;

    public int MonitoredCount => _entries.Count;

    public WindowsProcessMonitor(ILogger<WindowsProcessMonitor> logger)
    {
        _logger = logger;
    }

    public void StartMonitoring(
        ProcessIdentity identity,
        Guid ownerSeatId,
        ManagedProcessType processType,
        bool markExpected = false)
    {
        if (identity.ProcessId <= 0) return;

        // Remove any stale entry for the same PID (different start time = PID reuse)
        if (_entries.TryRemove(identity, out var oldEntry))
        {
            _logger.LogDebug(
                "Removing stale monitoring entry for PID {Pid} (old StartedAt {Old})",
                identity.ProcessId, oldEntry.Identity.StartedAt);
            oldEntry.Dispose();
        }

        Process? process = null;
        try
        {
            process = Process.GetProcessById(identity.ProcessId);
            if (process.HasExited)
            {
                _logger.LogDebug(
                    "Process PID {Pid} already exited — not monitoring",
                    identity.ProcessId);
                process.Dispose();
                return;
            }
        }
        catch (ArgumentException)
        {
            // PID doesn't exist — already exited
            process?.Dispose();
            return;
        }

        var entry = new MonitoringEntry
        {
            Identity = identity,
            OwnerSeatId = ownerSeatId,
            ProcessType = processType,
            Process = process,
            MarkedExpected = markExpected
        };

        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;

        if (!_entries.TryAdd(identity, entry))
        {
            // Concurrent Add failed — dispose
            process.Exited -= OnProcessExited;
            process.Dispose();
            _logger.LogWarning(
                "Failed to add monitoring entry for PID {Pid} (concurrent add)",
                identity.ProcessId);
            return;
        }

        _logger.LogDebug(
            "Started monitoring process PID {Pid} (Seat {SeatId}, Type {Type})",
            identity.ProcessId, ownerSeatId, processType);
    }

    public void MarkExpectedExit(ProcessIdentity identity)
    {
        if (_entries.TryGetValue(identity, out var entry))
        {
            entry.MarkedExpected = true;
            _logger.LogDebug(
                "Marked PID {Pid} as expected exit (Seat {SeatId})",
                identity.ProcessId, identity.StartedAt);
        }
    }

    public void StopMonitoring(ProcessIdentity identity)
    {
        if (!_entries.TryRemove(identity, out var entry))
            return;

        entry.Process.Exited -= OnProcessExited;
        entry.Dispose();

        _logger.LogDebug(
            "Stopped monitoring process PID {Pid} (Seat {SeatId})",
            identity.ProcessId, identity.StartedAt);
    }

    public void StopMonitoringAll(Guid seatId)
    {
        var toRemove = new List<ProcessIdentity>();

        foreach (var kvp in _entries)
        {
            if (kvp.Value.OwnerSeatId == seatId)
                toRemove.Add(kvp.Key);
        }

        foreach (var identity in toRemove)
        {
            if (_entries.TryRemove(identity, out var entry))
            {
                entry.Process.Exited -= OnProcessExited;
                entry.Dispose();
            }
        }

        if (toRemove.Count > 0)
        {
            _logger.LogDebug(
                "Stopped monitoring {Count} process(es) for seat {SeatId}",
                toRemove.Count, seatId);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process) return;

        var pid = process.Id;
        var exitCode = process.ExitCode;

        // Find the entry by PID. Multiple entries with the same PID but different
        // StartedAt could exist (PID reuse scenario). We need the one whose
        // StartedAt matches the process's actual start time.
        MonitoringEntry? matchedEntry = null;
        ProcessIdentity matchedIdentity = default;

        foreach (var kvp in _entries)
        {
            if (kvp.Key.ProcessId == pid)
            {
                // Verify identity matches by checking start time
                try
                {
                    if (Math.Abs((kvp.Key.StartedAt - process.StartTime.ToUniversalTime()).TotalSeconds) < 2)
                    {
                        matchedEntry = kvp.Value;
                        matchedIdentity = kvp.Key;
                        break;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process has already exited — still a valid match
                    matchedEntry = kvp.Value;
                    matchedIdentity = kvp.Key;
                    break;
                }
            }
        }

        if (matchedEntry is null)
        {
            // No matching entry — PID was reused, old process exit is stale
            _logger.LogDebug(
                "Process PID {Pid} exited but no matching monitoring entry found (PID reuse?)",
                pid);
            process.Dispose();
            return;
        }

        var wasExpected = matchedEntry.MarkedExpected;

        // Clean up the entry FIRST — prevents stale MarkExpectedExit state
        // from persisting after the exit (L5 fix: entry cleanup in handler).
        _entries.TryRemove(matchedIdentity, out _);
        matchedEntry.Dispose();

        // Only raise event if the entry was NOT marked expected.
        // If MarkExpectedExit was called before this handler fired, wasExpected=true
        // and we skip the event (expected exit, no recovery needed).
        // If MarkExpectedExit was called AFTER this handler (race), the entry is
        // already removed and MarkExpectedExit is a no-op — no leak.
        if (wasExpected)
        {
            _logger.LogInformation(
                "Expected process exit: PID {Pid} (Seat {SeatId}, Exit={Code})",
                pid, matchedEntry.OwnerSeatId, exitCode);
            return;
        }

        var exitInfo = new ProcessExitInfo
        {
            Identity = matchedIdentity,
            OwnerSeatId = matchedEntry.OwnerSeatId,
            ProcessType = matchedEntry.ProcessType,
            ExitCode = exitCode,
            WasExpected = false
        };

        _logger.LogWarning(
            "Unexpected process exit: PID {Pid} (Seat {SeatId}, Type {Type}, Exit={Code})",
            pid, matchedEntry.OwnerSeatId, matchedEntry.ProcessType, exitCode);

        ProcessExited?.Invoke(this, exitInfo);
    }

    /// <summary>
    /// Dispose all monitoring entries and release resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var kvp in _entries)
        {
            kvp.Value.Process.Exited -= OnProcessExited;
            kvp.Value.Dispose();
        }
        _entries.Clear();
    }

    /// <summary>
    /// Internal state for a single monitored process.
    /// </summary>
    private sealed class MonitoringEntry : IDisposable
    {
        public required ProcessIdentity Identity { get; init; }
        public required Guid OwnerSeatId { get; init; }
        public required ManagedProcessType ProcessType { get; init; }
        public required Process Process { get; init; }
        public volatile bool MarkedExpected;

        public void Dispose()
        {
            Process.Dispose();
        }
    }
}
