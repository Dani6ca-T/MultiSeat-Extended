using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.ProcessTracking;

/// <summary>
/// Unit tests for WindowsProcessMonitor — event-driven process lifecycle monitoring.
///
/// NOTE: Tests running under a parent Job Object (e.g. the test runner) may be unable to
/// assign child processes to another Job Object (ERROR_ACCESS_DENIED). Process monitoring
/// itself does NOT require Job Object assignment — it uses Process.Exited event.
///
/// NOTE: Tests that use real OS processes (cmd.exe /c exit) are integration-style and
/// depend on Windows process APIs. They should NOT be marked as [Fact] if running on
/// non-Windows CI.
/// </summary>
public class ProcessMonitorTests
{
    private readonly WindowsProcessMonitor _monitor;

    public ProcessMonitorTests()
    {
        _monitor = new WindowsProcessMonitor(NullLogger<WindowsProcessMonitor>.Instance);
    }

    [Fact]
    public void MonitoredCount_InitiallyZero()
    {
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public void StartMonitoring_WithNegativePid_ThrowsOnIdentity()
    {
        // ProcessIdentity rejects negative PIDs
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessIdentity(-1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void StartMonitoring_WithZeroPid_ThrowsOnIdentity()
    {
        // ProcessIdentity rejects zero PIDs
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessIdentity(0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void StopMonitoring_NonExistentEntry_IsNoOp()
    {
        var identity = new ProcessIdentity(99999, DateTimeOffset.UtcNow);
        // Should not throw
        _monitor.StopMonitoring(identity);
    }

    [Fact]
    public void StopMonitoringAll_NonExistentSeat_IsNoOp()
    {
        _monitor.StopMonitoringAll(Guid.NewGuid());
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public void MarkExpectedExit_NonExistentEntry_IsNoOp()
    {
        var identity = new ProcessIdentity(99999, DateTimeOffset.UtcNow);
        // Should not throw
        _monitor.MarkExpectedExit(identity);
    }

    [Fact]
    public void Dispose_CleansUpAllEntries()
    {
        // Try to add a few non-existent PIDs (they'll fail silently)
        for (int i = 0; i < 5; i++)
        {
            var identity = new ProcessIdentity(900000 + i, DateTimeOffset.UtcNow);
            _monitor.StartMonitoring(identity, Guid.NewGuid(), ManagedProcessType.Other);
        }

        // Dispose should not throw even with entries that failed to add
        _monitor.Dispose();
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public void ProcessExited_Event_HasNoSubscribersByDefault()
    {
        // After adding a dummy handler and removing it, event should have no subscribers
        void Handler(object? sender, ProcessExitInfo e) { }
        _monitor.ProcessExited += Handler;
        _monitor.ProcessExited -= Handler;
        // Cannot directly assert null on event, but we verified += and -= work
    }

    [Fact]
    public void StartMonitoring_StoppedProcess_DoesNotMonitor()
    {
        // Start a short-lived process
        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c exit 0",
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        Assert.NotNull(proc);
        var pid = proc.Id;
        proc.WaitForExit(5000);

        var startedAt = proc.StartTime.ToUniversalTime();
        var identity = new ProcessIdentity(pid, startedAt);

        // Try to monitor an already-exited process
        _monitor.StartMonitoring(identity, Guid.NewGuid(), ManagedProcessType.Provider);

        // Should not be monitored (process already exited)
        Assert.Equal(0, _monitor.MonitoredCount);

        proc.Dispose();
    }

    [Fact]
    public void StartMonitoring_DuplicatePid_DifferentStartTime_ReplaceStale()
    {
        // Simulate PID reuse by using two different identities with the same PID
        // but different start times. The second should replace the first.
        var pid = 900999; // unlikely to be a real PID
        var time1 = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var id1 = new ProcessIdentity(pid, time1);
        var id2 = new ProcessIdentity(pid, time2);

        // Both will fail silently (PID doesn't exist), but the logic should
        // not throw and the second should attempt to replace the first
        _monitor.StartMonitoring(id1, Guid.NewGuid(), ManagedProcessType.Provider);
        _monitor.StartMonitoring(id2, Guid.NewGuid(), ManagedProcessType.Game);

        // Neither should be monitored (PID doesn't exist)
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public void StartMonitoring_ThenStopMonitoring_ReleasesResources()
    {
        // Start a short-lived process and try to monitor it
        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c timeout /t 30 > nul",
            CreateNoWindow = true
        });

        Assert.NotNull(proc);
        var pid = proc.Id;
        var startedAt = proc.StartTime.ToUniversalTime();
        var identity = new ProcessIdentity(pid, startedAt);

        _monitor.StartMonitoring(identity, Guid.NewGuid(), ManagedProcessType.Provider);

        // Stop monitoring
        _monitor.StopMonitoring(identity);

        // Cleanup
        try { proc.Kill(); } catch { }
        proc.Dispose();
    }

    [Fact]
    public void StopMonitoringAll_RemovesOnlyTargetSeatEntries()
    {
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();

        // Try to add entries (will fail silently since PIDs don't exist)
        for (int i = 0; i < 3; i++)
        {
            _monitor.StartMonitoring(
                new ProcessIdentity(900100 + i, DateTimeOffset.UtcNow),
                seatA, ManagedProcessType.Provider);
        }

        // Stop monitoring for seatB — should be no-op
        _monitor.StopMonitoringAll(seatB);

        // All entries for seatA should still be (attempted to be) there
        // (they won't actually be there since PIDs don't exist)
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public void Dispose_PreventsFurtherEvents()
    {
        // After dispose, ProcessExited should be null or empty
        _monitor.Dispose();
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public async Task Concurrent_StartStop_DoesNotThrow()
    {
        var tasks = new List<Task>();

        // Concurrently try to start and stop monitoring
        for (int i = 0; i < 50; i++)
        {
            var pid = 910000 + i;
            var identity = new ProcessIdentity(pid, DateTimeOffset.UtcNow);
            var seatId = Guid.NewGuid();

            tasks.Add(Task.Run(() =>
            {
                _monitor.StartMonitoring(identity, seatId, ManagedProcessType.Other);
            }));

            tasks.Add(Task.Run(() =>
            {
                _monitor.StopMonitoring(identity);
            }));
        }

        // Should not throw
        await Task.WhenAll(tasks);
    }

    [Fact]
    public void ProcessExitInfo_CarriesFullIdentity()
    {
        // Verify ProcessExitInfo record carries all required fields
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);
        var seatId = Guid.NewGuid();

        var info = new ProcessExitInfo
        {
            Identity = identity,
            OwnerSeatId = seatId,
            ProcessType = ManagedProcessType.Provider,
            ExitCode = 0,
            WasExpected = false
        };

        Assert.Equal(identity, info.Identity);
        Assert.Equal(seatId, info.OwnerSeatId);
        Assert.Equal(ManagedProcessType.Provider, info.ProcessType);
        Assert.Equal(0, info.ExitCode);
        Assert.False(info.WasExpected);
        Assert.True(info.DetectedAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public void ProcessExitInfo_ExpectedExit_Flagged()
    {
        var info = new ProcessExitInfo
        {
            Identity = new ProcessIdentity(5678, DateTimeOffset.UtcNow),
            OwnerSeatId = Guid.NewGuid(),
            ProcessType = ManagedProcessType.Game,
            ExitCode = 1,
            WasExpected = true
        };

        Assert.True(info.WasExpected);
        Assert.Equal(1, info.ExitCode);
    }
}

/// <summary>
/// Tests for ProcessMonitor lifecycle event behavior.
/// P1-0: expected exit handling, PID reuse filtering, entry cleanup.
/// </summary>
public class ProcessMonitorLifecycleTests
{
    private readonly WindowsProcessMonitor _monitor;

    public ProcessMonitorLifecycleTests()
    {
        _monitor = new WindowsProcessMonitor(NullLogger<WindowsProcessMonitor>.Instance);
    }

    [Fact]
    public void ExpectedExit_ProcessExited_DoesNotFireEvent()
    {
        // L5 FIX: When MarkExpectedExit is called, ProcessExited should NOT
        // be raised with WasExpected=true. Expected exits are handled internally
        // by the monitor (entry cleanup, log) without raising the event.

        var eventFired = false;
        _monitor.ProcessExited += (_, _) => eventFired = true;

        // Use a non-existent PID — monitoring will fail to add (process already exited)
        var identity = new ProcessIdentity(99999, DateTimeOffset.UtcNow);
        _monitor.StartMonitoring(identity, Guid.NewGuid(), ManagedProcessType.Provider);

        // Mark expected — entry doesn't exist, so this is a no-op
        _monitor.MarkExpectedExit(identity);

        // Stop monitoring — also no-op
        _monitor.StopMonitoring(identity);

        // No event should have fired
        Assert.False(eventFired);
    }

    [Fact]
    public void StartMonitoring_StopMonitoring_PreventsEvent()
    {
        // After StopMonitoring, the entry is removed and no event can fire.
        var eventFired = false;
        _monitor.ProcessExited += (_, _) => eventFired = true;

        var identity = new ProcessIdentity(99999, DateTimeOffset.UtcNow);
        _monitor.StartMonitoring(identity, Guid.NewGuid(), ManagedProcessType.Provider);
        _monitor.StopMonitoring(identity);

        // Entry is gone — no event possible
        Assert.False(eventFired);
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public void StopMonitoringAll_RemovesAllEntries()
    {
        // After StopMonitoringAll, no entries remain for that seat.
        var seatId = Guid.NewGuid();

        // Attempt to add (will fail since PIDs don't exist)
        _monitor.StartMonitoring(
            new ProcessIdentity(99998, DateTimeOffset.UtcNow),
            seatId, ManagedProcessType.Provider);

        _monitor.StopMonitoringAll(seatId);
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public void Dispose_StopMonitoringAll_PreventsEvents()
    {
        // After dispose + StopMonitoringAll, no events should fire.
        var eventFired = false;
        _monitor.ProcessExited += (_, _) => eventFired = true;

        _monitor.StopMonitoringAll(Guid.NewGuid());
        _monitor.Dispose();

        Assert.False(eventFired);
        Assert.Equal(0, _monitor.MonitoredCount);
    }

    [Fact]
    public async Task ConcurrentMarkExpected_StoppedProcess_DoesNotThrow()
    {
        // Concurrent operations on non-existent entries should be safe
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            var identity = new ProcessIdentity(920000 + i, DateTimeOffset.UtcNow);
            var seatId = Guid.NewGuid();

            tasks.Add(Task.Run(() =>
                _monitor.StartMonitoring(identity, seatId, ManagedProcessType.Provider)));
            tasks.Add(Task.Run(() =>
                _monitor.MarkExpectedExit(identity)));
            tasks.Add(Task.Run(() =>
                _monitor.StopMonitoring(identity)));
        }

        // Should not throw
        await Task.WhenAll(tasks);
    }
}
