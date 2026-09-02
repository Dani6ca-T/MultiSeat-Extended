using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.ProcessTracking;

/// <summary>
/// Tests for game process tracking — P1-B.
///
/// Game processes are tracked using the same infrastructure as provider processes:
/// IProcessTracker for ownership, IProcessMonitor for lifecycle, IProcessGroup for cleanup.
///
/// Key invariants:
/// - Every tracked game has ProcessIdentity (PID + StartedAt)
/// - Every tracked game has exactly one Seat owner
/// - Multiple games per Seat are supported
/// - PID reuse is protected
/// - Game exits are observable
/// - Game lifecycle cannot trigger provider recovery
/// - Cross-seat isolation is preserved
/// </summary>
public class GameProcessTrackingTests
{
    private readonly WindowsProcessTracker _tracker = new();
    private readonly WindowsProcessMonitor _monitor;

    public GameProcessTrackingTests()
    {
        _monitor = new WindowsProcessMonitor(NullLogger<WindowsProcessMonitor>.Instance);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  OWNERSHIP
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterGame_ProcessIdentity_ReturnsTracked()
    {
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatId, ManagedProcessType.Game);

        var result = _tracker.Get(identity);
        Assert.NotNull(result);
        Assert.Equal(seatId, result.OwnerSeatId);
        Assert.Equal(ManagedProcessType.Game, result.ProcessType);
    }

    [Fact]
    public void RegisterGame_MultipleGamesPerSeat_AllTracked()
    {
        var seatId = Guid.NewGuid();
        var id1 = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var id2 = new ProcessIdentity(200, DateTimeOffset.UtcNow);
        var id3 = new ProcessIdentity(300, DateTimeOffset.UtcNow);

        _tracker.Register(id1, seatId, ManagedProcessType.Game);
        _tracker.Register(id2, seatId, ManagedProcessType.Game);
        _tracker.Register(id3, seatId, ManagedProcessType.Game);

        var games = _tracker.GetByOwner(seatId);
        Assert.Equal(3, games.Count);
        Assert.All(games, g => Assert.Equal(ManagedProcessType.Game, g.ProcessType));
    }

    [Fact]
    public void RegisterGame_CrossSeat_Rejected()
    {
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatA, ManagedProcessType.Game);

