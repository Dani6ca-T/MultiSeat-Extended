using System.Collections.Concurrent;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Per-seat mutual exclusion for lifecycle-critical operations.
///
/// Anything that mutates <c>seat.SessionId</c>, <c>seat.ApolloProcessId</c>, the Apollo instance
/// record for the seat, or the keep-alive mstsc for the seat's session must run through this gate.
/// Without it those operations interleave: a resolution change racing SessionHealthCheck's
/// automatic recovery leaves an orphaned mstsc on the stale session, an orphaned Apollo, and a
/// lost resolution change. The gate is what makes each compound sequence atomic per seat; the
/// orphan cleanups in SessionLauncher and SessionHealthCheck are the per-call safety net.
///
/// Different seats stay independent, so cross-seat parallelism is preserved. Semaphores are keyed
/// by seat id and never removed — with MaxSeats small (4 by default) that is trivial, and it means
/// a seat torn down and re-provisioned under the same id keeps using the same gate.
///
/// This is purely a mutual-exclusion primitive: it owns no seat state, logs nothing, and makes no
/// lifecycle decisions.
///
/// Ported from @Dani6ca-T's MultiSeat-Extended fork (commit 46fcae7).
/// </summary>
public sealed class SeatLifecycleGate
{
    /// <summary>
    /// Default wait applied when the caller does not specify one. Long enough for a real Apollo
    /// restart plus display isolation, short enough that a stuck holder cannot wedge a manual
    /// operation indefinitely.
    /// </summary>
    public static readonly TimeSpan DefaultAcquisitionTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    /// <summary>
    /// Acquire the per-seat lifecycle semaphore, honouring <paramref name="ct"/>.
    /// Dispose the returned lease (a <c>using</c> block) to release it.
    /// </summary>
    public Task<ILease> AcquireAsync(Guid seatId, CancellationToken ct) =>
        AcquireAsync(seatId, DefaultAcquisitionTimeout, ct);

    /// <summary>
    /// Acquire the per-seat lifecycle semaphore with an explicit timeout.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// The holder did not release within <paramref name="timeout"/>. Distinguished from
    /// cancellation deliberately: a timeout means something is stuck and is worth logging,
    /// whereas cancellation is the service shutting down.
    /// </exception>
    public async Task<ILease> AcquireAsync(Guid seatId, TimeSpan timeout, CancellationToken ct)
    {
        var semaphore = _gates.GetOrAdd(seatId, _ => new SemaphoreSlim(1, 1));

        var acquired = await semaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
        if (!acquired)
            throw new TimeoutException(
                $"Timed out waiting {timeout.TotalSeconds:F0}s for the lifecycle gate on seat {seatId}.");

        return new Lease(semaphore);
    }

    /// <summary>Disposable handle returned by <see cref="AcquireAsync"/>.</summary>
    public interface ILease : IDisposable { }

    private sealed class Lease : ILease
    {
        private readonly SemaphoreSlim _semaphore;
        private int _disposed;

        internal Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            // Only the first Dispose releases. Without this guard a stray double-dispose
            // over-releases the semaphore, letting a second caller in while the original
            // holder still believes it owns the gate — the exact race the gate exists to stop.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _semaphore.Release();
        }
    }
}
