using System.Collections.Concurrent;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.ProcessTracking;

/// <summary>
/// Deterministic concurrency tests for the provider recovery gate.
///
/// PROBLEM: When ProcessExited and HealthCheck fire simultaneously,
/// two concurrent HandleProviderExitedAsync calls could both attempt restart,
/// launching duplicate provider processes.
///
/// FIX: ConcurrentDictionary<Guid, bool> with TryAdd as atomic check-and-set.
/// Only one caller per seat can acquire the gate.
///
/// These tests prove the gate's atomicity without timing-based flakiness.
/// </summary>
public class RecoveryGateTests
{
    /// <summary>
    /// Simulates the recovery gate mechanism from SeatManager.
    /// This is a focused test of the gate's atomicity, not the full SeatManager.
    /// </summary>
    private sealed class RecoveryGate
    {
        private readonly ConcurrentDictionary<Guid, bool> _recoveryInProgress = new();

        /// <summary>
        /// Try to acquire the recovery gate for a seat.
        /// Returns true if acquired (caller should proceed with recovery).
        /// Returns false if already held (caller should skip).
        /// </summary>
        public bool TryAcquire(Guid seatId) => _recoveryInProgress.TryAdd(seatId, true);

        /// <summary>
        /// Release the recovery gate for a seat.
        /// </summary>
        public void Release(Guid seatId) => _recoveryInProgress.TryRemove(seatId, out _);
    }

    [Fact]
    public void SingleAcquire_Succeeds()
    {
        var gate = new RecoveryGate();
        var seatId = Guid.NewGuid();

        Assert.True(gate.TryAcquire(seatId));
        gate.Release(seatId);
    }

    [Fact]
    public void DoubleAcquire_SecondFails()
    {
        var gate = new RecoveryGate();
        var seatId = Guid.NewGuid();

        Assert.True(gate.TryAcquire(seatId));
        Assert.False(gate.TryAcquire(seatId)); // second attempt fails
        gate.Release(seatId);
    }

    [Fact]
    public void AcquireAfterRelease_Succeeds()
    {
        var gate = new RecoveryGate();
        var seatId = Guid.NewGuid();

        Assert.True(gate.TryAcquire(seatId));
        gate.Release(seatId);
        Assert.True(gate.TryAcquire(seatId)); // can acquire again
        gate.Release(seatId);
    }

    [Fact]
    public void DifferentSeats_IndependentGates()
    {
        var gate = new RecoveryGate();
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();

        Assert.True(gate.TryAcquire(seatA));
        Assert.True(gate.TryAcquire(seatB)); // different seat, not blocked
        gate.Release(seatA);
        gate.Release(seatB);
    }

    [Fact]
    public async Task ConcurrentAcquire_ExactlyOneWins()
    {
        // Simulate ProcessExited + HealthCheck firing simultaneously.
        // Both try to acquire the gate for the same seat.
        // Exactly one should succeed.
        var gate = new RecoveryGate();
        var seatId = Guid.NewGuid();

        int acquireCount = 0;
        var barrier = new Barrier(2); // synchronize two concurrent callers
        var tasks = new List<Task>();

        for (int i = 0; i < 2; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                barrier.SignalAndWait(); // ensure both start at the same time
                if (gate.TryAcquire(seatId))
                {
                    Interlocked.Increment(ref acquireCount);
                }
            }));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(1, acquireCount); // exactly one won
    }

    [Fact]
    public async Task ThreeConcurrentTriggers_ExactlyOneWins()
    {
        // Simulate ProcessExited + HealthCheck + manual restart
        var gate = new RecoveryGate();
        var seatId = Guid.NewGuid();

        int acquireCount = 0;
        var barrier = new Barrier(3);
        var tasks = new List<Task>();

        for (int i = 0; i < 3; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                barrier.SignalAndWait();
                if (gate.TryAcquire(seatId))
                {
                    Interlocked.Increment(ref acquireCount);
                }
            }));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(1, acquireCount);
    }

    [Fact]
    public void ConcurrentAcquire_Release_Reacquire()
    {
        // After release, another caller can acquire.
        var gate = new RecoveryGate();
        var seatId = Guid.NewGuid();

        // First acquisition
        Assert.True(gate.TryAcquire(seatId));

        // Second attempt fails
        Assert.False(gate.TryAcquire(seatId));

        // Release
        gate.Release(seatId);

        // Third attempt succeeds
        Assert.True(gate.TryAcquire(seatId));
        gate.Release(seatId);
    }

    [Fact]
    public async Task StressTest_100ConcurrentAcquires()
    {
        // 100 concurrent attempts to acquire the same seat gate.
        // Exactly one should succeed.
        var gate = new RecoveryGate();
        var seatId = Guid.NewGuid();

        int acquireCount = 0;
        var barrier = new Barrier(100);
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                barrier.SignalAndWait();
                if (gate.TryAcquire(seatId))
                {
                    Interlocked.Increment(ref acquireCount);
                }
            }));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(1, acquireCount);
    }

    [Fact]
    public async Task StressTest_MultipleSeatsConcurrent()
    {
        // Multiple seats, each with concurrent recovery attempts.
        // Each seat should have exactly one winner.
        var gate = new RecoveryGate();
        var seatCount = 10;
        var attemptsPerSeat = 20;
        var seats = Enumerable.Range(0, seatCount).Select(_ => Guid.NewGuid()).ToList();

        var winners = new ConcurrentDictionary<Guid, int>();
        var barrier = new Barrier(seatCount * attemptsPerSeat);
        var tasks = new List<Task>();

        foreach (var seatId in seats)
        {
            for (int i = 0; i < attemptsPerSeat; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    if (gate.TryAcquire(seatId))
                    {
                        winners.AddOrUpdate(seatId, 1, (_, count) => count + 1);
                    }
                }));
            }
        }

        await Task.WhenAll(tasks);

        // Each seat should have exactly one winner
        foreach (var seatId in seats)
        {
            Assert.True(winners.TryGetValue(seatId, out var count),
                $"Seat {seatId} should have a winner");
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public void Release_NonExistentSeat_IsNoOp()
    {
        var gate = new RecoveryGate();
        // Should not throw
        gate.Release(Guid.NewGuid());
    }
}
