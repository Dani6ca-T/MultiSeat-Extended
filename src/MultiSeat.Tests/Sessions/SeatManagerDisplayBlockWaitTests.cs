using System.Diagnostics;
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
/// G5 regression: provisioning waited a fixed 5 s for Apollo's startup display
/// enumeration and then parsed once. On fast hosts that is 4 s of dead time; on slow
/// hosts the block may not exist yet and a usable display is missed even though the
/// very next second would have found it.
///
/// These drive the real condition wait on a real SeatManager graph (same builder
/// pattern as the G2/G3 tests — the wait only needs the logger, but the constructor
/// takes the whole graph) against temp log files whose content the test controls.
/// No Apollo process and no real waiting on fixed delays is involved.
/// </summary>
public class SeatManagerDisplayBlockWaitTests
{
    private const string SudoUuid = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

    [Fact]
    public async Task BlockAlreadyPresent_ReturnsPromptly()
    {
        // The common case must not pay a fixed delay: a block that is already there
        // is parsed on the first poll, in milliseconds rather than seconds.
        var mgr = BuildManager();
        var logPath = TempLog(SeatLog("{rdp-surface}", SudoUuid, "VDD by MTT"));
        var sw = Stopwatch.StartNew();

        var result = await mgr.WaitForDisplayBlockAsync(
            logPath, CancellationToken.None, deadline: TimeSpan.FromSeconds(10));

        Assert.Equal(SudoUuid, result.DeviceId);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BlockAppearsLater_Detected()
    {
        // The slow-host case the fixed sleep missed: nothing usable at first, a valid
        // block lands mid-wait, and the wait returns it instead of timing out.
        var mgr = BuildManager();
        var logPath = TempLog("Info: Apollo starting up\n");
        _ = Task.Run(async () =>
        {
            await Task.Delay(600);
            await File.AppendAllTextAsync(logPath,
                SeatLog("{rdp-surface}", SudoUuid, "VDD by MTT"));
        });

        var result = await mgr.WaitForDisplayBlockAsync(
            logPath, CancellationToken.None, deadline: TimeSpan.FromSeconds(5));

        Assert.Equal(SudoUuid, result.DeviceId);
    }

    [Fact]
    public async Task MissingFile_AppearingLater_Detected()
    {
        // The log file itself may not exist yet when the wait starts (Apollo Creates
        // it during startup). Appearance counts as progress, same as growth.
        var mgr = BuildManager();
        var logPath = Path.Combine(Path.GetTempPath(), $"multiseat-blockwait-{Guid.NewGuid():N}.log");
        _ = Task.Run(async () =>
        {
            await Task.Delay(600);
            await File.WriteAllTextAsync(logPath,
                SeatLog("{rdp-surface}", SudoUuid, "VDD by MTT"));
        });

        try
        {
            var result = await mgr.WaitForDisplayBlockAsync(
                logPath, CancellationToken.None, deadline: TimeSpan.FromSeconds(5));

            Assert.Equal(SudoUuid, result.DeviceId);
        }
        finally
        {
            try { File.Delete(logPath); } catch { }
        }
    }

    [Fact]
    public async Task NoBlockBeforeDeadline_ReturnsNone()
    {
        // Timeout preserves the old failure shape exactly: no usable block means the
        // caller takes its existing "nothing found" branch. The wait must be bounded
        // (returns near the deadline, not instantly and not never).
        var mgr = BuildManager();
        var logPath = TempLog("Info: Apollo starting up\nInfo: still no displays\n");
        var sw = Stopwatch.StartNew();

        var result = await mgr.WaitForDisplayBlockAsync(
            logPath, CancellationToken.None, deadline: TimeSpan.FromSeconds(2));

        Assert.Null(result.DeviceId);
        Assert.Equal(0, result.DisplayCount);
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task CancelledMidWait_Propagates()
    {
        // Cancellation (worker stop, teardown) must abort the bounded wait, never be
        // converted into a "no block found" verdict.
        var mgr = BuildManager();
        var logPath = TempLog("Info: Apollo starting up\n");
        using var cts = new CancellationTokenSource(300);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mgr.WaitForDisplayBlockAsync(
                logPath, cts.Token, deadline: TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task PreCancelled_PropagatesImmediately()
    {
        var mgr = BuildManager();
        var logPath = TempLog(SeatLog("{rdp-surface}", SudoUuid, "VDD by MTT"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mgr.WaitForDisplayBlockAsync(
                logPath, cts.Token, deadline: TimeSpan.FromSeconds(30)));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static SeatManager BuildManager()
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-blockwait-{Guid.NewGuid():N}"),
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

        var controllerManager = new ControllerManager(NullLogger<ControllerManager>.Instance);

        return new SeatManager(
            NullLogger<SeatManager>.Instance,
            Options.Create(options),
            Mock.Of<IAccountManager>(),
            Mock.Of<ISessionLauncher>(),
            processInjector,
            displayManager,
            Mock.Of<IStreamingProvider>(),
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
    }

    private static string TempLog(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"multiseat-blockwait-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content);
        return path;
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
