using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MultiSeat.Service;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Emulators;
using MultiSeat.Service.Input;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Storage;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// Regression tests for the Apollo stale-seat race (G13/F1):
/// <see cref="SeatManager.StartApolloAsync"/>, <see cref="SeatManager.RestartApolloAsync"/>,
/// <see cref="SeatManager.StopApollo"/> and <see cref="SeatManager.SetNvencPresetAsync"/>
/// captured the seat BEFORE waiting for <see cref="SeatLifecycleGate"/> and used it AFTER,
/// with no post-gate revalidation. A teardown holding the gate (H2 ordering: removal →
/// TearingDown → teardown → gate release) could interleave, so the operation ran
/// <c>StartAsync</c>/<c>RestartAsync</c> against a logged-off session for a seat that no
/// longer exists in <c>_seats</c> — an orphan Apollo nothing will ever stop, plus a leaked
/// <c>ApolloManager</c> instance record.
///
/// The fix re-reads the authoritative seat from the registry after acquiring the gate and
/// aborts unless it is still eligible — the same shape as the already-fixed
/// <c>LaunchAppInSeatAsync</c> / <c>ResetDisplayAsync</c> / <c>ResetAudio</c> /
/// <c>ResetController</c> paths. <c>SeatInfo</c> is a mutable shared reference, so observing
/// <c>TearingDown</c> through the originally captured object is NOT freshness proof; only a
/// post-gate registry re-read is.
/// </summary>
public class ApolloLifecycleStaleSeatTests
{
    private static readonly Guid SeatA = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // ── Pure predicate ──────────────────────────────────────────────

