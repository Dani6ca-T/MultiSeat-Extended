namespace MultiSeat.Shared.Models;

/// <summary>
/// Classifies the role of a managed process within a seat.
///
/// Used by <see cref="ManagedProcess.ProcessType"/> and <see cref="IProcessTracker"/>
/// to distinguish between different kinds of processes when querying ownership.
///
/// INVARIANT: A ManagedProcessType value is assigned once at registration and never changes
/// for the lifetime of that registration.
/// </summary>
public enum ManagedProcessType
{
    /// <summary>
    /// A streaming provider process (e.g. Vibepollo, Apollo, Sunshine).
    /// Exactly one provider process is expected per seat during streaming.
    /// </summary>
    Provider,

    /// <summary>
    /// A game process launched into the seat's session.
    /// Multiple game processes may exist per seat (e.g. launcher + game).
    /// </summary>
    Game,

    /// <summary>
    /// A helper or utility process (e.g. display isolation helper, emulator config seeder).
    /// Typically short-lived and not tracked for crash recovery.
    /// </summary>
    Helper,

    /// <summary>
    /// Any other managed process that does not fit the above categories.
    /// </summary>
    Other
}
