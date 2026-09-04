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
/// Regression tests for the on-connect delayed-launch race (CONFIRMED #1 from the
/// stale-seat audit): the fire-and-forget Task.Run scheduled by OnConnect captures the
/// seat's session id into a closure that can outlive the seat. A teardown that finishes
/// during the settle delay (default 4 s) must stop the launch BEFORE LaunchInSessionAsync
/// touches the logged-off session. Cases covered:
///
///   A. Teardown before delay completes → no launch (Forget cancels the per-seat CTS).
///   B. Forget/cancellation during delay → no launch.
///   C. Normal delayed OnConnect → launch still occurs.
///   D. Captured stale sessionId differs from current sessionId → no launch.
///   E. Seat removed/recreated with same seat Id → old callback cannot launch.
///
/// All tests use a production-realistic short delay (50–200 ms) and then wait long
/// enough for the delay to elapse, so a launch that survives Forget actually fires.
/// This proves the cancellation is the reason a launch is missing, not timing.
/// </summary>
public class OnConnectAppLauncherStaleSeatTests
{
    private const string Connect = "CLIENT CONNECTED";
    private const string Disconnect = "CLIENT DISCONNECTED";

    private static string Line(string marker) =>
        $"[2026-08-29 10:26:20.078]: Info: {marker}\n";

    /// <summary>
    /// Seed the log with a DISCONNECT line so the first ProcessSeat call records
    /// state.Connected=false; the next CONNECT line appended after that produces a
    /// connect edge. (Seeding with a CONNECT line leaves state.Connected=true and the
    /// appended CONNECT never edges.)
    ///
    /// The path matches what ApolloManager.GetLogPath(account, configDir) returns:
    /// {configDir}/{account}/apollo.log. We seed the directory and file ourselves
    /// because OnConnectAppLauncher only reads what Apollo would have written.
    /// </summary>
    private static string TempLog(MultiSeatOptions options, string account, string content)
    {
        var seatDir = Path.Combine(options.ApolloConfigDir, account);
        Directory.CreateDirectory(seatDir);
        var path = Path.Combine(seatDir, "apollo.log");
        File.WriteAllText(path, Line(Disconnect));
        return path;
    }

    private static void Append(string path, string content) =>
        File.AppendAllText(path, content);

    private static (OnConnectAppLauncher launcher, RecordingInjector injector)
        BuildLauncher(MultiSeatOptions options, Func<Guid, int?>? sessionLookup)
    {
        var apollo = BuildApolloManager(options);
        var injector = new RecordingInjector(options);

        var launcher = new OnConnectAppLauncher(
            NullLogger<OnConnectAppLauncher>.Instance,
            Options.Create(options),
            apollo,
            injector,
            sessionLookup);

        return (launcher, injector);
    }

    private static ApolloManager BuildApolloManager(MultiSeatOptions options)
    {
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

        return new ApolloManager(
            new TestLogger<ApolloManager>(),
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());
    }

    // ── Case A: teardown before delay completes → no launch ───────────

