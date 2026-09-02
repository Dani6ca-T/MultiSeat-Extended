using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MultiSeat.Shared;

namespace MultiSeat.Service.ProcessTracking;

/// <summary>
/// Manages per-seat process groups. Each seat gets its own <see cref="WindowsProcessGroup"/>
/// (Job Object with KILL_ON_JOB_CLOSE) that provides conditional process cleanup on seat teardown.
///
/// Thread-safety: All operations are safe to call concurrently from multiple threads.
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for seat-to-group mapping.
///
/// Lifecycle:
///   1. CreateForSeat called during seat provisioning
///   2. Processes assigned via group.AssignProcess() during seat lifetime
///   3. DisposeForSeat called during seat teardown — terminates all assigned processes
///   4. On service shutdown, Dispose() disposes all remaining groups
/// </summary>
public sealed class WindowsProcessGroupManager : IProcessGroupManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<Guid, WindowsProcessGroup> _groups = new();
    private bool _disposed;

    public WindowsProcessGroupManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Get or create the process group for a seat.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
    public IProcessGroup GetOrCreateForSeat(Guid seatId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _groups.GetOrAdd(seatId, _ =>
            new WindowsProcessGroup(_loggerFactory.CreateLogger<WindowsProcessGroup>()));
    }

    /// <summary>
    /// Get the process group for a seat, or null if none exists.
    /// </summary>
    public IProcessGroup? GetForSeat(Guid seatId)
    {
        if (_disposed) return null;
        return _groups.GetValueOrDefault(seatId);
    }

    /// <summary>
    /// Dispose and remove the process group for a seat.
    /// This terminates all processes in the group.
    /// No-op if no group exists for the seat.
    /// </summary>
    public void DisposeForSeat(Guid seatId)
    {
        if (_groups.TryRemove(seatId, out var group))
        {
            group.Dispose();
        }
    }

    /// <summary>
    /// Dispose all process groups. Called on service shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var group in _groups.Values)
        {
            group.Dispose();
        }
        _groups.Clear();
    }
}