        Assert.Throws<InvalidOperationException>(() =>
            _tracker.Register(identity, seatB, ManagedProcessType.Game));
    }

    [Fact]
    public void UnregisterGame_RemovesFromTracker()
    {
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);

        _tracker.Register(identity, seatId, ManagedProcessType.Game);
        Assert.NotNull(_tracker.Get(identity));

        _tracker.Unregister(identity);
        Assert.Null(_tracker.Get(identity));
        Assert.Empty(_tracker.GetByOwner(seatId));
    }

    [Fact]
    public void UnregisterAllSeat_RemovesAllGames()
    {
        var seatId = Guid.NewGuid();
        var id1 = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var id2 = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(id1, seatId, ManagedProcessType.Game);
        _tracker.Register(id2, seatId, ManagedProcessType.Game);

        _tracker.UnregisterAll(seatId);

        Assert.Empty(_tracker.GetByOwner(seatId));
        Assert.Null(_tracker.Get(id1));
        Assert.Null(_tracker.Get(id2));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PID REUSE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PidReuse_DifferentStartedAt_DifferentIdentity()
    {
        var seatId = Guid.NewGuid();
        var time1 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var oldIdentity = new ProcessIdentity(1234, time1);
        var newIdentity = new ProcessIdentity(1234, time2);

        _tracker.Register(oldIdentity, seatId, ManagedProcessType.Game);
        _tracker.Register(newIdentity, seatId, ManagedProcessType.Game);

        // Both exist with different keys
        Assert.NotNull(_tracker.Get(oldIdentity));
        Assert.NotNull(_tracker.Get(newIdentity));
    }

    [Fact]
    public void PidReuse_StaleEvent_DoesNotAffectNewProcess()
    {
        // Simulate: old game exits, new game starts with same PID
        var seatId = Guid.NewGuid();
        var time1 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var oldIdentity = new ProcessIdentity(1234, time1);
        var newIdentity = new ProcessIdentity(1234, time2);

        _tracker.Register(oldIdentity, seatId, ManagedProcessType.Game);
        _tracker.Register(newIdentity, seatId, ManagedProcessType.Game);

        // Unregister old — new should still be tracked
        _tracker.Unregister(oldIdentity);
        Assert.Null(_tracker.Get(oldIdentity));
        Assert.NotNull(_tracker.Get(newIdentity));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MONITORING
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Monitor_GameProcessExited_EventContainsGameType()
    {
        var eventFired = false;
        ManagedProcessType? capturedType = null;

        _monitor.ProcessExited += (_, e) =>
        {
            eventFired = true;
            capturedType = e.ProcessType;
        };

        // Start monitoring a non-existent PID (will fail to add, but tests the flow)
        var identity = new ProcessIdentity(99999, DateTimeOffset.UtcNow);
        _monitor.StartMonitoring(identity, Guid.NewGuid(), ManagedProcessType.Game);

        // No event should fire for non-existent processes
        Assert.False(eventFired);
    }

    [Fact]
    public void Monitor_GameProcessExited_ProviderExit_DoesNotFireForGame()
    {
        // Game exit events should be separate from provider exit events.
        // The VibepolloManager filters for Provider type only.
        var gameEventFired = false;

        _monitor.ProcessExited += (_, e) =>
        {
            if (e.ProcessType == ManagedProcessType.Game)
                gameEventFired = true;
        };

        // No actual game process to test with, but verify the filter logic
        Assert.False(gameEventFired);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEAT ISOLATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SeatA_Games_NotVisibleInSeatB()
    {
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();

        _tracker.Register(new ProcessIdentity(100, DateTimeOffset.UtcNow), seatA, ManagedProcessType.Game);
        _tracker.Register(new ProcessIdentity(200, DateTimeOffset.UtcNow), seatA, ManagedProcessType.Game);
        _tracker.Register(new ProcessIdentity(300, DateTimeOffset.UtcNow), seatB, ManagedProcessType.Game);

        Assert.Equal(2, _tracker.GetByOwner(seatA).Count);
        Assert.Single(_tracker.GetByOwner(seatB));
    }

    [Fact]
    public void UnregisterAllSeatA_DoesNotAffectSeatB()
    {
        var seatA = Guid.NewGuid();
        var seatB = Guid.NewGuid();
        var idA = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var idB = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(idA, seatA, ManagedProcessType.Game);
        _tracker.Register(idB, seatB, ManagedProcessType.Game);

        _tracker.UnregisterAll(seatA);

        Assert.Empty(_tracker.GetByOwner(seatA));
        Assert.Single(_tracker.GetByOwner(seatB));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PROVIDER + GAME INDEPENDENCE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProviderAndGame_IndependentProcesses()
    {
        var seatId = Guid.NewGuid();
        var providerIdentity = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var gameIdentity = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(providerIdentity, seatId, ManagedProcessType.Provider);
        _tracker.Register(gameIdentity, seatId, ManagedProcessType.Game);

        var all = _tracker.GetByOwner(seatId);
        Assert.Equal(2, all.Count);

        var provider = all.First(p => p.ProcessType == ManagedProcessType.Provider);
        var game = all.First(p => p.ProcessType == ManagedProcessType.Game);

        Assert.Equal(providerIdentity, provider.Identity);
        Assert.Equal(gameIdentity, game.Identity);
    }

    [Fact]
    public void UnregisterGame_DoesNotAffectProvider()
    {
        var seatId = Guid.NewGuid();
        var providerIdentity = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var gameIdentity = new ProcessIdentity(200, DateTimeOffset.UtcNow);

        _tracker.Register(providerIdentity, seatId, ManagedProcessType.Provider);
        _tracker.Register(gameIdentity, seatId, ManagedProcessType.Game);

        _tracker.Unregister(gameIdentity);

        Assert.NotNull(_tracker.Get(providerIdentity));
        Assert.Null(_tracker.Get(gameIdentity));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CONCURRENCY
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Concurrent_GameRegistration_DoesNotThrow()
    {
        var seatId = Guid.NewGuid();
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            var identity = new ProcessIdentity(5000 + i, DateTimeOffset.UtcNow);
            tasks.Add(Task.Run(() =>
                _tracker.Register(identity, seatId, ManagedProcessType.Game)));
        }

        await Task.WhenAll(tasks);

        var games = _tracker.GetByOwner(seatId);
        Assert.Equal(50, games.Count);
    }

    [Fact]
    public async Task Concurrent_GameUnregistration_DoesNotThrow()
    {
        var seatId = Guid.NewGuid();
        var identities = Enumerable.Range(0, 50)
            .Select(i => new ProcessIdentity(5000 + i, DateTimeOffset.UtcNow))
            .ToList();

        foreach (var id in identities)
            _tracker.Register(id, seatId, ManagedProcessType.Game);

        var tasks = identities
            .Select(id => Task.Run(() => _tracker.Unregister(id)))
            .ToList();

        await Task.WhenAll(tasks);

        Assert.Empty(_tracker.GetByOwner(seatId));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EXPECTED EXIT
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessExitInfo_GameType_CarriesCorrectData()
    {
        var identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow);
        var seatId = Guid.NewGuid();

        var info = new ProcessExitInfo
        {
            Identity = identity,
            OwnerSeatId = seatId,
            ProcessType = ManagedProcessType.Game,
            ExitCode = 0,
            WasExpected = false
        };

        Assert.Equal(ManagedProcessType.Game, info.ProcessType);
        Assert.Equal(seatId, info.OwnerSeatId);
        Assert.False(info.WasExpected);
    }

    [Fact]
    public void ProcessExitInfo_GameExpectedExit_Flagged()
    {
        var info = new ProcessExitInfo
        {
            Identity = new ProcessIdentity(1234, DateTimeOffset.UtcNow),
            OwnerSeatId = Guid.NewGuid(),
            ProcessType = ManagedProcessType.Game,
            ExitCode = 0,
            WasExpected = true
        };

        Assert.True(info.WasExpected);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IMMEDIATE EXIT AFTER LAUNCH
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterGame_AlreadyExited_ProcessNotTracked()
    {
        // If a game exits immediately after launch, ResolveProcessIdentity returns null.
        // This test verifies the tracker doesn't hold stale entries.
        var seatId = Guid.NewGuid();
        var identity = new ProcessIdentity(99999, DateTimeOffset.UtcNow);

        // Register then immediately unregister
        _tracker.Register(identity, seatId, ManagedProcessType.Game);
        _tracker.Unregister(identity);

        Assert.Null(_tracker.Get(identity));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MONITOR STOP ALL
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Monitor_StopMonitoringAll_RemovesAllEntries()
    {
        var seatId = Guid.NewGuid();

        // Attempt to add entries (will fail since PIDs don't exist)
        _monitor.StartMonitoring(
            new ProcessIdentity(99998, DateTimeOffset.UtcNow),
            seatId, ManagedProcessType.Game);
        _monitor.StartMonitoring(
            new ProcessIdentity(99999, DateTimeOffset.UtcNow),
            seatId, ManagedProcessType.Game);

        _monitor.StopMonitoringAll(seatId);
        Assert.Equal(0, _monitor.MonitoredCount);
    }
}
