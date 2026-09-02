using System.Collections.Concurrent;
using System.Diagnostics;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.ProcessTracking;

/// <summary>
/// Thread-safe implementation of <see cref="IProcessTracker"/> for Windows.
///
/// Tracks managed processes with PID + start time identity to protect against PID reuse.
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for lock-free concurrent access.
///
/// Thread-safety model:
///   - ConcurrentDictionary provides atomic per-key operations.
///   - Per-key value replacement is safe because ProcessIdentity includes start time.
///   - GetAll() and GetByOwner() return snapshots (ToList()), not live views.
///
/// PID reuse protection:
///   When IsAlive() is called, it verifies both:
///   1. The PID exists (Process.GetProcessById does not throw)
///   2. The process start time matches the stored StartedAt
///   If either check fails, the process is considered dead (PID may have been reused).
/// </summary>
public sealed class WindowsProcessTracker : IProcessTracker
{
    // Key: ProcessIdentity (PID + StartedAt)
    // Value: ManagedProcess record
    //
    // We use ProcessIdentity as the key rather than just PID because:
    // - Two different process instances may share a PID over time (reuse)
    // - The composite key naturally handles this: stale entries are keyed by old PID+time
    //   and don't collide with new entries at the same PID but different time.
    private readonly ConcurrentDictionary<ProcessIdentity, ManagedProcess> _processes = new();

    // Secondary index: SeatId -> (ProcessIdentity -> byte) for efficient GetByOwner queries.
    // Uses ConcurrentDictionary instead of ConcurrentBag so individual entries can be
    // removed on Unregister (fixes L2: stale accumulation).
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<ProcessIdentity, byte>> _bySeat = new();

    /// <summary>
    /// Register a process as owned by a seat.
    /// If a process with the same PID but different start time exists (stale), it is replaced.
    ///
    /// INVARIANT-2 enforcement: If the same PID+StartedAt is already registered for a
    /// different seat, throws InvalidOperationException. Same seat = re-registration (overwrite).
    /// Same PID with different start time = PID reuse = replace stale entry.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the same ProcessIdentity is already registered for a different seat.
    /// </exception>
    public void Register(ProcessIdentity identity, Guid ownerSeatId, ManagedProcessType processType)
    {
        // Check for cross-seat conflict before writing.
        // If the identity is already tracked for a different seat, reject (INVARIANT-2).
        if (_processes.TryGetValue(identity, out var existing) &&
            existing.OwnerSeatId != ownerSeatId)
        {
            throw new InvalidOperationException(
                $"Process {identity} is already registered for seat {existing.OwnerSeatId} " +
                $"— cannot register for seat {ownerSeatId} (INVARIANT-2: one process, one seat).");
        }

        var process = new ManagedProcess
        {
            Identity = identity,
            OwnerSeatId = ownerSeatId,
            ProcessType = processType
        };

        _processes[identity] = process;

        var seatDict = _bySeat.GetOrAdd(ownerSeatId, _ => new ConcurrentDictionary<ProcessIdentity, byte>());
        seatDict[identity] = 0;
    }

    /// <summary>
    /// Unregister a process from tracking. No-op if not tracked.
    /// Also removes from the secondary seat index to prevent stale accumulation.
    /// </summary>
    public void Unregister(ProcessIdentity identity)
    {
        if (_processes.TryRemove(identity, out var process))
        {
            // Remove from secondary index to prevent stale identity accumulation (L2).
            if (_bySeat.TryGetValue(process.OwnerSeatId, out var seatDict))
            {
                seatDict.TryRemove(identity, out _);
                // Clean up empty seat entries
                if (seatDict.IsEmpty)
                    _bySeat.TryRemove(process.OwnerSeatId, out _);
            }
        }
    }

    /// <summary>
    /// Unregister all processes owned by a seat.
    /// Called during seat teardown.
    /// </summary>
    public void UnregisterAll(Guid seatId)
    {
        if (_bySeat.TryRemove(seatId, out var seatDict))
        {
            foreach (var identity in seatDict.Keys)
            {
                _processes.TryRemove(identity, out _);
            }
        }
    }

    /// <summary>
    /// Get the tracked process record, or null if not tracked.
    /// </summary>
    public ManagedProcess? Get(ProcessIdentity identity)
    {
        return _processes.GetValueOrDefault(identity);
    }

    /// <summary>
    /// Get all processes owned by a seat.
    /// Returns a snapshot — the list does not reflect subsequent changes.
    /// </summary>
    public IReadOnlyList<ManagedProcess> GetByOwner(Guid seatId)
    {
        if (!_bySeat.TryGetValue(seatId, out var seatDict))
            return [];

        return seatDict.Keys
            .Select(id => _processes.GetValueOrDefault(id))
            .Where(p => p is not null)
            .Cast<ManagedProcess>()
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Get all tracked processes across all seats.
    /// Returns a snapshot.
    /// </summary>
    public IReadOnlyList<ManagedProcess> GetAll()
    {
        return _processes.Values
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Check if a process is alive by verifying PID existence and start time match.
    /// Returns false if the PID was reused (start time differs) or the process has exited.
    /// </summary>
    public bool IsAlive(ProcessIdentity identity)
    {
        try
        {
            using var proc = Process.GetProcessById(identity.ProcessId);
            // Process.GetProcessById can find a process with a recycled PID.
            // Verify start time matches to detect reuse.
            return proc.StartTime.ToUniversalTime() == identity.StartedAt;
        }
        catch (ArgumentException)
        {
            // PID does not exist — process has exited
            return false;
        }
    }
}
