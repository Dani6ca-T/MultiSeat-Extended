using MultiSeat.Service.Sessions;
using Xunit;

namespace MultiSeat.Tests.Concurrency;

/// <summary>
/// Tests for the per-seat lifecycle gate — the mutual-exclusion primitive that serializes
/// operations mutating <c>seat.SessionId</c>, <c>seat.ApolloProcessId</c>, the ApolloManager
/// instance record, and the keep-alive mstsc for the same seat.
///
/// Ported from @Dani6ca-T's MultiSeat-Extended fork (commit 46fcae7).
/// </summary>
public class SeatLifecycleGateTests
{
    private static readonly Guid SeatA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeatB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SameSeatOperationsSerialize()
    {
        var gate = new SeatLifecycleGate();
        var secondAcquireReturned =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (await gate.AcquireAsync(SeatA, CancellationToken.None))
        {
            _ = Task.Run(async () =>
            {
                var lease = await gate.AcquireAsync(SeatA, CancellationToken.None);
                secondAcquireReturned.SetResult(true);
                lease.Dispose();
            });

            // If the gate were broken the second acquisition would complete here.
            await Task.Delay(50);
            Assert.False(secondAcquireReturned.Task.IsCompleted,
                "second acquisition completed while the first still held the gate");
        }

        // Released — the queued waiter must now get in.
        var completed = await Task.WhenAny(
            secondAcquireReturned.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(secondAcquireReturned.Task, completed);
    }

    [Fact]
    public async Task DifferentSeatsDoNotBlockEachOther()
    {
        var gate = new SeatLifecycleGate();

        // Holding seat A must not delay seat B — proves the semaphore is per-id and not global,
        // which is what preserves cross-seat parallelism on a multi-seat host.
        using var a = await gate.AcquireAsync(SeatA, CancellationToken.None);

        var bAcquired = false;
        using (await gate.AcquireAsync(SeatB, CancellationToken.None))
            bAcquired = true;

        Assert.True(bAcquired);
    }

    [Fact]
    public async Task CancellationBeforeAcquisition_DoesNotReleaseAnotherOwner()
    {
        var gate = new SeatLifecycleGate();
        using var first = await gate.AcquireAsync(SeatA, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await gate.AcquireAsync(SeatA, cts.Token));

        // A cancelled waiter never held the semaphore, so it must not have released the owner's.
        var blocked = Task.Run(async () =>
        {
            using var lease = await gate.AcquireAsync(
                SeatA, TimeSpan.FromSeconds(2), CancellationToken.None);
        });

        await Task.Delay(50);
        Assert.False(blocked.IsCompleted,
            "a cancelled waiter released another owner's semaphore");
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var gate = new SeatLifecycleGate();
        var lease = await gate.AcquireAsync(SeatA, CancellationToken.None);

        // A double dispose must not over-release: that would let a second caller in while the
        // original holder still believed it owned the gate.
        lease.Dispose();
        lease.Dispose();

        using var fresh = await gate.AcquireAsync(
            SeatA, TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.NotNull(fresh);
    }

    [Fact]
    public async Task CancelledWaiterDoesNotPoisonSeatGate()
    {
        var gate = new SeatLifecycleGate();

        for (var i = 0; i < 5; i++)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await gate.AcquireAsync(SeatA, cts.Token));
        }

        using var lease = await gate.AcquireAsync(SeatA, CancellationToken.None);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task LeaseReleasesOnScopeExit()
    {
        var gate = new SeatLifecycleGate();

        using (await gate.AcquireAsync(SeatA, CancellationToken.None)) { }

        // A short timeout is the assertion: a leaked semaphore would make this throw.
        using var second = await gate.AcquireAsync(
            SeatA, TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task AcquisitionTimesOutRatherThanHangingForever()
    {
        var gate = new SeatLifecycleGate();
        using var held = await gate.AcquireAsync(SeatA, CancellationToken.None);

        // A stuck holder must surface as TimeoutException — distinct from cancellation, so a
        // caller can tell "something is wedged" from "the service is shutting down".
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await gate.AcquireAsync(SeatA, TimeSpan.FromMilliseconds(100), CancellationToken.None));
    }

    [Fact]
    public async Task HelpersCalledUnderTheGateDoNotDeadlock()
    {
        // Gated callers invoke non-gated helpers (ApplyDisplayIsolationAsync,
        // ApolloManager.StartAsync). Those helpers must never re-acquire the gate: the
        // semaphore is not reentrant, so a nested acquire would deadlock the seat.
        var gate = new SeatLifecycleGate();
        var outerInside = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOuter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var outer = Task.Run(async () =>
        {
            using var lease = await gate.AcquireAsync(SeatA, CancellationToken.None);
            outerInside.SetResult(true);
            await releaseOuter.Task;   // stands in for helper work done under the gate
        });

        await outerInside.Task;

        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await gate.AcquireAsync(SeatA, cts.Token));
        }

        releaseOuter.SetResult(true);
        await outer;

        using var fresh = await gate.AcquireAsync(SeatA, CancellationToken.None);
        Assert.NotNull(fresh);
    }
}
