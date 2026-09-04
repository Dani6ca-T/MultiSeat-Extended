using System.Collections.Concurrent;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// Regression tests for the H2 teardown invariant: a failed lifecycle-gate acquisition must
/// never make a live seat disappear from the registry (which previously orphaned its
/// session/Apollo/ports with no recovery path).
///
/// The exercises drive the real production seam — <see cref="SeatManager.TryBeginTeardownAsync"/>
/// — with a bare dictionary + real <see cref="SeatLifecycleGate"/>, because the full
/// TeardownSeatAsync pipeline needs real Windows sessions/accounts. The ordering decision
/// (gate first, remove only once the gate is held) is the part this fix changes, so it is the
/// part pinned here.
/// </summary>
public class SeatManagerTeardownTests
{
    private static SeatInfo Seat(string accountName = "AccountA", SeatStatus status = SeatStatus.Ready) =>
        new() { AccountName = accountName, Status = status };

    [Fact]
    public async Task GateTimeout_DoesNotRemoveSeatFromRegistry()
    {
        var seats = new ConcurrentDictionary<Guid, SeatInfo>();
        var gate = new SeatLifecycleGate();
        var seat = Seat();
        seats[seat.Id] = seat;

        // Another lifecycle operation owns the gate (e.g. a slow recovery or resolution change).
        using (var holder = await gate.AcquireAsync(seat.Id, CancellationToken.None))
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                SeatManager.TryBeginTeardownAsync(
                    seats, gate, seat.Id, TimeSpan.FromMilliseconds(100), CancellationToken.None));

            // The seat must still be registered — no invisible orphan, status untouched.
            Assert.True(seats.ContainsKey(seat.Id));
            Assert.Same(seat, seats[seat.Id]);
            Assert.Equal(SeatStatus.Ready, seat.Status);
        }
    }

    [Fact]
    public async Task SuccessfulTeardown_RemovesSeat()
    {
        var seats = new ConcurrentDictionary<Guid, SeatInfo>();
        var gate = new SeatLifecycleGate();
        var seat = Seat();
        seats[seat.Id] = seat;

        var (removed, lease) = await SeatManager.TryBeginTeardownAsync(
            seats, gate, seat.Id, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Same(seat, removed);
        Assert.NotNull(lease);
        Assert.False(seats.ContainsKey(seat.Id)); // gone once the gate was acquired

        lease!.Dispose();
    }

    [Fact]
    public async Task DoubleTeardown_OnlyOneCallerWins()
    {
        var seats = new ConcurrentDictionary<Guid, SeatInfo>();
        var gate = new SeatLifecycleGate();
        var seat = Seat();
        seats[seat.Id] = seat;

        // Two concurrent teardowns for the same seat: the gate serializes them; the first
        // removes the seat and holds the lease through its teardown (here: release promptly,
        // as production does when the using block exits); the second then acquires the gate,
        // sees the seat gone, and becomes a no-op. Neither double-disposes, and no seat is
        // left behind.
        Task<bool> Attempt() => Task.Run(async () =>
        {
            var (removed, lease) = await SeatManager.TryBeginTeardownAsync(
                seats, gate, seat.Id, TimeSpan.FromSeconds(5), CancellationToken.None);
            if (removed is null)
                return false; // lost the race / seat already gone
            lease!.Dispose(); // production: teardown runs under the lease, then it is disposed
            return true;
        });

        var results = await Task.WhenAll(Attempt(), Attempt());

        Assert.Equal(1, results.Count(won => won)); // exactly one caller won
        Assert.True(seats.IsEmpty);
    }

    [Fact]
    public async Task TeardownRetry_SucceedsAfterGateTimeout()
    {
        var seats = new ConcurrentDictionary<Guid, SeatInfo>();
        var gate = new SeatLifecycleGate();
        var seat = Seat();
        seats[seat.Id] = seat;

        // First attempt times out while another operation holds the gate…
        using (var holder = await gate.AcquireAsync(seat.Id, CancellationToken.None))
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                SeatManager.TryBeginTeardownAsync(
                    seats, gate, seat.Id, TimeSpan.FromMilliseconds(100), CancellationToken.None));
            Assert.True(seats.ContainsKey(seat.Id)); // still registered, recoverable
        }

        // …and once the gate is free, a later teardown succeeds normally.
        var (removed, lease) = await SeatManager.TryBeginTeardownAsync(
            seats, gate, seat.Id, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Same(seat, removed);
        Assert.False(seats.ContainsKey(seat.Id));
        lease!.Dispose();
    }

    [Fact]
    public async Task ProvisionFailureState_DoesNotBreakLaterTeardown()
    {
        var seats = new ConcurrentDictionary<Guid, SeatInfo>();
        var gate = new SeatLifecycleGate();

        // A failed provision parks the seat in Error and leaves it registered (its resources
        // were released by the failure path). Such a seat must still be tear-down-able…
        var failed = Seat(status: SeatStatus.Error);
        seats[failed.Id] = failed;

        var (removed, lease) = await SeatManager.TryBeginTeardownAsync(
            seats, gate, failed.Id, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Same(failed, removed);
        lease!.Dispose();
    }
}
