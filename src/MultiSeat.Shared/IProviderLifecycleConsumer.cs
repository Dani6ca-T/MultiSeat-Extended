using MultiSeat.Shared.Models;

namespace MultiSeat.Shared;

/// <summary>
/// Contract for components that consume provider process exit notifications.
///
/// When a monitored provider process exits, the monitor raises ProcessExited.
/// The lifecycle consumer receives this signal and decides whether recovery
/// is needed (unexpected exit) or can be ignored (expected stop).
///
/// ARCHITECTURE:
///   ProcessMonitor.ProcessExited (event-driven signal, immediate)
///       ↓
///   IProviderLifecycleConsumer.HandleProviderExitedAsync (decision point)
///       ↓
///   Expected → ignore
///   Unexpected → recovery signal → future P1 backoff
///
/// This interface lives in MultiSeat.Shared (domain layer) without any Windows
/// dependency. The implementation is in MultiSeat.Service (infrastructure).
/// </summary>
public interface IProviderLifecycleConsumer
{
    /// <summary>
    /// Called when a provider process exits unexpectedly.
    /// The consumer decides whether to restart, enter error state, or ignore.
    /// Expected exits (WasExpected=true) are already filtered by the caller.
    /// </summary>
    /// <param name="exitInfo">Full exit information including identity, exit code, and seat.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleProviderExitedAsync(ProcessExitInfo exitInfo, CancellationToken ct);
}
