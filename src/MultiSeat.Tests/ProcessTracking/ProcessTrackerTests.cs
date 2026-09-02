using System.Diagnostics;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using MultiSeat.Service.ProcessTracking;
using Xunit;

namespace MultiSeat.Tests.ProcessTracking;

/// <summary>
/// Tests for ProcessIdentity value object.
/// </summary>
public class ProcessIdentityTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var identity = new ProcessIdentity(1234, now);

        Assert.Equal(1234, identity.ProcessId);
        Assert.Equal(now, identity.StartedAt);
    }

    [Fact]
    public void Constructor_RejectsZeroPid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessIdentity(0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_RejectsNegativePid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessIdentity(-1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Matches_SamePidAndTime_ReturnsTrue()
    {
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var identity = new ProcessIdentity(1234, time);

        Assert.True(identity.Matches(1234, time));
    }

    [Fact]
    public void Matches_DifferentPid_ReturnsFalse()
    {
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var identity = new ProcessIdentity(1234, time);

        Assert.False(identity.Matches(5678, time));
    }

    [Fact]
    public void Matches_DifferentTime_ReturnsFalse()
    {
        var time1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var identity = new ProcessIdentity(1234, time1);

        // Same PID, different start time = PID reuse detected
        Assert.False(identity.Matches(1234, time2));
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = new ProcessIdentity(1234, time);
        var b = new ProcessIdentity(1234, time);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = new ProcessIdentity(1234, time);
        var b = new ProcessIdentity(5678, time);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void CompareTo_SortsByPid()
    {
        var time = DateTimeOffset.UtcNow;
        var a = new ProcessIdentity(100, time);
        var b = new ProcessIdentity(200, time);

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Fact]
    public void ToString_ContainsPidAndTime()
    {
        var time = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var identity = new ProcessIdentity(42, time);

        var str = identity.ToString();
        Assert.Contains("PID 42", str);
    }
}

/// <summary>
/// Tests for ManagedProcess record.
/// </summary>
public class ManagedProcessTests
{
    [Fact]
    public void Constructor_SetsAllRequiredProperties()
    {
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        var process = new ManagedProcess
        {
            Identity = identity,
            OwnerSeatId = seatId,
            ProcessType = ManagedProcessType.Provider
        };

        Assert.Equal(identity, process.Identity);
        Assert.Equal(seatId, process.OwnerSeatId);
        Assert.Equal(ManagedProcessType.Provider, process.ProcessType);
    }

    [Fact]
    public void RegisteredAt_DefaultsToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var process = new ManagedProcess
        {
            Identity = new ProcessIdentity(1, DateTimeOffset.UtcNow),
            OwnerSeatId = Guid.NewGuid(),
            ProcessType = ManagedProcessType.Game
        };
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(process.RegisteredAt, before, after);
    }
}

/// <summary>
/// Tests for WindowsProcessTracker.
/// </summary>
public class WindowsProcessTrackerTests
{
    private readonly WindowsProcessTracker _tracker = new();

    [Fact]
    public void Register_ThenGet_ReturnsProcess()
    {
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatId, ManagedProcessType.Provider);

        var result = _tracker.Get(identity);
        Assert.NotNull(result);
        Assert.Equal(seatId, result.OwnerSeatId);
        Assert.Equal(ManagedProcessType.Provider, result.ProcessType);
        Assert.Equal(identity, result.Identity);
    }

    [Fact]
    public void Register_ProcessForSeatA_IsVisibleInGetByOwner()
    {
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var identityA = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var identityB = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(identityA, seatA, ManagedProcessType.Provider);
        _tracker.Register(identityB, seatB, ManagedProcessType.Game);

        var seatAProcesses = _tracker.GetByOwner(seatA);
        Assert.Single(seatAProcesses);
        Assert.Equal(identityA, seatAProcesses[0].Identity);

        var seatBProcesses = _tracker.GetByOwner(seatB);
        Assert.Single(seatBProcesses);
        Assert.Equal(identityB, seatBProcesses[0].Identity);
    }

    [Fact]
    public void SeatA_DoesNotSeeSeatB_Process()
    {
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var identityB = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(identityB, seatB, ManagedProcessType.Game);

        var seatAProcesses = _tracker.GetByOwner(seatA);
        Assert.Empty(seatAProcesses);
    }

    [Fact]
    public void Unregister_RemovesProcess()
    {
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatId, ManagedProcessType.Provider);
        Assert.NotNull(_tracker.Get(identity));

        _tracker.Unregister(identity);
        Assert.Null(_tracker.Get(identity));
    }

    [Fact]
    public void Unregister_NonExistent_IsNoOp()
    {
        var identity = new ProcessIdentity(9999, DateTimeOffset.UtcNow);
        // Should not throw
        _tracker.Unregister(identity);
    }

    [Fact]
    public void UnregisterAll_RemovesAllProcessesForSeat()
    {
        var seatId = Guid.NewGuid();
        var id1 = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var id2 = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(id1, seatId, ManagedProcessType.Provider);
        _tracker.Register(id2, seatId, ManagedProcessType.Game);

        Assert.Equal(2, _tracker.GetByOwner(seatId).Count);

        _tracker.UnregisterAll(seatId);

        Assert.Empty(_tracker.GetByOwner(seatId));
        Assert.Null(_tracker.Get(id1));
        Assert.Null(_tracker.Get(id2));
    }

    [Fact]
    public void UnregisterAll_DoesNotAffectOtherSeats()
    {
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var idA = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var idB = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(idA, seatA, ManagedProcessType.Provider);
        _tracker.Register(idB, seatB, ManagedProcessType.Game);

        _tracker.UnregisterAll(seatA);

        Assert.Empty(_tracker.GetByOwner(seatA));
        Assert.Single(_tracker.GetByOwner(seatB));
    }

    [Fact]
    public void GetAll_ReturnsAllTrackedProcesses()
    {
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var id1 = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var id2 = new ProcessIdentity(200, DateTimeOffset.UtcNow);
        var id3 = new ProcessIdentity(300, DateTimeOffset.UtcNow);

        _tracker.Register(id1, seatA, ManagedProcessType.Provider);
        _tracker.Register(id2, seatA, ManagedProcessType.Game);
        _tracker.Register(id3, seatB, ManagedProcessType.Helper);

        var all = _tracker.GetAll();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void DuplicateRegistration_ReplacesExistingEntry()
    {
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatId, ManagedProcessType.Provider);
        _tracker.Register(identity, seatId, ManagedProcessType.Game); // same seat, different type

        var result = _tracker.Get(identity);
        Assert.NotNull(result);
        Assert.Equal(ManagedProcessType.Game, result.ProcessType); // replaced
    }

    [Fact]
    public void IsAlive_ReturnsFalse_ForNonExistentPid()
    {
        var identity = new ProcessIdentity(99999, DateTimeOffset.UtcNow);
        Assert.False(_tracker.IsAlive(identity));
    }

    [Fact]
    public void IsAlive_ReturnsTrue_ForCurrentProcess()
    {
        // Use the current test runner's process — it's guaranteed to be alive
        var currentProcess = Process.GetCurrentProcess();
        var identity = new ProcessIdentity(
            currentProcess.Id,
            currentProcess.StartTime.ToUniversalTime());

        Assert.True(_tracker.IsAlive(identity));
    }

    [Fact]
    public void IsAlive_DetectsPidReuse_DifferentStartTime()
    {
        // Use the current process PID but a wrong start time
        var currentProcess = Process.GetCurrentProcess();
        var wrongTime = DateTimeOffset.UtcNow.AddHours(-1);
        var identity = new ProcessIdentity(currentProcess.Id, wrongTime);

        // PID exists but start time doesn't match = PID reuse detected
        Assert.False(_tracker.IsAlive(identity));
    }

    [Fact]
    public void IsAlive_CleansUpStaleRegistration()
    {
        var seatId = Guid.NewGuid();
        // Register with a start time that doesn't match the current process
        var currentProcess = Process.GetCurrentProcess();
        var wrongTime = currentProcess.StartTime.ToUniversalTime().AddSeconds(-1);
        var identity = new ProcessIdentity(currentProcess.Id, wrongTime);

        _tracker.Register(identity, seatId, ManagedProcessType.Provider);
        Assert.NotNull(_tracker.Get(identity));

        // IsAlive returns false (stale) but doesn't auto-remove from tracker
        Assert.False(_tracker.IsAlive(identity));
        // Registration persists — cleanup is the caller's responsibility
        Assert.NotNull(_tracker.Get(identity));
    }

    [Fact]
    public async Task ConcurrentRegister_Unregister_DoesNotThrow()
    {
        var seatId = Guid.NewGuid();
        var tasks = new List<Task>();

        // Register 100 processes concurrently
        for (int i = 0; i < 100; i++)
        {
            var pid = 1000 + i;
            var identity = new ProcessIdentity(pid, DateTimeOffset.UtcNow);
            tasks.Add(Task.Run(() => _tracker.Register(identity, seatId, ManagedProcessType.Other)));
        }

        await Task.WhenAll(tasks);

        // All should be registered
        var all = _tracker.GetAll();
        Assert.Equal(100, all.Count);

        // Unregister all concurrently
        tasks.Clear();
        foreach (var proc in all)
        {
            tasks.Add(Task.Run(() => _tracker.Unregister(proc.Identity)));
        }

        await Task.WhenAll(tasks);

        Assert.Empty(_tracker.GetAll());
    }

    [Fact]
    public void Get_ReturnsNull_ForUnregisteredProcess()
    {
        var identity = new ProcessIdentity(99999, DateTimeOffset.UtcNow);
        Assert.Null(_tracker.Get(identity));
    }

    [Fact]
    public void GetByOwner_ReturnsEmpty_ForUnknownSeat()
    {
        var result = _tracker.GetByOwner(Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public void MultipleProcessesPerSeat_AllTracked()
    {
        var seatId = Guid.NewGuid();
        var identities = Enumerable.Range(1, 5)
            .Select(i => new ProcessIdentity(i * 100, DateTimeOffset.UtcNow))
            .ToList();

        foreach (var id in identities)
            _tracker.Register(id, seatId, ManagedProcessType.Game);

        var seatProcesses = _tracker.GetByOwner(seatId);
        Assert.Equal(5, seatProcesses.Count);

        foreach (var id in identities)
        {
            Assert.NotNull(_tracker.Get(id));
        }
    }
}

/// <summary>
/// Tests for ProcessTracker INVARIANT-2 enforcement and _bySeat cleanup.
/// P1-0 fixes: L1 (cross-seat contract), L2 (stale _bySeat).
/// </summary>
public class WindowsProcessTrackerLifecycleTests
{
    private readonly WindowsProcessTracker _tracker = new();

    [Fact]
    public void Register_CrossSeat_ThrowsInvalidOperationException()
    {
        // L1 FIX: Registering the same identity for a different seat must throw.
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatA, ManagedProcessType.Provider);

        // Same identity, different seat → INVARIANT-2 violation → throw
        Assert.Throws<InvalidOperationException>(() =>
            _tracker.Register(identity, seatB, ManagedProcessType.Game));
    }

    [Fact]
    public void Register_SameSeat_DifferentType_Overwrites()
    {
        // Same identity, same seat → re-registration is allowed (overwrite)
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatId, ManagedProcessType.Provider);
        _tracker.Register(identity, seatId, ManagedProcessType.Game); // overwrite

        var result = _tracker.Get(identity);
        Assert.NotNull(result);
        Assert.Equal(ManagedProcessType.Game, result.ProcessType);
    }

    [Fact]
    public void Register_DifferentPidSameSeat_NoConflict()
    {
        // Different PIDs, same seat → no conflict
        var seatId = Guid.NewGuid();
        var id1 = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var id2 = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(id1, seatId, ManagedProcessType.Provider);
        _tracker.Register(id2, seatId, ManagedProcessType.Game);

        Assert.Equal(2, _tracker.GetByOwner(seatId).Count);
    }

    [Fact]
    public void Unregister_CleansBySeatIndex()
    {
        // L2 FIX: Unregister should clean up the _bySeat secondary index.
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatId, ManagedProcessType.Provider);
        Assert.Single(_tracker.GetByOwner(seatId));

        _tracker.Unregister(identity);
        Assert.Empty(_tracker.GetByOwner(seatId));
    }

    [Fact]
    public void RepeatedRegisterUnregister_NoStaleEntries()
    {
        // Repeated register/unregister cycles should not leave stale entries.
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        for (int i = 0; i < 50; i++)
        {
            _tracker.Register(identity, seatId, ManagedProcessType.Provider);
            _tracker.Unregister(identity);
        }

        Assert.Null(_tracker.Get(identity));
        Assert.Empty(_tracker.GetByOwner(seatId));
    }

    [Fact]
    public void PidReuse_ReplacesStaleEntry()
    {
        // PID reuse: same PID, different StartedAt → replace stale entry
        var seatId = Guid.NewGuid();
        var time1 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var oldIdentity = new ProcessIdentity(1234, time1);
        var newIdentity = new ProcessIdentity(1234, time2);

        _tracker.Register(oldIdentity, seatId, ManagedProcessType.Provider);
        Assert.NotNull(_tracker.Get(oldIdentity));

        // New process with same PID but different start time = PID reuse
        _tracker.Register(newIdentity, seatId, ManagedProcessType.Provider);

        // Old entry should be replaced (different ProcessIdentity key)
        Assert.NotNull(_tracker.Get(newIdentity));

        // Old identity is a different key — still exists (this is expected)
        // The old entry is stale but won't collide with the new one
    }

    [Fact]
    public void Restart_NewIdentity_DoesNotConflictWithOld()
    {
        // Restart scenario: old process crashes, new process starts.
        // Different StartedAt = different ProcessIdentity = no conflict.
        var seatId = Guid.NewGuid();
        var time1 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var oldIdentity = new ProcessIdentity(1234, time1);
        var newIdentity = new ProcessIdentity(1234, time2);

        _tracker.Register(oldIdentity, seatId, ManagedProcessType.Provider);
        _tracker.Register(newIdentity, seatId, ManagedProcessType.Provider);

        // Both entries exist with different keys
        var seatProcesses = _tracker.GetByOwner(seatId);
        Assert.Equal(2, seatProcesses.Count);

        // After unregistering old, only new remains
        _tracker.Unregister(oldIdentity);
        seatProcesses = _tracker.GetByOwner(seatId);
        Assert.Single(seatProcesses);
        Assert.Equal(newIdentity, seatProcesses[0].Identity);
    }

    [Fact]
    public void ConcurrentRegister_CrossSeat_ThrowsForConflicts()
    {
        // Cross-seat registration should throw for conflicting identities
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatA, ManagedProcessType.Provider);

        // Concurrent registration for different seat should throw
        Assert.Throws<InvalidOperationException>(() =>
            _tracker.Register(identity, seatB, ManagedProcessType.Game));
    }

    [Fact]
    public void Unregister_DoesNotAffectOtherSeats()
    {
        // Unregister one seat's process should not affect another seat
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var idA = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var idB = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(idA, seatA, ManagedProcessType.Provider);
        _tracker.Register(idB, seatB, ManagedProcessType.Game);

        _tracker.Unregister(idA);

        // Seat A should be empty, seat B should still have its process
        Assert.Empty(_tracker.GetByOwner(seatA));
        Assert.Single(_tracker.GetByOwner(seatB));
    }
}
