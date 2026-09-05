using System.Diagnostics;
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
/// Regression tests for the launched-app teardown fix: <c>TeardownSeatInternalAsync</c> now
/// explicitly terminates the seat's launched applications (identity-aware, PID + start time)
/// before the session logoff, instead of relying on session logoff alone.
///
/// The dashboard-launched root process is represented on the seat as
/// <see cref="SeatInfo.LaunchedProcessId"/> + <see cref="SeatInfo.LaunchedProcessStartedAt"/>.
/// A live process whose identity matches is terminated; a PID that was recycled onto a
/// different process is left alone (never kill an unrelated process); an already-exited
/// process is successful cleanup.
/// </summary>
public class SeatManagerLaunchedAppCleanupTests
{
    private static readonly Guid SeatA = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>A durable local process standing in for the dashboard-launched app.</summary>
    private static Process SpawnSleeper()
    {
        var psi = new ProcessStartInfo("ping.exe", "-t 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        return Process.Start(psi)!;
    }

    private static void KillQuietly(Process p)
    {
        try { if (!p.HasExited) { p.Kill(); p.WaitForExit(2000); } } catch { /* best effort */ }
        p.Dispose();
    }

    [Fact]
    public async Task Teardown_TerminatesLiveDashboardLaunchedProcess()
    {
        // Normal case: the launched app is still running when the seat is torn down. Teardown
        // must terminate it explicitly (the process is not in the seat's session, so session
        // logoff would not touch it — only the new explicit kill can).
        var sleeper = SpawnSleeper();
        var mgr = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7,
                launchedPid: sleeper.Id,
                launchedStartedAt: sleeper.StartTime.ToUniversalTime());

            await mgr.TeardownSeatAsync(SeatA, CancellationToken.None);
            Assert.Null(mgr.GetSeat(SeatA));

            Assert.True(sleeper.WaitForExit(5000),
                "Seat teardown must terminate the launched app process");
        }
        finally
        {
            KillQuietly(sleeper);
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task Teardown_RecycledPid_DoesNotTerminateUnrelatedProcess()
    {
        // The seat's recorded identity points at a process that no longer matches (the record
        // carries a stale start time — the PID-reuse shape). The unrelated live process must
        // survive teardown untouched.
        var other = SpawnSleeper();
        var mgr = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7,
                launchedPid: other.Id,
                launchedStartedAt: other.StartTime.ToUniversalTime().AddHours(-1));

            await mgr.TeardownSeatAsync(SeatA, CancellationToken.None);
            Assert.Null(mgr.GetSeat(SeatA));

            Thread.Sleep(300); // give any (wrong) kill attempt a moment to happen
            Assert.False(other.HasExited,
                "A stale/recycled PID must never cause an unrelated process to be terminated");
        }
        finally
        {
            KillQuietly(other);
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task Teardown_AlreadyExitedApp_IsSuccessfulCleanup()
    {
        // The app exited before teardown: cleanup must treat that as done and teardown must
        // still complete without throwing.
        var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        var startedAt = gone.StartTime.ToUniversalTime();
        Assert.True(gone.WaitForExit(5000));
        gone.Dispose();

        var mgr = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7,
                launchedPid: 1234567 /* any dead PID */,
                launchedStartedAt: startedAt);

            await mgr.TeardownSeatAsync(SeatA, CancellationToken.None);
            Assert.Null(mgr.GetSeat(SeatA));
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task Teardown_WithoutLaunchedApp_CompletesNormally()
    {
        // No app was ever launched: no identity to kill, teardown proceeds as before.
        var mgr = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA, sessionId: 7, launchedPid: 0, launchedStartedAt: null);
            await mgr.TeardownSeatAsync(SeatA, CancellationToken.None);
            Assert.Null(mgr.GetSeat(SeatA));
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static SeatManager BuildSeatManager()
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-launchclean-{Guid.NewGuid():N}"),
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
            processInjector,
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

        return mgr;
    }

    private static void RegisterReadySeat(
        SeatManager mgr, Guid seatId, int sessionId, int launchedPid, DateTimeOffset? launchedStartedAt)
    {
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = SeatStatus.Ready,
            SessionId = sessionId,
            LaunchedProcessId = launchedPid,
            LaunchedProcessStartedAt = launchedStartedAt
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
