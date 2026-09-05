using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
/// Regression tests for the app-launch stale-seat race: <see cref="SeatManager.LaunchAppInSeatAsync"/>
/// was ungated, so a teardown that acquires <see cref="SeatLifecycleGate"/> and removes the seat could
/// interleave between the Ready/Streaming status check and <c>LaunchInSessionAsync</c>, launching the app
/// into a session that is being disconnected + logged off — an orphan process with no seat in <c>_seats</c>
/// to ever return to Ready or kill it (same race class as ResetController / SetResolutionAsync /
/// OnConnectAppLauncher, which are already gated).
///
/// The fix wraps the whole revalidate → launch → state-mutate transaction in the per-seat lifecycle gate,
/// re-reads the seat AFTER the gate, and re-confirms the session id is unchanged. A launch that captured a
/// seat before teardown and only then enters the gate must see the post-teardown state and abort before any
/// process is created.
/// </summary>
public class LaunchAppInSeatStaleSeatTests
{
    private static readonly Guid SeatA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly LaunchAppRequest Request = new()
    {
        ExecutablePath = @"C:\Games\launcher.exe",
        Arguments = "--fullscreen",
        WorkingDirectory = @"C:\Games"
    };

    // ── Pure predicate (mirrors ControllerResetStillValid / ResolutionChangeStillValid) ──

    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    public void AppLaunchStillValid_AllowsLaunchableStates(SeatStatus status)
    {
        // Launch is offered only on Ready or Streaming seats (the pre-gate precondition); both
        // must survive the gate wait.
        Assert.True(SeatManager.AppLaunchStillValid(status),
            $"Status {status} should be a valid precondition for LaunchAppInSeatAsync");
    }

    [Theory]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    [InlineData(SeatStatus.Configuring)]
    [InlineData(SeatStatus.Connecting)]
    [InlineData(SeatStatus.Error)]
    [InlineData(SeatStatus.TearingDown)]
    public void AppLaunchStillValid_RejectsNonLaunchableStates(SeatStatus status)
    {
        // TearingDown is the H2/H3 "removed" signal: a concurrent teardown removed the seat from
        // _seats while the request waited for the gate. LaunchInSessionAsync for such a seat would
        // create a real process in a session being logged off — nothing in _seats would ever track
        // or kill it. Error/Connecting/etc. mean the session can no longer host the app either.
        Assert.False(SeatManager.AppLaunchStillValid(status),
            $"Status {status} should invalidate LaunchAppInSeatAsync");
    }

    // ── Race scenario tests ─────────────────────────────────────────────

