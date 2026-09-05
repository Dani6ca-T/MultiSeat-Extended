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

using FollowAction = MultiSeat.Service.Streaming.ClientResolutionFollower.FollowAction;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// G2 regression: SetResolutionAsync assigned seat.Width/Height BEFORE the
/// reconnect/relaunch/config/start transaction ran. When any downstream step threw,
/// the seat permanently reported the requested size it never reached, and
/// ClientResolutionFollower.Decide then answered AlreadyCorrectSize for a resolution
/// that was never applied.
///
/// These drive the real SetResolutionAsync with a real SeatManager graph (same builder
/// pattern as the stale-seat tests): the session launcher and streaming provider are
/// Moq collaborators — the established seam for code that would otherwise touch Win32
/// (Mock.Of is already used for IAccountManager/IProcessTracker/IProcessMonitor) —
/// while the config rebuild is the real ApolloConfigBuilder on a temp dir, so the
/// tests also pin that the staged size is what Apollo is told to advertise.
/// </summary>
public class SeatManagerResolutionCommitTests
{
    private const int OldWidth = 1920;
    private const int OldHeight = 1080;
    private const int NewWidth = 2560;
    private const int NewHeight = 1440;

    [Fact]
    public async Task SuccessfulResolutionChange_CommitsNewDimensions()
    {
        // Characterization: the happy path must keep working exactly as before —
        // new dims committed, Apollo (mock) PID recorded, real config advertises them.
        var (mgr, presets, configDir) = BuildSeatManager(startResult: 4321);
        var seatId = RegisterSeat(mgr, OldWidth, OldHeight);

        await mgr.SetResolutionAsync(seatId, NewWidth, NewHeight, presets, CancellationToken.None);

        var seat = mgr.GetSeat(seatId)!;
        Assert.Equal(NewWidth, seat.Width);
        Assert.Equal(NewHeight, seat.Height);
        Assert.Equal(4321, seat.StreamingProcessId);

        var conf = Assert.Single(
            Directory.EnumerateFiles(configDir, "sunshine.conf", SearchOption.AllDirectories));
        Assert.Contains($"resolutions = [{NewWidth}x{NewHeight}]",
            await File.ReadAllTextAsync(conf));
    }

    [Fact]
    public async Task FailedStart_RestoresOldDimensions()
    {
        // Failure at the FINAL startup stage: Apollo never came up at the new size,
        // so the seat must still report the established size.
        var (mgr, presets, _) = BuildSeatManager(
            startBehavior: m => m.Setup(s => s.StartAsync(
                    It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Apollo failed to start")));
        var seatId = RegisterSeat(mgr, OldWidth, OldHeight);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mgr.SetResolutionAsync(seatId, NewWidth, NewHeight, presets, CancellationToken.None));

        var seat = mgr.GetSeat(seatId)!;
        Assert.Equal(OldWidth, seat.Width);
        Assert.Equal(OldHeight, seat.Height);
    }

    [Fact]
    public async Task FailedDisconnect_RestoresOldDimensions()
    {
        // Failure at an EARLY downstream stage: the session was never even taken down,
        // so the old size is trivially still the truth and must be what the seat says.
        var (mgr, presets, _) = BuildSeatManager(
            launcherBehavior: m => m.Setup(l => l.DisconnectSession(It.IsAny<int>()))
                .Throws(new InvalidOperationException("session disconnect failed")));
        var seatId = RegisterSeat(mgr, OldWidth, OldHeight);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mgr.SetResolutionAsync(seatId, NewWidth, NewHeight, presets, CancellationToken.None));

        var seat = mgr.GetSeat(seatId)!;
        Assert.Equal(OldWidth, seat.Width);
        Assert.Equal(OldHeight, seat.Height);
    }

    [Fact]
    public async Task FollowerDoesNotReportAlreadyCorrectSizeAfterFailedResize()
    {
        // The user-visible consequence: after a failed resize to 2560x1440, the follower
        // must NOT conclude the seat is already that size — otherwise the client never
        // gets its requested size and no retry is ever attempted.
        var (mgr, presets, _) = BuildSeatManager(
            startBehavior: m => m.Setup(s => s.StartAsync(
                    It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Apollo failed to start")));
        var seatId = RegisterSeat(mgr, OldWidth, OldHeight);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mgr.SetResolutionAsync(seatId, NewWidth, NewHeight, presets, CancellationToken.None));

        var seat = mgr.GetSeat(seatId)!;
        Assert.NotEqual(
            FollowAction.AlreadyCorrectSize,
            ClientResolutionFollower.Decide(
                new RequestedMode(NewWidth, NewHeight, 60),
                seat.Width, seat.Height, lastApplied: null));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (
        SeatManager Manager, SeatPresetStore Presets, string ConfigDir) BuildSeatManager(
            int startResult = 0,
            Action<Mock<IStreamingProvider>>? startBehavior = null,
            Action<Mock<ISessionLauncher>>? launcherBehavior = null)
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"multiseat-rescommit-{Guid.NewGuid():N}");
        var presetPath = Path.Combine(Path.GetTempPath(), $"multiseat-rescommit-presets-{Guid.NewGuid():N}.json");
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = configDir,
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
        var displayManager = new VirtualDisplayManager(
            NullLogger<VirtualDisplayManager>.Instance,
            Options.Create(options),
            processInjector);

        var apolloManager = new ApolloManager(
            NullLogger<ApolloManager>.Instance,
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());

        // Collaborators that would touch Win32 are Moq seams (same practice as the
        // stale-seat tests' Mock.Of collaborators). Everything the commit ordering
        // depends on — the seat object, the real config rebuild — stays real.
        var launcherMock = new Mock<ISessionLauncher>();
        launcherMock.Setup(l => l.LaunchSessionAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<RdpGeometry>()))
            .ReturnsAsync(0);
        launcherBehavior?.Invoke(launcherMock);

        var streamingMock = new Mock<IStreamingProvider>();
        if (startBehavior is not null)
            startBehavior(streamingMock);
        else
            streamingMock.Setup(s => s.StartAsync(
                    It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(startResult);

        var presets = new SeatPresetStore(NullLogger<SeatPresetStore>.Instance, presetPath);

        var controllerManager = new ControllerManager(NullLogger<ControllerManager>.Instance);
        var mgr = new SeatManager(
            NullLogger<SeatManager>.Instance,
            Options.Create(options),
            Mock.Of<IAccountManager>(),
            launcherMock.Object,
            processInjector,
            displayManager,
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
            new InputRouter(
                NullLogger<InputRouter>.Instance,
                controllerManager),
            new InputHookManager(NullLogger<InputHookManager>.Instance, Options.Create(options)),
            new HidHideConfigurator(NullLogger<HidHideConfigurator>.Instance, Options.Create(options)),
            new OnConnectAppLauncher(
                NullLogger<OnConnectAppLauncher>.Instance,
                Options.Create(options),
                apolloManager,
                processInjector),
            Array.Empty<IEmulatorConfigSeeder>(),
            new SeatLifecycleGate());

        return (mgr, presets, configDir);
    }

    private static Guid RegisterSeat(SeatManager mgr, int width, int height)
    {
        var seatId = Guid.NewGuid();
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = SeatStatus.Ready,
            SessionId = 7,
            PortBase = 48100,
            Width = width,
            Height = height,
            Fps = 60,
            AutoStart = false
        };
        // Use the existing internal TryRegisterSeat seam (same as the stale-seat tests).
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
}
