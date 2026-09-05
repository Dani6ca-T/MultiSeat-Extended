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
/// G3 regression: SetResolutionAsync performed kill → disconnect → relaunch → rebuild →
/// start, then stopped — while the reconnect/recovery path additionally settles the
/// display pipeline and re-applies display isolation after Apollo is back. A successful
/// resize therefore silently left the seat de-isolated until some later recovery
/// happened to repair it.
///
/// These drive the real SetResolutionAsync with a real SeatManager graph (same builder
/// pattern as the G2 commit tests): the session launcher and streaming provider are
/// Moq collaborators — the established seam for code that would otherwise touch Win32 —
/// while the config rebuild and log-path resolution are the real ApolloManager on a
/// temp dir. Isolation itself runs against the mocked launcher, so its invocation,
/// ordering, and arguments are directly observable via the mock.
/// </summary>
public class SeatManagerResolutionRepairTests
{
    private const string KnownUuid = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
    private const string OldUuid = "{11111111-2222-3333-4444-555555555555}";
    private const string NewUuid = "{99999999-8888-7777-6666-555555555555}";

    [Fact]
    public async Task SuccessfulResolutionChange_RepairsDisplayPipelineAfterStart()
    {
        // The repair must run, and it must run AFTER the new Apollo exists: isolation
        // against a display whose Apollo is not back yet would target a dead pipeline.
        var h = BuildHarness();
        var seatId = RegisterSeat(h.Manager, KnownUuid);
        var startCalled = false;
        var helperSawStart = false;
        h.Streaming.Setup(s => s.StartAsync(It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
            .Callback(() => startCalled = true)
            .ReturnsAsync(4321);
        h.Launcher.Setup(l => l.RunHelperInSeatSession(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => helperSawStart = startCalled);

        await h.Manager.SetResolutionAsync(seatId, 2560, 1440, h.Presets, CancellationToken.None);

        Assert.True(helperSawStart);
        h.Launcher.Verify(l => l.RunHelperInSeatSession(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.Is<string>(cmd =>
                cmd.Contains("--setup-display-isolation", StringComparison.Ordinal) &&
                cmd.Contains(KnownUuid, StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task StaleDisplayPath_HealedThroughExistingRefreshMechanism()
    {
        // The seat carries the previous Apollo instance's UUID while the log's LATEST
        // block already names the recreated display. The repair must adopt the new UUID
        // through the existing G1 refresh — no duplicate resolver — and re-point Apollo's
        // config at it. A first-block parse would keep the stale UUID and fail this.
        var h = BuildHarness();
        var seatId = RegisterSeat(h.Manager, OldUuid);
        h.Streaming.Setup(s => s.StartAsync(It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4321);

        var seat = h.Manager.GetSeat(seatId)!;
        RegisterApolloInstance(h.Apollo, seat, h.ConfigDir);
        var logPath = h.Apollo.GetLogPath(seat.AccountName, h.ConfigDir);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await File.WriteAllTextAsync(logPath,
            SeatLog("{rdp-surface}", OldUuid, "VDD by MTT")
            + "\nInfo: CLIENT CONNECTED\n"
            + SeatLog("{rdp-surface}", NewUuid, "VDD by MTT"));

        await h.Manager.SetResolutionAsync(seatId, 2560, 1440, h.Presets, CancellationToken.None);

        Assert.Equal(NewUuid, seat.DisplayDevicePath);
        var conf = Assert.Single(
            Directory.EnumerateFiles(h.ConfigDir, "sunshine.conf", SearchOption.AllDirectories));
        Assert.Contains($"output_name = {NewUuid}", await File.ReadAllTextAsync(conf));
    }

    [Fact]
    public async Task RepairCancellationAfterSuccessfulStart_KeepsCommittedResolution()
    {
        // G2 boundary: Apollo started fine at the new size (commit done), then the
        // repair's own settle delay is canceled. The cancellation must propagate, but
        // it must NOT roll the seat back to the old size — the new resolution is
        // established, and restoring the old one would reintroduce the G2 lie.
        var h = BuildHarness();
        var seatId = RegisterSeat(h.Manager, KnownUuid);
        h.Streaming.Setup(s => s.StartAsync(It.IsAny<SeatInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        using var cts = new CancellationTokenSource(500);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Manager.SetResolutionAsync(seatId, 2560, 1440, h.Presets, cts.Token));

        var seat = h.Manager.GetSeat(seatId)!;
        Assert.Equal(2560, seat.Width);
        Assert.Equal(1440, seat.Height);
        Assert.Equal(42, seat.StreamingProcessId);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private sealed record Harness(
        SeatManager Manager,
        SeatPresetStore Presets,
        string ConfigDir,
        ApolloManager Apollo,
        Mock<ISessionLauncher> Launcher,
        Mock<IStreamingProvider> Streaming);

    private static Harness BuildHarness()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"multiseat-resrepair-{Guid.NewGuid():N}");
        var presetPath = Path.Combine(Path.GetTempPath(), $"multiseat-resrepair-presets-{Guid.NewGuid():N}.json");
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

        var launcherMock = new Mock<ISessionLauncher>();
        launcherMock.Setup(l => l.LaunchSessionAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<RdpGeometry>()))
            .ReturnsAsync(0);

        var streamingMock = new Mock<IStreamingProvider>();

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

        return new Harness(mgr, presets, configDir, apolloManager, launcherMock, streamingMock);
    }

    private static Guid RegisterSeat(SeatManager mgr, string displayPath)
    {
        var seatId = Guid.NewGuid();
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = SeatStatus.Ready,
            SessionId = 7,
            PortBase = 48100,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            DisplayDevicePath = displayPath,
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

    private static void RegisterApolloInstance(ApolloManager apollo, SeatInfo seat, string configDir)
    {
        // UpdateDisplayOutput routes through the instance record's ConfigPath (as in
        // production, where StartAsync registers it). Seed the record the same way the
        // seat itself is seeded above: fixture setup, not the behavior under test.
        var configPath = Path.Combine(configDir, seat.AccountName, "sunshine.conf");
        var instances = (System.Collections.Concurrent.ConcurrentDictionary<Guid, ApolloInstance>)typeof(ApolloManager)
            .GetField("_instances", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(apollo)!;
        instances[seat.Id] = new ApolloInstance(
            seat.Id,
            new ProcessIdentity(4321, DateTimeOffset.UtcNow),
            4321,
            configPath,
            seat.SessionId,
            seat.AccountName,
            DateTimeOffset.UtcNow,
            0);
    }

    private static string DisplayLogJson(params string[] entries) =>
        "Info: Currently available display devices:\n[\n" +
        string.Join(",\n", entries) + "\n]\n";

    private static string DisplayEntry(
        string deviceId, string friendlyName, bool primary, int refreshNumerator) =>
        $$"""
            {
              "device_id": "{{deviceId}}",
              "display_name": "IGNORED",
              "edid": null,
              "friendly_name": "{{friendlyName}}",
              "info": {
                "hdr_state": "Disabled",
                "origin_point": { "x": 0, "y": 0 },
                "primary": {{(primary ? "true" : "false")}},
                "refresh_rate": {
                  "type": "rational",
                  "value": { "denominator": 1, "numerator": {{refreshNumerator}} }
                },
                "resolution": { "height": 1080, "width": 1920 },
                "resolution_scale": {
                  "type": "rational",
                  "value": { "denominator": 100, "numerator": 100 }
                }
              }
            }
        """;

    private static string SeatLog(string rdpId, string vddId, string vddName) =>
        DisplayLogJson(
            DisplayEntry(rdpId, "", primary: true, refreshNumerator: 1000),
            DisplayEntry(vddId, vddName, primary: false, refreshNumerator: 60));
}
