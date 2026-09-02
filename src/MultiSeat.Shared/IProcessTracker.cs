using MultiSeat.Shared.Models;

namespace MultiSeat.Shared;

/// <summary>
/// Centralized interface for tracking process ownership across seats.
///
/// INVARIANT-1: Every registered process has exactly one owner (SeatId).
/// INVARIANT-2: One process cannot belong to two Seats.
/// INVARIANT-3: Process identity uses PID + start time to protect against PID reuse.
///
/// Thread-safety: All implementations must be thread-safe. Multiple async flows
/// (seat start, seat stop, crash detection, cleanup) may call these methods concurrently.
/// </summary>
public interface IProcessTracker
{
    /// <summary>
    /// Register a process as owned by a seat.
    ///
    /// If a process with the same PID but different start time is already registered
    /// (PID reuse), the stale entry is replaced.
    ///
    /// INVARIANT-2 enforcement: If a process with the same PID+StartedAt is already
    /// registered for a different seat, this call throws.
    /// </summary>
    /// <param name="identity">Process identity (PID + start time).</param>
    /// <param name="ownerSeatId">The seat that owns this process.</param>
    /// <param name="processType">The role of the process.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the same PID+StartedAt is registered for a different seat.
    /// </exception>
    void Register(ProcessIdentity identity, Guid ownerSeatId, ManagedProcessType processType);

    /// <summary>
    /// Unregister a process from tracking.
    /// No-op if the process is not currently tracked.
    /// </summary>
    void Unregister(ProcessIdentity identity);

    /// <summary>
    /// Unregister all processes owned by a seat.
    /// Called during seat teardown to ensure no stale entries remain.
    /// </summary>
    void UnregisterAll(Guid seatId);

    /// <summary>
    /// Get the tracked process record, or null if not tracked.
    /// </summary>
    ManagedProcess? Get(ProcessIdentity identity);

    /// <summary>
    /// Get all processes owned by a seat.
    /// </summary>
    IReadOnlyList<ManagedProcess> GetByOwner(Guid seatId);

    /// <summary>
    /// Get all tracked processes across all seats.
    /// </summary>
    IReadOnlyList<ManagedProcess> GetAll();

    /// <summary>
    /// Check if a process is alive by verifying PID existence and start time match.
    /// Returns false if the PID was reused (start time differs) or the process has exited.
    /// </summary>
    bool IsAlive(ProcessIdentity identity);
}
