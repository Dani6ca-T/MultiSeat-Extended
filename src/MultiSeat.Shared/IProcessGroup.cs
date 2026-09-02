namespace MultiSeat.Shared;

/// <summary>
/// Abstraction for a process group that provides conditional cleanup guarantee.
///
/// On Windows, this wraps a Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
/// When the group is disposed, all successfully assigned processes are terminated.
///
/// CONDITIONAL GUARANTEE: AssignProcess is best-effort. If the target process is already
/// in another Job Object (ERROR_ACCESS_DENIED), assignment fails silently and the process
/// remains outside this group. In that case, cleanup relies on the explicit termination
/// path (e.g. Process.Kill in the provider manager), not on KILL_ON_JOB_CLOSE.
///
/// INVARIANT-1: Each process group belongs to exactly one seat.
/// INVARIANT-2: A process can be assigned to at most one group.
/// INVARIANT-3: Disposing the group terminates all successfully assigned processes.
///
/// This interface lives in MultiSeat.Shared (domain layer) without any Windows
/// dependency. The Windows implementation is in MultiSeat.Service (infrastructure).
/// </summary>
public interface IProcessGroup : IDisposable
{
    /// <summary>
    /// Attempt to assign a process to this group by its process ID.
    /// Best-effort: if the process is already in another Job Object, assignment fails
    /// silently and the process remains outside this group. Cleanup for such processes
    /// relies on the explicit termination path (e.g. Process.Kill).
    /// </summary>
    /// <param name="processId">The PID of the process to assign.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when processId is zero or negative.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the group has been disposed.
    /// </exception>
    void AssignProcess(int processId);
}
