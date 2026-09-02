using MultiSeat.Shared.Models;

namespace MultiSeat.Shared;

/// <summary>
/// Event-driven process lifecycle monitor.
///
/// Replaces polling-based health checks for process liveness detection.
/// Uses OS-level process exit notification (WaitForSingleObject on process handle)
/// for immediate, efficient detection without polling overhead.
///
/// INVARIANT-1: Every monitored process is identified by ProcessIdentity (PID + StartedAt).
/// INVARIANT-2: Exit events carry full identity, preventing PID-reuse confusion.
/// INVARIANT-3: Expected exits (intentional Stop) are distinguished from unexpected (crash).
/// INVARIANT-4: Monitoring resources are released when StopMonitoring is called or the
///              monitor is disposed.
///
/// Thread-safety: All implementations must be thread-safe.
///
/// This interface lives in MultiSeat.Shared (domain layer) without any Windows dependency.
/// The Windows implementation is in MultiSeat.Service (infrastructure).
/// </summary>
public interface IProcessMonitor : IDisposable
{
    /// <summary>
    /// Raised when a monitored process exits. The event args contain full identity
    /// information to prevent PID-reuse confusion.
    ///
    /// The handler is invoked on a thread-pool thread, not the calling thread.
    /// Consumers must handle concurrency.
    /// </summary>
    event EventHandler<ProcessExitInfo>? ProcessExited;

    /// <summary>
    /// Start monitoring a process for exit.
    ///
    /// The process is identified by its ProcessIdentity (PID + start time).
    /// When the process exits, <see cref="ProcessExited"/> fires with a
    /// <see cref="ProcessExitInfo"/> containing the exit code and identity.
    ///
    /// If the PID is reused (different StartedAt), the exit event for the old
    /// process is suppressed — the identity mismatch prevents confusion.
    /// </summary>
    /// <param name="identity">Composite identity (PID + start time).</param>
    /// <param name="ownerSeatId">The seat that owns this process.</param>
    /// <param name="processType">The type of process being monitored.</param>
    /// <param name="markExpected">
    /// When set to true, the exit will be marked as WasExpected=true in the event.
    /// Used when the caller is about to intentionally kill the process.
    /// </param>
    void StartMonitoring(
        ProcessIdentity identity,
        Guid ownerSeatId,
        ManagedProcessType processType,
        bool markExpected = false);

    /// <summary>
    /// Mark a monitored process as "expected to exit" before killing it.
    ///
    /// This sets a flag so that when the exit event fires, it carries
    /// WasExpected=true. This prevents unnecessary crash recovery.
    ///
    /// Must be called BEFORE the actual kill. If the process already exited
    /// (race), the flag is set but the exit event may already have fired
    /// with WasExpected=false — this is acceptable and documented.
    /// </summary>
    /// <param name="identity">The process identity to mark.</param>
    void MarkExpectedExit(ProcessIdentity identity);

    /// <summary>
    /// Stop monitoring a process. Releases all OS resources (handles, subscriptions).
    ///
    /// Does NOT kill the process. If the process is still running after this call,
    /// it continues to run but is no longer monitored.
    ///
    /// If the process already exited and the exit event hasn't been delivered yet,
    /// this call prevents the event from firing.
    /// </summary>
    /// <param name="identity">The process identity to stop monitoring.</param>
    void StopMonitoring(ProcessIdentity identity);

    /// <summary>
    /// Stop monitoring all processes owned by a seat.
    /// Called during seat teardown.
    /// </summary>
    /// <param name="seatId">The seat whose processes should stop being monitored.</param>
    void StopMonitoringAll(Guid seatId);

    /// <summary>
    /// Get the number of currently monitored processes.
    /// </summary>
    int MonitoredCount { get; }
}