    [Fact]
    public async Task LaunchAppInSeat_AfterTeardown_ThrowsSeatNotFound()
    {
        // Teardown first, then launch: the seat is no longer registered, so the pre-gate GetSeat
        // rejects the call before any gate or launch work (the API endpoint surfaces this as 404
        // via its own GetSeat pre-check). Documents that a fully-removed seat never reaches the
        // launch path.
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7);
            await mgr.TeardownSeatAsync(SeatA, CancellationToken.None);
            Assert.Null(mgr.GetSeat(SeatA));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgr.LaunchAppInSeatAsync(SeatA, Request, CancellationToken.None));
            Assert.Equal("Seat not found.", ex.Message);
            Assert.Equal(0, recorder.LaunchCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task LaunchAppInSeat_RemovedWhileWaitingForGate_DoesNotLaunch()
    {
        // The core race (H2 ordering): the seat is captured as Ready and passes the pre-gate
        // status check, then a teardown removes it from _seats while the launch waits for the
        // gate. After the gate the post-gate GetSeat re-check must return null and abort before
        // LaunchInSessionAsync. Deterministic: we hold the gate so the launch is guaranteed to
        // have passed the pre-gate check and be blocked on AcquireAsync before we remove the
        // seat — no timing dependence.
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7);

            // Hold the gate as a teardown would while removing the seat from the registry.
            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);

            // Start the launch: runs synchronously through GetSeat + pre-gate Ready check, then
            // blocks on the gate we hold.
            var launchTask = mgr.LaunchAppInSeatAsync(SeatA, Request, CancellationToken.None);

            // Simulate teardown's TryRemove: remove the seat from _seats under the held gate.
            RemoveSeat(mgr, SeatA);

            // Release the gate; the launch's post-gate GetSeat must now return null and abort.
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => launchTask);
            Assert.Equal("Seat was removed while launching app.", ex.Message);
            Assert.Equal(0, recorder.LaunchCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task LaunchAppInSeat_SeatFlippedToTearingDown_WhileWaitingForGate_DoesNotLaunch()
    {
        // Defensive variant of the race: the captured seat object reads TearingDown after the gate
        // (a teardown that flipped the status on the still-referenced object). The status
        // predicate, not just the null re-check, must reject it.
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var launchTask = mgr.LaunchAppInSeatAsync(SeatA, Request, CancellationToken.None);

            // Flip the status on the registered seat to TearingDown (the H2 "removed" signal).
            mgr.GetSeat(SeatA)!.TransitionTo(SeatStatus.TearingDown, NullLogger.Instance);

            lease.Dispose();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => launchTask);
            Assert.Equal("Seat was removed while launching app.", ex.Message);
            Assert.Equal(0, recorder.LaunchCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task LaunchAppInSeat_SessionChangedWhileWaitingForGate_DoesNotLaunch()
    {
        // A session replacement (SetResolutionAsync / /session-reconnect) while the launch waits
        // for the gate must also abort: launching into the seat's OLD session would inject the app
        // into a session that was just disconnected/replaced. The fix captures the session id at
        // entry and re-confirms it under the gate.
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var launchTask = mgr.LaunchAppInSeatAsync(SeatA, Request, CancellationToken.None);

            // Simulate a session replacement that ran while the launch waited for the gate.
            mgr.GetSeat(SeatA)!.SessionId = 99;

            lease.Dispose();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => launchTask);
            Assert.Equal("Seat was removed while launching app.", ex.Message);
            Assert.Equal(0, recorder.LaunchCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task LaunchAppInSeat_OnLiveReadySeat_LaunchesInCapturedSession()
    {
        // Valid path: launch on a Ready seat must still work end-to-end after the fix — the gate
        // is acquired, the seat + session revalidate, and the app is launched into that session.
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7);
            await mgr.LaunchAppInSeatAsync(SeatA, Request, CancellationToken.None);

            Assert.Equal(1, recorder.LaunchCount);
            Assert.Equal(7, recorder.LastSessionId);
            Assert.Equal(@"C:\Games\launcher.exe", recorder.LastExecutable);
            var seat = mgr.GetSeat(SeatA);
            Assert.NotNull(seat);
            Assert.Equal(SeatStatus.Streaming, seat!.Status);
            Assert.Equal(@"C:\Games\launcher.exe", seat.LaunchApp);
            Assert.Equal(recorder.ReturnedPid, seat.LaunchedProcessId);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (SeatManager mgr, RecordingProcessInjector recorder) BuildSeatManager()
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-launch-{Guid.NewGuid():N}"),
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
        var recorder = new RecordingProcessInjector(
            new TestLogger<ProcessInjector>(),
            Options.Create(options),
            sessionLauncher);
        var displayManager = new VirtualDisplayManager(
            new TestLogger<VirtualDisplayManager>(),
            Options.Create(options),
            processInjector);

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
            recorder, // recording subclass — records launches without touching the OS
            displayManager,
            apolloManager, // also satisfies IStreamingProvider (ApolloManager : IStreamingProvider)
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

        return (mgr, recorder);
    }

    private static void RegisterReadySeat(SeatManager mgr, Guid seatId, int sessionId)
    {
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = SeatStatus.Ready,
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
        // teardown pipeline (display/Apollo/session cleanup needs real components).
        var seatsDict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(seatsDict.TryRemove(seatId, out _),
            "Test setup: seat should be registered before removal");
    }

    /// <summary>
    /// Records every LaunchInSessionAsync call so tests can assert the post-gate validation
    /// prevents the call. Bypasses the real OS process-creation path.
    /// </summary>
    private sealed class RecordingProcessInjector : ProcessInjector
    {
        private int _launchCount;

        public RecordingProcessInjector(
            ILogger<ProcessInjector> logger,
            IOptions<MultiSeatOptions> options,
            SessionLauncher sessionLauncher) : base(logger, options, sessionLauncher)
        { }

        public int LaunchCount => _launchCount;
        public int? LastSessionId { get; private set; }
        public string? LastExecutable { get; private set; }
        public int ReturnedPid { get; private set; } = 4242;

        public override System.Threading.Tasks.Task<int> LaunchInSessionAsync(
            int sessionId,
            string accountName,
            string exePath,
            string? arguments = null,
            string? workingDir = null,
            CancellationToken ct = default,
            bool allowConsoleSession = false)
        {
            System.Threading.Interlocked.Increment(ref _launchCount);
            LastSessionId = sessionId;
            LastExecutable = exePath;
            return System.Threading.Tasks.Task.FromResult(ReturnedPid);
        }
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