    [Fact]
    public async Task Forget_BeforeDelayCompletes_CancelsLaunch()
    {
        var options = new MultiSeatOptions
        {
            LaunchOnConnectDelayMs = 200,
            LaunchOnConnect = [new LaunchOnConnectApp { Path = @"C:\never\invoked.exe" }],
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-stale-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };
        var path = TempLog(options, "TestAccount", Line(Connect));
        var liveSeats = new Dictionary<Guid, (bool Exists, int SessionId)>();
        var (launcher, injector) = BuildLauncher(options, id => liveSeats.TryGetValue(id, out var s) && s.Exists ? s.SessionId : null);
        try
        {
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };
            liveSeats[seat.Id] = (true, 1);

            launcher.ProcessSeat(seat, CancellationToken.None);

            Append(path, Line(Connect));
            launcher.ProcessSeat(seat, CancellationToken.None);

            // Forget immediately. The per-seat CTS must be cancelled synchronously, so
            // even after waiting longer than the delay, the launch must NOT happen.
            launcher.Forget(seat.Id);
            liveSeats.Remove(seat.Id);

            await Task.Delay(500);

            Assert.Empty(injector.Launches);
        }
        finally { Cleanup(options, path); }
    }

    // ── Case B: Forget during the delay ────────────────────────────────

    [Fact]
    public async Task ForgetDuringDelay_PreventsLaunch()
    {
        var options = new MultiSeatOptions
        {
            LaunchOnConnectDelayMs = 200,
            LaunchOnConnect = [new LaunchOnConnectApp { Path = @"C:\never\invoked.exe" }],
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-stale-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };
        var path = TempLog(options, "TestAccount", Line(Connect));
        var liveSeats = new Dictionary<Guid, (bool Exists, int SessionId)>();
        var (launcher, injector) = BuildLauncher(options, id => liveSeats.TryGetValue(id, out var s) && s.Exists ? s.SessionId : null);
        try
        {
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };
            liveSeats[seat.Id] = (true, 1);

            // First tick — seed state from the initial DISCONNECT line.
            launcher.ProcessSeat(seat, CancellationToken.None);

            Append(path, Line(Connect));
            // Second tick — fire OnConnect on the new connect edge.
            launcher.ProcessSeat(seat, CancellationToken.None);

            // Let the background task start the Task.Delay. Then Forget.
            await Task.Delay(50);
            launcher.Forget(seat.Id);
            liveSeats.Remove(seat.Id);

            // Wait long enough for the delay to elapse. With Forget cancelling the
            // per-seat CTS, the launch must NOT happen.
            await Task.Delay(500);

            Assert.Empty(injector.Launches);
        }
        finally { Cleanup(options, path); }
    }

    // ── Case C: normal delayed OnConnect → launch still occurs ─────────

    [Fact]
    public async Task NormalDelayedOnConnect_Launches()
    {
        var options = new MultiSeatOptions
        {
            LaunchOnConnectDelayMs = 50,
            LaunchOnConnect = [new LaunchOnConnectApp { Path = @"C:\never\invoked.exe" }],
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-stale-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };
        var path = TempLog(options, "TestAccount", Line(Connect));
        var liveSeats = new Dictionary<Guid, (bool Exists, int SessionId)>();
        var (launcher, injector) = BuildLauncher(options, id => liveSeats.TryGetValue(id, out var s) && s.Exists ? s.SessionId : null);
        try
        {
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };
            liveSeats[seat.Id] = (true, 1);

            // Tick once to seed state.
            launcher.ProcessSeat(seat, CancellationToken.None);

            Append(path, Line(Connect));

            // Tick again to fire OnConnect on the new connect edge.
            launcher.ProcessSeat(seat, CancellationToken.None);

            // Wait long enough for the 50 ms delay to elapse and the launch to fire.
            await Task.Delay(500);

            var launch = Assert.Single(injector.Launches);
            Assert.Equal(1, launch.SessionId);
            Assert.Equal(@"C:\never\invoked.exe", launch.ExePath);
        }
        finally { Cleanup(options, path); }
    }

    // ── Case D: captured sessionId no longer matches current sessionId ─

    [Fact]
    public async Task SessionIdChanged_DuringDelay_PreventsLaunch()
    {
        var options = new MultiSeatOptions
        {
            LaunchOnConnectDelayMs = 200,
            LaunchOnConnect = [new LaunchOnConnectApp { Path = @"C:\never\invoked.exe" }],
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-stale-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };
        var path = TempLog(options, "TestAccount", Line(Connect));
        var liveSeats = new Dictionary<Guid, (bool Exists, int SessionId)>();
        var (launcher, injector) = BuildLauncher(options, id => liveSeats.TryGetValue(id, out var s) && s.Exists ? s.SessionId : null);
        try
        {
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };
            liveSeats[seat.Id] = (true, 1);

            launcher.ProcessSeat(seat, CancellationToken.None);

            Append(path, Line(Connect));
            // Second tick fires OnConnect.
            launcher.ProcessSeat(seat, CancellationToken.None);

            // Same seat, but SetResolutionAsync / /session-reconnect replaced its session.
            liveSeats[seat.Id] = (true, 2);

            await Task.Delay(500);

            Assert.Empty(injector.Launches);
        }
        finally { Cleanup(options, path); }
    }

    // ── Case E: seat removed, then a NEW seat created with same Guid ───

    [Fact]
    public async Task OldCallback_DoesNotLaunch_IntoANewSeatWithSameId()
    {
        var options = new MultiSeatOptions
        {
            LaunchOnConnectDelayMs = 200,
            LaunchOnConnect = [new LaunchOnConnectApp { Path = @"C:\never\invoked.exe" }],
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-stale-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };
        var path = TempLog(options, "TestAccount", Line(Connect));
        var liveSeats = new Dictionary<Guid, (bool Exists, int SessionId)>();
        var (launcher, injector) = BuildLauncher(options, id => liveSeats.TryGetValue(id, out var s) && s.Exists ? s.SessionId : null);
        try
        {
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };
            liveSeats[seat.Id] = (true, 1);

            launcher.ProcessSeat(seat, CancellationToken.None);

            Append(path, Line(Connect));
            // Second tick fires OnConnect.
            launcher.ProcessSeat(seat, CancellationToken.None);

            // Tear down the OLD lifecycle, then re-create a NEW seat with the SAME Guid.
            launcher.Forget(seat.Id);
            liveSeats.Remove(seat.Id);

            var newSeat = new SeatInfo { AccountName = "TestAccount", SessionId = 5, Status = SeatStatus.Ready };
            liveSeats[newSeat.Id] = (true, 5);

            await Task.Delay(500);

            Assert.Empty(injector.Launches);
        }
        finally { Cleanup(options, path); }
    }

    private static void Cleanup(MultiSeatOptions options, string path)
    {
        try { Directory.Delete(options.ApolloConfigDir, recursive: true); } catch { /* best effort */ }
        try { File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>Minimal ILogger implementation for unit tests.</summary>
    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        { }
    }

    /// <summary>
    /// Records every LaunchInSessionAsync call so tests can assert on the actual
    /// captured sessionId/exePath arguments. Used because Moq cannot match against
    /// methods that take optional parameters in expression trees.
    /// </summary>
    private sealed class RecordingInjector : ProcessInjector
    {
        public List<LaunchCall> Launches { get; } = new();

        public RecordingInjector(MultiSeatOptions options) : base(
            NullLogger<ProcessInjector>.Instance,
            Options.Create(options),
            new SessionLauncher(
                new TestLogger<SessionLauncher>(),
                Options.Create(options),
                Mock.Of<IAccountManager>()))
        { }

        public override Task<int> LaunchInSessionAsync(
            int sessionId,
            string accountName,
            string exePath,
            string? arguments = null,
            string? workingDir = null,
            CancellationToken ct = default,
            bool allowConsoleSession = false)
        {
            // Use a thread-safe add — multiple background tasks may overlap.
            lock (Launches) Launches.Add(new LaunchCall(sessionId, accountName, exePath, arguments, workingDir));
            return Task.FromResult(0);
        }

        public override Task<int> LaunchApolloInSessionAsync(
            int sessionId,
            string accountName,
            string apolloExePath,
            string configPath,
            CancellationToken ct)
            => Task.FromResult(0);

        public sealed record LaunchCall(
            int SessionId,
            string AccountName,
            string ExePath,
            string? Arguments,
            string? WorkingDir);
    }
}