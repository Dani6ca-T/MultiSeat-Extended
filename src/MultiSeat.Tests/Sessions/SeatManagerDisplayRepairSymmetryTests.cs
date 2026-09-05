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
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// G15 regression: F3 display-repair asymmetry. The display-repair pipeline
/// (<see cref="SeatManager.ApplyDisplayIsolationAsync"/>, which already includes the
/// G1 identity refresh) is invoked from three classes of successful lifecycle
/// path: provisioning, resolution change (G3), reconnect/recovery. Two paths were
/// missing it: user-triggered <c>StartApolloAsync</c> and user-triggered
/// <c>ResetDisplayAsync</c>. After a successful start or reset, Apollo has
/// re-created the SudoVDA monitor (or the virtual display itself has been
/// recreated), so the seat is de-isolated until some later recovery happens to
/// fix it.
///
/// These tests use the existing <see cref="ISessionLauncher"/> seam — the same
/// observable boundary <see cref="SeatManagerResolutionRepairTests"/> pins for
/// the G3 path — to assert that the in-session <c>--setup-display-isolation</c>
/// helper is invoked only after the underlying operation has committed (G2/G3
/// ordering), and is NOT invoked when the underlying operation fails. The
/// existing <see cref="IVirtualDisplayManager"/> recording fake drives reset
/// success/failure.
/// </summary>
public class SeatManagerDisplayRepairSymmetryTests
{
    private const string KnownUuid = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

    // ── Test A — StartApolloAsync invokes display repair ───────────

