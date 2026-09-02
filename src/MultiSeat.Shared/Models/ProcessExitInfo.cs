namespace MultiSeat.Shared.Models;

/// <summary>
/// Immutable value object describing a process exit event.
///
/// Used by <see cref="IProcessMonitor"/> to communicate process termination
/// to consumers without exposing System.Diagnostics.Process or Windows APIs.
/// </summary>
public sealed record ProcessExitInfo
{
    /// <summary>
    /// Composite identity of the process that exited (PID + start time).
    /// Used to protect against PID reuse — an exit event for a stale PID
    /// (different StartedAt) is ignored.
    /// </summary>
    public required ProcessIdentity Identity { get; init; }

    /// <summary>
    /// The seat that owned this process.
    /// </summary>
    public required Guid OwnerSeatId { get; init; }

    /// <summary>
    /// The type of process that exited (Provider, Game, Helper, Other).
    /// </summary>
    public required ManagedProcessType ProcessType { get; init; }

    /// <summary>
    /// The Windows process exit code.
    /// 0 = success, non-zero = error/crash.
    /// </summary>
    public required int ExitCode { get; init; }

    /// <summary>
    /// Whether this exit was expected (e.g. Seat.Stop → Provider.Stop).
    /// Expected exits should NOT trigger recovery.
    /// </summary>
    public required bool WasExpected { get; init; }

    /// <summary>
    /// UTC timestamp when the exit was detected.
    /// </summary>
    public DateTimeOffset DetectedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
