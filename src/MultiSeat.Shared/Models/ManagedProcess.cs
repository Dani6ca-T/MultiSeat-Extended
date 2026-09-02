namespace MultiSeat.Shared.Models;

/// <summary>
/// Represents a managed process with ownership, identity, and type.
///
/// This is a domain-level record. It does NOT hold a <c>System.Diagnostics.Process</c>
/// reference — the process lifecycle is checked via <see cref="IProcessTracker.IsAlive"/>
/// or by querying the OS with the stored <see cref="ProcessIdentity"/>.
///
/// INVARIANT-1: Every ManagedProcess has an owner (SeatId).
/// INVARIANT-2: One process cannot belong to two Seats (enforced by IProcessTracker.Register).
/// INVARIANT-3: ProcessIdentity uses PID + StartedAt to protect against PID reuse.
/// INVARIANT-4: ProcessType is assigned once at registration and never changes.
/// </summary>
public sealed record ManagedProcess
{
    /// <summary>
    /// Unique identity of this process instance (PID + start time).
    /// </summary>
    public required ProcessIdentity Identity { get; init; }

    /// <summary>
    /// The SeatId that owns this process.
    /// Every managed process must have exactly one owner.
    /// </summary>
    public required Guid OwnerSeatId { get; init; }

    /// <summary>
    /// The role this process plays within the seat.
    /// </summary>
    public required ManagedProcessType ProcessType { get; init; }

    /// <summary>
    /// The UTC time when this process was registered with the tracker.
    /// May differ from <see cref="ProcessIdentity.StartedAt"/> if registration
    /// happens after the process is launched (e.g. polling for a known PID).
    /// </summary>
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
}
