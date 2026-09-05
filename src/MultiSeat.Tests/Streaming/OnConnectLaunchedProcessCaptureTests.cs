using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// Tests for the teardown-capture contract of <see cref="OnConnectAppLauncher"/>: launched
/// apps are recorded with their process identity (PID + start time), seat teardown can read
/// them via <see cref="OnConnectAppLauncher.GetLaunchedProcesses"/> BEFORE
/// <see cref="OnConnectAppLauncher.Forget"/> drops the state, and after Forget nothing remains.
/// The a461885 cancellation/session-id protections are untouched — the launch itself still
/// happens normally (the existing stale-seat suite keeps covering the cancellation cases).
/// </summary>
public class OnConnectLaunchedProcessCaptureTests
{
    private const string Connect = "CLIENT CONNECTED";
    private const string Disconnect = "CLIENT DISCONNECTED";

    private static string Line(string marker) =>
        $"[2026-08-29 10:26:20.078]: Info: {marker}\n";

    /// <summary>Seed the seat's apollo.log with a DISCONNECT so the first tick has no edge.</summary>
    private static string TempLog(MultiSeatOptions options, string account)
    {
        var seatDir = Path.Combine(options.ApolloConfigDir, account);
        Directory.CreateDirectory(seatDir);
        var path = Path.Combine(seatDir, "apollo.log");
        File.WriteAllText(path, Line(Disconnect));
        return path;
    }

    private static (OnConnectAppLauncher launcher, MultiSeatOptions options) BuildLauncher()
    {
        var options = new MultiSeatOptions
        {
            LaunchOnConnectDelayMs = 50,
            LaunchOnConnect = [new LaunchOnConnectApp { Path = @"C:\never\invoked.exe" }],
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-capture-{Guid.NewGuid():N}"),
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

        var apollo = new ApolloManager(
            new TestLogger<ApolloManager>(),
            Options.Create(options),
            configBuilder,
            new ProcessInjector(new TestLogger<ProcessInjector>(), Options.Create(options), sessionLauncher),
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());

        // Returns the TEST RUNNER's own PID — a real, always-alive process — so the launcher's
        // identity capture (PID + start time) records a genuine identity.
        var injector = new RealPidInjector(options, sessionLauncher);

        var launcher = new OnConnectAppLauncher(
            NullLogger<OnConnectAppLauncher>.Instance,
            Options.Create(options),
            apollo,
            injector);

        return (launcher, options);
    }

    [Fact]
    public async Task LaunchedAppIdentity_IsAvailable_UntilForget()
    {
        var (launcher, options) = BuildLauncher();
        var path = TempLog(options, "TestAccount");
        try
        {
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };
            var realPid = Environment.ProcessId;

            // Tick 1: seed state from the initial DISCONNECT line (no edge).
            launcher.ProcessSeat(seat, CancellationToken.None);

            // Append a CONNECT line; tick 2 fires OnConnect (launch delayed by 50 ms).
            File.AppendAllText(path, Line(Connect));
            launcher.ProcessSeat(seat, CancellationToken.None);

            // Wait long enough for the delay to elapse and the launch to fire.
            await Task.Delay(600);

            // The launched app is recorded with its identity (PID + start time) — this is
            // what teardown reads BEFORE Forget to terminate the app explicitly.
            var launched = launcher.GetLaunchedProcesses(seat.Id);
            var identity = Assert.Single(launched);
            Assert.Equal(realPid, identity.ProcessId);
            Assert.Equal(ApolloManager.GetProcessStartTime(realPid), identity.StartedAt);

            // Forget drops the state — teardown must therefore capture before Forget, which is
            // exactly the ordering SeatManager.TeardownSeatInternalAsync uses.
            launcher.Forget(seat.Id);
            Assert.Empty(launcher.GetLaunchedProcesses(seat.Id));
        }
        finally
        {
            try { Directory.Delete(options.ApolloConfigDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Forget_StillCancelsInFlightLaunch_WhenNoLaunchCompleted()
    {
        // The a461885 protection is intact: Forget during the settle delay must stop the
        // launch, and therefore nothing is ever recorded to capture.
        var (launcher, options) = BuildLauncher();
        var path = TempLog(options, "TestAccount");
        try
        {
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };

            launcher.ProcessSeat(seat, CancellationToken.None);

            File.AppendAllText(path, Line(Connect));
            launcher.ProcessSeat(seat, CancellationToken.None);

            // Forget immediately — within the 50 ms settle delay — cancelling the launch.
            launcher.Forget(seat.Id);

            await Task.Delay(600);

            // Nothing launched, nothing captured, and Forget is idempotent.
            Assert.Empty(launcher.GetLaunchedProcesses(seat.Id));
            launcher.Forget(seat.Id);
        }
        finally
        {
            try { Directory.Delete(options.ApolloConfigDir, recursive: true); } catch { /* best effort */ }
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

    /// <summary>ProcessInjector override that returns the test runner's own (real) PID.</summary>
    private sealed class RealPidInjector : ProcessInjector
    {
        public RealPidInjector(MultiSeatOptions options, SessionLauncher sessionLauncher)
            : base(NullLogger<ProcessInjector>.Instance, Options.Create(options), sessionLauncher)
        { }

        public override Task<int> LaunchInSessionAsync(
            int sessionId,
            string accountName,
            string exePath,
            string? arguments = null,
            string? workingDir = null,
            CancellationToken ct = default,
            bool allowConsoleSession = false)
            => Task.FromResult(Environment.ProcessId);

        public override Task<int> LaunchApolloInSessionAsync(
            int sessionId,
            string accountName,
            string apolloExePath,
            string configPath,
            CancellationToken ct)
            => Task.FromResult(Environment.ProcessId);
    }
}