    [Theory]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    [InlineData(SeatStatus.Configuring)]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Connecting)]
    [InlineData(SeatStatus.Error)]
    public void ApolloOperationStillValid_AllowsNonTearingDownStates(SeatStatus status)
    {
        // Apollo start/stop/restart/preset-change are repair actions offered on any
        // registered seat — including Error, where (re)starting Apollo is the legitimate
        // manual repair the health check never performs. Only TearingDown invalidates:
        // by then the seat is no longer a member of _seats.
        Assert.True(SeatManager.ApolloOperationStillValid(status),
            $"Status {status} should be a valid precondition for Apollo lifecycle operations");
    }

    [Fact]
    public void ApolloOperationStillValid_RejectsTearingDown()
    {
        // TearingDown is the H2 "removed" signal: a concurrent teardown removed the seat
        // from _seats while the request waited for the gate. Running Apollo work for such
        // a seat would orphan the new process plus its ApolloManager instance record.
        Assert.False(SeatManager.ApolloOperationStillValid(SeatStatus.TearingDown));
    }

    // ── StartApolloAsync ────────────────────────────────────────────

    [Fact]
    public async Task StartApollo_RemovedWhileWaitingForGate_DoesNotStart()
    {
        // Core race: seat captured while registered, teardown removes it from _seats while
        // the start waits for the gate. Post-gate GetSeat must return null and abort before
        // StartAsync. Deterministic: the gate is held so the start is guaranteed blocked on
        // AcquireAsync before the removal — no timing dependence.
        var (mgr, streaming) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var startTask = mgr.StartApolloAsync(SeatA, CancellationToken.None);

            // Simulate teardown's TryRemove under the held gate.
            RemoveSeat(mgr, SeatA);
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<ResourceNotFoundException>(() => startTask);
            Assert.Equal("Seat was removed while starting Apollo.", ex.Message);
            Assert.Equal(0, streaming.StartCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task StartApollo_TearingDownWhileWaitingForGate_DoesNotStart()
    {
        // Defensive variant: the registered seat reads TearingDown after the gate (a teardown
        // that flipped the status on the still-referenced object). The status predicate, not
        // just the null re-check, must reject it.
        var (mgr, streaming) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var startTask = mgr.StartApolloAsync(SeatA, CancellationToken.None);

            mgr.GetSeat(SeatA)!.TransitionTo(SeatStatus.TearingDown, NullLogger.Instance);
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<ResourceNotFoundException>(() => startTask);
            Assert.Equal("Seat was removed while starting Apollo.", ex.Message);
            Assert.Equal(0, streaming.StartCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task StartApollo_OnLiveReadySeat_StartsApollo()
    {
        // Valid path: start on a live Ready seat with a session must still work end-to-end —
        // the gate is acquired and released, Apollo starts, the PID is recorded.
        var (mgr, streaming) = BuildSeatManager(startPid: 1234);
        try
        {
            RegisterReadySeat(mgr, SeatA);
            await mgr.StartApolloAsync(SeatA, CancellationToken.None);

            Assert.Equal(1, streaming.StartCount);
            Assert.Equal(1234, mgr.GetSeat(SeatA)!.StreamingProcessId);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task StartApollo_WithoutSession_StillRejected()
    {
        // Pre-existing precondition is preserved: a seat with no session cannot start Apollo.
        // The session check now runs against the post-gate authoritative seat.
        var (mgr, streaming) = BuildSeatManager();
        try
        {
            RegisterSeat(mgr, SeatA, SeatStatus.Ready, sessionId: -1);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgr.StartApolloAsync(SeatA, CancellationToken.None));
            Assert.Equal("No active session — provision the seat first.", ex.Message);
            Assert.Equal(0, streaming.StartCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── RestartApolloAsync ──────────────────────────────────────────

    [Fact]
    public async Task RestartApollo_RemovedWhileWaitingForGate_DoesNotRestart()
    {
        // Same race for restart: neither Stop nor Start may run for the stale seat.
        var (mgr, streaming) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var restartTask = mgr.RestartApolloAsync(SeatA, CancellationToken.None);

            RemoveSeat(mgr, SeatA);
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<ResourceNotFoundException>(() => restartTask);
            Assert.Equal("Seat was removed while restarting Apollo.", ex.Message);
            Assert.Equal(0, streaming.StopCount);
            Assert.Equal(0, streaming.StartCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task RestartApollo_TearingDownWhileWaitingForGate_DoesNotRestart()
    {
        var (mgr, streaming) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var restartTask = mgr.RestartApolloAsync(SeatA, CancellationToken.None);

            mgr.GetSeat(SeatA)!.TransitionTo(SeatStatus.TearingDown, NullLogger.Instance);
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<ResourceNotFoundException>(() => restartTask);
            Assert.Equal("Seat was removed while restarting Apollo.", ex.Message);
            Assert.Equal(0, streaming.StopCount);
            Assert.Equal(0, streaming.StartCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task RestartApollo_OnLiveReadySeat_RestartsApollo()
    {
        // Valid path: stop + start run, the new PID is recorded. DisplayDevicePath stays
        // empty so display isolation is skipped (F3 asymmetry is out of scope for G13).
        var (mgr, streaming) = BuildSeatManager(startPid: 4321);
        try
        {
            RegisterReadySeat(mgr, SeatA);
            await mgr.RestartApolloAsync(SeatA, CancellationToken.None);

            Assert.Equal(1, streaming.StopCount);
            Assert.Equal(1, streaming.StartCount);
            Assert.Equal(4321, mgr.GetSeat(SeatA)!.StreamingProcessId);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── StopApollo ──────────────────────────────────────────────────

    [Fact]
    public async Task StopApollo_RemovedWhileWaitingForGate_DoesNotStop()
    {
        // Kill-only is still a mutation of the instance record: a stale Stop must not run.
        var (mgr, streaming) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var stopTask = mgr.StopApollo(SeatA);

            RemoveSeat(mgr, SeatA);
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<ResourceNotFoundException>(() => stopTask);
            Assert.Equal("Seat was removed while stopping Apollo.", ex.Message);
            Assert.Equal(0, streaming.StopCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task StopApollo_OnLiveReadySeat_StopsApollo()
    {
        var (mgr, streaming) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);
            await mgr.StopApollo(SeatA);

            Assert.Equal(1, streaming.StopCount);
            Assert.Equal(0, mgr.GetSeat(SeatA)!.StreamingProcessId);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── SetNvencPresetAsync ─────────────────────────────────────────

    [Fact]
    public async Task SetNvencPreset_RemovedWhileWaitingForGate_DoesNotReconfigure()
    {
        // Same race for the NVENC path: no kill, no start, and the staged preset must not
        // be written onto the detached seat.
        var (mgr, streaming) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);
            var presetStore = NewPresetStore();

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var nvencTask = mgr.SetNvencPresetAsync(SeatA, NvencQualityPreset.Quality, presetStore, CancellationToken.None);

            RemoveSeat(mgr, SeatA);
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<ResourceNotFoundException>(() => nvencTask);
            Assert.Equal("Seat was removed while changing NVENC preset.", ex.Message);
            Assert.Equal(0, streaming.KillForReconnectCount);
            Assert.Equal(0, streaming.StartCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task SetNvencPreset_OnLiveReadySeat_ChangesPreset()
    {
        var (mgr, streaming) = BuildSeatManager(startPid: 7777);
        try
        {
            RegisterReadySeat(mgr, SeatA);
            var presetStore = NewPresetStore();

            await mgr.SetNvencPresetAsync(SeatA, NvencQualityPreset.Quality, presetStore, CancellationToken.None);

            Assert.Equal(1, streaming.KillForReconnectCount);
            Assert.Equal(1, streaming.StartCount);
            Assert.Equal(NvencQualityPreset.Quality, mgr.GetSeat(SeatA)!.NvencPreset);
            Assert.Equal(7777, mgr.GetSeat(SeatA)!.StreamingProcessId);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (SeatManager mgr, RecordingStreamingProvider streaming) BuildSeatManager(int startPid = 1111)
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-apollo-stale-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };

        var configBuilder = new ApolloConfigBuilder(
            new TestLogger<ApolloConfigBuilder>(),
            Options.Create(options));
        var serverQuery = new ApolloServerQuery(new TestLogger<ApolloServerQuery>());
        var sessionLauncher = new SessionLauncher(
            new TestLogger<SessionLauncher>(),
            Options.Create(options),
            Mock.Of<IAccountManager>());
        var processInjector = new ProcessInjector(
            new TestLogger<ProcessInjector>(),
            Options.Create(options),
            sessionLauncher);
        var display = new RecordingDisplayManager();
        var streaming = new RecordingStreamingProvider(startPid);
        var apolloManager = new ApolloManager(
            new TestLogger<ApolloManager>(),
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());

        var portAllocator = new PortAllocator();
        var firewall = new FirewallManager(new TestLogger<FirewallManager>(), Options.Create(options));
        var audioDeviceEnumerator = new AudioDeviceEnumerator(new TestLogger<AudioDeviceEnumerator>());
        var audioRouter = new AudioRouter(
            new TestLogger<AudioRouter>(),
            Options.Create(options),
            audioDeviceEnumerator,
            processInjector);
        var controllerManager = new ControllerManager(new TestLogger<ControllerManager>());
        var inputRouter = new InputRouter(new TestLogger<InputRouter>(), controllerManager);
        var inputHookManager = new InputHookManager(
            new TestLogger<InputHookManager>(), Options.Create(options));
        var hidHide = new HidHideConfigurator(new TestLogger<HidHideConfigurator>(), Options.Create(options));
        var onConnect = new OnConnectAppLauncher(
            new TestLogger<OnConnectAppLauncher>(),
            Options.Create(options),
            apolloManager,
            processInjector);
        var gate = new SeatLifecycleGate();

        var mgr = new SeatManager(
            new NullLogger<SeatManager>(),
            Options.Create(options),
            Mock.Of<IAccountManager>(),
            sessionLauncher,
            processInjector,
            display,
            streaming, // recording fake — never launches a real Apollo process
            apolloManager,
            portAllocator,
            firewall,
            audioRouter,
            controllerManager,
            inputRouter,
            inputHookManager,
            hidHide,
            onConnect,
            Array.Empty<IEmulatorConfigSeeder>(),
            gate);

        return (mgr, streaming);
    }

    private static SeatPresetStore NewPresetStore() =>
        new(NullLogger<SeatPresetStore>.Instance,
            Path.Combine(Path.GetTempPath(), $"multiseat-presets-{Guid.NewGuid():N}.json"));

    private static void RegisterReadySeat(SeatManager mgr, Guid seatId) =>
        RegisterSeat(mgr, seatId, SeatStatus.Ready, sessionId: 0);

    private static void RegisterSeat(SeatManager mgr, Guid seatId, SeatStatus status, int sessionId)
    {
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = status,
            SessionId = sessionId
        };
        // Use the existing internal TryRegisterSeat seam.
        var seatsDict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        var ownershipLock = typeof(SeatManager)
            .GetField("_accountOwnershipLock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(SeatManager.TryRegisterSeat(seatsDict, ownershipLock, seat),
            "Test setup: seat registration should succeed");
    }

    private static void RemoveSeat(SeatManager mgr, Guid seatId)
    {
        // Mirror teardown's TryRemove from the internal registry, without running the whole
        // teardown pipeline (Apollo/session cleanup needs real components).
        var seatsDict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(seatsDict.TryRemove(seatId, out _),
            "Test setup: seat should be registered before removal");
    }

    /// <summary>
    /// Records every streaming lifecycle call so tests can assert the post-gate validation
    /// prevents them. Never launches a real process.
    /// </summary>
    private sealed class RecordingStreamingProvider(int startPid) : IStreamingProvider
    {
        private int _startCount;
        private int _stopCount;
        private int _killForReconnectCount;
        private int _restartCount;

        public int StartCount => _startCount;
        public int StopCount => _stopCount;
        public int KillForReconnectCount => _killForReconnectCount;
        public int RestartCount => _restartCount;

        public Task<int> StartAsync(SeatInfo seat, CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref _startCount);
            return Task.FromResult(startPid);
        }

        public void Stop(SeatInfo seat) =>
            System.Threading.Interlocked.Increment(ref _stopCount);

        public void KillForReconnect(SeatInfo seat) =>
            System.Threading.Interlocked.Increment(ref _killForReconnectCount);

        public Task<int> RestartAsync(SeatInfo seat, CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref _restartCount);
            return Task.FromResult(startPid);
        }

        public bool IsAlive(Guid seatId) => false;
        public Task<ApolloServerInfo?> QueryHealthAsync(SeatInfo seat, CancellationToken ct) =>
            Task.FromResult<ApolloServerInfo?>(null);
        public int GetRestartCount(Guid seatId) => 0;
        public TimeSpan? GetUptime(Guid seatId) => null;
    }

    private sealed class RecordingDisplayManager : IVirtualDisplayManager
    {
        public bool IsDriverAvailable => true;
        public IReadOnlyList<object> EnumerateAllConnectedPaths() => [];

        public Task CreateDisplayAsync(SeatInfo seat, CancellationToken ct) => Task.CompletedTask;
        public Task DestroyDisplayAsync(SeatInfo seat, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        { }
    }
}
