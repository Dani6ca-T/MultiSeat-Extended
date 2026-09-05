using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.ProcessTracking;

/// <summary>
/// Best-effort, identity-safe termination of the application processes a seat launched
/// (dashboard "launch" + launch-on-connect apps). Used by seat teardown so cleanup does not
/// depend solely on session logoff.
///
/// PID-REUSE SAFETY: a raw PID is never killed blindly. Each candidate carries a
/// <see cref="ProcessIdentity"/> (PID + start time) captured at launch, and a process is
/// terminated only while the OS still reports that exact process at that PID. If the PID was
/// recycled onto a different process, or the original already exited, nothing is killed —
/// both are treated as successful cleanup (there is nothing of the seat's left to kill).
///
/// The OS probes are injectable so the loop's failure isolation is unit-testable without
/// spawning processes; the production entry point binds them to real Windows calls.
/// </summary>
internal static class LaunchedProcessCleanup
{
    /// <summary>Terminate every listed process whose identity still matches the OS, best-effort.</summary>
    public static void TerminateAll(IEnumerable<ProcessIdentity> identities, ILogger logger) =>
        TerminateAll(identities, IsAliveAndSameProcess, KillProcessTree, logger);

    /// <summary>
    /// Terminate every identity <paramref name="isAliveAndSame"/> admits, using
    /// <paramref name="killTree"/>. Each candidate is independent: a failure terminating one
    /// process is logged and never aborts the remaining candidates or throws to the caller.
    /// </summary>
    internal static void TerminateAll(
        IEnumerable<ProcessIdentity> identities,
        Func<ProcessIdentity, bool> isAliveAndSame,
        Action<ProcessIdentity> killTree,
        ILogger logger)
    {
        foreach (var identity in identities)
        {
            try
            {
                // Not alive, or the PID now belongs to a different process (recycled): there
                // is nothing of the seat's to kill. Both outcomes are successful cleanup.
                if (!isAliveAndSame(identity))
                    continue;

                killTree(identity);
                logger.LogInformation(
                    "Terminated launched app process PID {Pid} (started {Started})",
                    identity.ProcessId, identity.StartedAt);
            }
            catch (Exception ex)
            {
                // Best-effort: log and continue with the remaining candidates. A cleanup
                // failure must never abort teardown or turn into killing another PID.
                logger.LogWarning(ex,
                    "Could not terminate launched app process PID {Pid} (continuing teardown)",
                    identity.ProcessId);
            }
        }
    }

    /// <summary>
    /// True when the OS process at <paramref name="identity"/>'s PID is alive AND was started
    /// at the recorded time — i.e. it is still the exact process instance that was launched.
    /// PID reuse yields false (the original exited; the PID now names a different process).
    /// Mirrors <see cref="WindowsProcessTracker.IsAlive"/>.
    /// </summary>
    internal static bool IsAliveAndSameProcess(ProcessIdentity identity)
    {
        try
        {
            using var proc = Process.GetProcessById(identity.ProcessId);
            return !proc.HasExited && proc.StartTime.ToUniversalTime() == identity.StartedAt;
        }
        catch (ArgumentException) { return false; }         // PID no longer exists — exited
        catch (InvalidOperationException) { return false; } // process object invalid
        catch (Win32Exception) { return false; }            // access denied or OS error
    }

    /// <summary>Kill the process and its descendant tree (best-effort; caller isolates errors).</summary>
    private static void KillProcessTree(ProcessIdentity identity)
    {
        using var proc = Process.GetProcessById(identity.ProcessId);
        if (!proc.HasExited)
        {
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
        }
    }
}
