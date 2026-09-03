using System.Collections.Concurrent;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Per-seat mutual exclusion for lifecycle-critical operations.
///
/// Anything that mutates <c>seat.SessionId</c>, <c>seat.ApolloProcessId</c>, the Apollo instance
/// record for the seat, or the keep-alive mstsc for the seat's session must run through this gate.
/// A successful <see cref="AcquireAsync"/> returns an <see cref="IDisposable"/> lease whose
/// <c>Dispose</c> releases the semaphore; using it with a <c>using</c> block is the intended pattern.
///
/// Different seats are independent: the gate preserves cross-seat parallelism. The semaphore is
/// keyed by <see cref="Guid"/> and never removed; with <c>MaxSeats</c> small (4 by default) the
/// memory footprint is trivial.
///
/// This is purely a mutual-exclusion primitive. It does not own seat state, does not log, does not
/// make lifecycle decisions.
/// </summary>
public sealed class SeatLifecycleGate
{
    /// <summary>
    /// Default timeout applied when the caller does not specify one. Long enough that a real
    /// Apollo restart + display isolation completes comfortably, short enough that a stuck
    /// holder cannot wedge a manual operation indefinitely.
    /// </summary>
    public static readonly TimeSpan DefaultAcquisitionTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    /// <summary>
    /// Wait for and acquire the per-seat lifecycle semaphore, honouring <paramref name="ct"/>.
    ///
    /// On success the returned lease must be disposed (typically via a <c>using</c> block) to
    /// release the semaphore. If cancellation fires before acquisition, no semaphore is held
    /// and the returned lease is safe to dispose (it is a no-op).
    ///
    /// The default acquisition timeout is <see cref="DefaultAcquisitionTimeout"/>. A holder that
    /// does not release within that window causes the next acquisition to throw
    /// <see cref="TimeoutException"/>; the caller should log and decide whether to retry or
    /// surface the failure to the operator.
    /// </summary>
    public Task<ILease> AcquireAsync(Guid seatId, CancellationToken ct) =>
        AcquireAsync(seatId, DefaultAcquisitionTimeout, ct);

    /// <summary>
    /// Wait for and acquire the per-seat lifecycle semaphore, with an explicit acquisition
    /// timeout. See <see cref="AcquireAsync(Guid, CancellationToken)"/> for semantics.
    /// </summary>
    public async Task<ILease> AcquireAsync(Guid seatId, TimeSpan timeout, CancellationToken ct)
    {
        var semaphore = _gates.GetOrAdd(seatId, _ => new SemaphoreSlim(1, 1));

        // WaitAsync(timeout, ct): true = acquired, false = timed out, OperationCanceledException
        // = ct cancelled. We surface timeout as TimeoutException so the caller can distinguish
        // it from cancellation.
        var acquired = await semaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
        if (!acquired)
            throw new TimeoutException(
                $"Timed out waiting {timeout.TotalSeconds:F0}s for lifecycle gate on seat {seatId}.");

        return new Lease(semaphore);
    }

    /// <summary>
    /// Disposable handle returned by <see cref="AcquireAsync"/>. Hides the concrete
    /// implementation so tests can substitute a fake if needed without exposing the semaphore.
    /// </summary>
    public interface ILease : IDisposable { }

    private sealed class Lease : ILease
    {
        private readonly SemaphoreSlim _semaphore;
        private int _disposed;

        internal Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            // Interlocked.Exchange makes repeated Dispose() calls harmless: only the first one
            // releases the semaphore. Without this guard, a stray double-dispose (e.g. a
            // caller wrapping `using` around a method that already disposed the lease) would
            // over-release and let a second waiter acquire the gate while the original holder
            // thought it still held it.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _semaphore.Release();
        }
    }
}