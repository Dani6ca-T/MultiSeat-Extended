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
/// Regression tests for G13/F2: <see cref="SeatManager.ProvisionSeatAsync"/> recorded the
/// <c>ApolloManager.StartAsync</c> result and continued provisioning even when startup failed
/// (<c>pid &lt;= 0</c>), eventually parking the seat in <c>Ready</c> with no running Apollo.
/// Recovery could never fire for such a seat: <c>SessionHealthCheck.ApolloNeedsRestart</c>
/// requires <c>StreamingProcessId &gt; 0</c>, and the session is alive with status
/// <c>Ready</c>, so no other check fires either — a deterministically broken seat that looks
/// healthy on the dashboard.
///
/// The fix makes <c>pid &lt;= 0</c> enter the existing provisioning failure path (Error +
/// best-effort cleanup + rethrow) instead of continuing as if Apollo were alive. G4 readiness
/// probing itself is untouched — it remains inside <c>ApolloManager.StartAsync</c>; this
/// checkpoint only stops treating its failure signal as success.
/// </summary>
public class ProvisionApolloFailureTests
{
    // NOTE: ProvisionSeatAsync step 2.5 writes a RustDesk seed file under
    // C:\Users\<account>\AppData\... (best-effort, production behavior). The tests use a
    // unique account name per test and delete the directory afterwards if this run created
    // it, so the machine is left exactly as found.

    [Fact]
    public async Task Provision_ApolloStartReturnsMinusOne_EntersFailurePath()
    {
        // StartAsync → -1 (readiness timeout / failed launch, already cleaned up inside
        // ApolloManager). Provisioning must fail instead of continuing toward Ready.
        const string account = "g13f2minusone";
        var (mgr, streaming) = BuildSeatManager(startPid: -1);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgr.ProvisionSeatAsync(new SeatRequest { AccountName = account }, CancellationToken.None));
            Assert.Equal("Apollo failed to start — see apollo.log for details.", ex.Message);

            // Only the first startup attempt ran; the UUID-restart second start is never reached.
            Assert.Equal(1, streaming.StartCount);

            // Existing failure-path semantics: the seat is parked in Error (never Ready) with
            // the failed PID recorded for diagnostics. Registry removal is F7 scope, not G13.
            var seat = Assert.Single(mgr.GetAllSeats(), s => s.AccountName == account);
            Assert.Equal(SeatStatus.Error, seat.Status);
            Assert.Equal(-1, seat.StreamingProcessId);
            Assert.Null(seat.ReadyAt);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
            CleanupRustDeskSeed(account);
        }
    }

    [Fact]
    public async Task Provision_ApolloStartReturnsZero_EntersFailurePath()
    {
        // The contract is pid <= 0, not just -1: 0 (never-started) must fail identically.
        const string account = "g13f2zero";
        var (mgr, streaming) = BuildSeatManager(startPid: 0);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgr.ProvisionSeatAsync(new SeatRequest { AccountName = account }, CancellationToken.None));
            Assert.Equal("Apollo failed to start — see apollo.log for details.", ex.Message);

            Assert.Equal(1, streaming.StartCount);

            var seat = Assert.Single(mgr.GetAllSeats(), s => s.AccountName == account);
            Assert.Equal(SeatStatus.Error, seat.Status);
            Assert.Equal(0, seat.StreamingProcessId);
            Assert.Null(seat.ReadyAt);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
            CleanupRustDeskSeed(account);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (SeatManager mgr, RecordingStreamingProvider streaming) BuildSeatManager(int startPid)
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-apollo-prov-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
            // Defaults kept: AudioMode.PerSession (no cables), HidHide off, ViGEm off,
            // no emulator seeders below — so provisioning touches no real devices.
        };

        var accounts = new Mock<IAccountManager>();
        accounts.Setup(a => a.AccountExists(It.IsAny<string>())).Returns(true);

        var sessionLauncher = new Mock<ISessionLauncher>();
        sessionLauncher
            .Setup(s => s.LaunchSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<RdpGeometry>()))
            .ReturnsAsync(7);

        var configBuilder = new ApolloConfigBuilder(
            new TestLogger<ApolloConfigBuilder>(),
            Options.Create(options));
        var serverQuery = new ApolloServerQuery(new TestLogger<ApolloServerQuery>());
        var processInjector = new ProcessInjector(
            new TestLogger<ProcessInjector>(),
            Options.Create(options),
            new SessionLauncher(
                new TestLogger<SessionLauncher>(),
                Options.Create(options),
                accounts.Object));
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
        // Real FirewallManager: RunNetshAsync never throws (exit code only), and teardown's
        // ClosePorts removes anything an elevated run created. No firewall state can leak.
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
            accounts.Object,
            sessionLauncher.Object,
            processInjector,
            display,
            streaming, // recording fake — StartAsync returns the configured pid, nothing launches
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

    private static void CleanupRustDeskSeed(string account)
    {
        // Remove ONLY the directory this test's provisioning seed may have created.
        try
        {
            var dir = Path.Combine(@"C:\Users", account);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort: never fail the test on host cleanup.
        }
    }

    /// <summary>
    /// Records streaming lifecycle calls without launching anything. StartAsync answers
    /// with the configured pid (use -1/0 for startup failure, positive for success).
    /// </summary>
    private sealed class RecordingStreamingProvider(int startPid) : IStreamingProvider
    {
        private int _startCount;

        public int StartCount => _startCount;

        public Task<int> StartAsync(SeatInfo seat, CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref _startCount);
            return Task.FromResult(startPid);
        }

        public void Stop(SeatInfo seat) { }
        public void KillForReconnect(SeatInfo seat) { }
        public Task<int> RestartAsync(SeatInfo seat, CancellationToken ct) => Task.FromResult(-1);
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