    [Fact]
    public async Task StartApollo_OnLiveReadySeat_AppliesDisplayIsolationAfterStart()
    {
        // Successful start on a Ready seat with a known SudoVDA display must end with
        // --setup-display-isolation, the same observable boundary the G3 resolution-repair
        // path produces. Without the G15 fix the helper is never invoked: StartApolloAsync
        // returns right after streaming.StartAsync succeeds, leaving the seat de-isolated.
        var (mgr, streaming, launcher) = BuildSeatManager();

        var seatId = RegisterReadySeat(mgr, displayPath: KnownUuid, sessionId: 7);
        streaming.Setup(s => s.StartAsync(It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4321);

        await mgr.StartApolloAsync(seatId, CancellationToken.None);

        Assert.Equal(1, streaming.Invocations.Count(i =>
            i.Method.Name == nameof(IStreamingProvider.StartAsync)));

        launcher.Verify(l => l.RunHelperInSeatSession(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.Is<string>(cmd =>
                cmd.Contains("--setup-display-isolation", StringComparison.Ordinal) &&
                cmd.Contains(KnownUuid, StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task StartApollo_HelperRunsAfterStreamingStart()
    {
        // Pin the ordering explicitly: Apollo's StartAsync must precede the
        // --setup-display-isolation helper. A naïve "unconditional repair" patch that calls
        // ApplyDisplayIsolationAsync before StartAsync would target a not-yet-restarted
        // display pipeline, so this test pins the order that production depends on.
        var (mgr, streaming, launcher) = BuildSeatManager();

        var seatId = RegisterReadySeat(mgr, displayPath: KnownUuid, sessionId: 7);
        var startCalled = false;
        var helperSawStart = false;
        streaming.Setup(s => s.StartAsync(It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
            .Callback(() => startCalled = true)
            .ReturnsAsync(4321);
        launcher.Setup(l => l.RunHelperInSeatSession(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => helperSawStart = startCalled);

        await mgr.StartApolloAsync(seatId, CancellationToken.None);

        Assert.True(helperSawStart,
            "Expected --setup-display-isolation helper to run AFTER streaming.StartAsync");
    }

    // ── Test B — ResetDisplayAsync invokes display repair ──────────

    [Fact]
    public async Task ResetDisplay_OnLiveReadySeat_AppliesDisplayIsolationAfterReset()
    {
        // Successful display reset on a Ready seat must end with --setup-display-isolation.
        // The reset itself destroys + recreates the SudoVDA monitor; without the repair,
        // the seat leaves isolation until the next recovery tick.
        var (mgr, display, launcher) = BuildSeatManagerWithDisplay();

        var seatId = RegisterReadySeat(mgr, displayPath: KnownUuid, sessionId: 7);

        await mgr.ResetDisplayAsync(seatId, CancellationToken.None);

        Assert.Equal(1, display.DestroyCount);
        Assert.Equal(1, display.CreateCount);

        launcher.Verify(l => l.RunHelperInSeatSession(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.Is<string>(cmd =>
                cmd.Contains("--setup-display-isolation", StringComparison.Ordinal) &&
                cmd.Contains(KnownUuid, StringComparison.Ordinal))), Times.Once);
    }

    // ── Test C — Repair must not run after failure ─────────────────

    [Fact]
    public async Task StartApollo_StartAsyncThrows_DoesNotApplyDisplayIsolation()
    {
        // A failed start must not trigger the repair pipeline: the new Apollo is not there,
        // and applying isolation would target a dead pipeline. Pin the negative case so a
        // naïve unconditional repair call is rejected.
        var (mgr, streaming, launcher) = BuildSeatManager();

        var seatId = RegisterReadySeat(mgr, displayPath: KnownUuid, sessionId: 7);
        streaming.Setup(s => s.StartAsync(It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Synthetic start failure for test"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.StartApolloAsync(seatId, CancellationToken.None));

        launcher.Verify(l => l.RunHelperInSeatSession(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.Is<string>(cmd =>
                cmd.Contains("--setup-display-isolation", StringComparison.Ordinal))),
            Times.Never);
    }

    [Fact]
    public async Task ResetDisplay_DestroyFails_DoesNotApplyDisplayIsolation()
    {
        // If DestroyDisplayAsync throws, no repair must run — the seat may be in an
        // inconsistent state and the helper would orphan the recording fake's bookkeeping.
        var (mgr, display, launcher) = BuildSeatManagerWithDisplay();
        display.FailNextDestroy = true;

        var seatId = RegisterReadySeat(mgr, displayPath: KnownUuid, sessionId: 7);

        await Assert.ThrowsAnyAsync<Exception>(
            () => mgr.ResetDisplayAsync(seatId, CancellationToken.None));

        launcher.Verify(l => l.RunHelperInSeatSession(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.Is<string>(cmd =>
                cmd.Contains("--setup-display-isolation", StringComparison.Ordinal))),
            Times.Never);
        Assert.Equal(0, display.CreateCount);
    }

    // ── Test D — G3 repair path remains intact (exercised indirectly) ───
    // The G3 repair tests in SeatManagerResolutionRepairTests already pin this contract;
    // running them unchanged covers Test D. No additional test here.

    // ── Helpers ──────────────────────────────────────────────────────

    private static Guid RegisterReadySeat(SeatManager mgr, string displayPath, int sessionId)
    {
        var seatId = Guid.NewGuid();
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = SeatStatus.Ready,
            SessionId = sessionId,
            PortBase = 48100,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            DisplayDevicePath = displayPath,
            AutoStart = false
        };
        var seatsDict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        var ownershipLock = typeof(SeatManager)
            .GetField("_accountOwnershipLock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(SeatManager.TryRegisterSeat(seatsDict, ownershipLock, seat),
            "Test setup: seat registration should succeed");
        return seatId;
    }

    private static (SeatManager mgr, Mock<IStreamingProvider> streaming, Mock<ISessionLauncher> launcher)
        BuildSeatManager()
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-g15-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };

        var configBuilder = new ApolloConfigBuilder(
            NullLogger<ApolloConfigBuilder>.Instance,
            Options.Create(options));
        var serverQuery = new ApolloServerQuery(NullLogger<ApolloServerQuery>.Instance);
        var realSessionLauncher = new SessionLauncher(
            NullLogger<SessionLauncher>.Instance,
            Options.Create(options),
            Mock.Of<IAccountManager>());
        var processInjector = new ProcessInjector(
            NullLogger<ProcessInjector>.Instance,
            Options.Create(options),
            realSessionLauncher);
        var display = new FailNeverDisplayManager();
        var streamingMock = new Mock<IStreamingProvider>();
        var apolloManager = new ApolloManager(
            NullLogger<ApolloManager>.Instance,
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());

        var launcherMock = new Mock<ISessionLauncher>();
        launcherMock.Setup(l => l.LaunchSessionAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<RdpGeometry>()))
            .ReturnsAsync(0);

        var controllerManager = new ControllerManager(NullLogger<ControllerManager>.Instance);
        var mgr = new SeatManager(
            NullLogger<SeatManager>.Instance,
            Options.Create(options),
            Mock.Of<IAccountManager>(),
            launcherMock.Object,
            processInjector,
            display,
            streamingMock.Object,
            apolloManager,
            new PortAllocator(),
            new FirewallManager(NullLogger<FirewallManager>.Instance, Options.Create(options)),
            new AudioRouter(
                NullLogger<AudioRouter>.Instance,
                Options.Create(options),
                new AudioDeviceEnumerator(NullLogger<AudioDeviceEnumerator>.Instance),
                processInjector),
            controllerManager,
            new InputRouter(NullLogger<InputRouter>.Instance, controllerManager),
            new InputHookManager(NullLogger<InputHookManager>.Instance, Options.Create(options)),
            new HidHideConfigurator(NullLogger<HidHideConfigurator>.Instance, Options.Create(options)),
            new OnConnectAppLauncher(
                NullLogger<OnConnectAppLauncher>.Instance,
                Options.Create(options),
                apolloManager,
                processInjector),
            Array.Empty<IEmulatorConfigSeeder>(),
            new SeatLifecycleGate());

        return (mgr, streamingMock, launcherMock);
    }

    private static (SeatManager mgr, FailControllableDisplayManager display, Mock<ISessionLauncher> launcher)
        BuildSeatManagerWithDisplay()
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-g15-display-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };

        var configBuilder = new ApolloConfigBuilder(
            NullLogger<ApolloConfigBuilder>.Instance,
            Options.Create(options));
        var serverQuery = new ApolloServerQuery(NullLogger<ApolloServerQuery>.Instance);
        var realSessionLauncher = new SessionLauncher(
            NullLogger<SessionLauncher>.Instance,
            Options.Create(options),
            Mock.Of<IAccountManager>());
        var processInjector = new ProcessInjector(
            NullLogger<ProcessInjector>.Instance,
            Options.Create(options),
            realSessionLauncher);
        var display = new FailControllableDisplayManager();
        var apolloManager = new ApolloManager(
            NullLogger<ApolloManager>.Instance,
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());

        var launcherMock = new Mock<ISessionLauncher>();
        var streamingMock = new Mock<IStreamingProvider>();

        var controllerManager = new ControllerManager(NullLogger<ControllerManager>.Instance);
        var mgr = new SeatManager(
            NullLogger<SeatManager>.Instance,
            Options.Create(options),
            Mock.Of<IAccountManager>(),
            launcherMock.Object,
            processInjector,
            display,
            streamingMock.Object,
            apolloManager,
            new PortAllocator(),
            new FirewallManager(NullLogger<FirewallManager>.Instance, Options.Create(options)),
            new AudioRouter(
                NullLogger<AudioRouter>.Instance,
                Options.Create(options),
                new AudioDeviceEnumerator(NullLogger<AudioDeviceEnumerator>.Instance),
                processInjector),
            controllerManager,
            new InputRouter(NullLogger<InputRouter>.Instance, controllerManager),
            new InputHookManager(NullLogger<InputHookManager>.Instance, Options.Create(options)),
            new HidHideConfigurator(NullLogger<HidHideConfigurator>.Instance, Options.Create(options)),
            new OnConnectAppLauncher(
                NullLogger<OnConnectAppLauncher>.Instance,
                Options.Create(options),
                apolloManager,
                processInjector),
            Array.Empty<IEmulatorConfigSeeder>(),
            new SeatLifecycleGate());

        return (mgr, display, launcherMock);
    }

    /// <summary>Default fake display manager for the StartApollo tests: never throws.</summary>
    private sealed class FailNeverDisplayManager : IVirtualDisplayManager
    {
        public bool IsDriverAvailable => true;
        public IReadOnlyList<object> EnumerateAllConnectedPaths() => [];
        public Task CreateDisplayAsync(SeatInfo seat, CancellationToken ct) => Task.CompletedTask;
        public Task DestroyDisplayAsync(SeatInfo seat, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Display manager used by ResetDisplay tests: counts destroy/create, and can be
    /// instructed to throw on the next destroy so the negative test can pin
    /// "no repair runs after failure".
    /// </summary>
    private sealed class FailControllableDisplayManager : IVirtualDisplayManager
    {
        private int _destroyCount;
        private int _createCount;

        public int DestroyCount => _destroyCount;
        public int CreateCount => _createCount;
        public bool FailNextDestroy { get; set; }

        public bool IsDriverAvailable => true;
        public IReadOnlyList<object> EnumerateAllConnectedPaths() => [];

        public Task CreateDisplayAsync(SeatInfo seat, CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref _createCount);
            return Task.CompletedTask;
        }

        public Task DestroyDisplayAsync(SeatInfo seat, CancellationToken ct)
        {
            if (FailNextDestroy)
                throw new InvalidOperationException("Synthetic destroy failure for test");
            System.Threading.Interlocked.Increment(ref _destroyCount);
            return Task.CompletedTask;
        }
    }
}