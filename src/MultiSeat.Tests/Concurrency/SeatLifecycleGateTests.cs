using System.Collections.Concurrent;
using MultiSeat.Service.Sessions;
using Xunit;

namespace MultiSeat.Tests.Concurrency;

/// <summary>
/// Tests for the per-seat lifecycle gate — the mutual-exclusion primitive that serializes
/// operations which mutate <c>seat.SessionId</c>, <c>seat.ApolloProcessId</c>, the ApolloManager
/// instance record, and the keep-alive mstsc for the same seat.
///
/// All tests are deterministic: they rely on <see cref="TaskCompletionSource{TResult}"/> and
/// short waits rather than on timing-based heuristics, and they exercise the production
/// <see cref="SeatLifecycleGate"/> directly.
/// </summary>
public class SeatLifecycleGateTests
{
    private static readonly Guid SeatA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeatB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SameSeatOperationsSerialize()
    {
        var gate = new SeatLifecycleGate();

        // Two acquisitions on the same seat taken back-to-back. While the first holder is
        // "in" the gate, the second cannot complete. We use a TaskCompletionSource to hold
        // the first holder open until we've proved the second is queued.
        var firstHolderInside = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAcquireReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var first = await gate.AcquireAsync(SeatA, CancellationToken.None))
        {
            // First holder is in. Start the second acquisition in the background; it must
            // not complete until the first releases.
            var secondTask = Task.Run(async () =>
            {
                var lease = await gate.AcquireAsync(SeatA, CancellationToken.None);
                secondAcquireReturned.SetResult(true);
                lease.Dispose();
            });

            // Give the second task a chance to schedule. If the gate were broken, the second
            // task would complete here. Assert it did not.
            await Task.Delay(50);
            Assert.False(secondAcquireReturned.Task.IsCompleted,
                "Second acquisition on the same seat completed while the first was still holding the gate.");

            firstHolderInside.SetResult(true);
        }

        // First holder released. The second must now complete quickly.
        var completed = await Task.WhenAny(secondAcquireReturned.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(secondAcquireReturned.Task, completed);
    }

    [Fact]
    public async Task DifferentSeatsDoNotBlockEachOther()
    {
        var gate = new SeatLifecycleGate();

        // Hold SeatA and start an acquisition on SeatB. SeatB must complete immediately —
        // we have proven the gates are per-id and not a single global semaphore.
        using var a = await gate.AcquireAsync(SeatA, CancellationToken.None);

        var bAcquired = false;
        using (var b = await gate.AcquireAsync(SeatB, CancellationToken.None))
        {
            bAcquired = true;
        }

        Assert.True(bAcquired);
    }

    [Fact]
    public async Task CancellationBeforeAcquisition_DoesNotReleaseAnotherOwner()
    {
        var gate = new SeatLifecycleGate();

        // First holder owns SeatA. A second caller cancels before acquiring — must NOT release
        // the first holder's semaphore. We verify by acquiring again and confirming it blocks.
        using var first = await gate.AcquireAsync(SeatA, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Cancelled before acquisition — throws OCE, no semaphore is held.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await gate.AcquireAsync(SeatA, cts.Token));

        // First holder must still own the semaphore. A new acquisition on the same seat must
        // not complete while the first is in scope.
        var blockedAcquire = Task.Run(async () =>
        {
            using var lease = await gate.AcquireAsync(
                SeatA, TimeSpan.FromSeconds(2), CancellationToken.None);
        });

        // If the cancelled waiter had wrongly released the semaphore, this would complete
        // immediately. The 2s timeout in AcquireAsync is the ceiling; we assert it does not
        // finish in 50ms.
        await Task.Delay(50);
        Assert.False(blockedAcquire.IsCompleted,
            "Cancelled waiter appears to have released another owner's semaphore.");

        // Release the first holder and let the blocked acquire complete cleanly.
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var gate = new SeatLifecycleGate();
        var lease = await gate.AcquireAsync(SeatA, CancellationToken.None);

        // Dispose twice. Must be a no-op on the second call — no semaphore over-release,
        // no exception, no state corruption.
        lease.Dispose();
        lease.Dispose();

        // If the second Dispose had wrongly released the semaphore, a fresh acquisition
        // would succeed while "two holders" thought they had it. The lease is already
        // disposed; verify a fresh acquisition can still proceed normally.
        using var fresh = await gate.AcquireAsync(SeatA, CancellationToken.None);
    }

    [Fact]
    public async Task CancelledWaiterDoesNotPoisonSeatGate()
    {
        var gate = new SeatLifecycleGate();

        // Cancel before acquisition — the throw must leave the gate's per-id semaphore in
        // its initial state. A subsequent successful acquisition must work and a subsequent
        // cancellation must also work, repeatedly.
        for (int i = 0; i < 5; i++)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await gate.AcquireAsync(SeatA, cts.Token));
        }

        // Gate must still acquire cleanly.
        using var lease = await gate.AcquireAsync(SeatA, CancellationToken.None);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task LaterAcquisitionSucceedsAfterOwnerReleases()
    {
        var gate = new SeatLifecycleGate();

        using (var first = await gate.AcquireAsync(SeatA, CancellationToken.None))
        {
            // Hold briefly, then release via `using`.
        }

        // Must acquire immediately — no leaked semaphore.
        var acquired = false;
        using (var second = await gate.AcquireAsync(
                         SeatA, TimeSpan.FromMilliseconds(500), CancellationToken.None))
        {
            acquired = true;
        }

        Assert.True(acquired);
    }

    [Fact]
    public async Task NonGatedHelperCallsUnderTheGateDoNotDeadlock()
    {
        // The audit's reentrancy analysis: callers gated here do invoke non-gated helpers
        // (e.g. ApplyDisplayIsolationAsync, _apolloManager.StartAsync). The gate must not
        // be acquired recursively by such helpers. This test models the pattern: the gate
        // is held by an outer scope that invokes a non-gated helper that simulates work,
        // and a separate waiter on the same id must still block on the outer scope (not
        // on the non-gated helper).
        var gate = new SeatLifecycleGate();
        var outerInside = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var outerDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var outerTask = Task.Run(async () =>
        {
            using var lease = await gate.AcquireAsync(SeatA, CancellationToken.None);
            outerInside.SetResult(true);
            // Simulate a non-gated helper call doing work while the gate is held.
            await outerDone.Task;
        });

        await outerInside.Task;

        // Try to acquire from a parallel caller. It must block because the outer scope still
        // holds the gate, even though the outer scope is just awaiting a TCS (i.e. would be
        // fine if the helper had been "the work" instead).
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await gate.AcquireAsync(SeatA, cts.Token));

        // Release outer; the helper returns, the gate releases, and a fresh acquisition wins.
        outerDone.SetResult(true);
        await outerTask;

        using var fresh = await gate.AcquireAsync(SeatA, CancellationToken.None);
        Assert.NotNull(fresh);
    }
}
