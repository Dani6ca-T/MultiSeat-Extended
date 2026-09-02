namespace MultiSeat.Shared;

/// <summary>
/// Manages per-seat process groups. Each seat gets its own group that guarantees
/// cleanup of all associated processes when the seat is torn down.
///
/// INVARIANT-1: Each seat has at most one process group.
/// INVARIANT-2: A process group belongs to exactly one seat.
/// INVARIANT-3: Disposing the manager disposes all groups.
/// </summary>
public interface IProcessGroupManager : IDisposable
{
    /// <summary>
    /// Get or create the process group for a seat.
    /// If no group exists for the seat, a new one is created.
    /// </summary>
    IProcessGroup GetOrCreateForSeat(Guid seatId);

    /// <summary>
    /// Get the process group for a seat, or null if none exists.
    /// </summary>
    IProcessGroup? GetForSeat(Guid seatId);

    /// <summary>
    /// Dispose and remove the process group for a seat.
    /// This terminates all processes in the group.
    /// No-op if no group exists for the seat.
    /// </summary>
    void DisposeForSeat(Guid seatId);
}
