using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
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
/// Regression tests for the OnConnect disconnect kill path: a client disconnect must
/// terminate a launched app ONLY while its PID still denotes the exact process instance
/// that was launched (PID + start time match). A recycled PID must never cause an
/// unrelated process to be killed.
///
/// Launch already records <see cref="ProcessIdentity"/> (PID + start time); the bug was
/// that OnDisconnect ignored the start time and killed by raw PID.
/// </summary>
public class OnConnectDisconnectIdentityTests
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

    private static MultiSeatOptions NewOptions(bool killOnDisconnect, int delayMs = 50) => new()
    {
        LaunchOnConnectDelayMs = delayMs,
        LaunchOnConnect = [new LaunchOnConnectApp { Path = @"C:\never\invoked.exe" }],
        KillLaunchOnConnectAppsOnDisconnect = killOnDisconnect,
        ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-disckill-{Guid.NewGuid():N}"),
        ApolloExePath = @"C:\never\apollo.exe"
    };

    private static ApolloManager BuildApolloManager(MultiSeatOptions options)
    {
        var configBuilder = new ApolloConfigBuilder(
            new TestLogger<ApolloConfigBuilder>(), Options.Create(options));
        var serverQuery = new ApolloServerQuery(new TestLogger<ApolloServerQuery>());
        var sessionLauncher = new SessionLauncher(
            new TestLogger<SessionLauncher>(), Options.Create(options), Mock.Of<IAccountManager>());
        var processInjector = new ProcessInjector(
            new TestLogger<ProcessInjector>(), Options.Create(options), sessionLauncher);
        return new ApolloManager(
            new TestLogger<ApolloManager>(), Options.Create(options), configBuilder,
            processInjector, serverQuery,
            Mock.Of<IProcessTracker>(), Mock.Of<IProcessMonitor>());
    }

    private static OnConnectAppLauncher BuildLauncher(
        MultiSeatOptions options, ProcessInjector injector, Func<Guid, int?>? sessionLookup = null) =>
        new(NullLogger<OnConnectAppLauncher>.Instance, Options.Create(options),
            BuildApolloManager(options), injector, sessionLookup);

    /// <summary>A durable local process standing in for the on-connect launched app.</summary>
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

    /// <summary>Fire a connect edge and wait for the delayed launch to complete.</summary>
    private static async Task FireConnectAndWait(OnConnectAppLauncher launcher, SeatInfo seat, string logPath)
    {
        launcher.ProcessSeat(seat, CancellationToken.None); // tick 1: seed (no edge)
        File.AppendAllText(logPath, Line(Connect));
        launcher.ProcessSeat(seat, CancellationToken.None); // tick 2: connect edge → detached launch
        await Task.Delay(600); // delay (50 ms) + launch must have fired
    }

    private static void FireDisconnect(OnConnectAppLauncher launcher, SeatInfo seat, string logPath)
    {
        File.AppendAllText(logPath, Line(Disconnect));
        launcher.ProcessSeat(seat, CancellationToken.None); // tick 3: disconnect edge → synchronous kill
    }

    /// <summary>Replace the recorded start time (simulates PID reuse: same PID, different instance).</summary>
    private static void StaleRecordedStartTime(OnConnectAppLauncher launcher, Guid seatId, DateTimeOffset staleAt)
    {
        var statesField = typeof(OnConnectAppLauncher)
            .GetField("_states", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var states = (ConcurrentDictionary<Guid, OnConnectAppLauncher.SeatConnState>)
            statesField.GetValue(launcher)!;
        var state = states[seatId];
        lock (state.Gate)
        {
            Assert.Single(state.Launched);
            state.Launched[0] = new ProcessIdentity(state.Launched[0].ProcessId, staleAt);
        }
    }

    /// <summary>Record an identity directly (for the already-exited case).</summary>
    private static void RecordIdentity(OnConnectAppLauncher launcher, Guid seatId, ProcessIdentity identity)
    {
        var statesField = typeof(OnConnectAppLauncher)
            .GetField("_states", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var states = (ConcurrentDictionary<Guid, OnConnectAppLauncher.SeatConnState>)
            statesField.GetValue(launcher)!;
        var state = states[seatId];
        lock (state.Gate)
        {
            state.Launched.Add(identity);
            // Simulate a prior connect so the appended DISCONNECT below produces a
            // real connected→disconnected edge (otherwise no edge fires and
            // OnDisconnect is never invoked).
            state.Connected = true;
        }
    }

    private static void Cleanup(MultiSeatOptions options)
    {
        try { Directory.Delete(options.ApolloConfigDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Disconnect_MatchingIdentity_KillsLaunchedApp()
    {
        var options = NewOptions(killOnDisconnect: true);
        var sleeper = SpawnSleeper();
        try
        {
            var injector = new FixedPidInjector(options, sleeper.Id);
            var launcher = BuildLauncher(options, injector);
            var path = TempLog(options, "TestAccount");
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };

            await FireConnectAndWait(launcher, seat, path);
            var identity = Assert.Single(launcher.GetLaunchedProcesses(seat.Id));
            Assert.Equal(sleeper.Id, identity.ProcessId);

            FireDisconnect(launcher, seat, path);

            Assert.True(sleeper.WaitForExit(5000),
                "A launched app whose identity still matches must be terminated on disconnect");
            Assert.Empty(launcher.GetLaunchedProcesses(seat.Id));
        }
        finally
        {
            KillQuietly(sleeper);
            Cleanup(options);
        }
    }

    [Fact]
    public async Task Disconnect_RecycledPid_DoesNotKillUnrelatedProcess()
    {
        var options = NewOptions(killOnDisconnect: true);
        var other = SpawnSleeper();
        try
        {
            var injector = new FixedPidInjector(options, other.Id);
            var launcher = BuildLauncher(options, injector);
            var path = TempLog(options, "TestAccount");
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };

            await FireConnectAndWait(launcher, seat, path);
            Assert.Single(launcher.GetLaunchedProcesses(seat.Id));

            // The original app exited and the OS recycled its PID onto a different
            // process: same PID, different start time.
            StaleRecordedStartTime(launcher, seat.Id, other.StartTime.ToUniversalTime().AddHours(-1));

            FireDisconnect(launcher, seat, path);

            Thread.Sleep(300); // give any (wrong) kill attempt a moment to happen
            Assert.False(other.HasExited,
                "A recycled PID must never cause an unrelated process to be terminated");
        }
        finally
        {
            KillQuietly(other);
            Cleanup(options);
        }
    }

    [Fact]
    public async Task Disconnect_AlreadyExitedApp_IsHandledSafely()
    {
        var options = NewOptions(killOnDisconnect: true);
        using var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        var startedAt = gone.StartTime.ToUniversalTime();
        Assert.True(gone.WaitForExit(5000));
        gone.Dispose();

        var injector = new FixedPidInjector(options, 0); // launch path unused here
        var launcher = BuildLauncher(options, injector);
        var path = TempLog(options, "TestAccount");
        var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };

        launcher.ProcessSeat(seat, CancellationToken.None); // tick 1: seed (no edge)
        RecordIdentity(launcher, seat.Id, new ProcessIdentity(1234567, startedAt));

        // Must not throw; the exited entry is dropped as successful cleanup.
        FireDisconnect(launcher, seat, path);
        Assert.Empty(launcher.GetLaunchedProcesses(seat.Id));
        await Task.CompletedTask;

        Cleanup(options);
    }

    [Fact]
    public async Task Disconnect_KillDisabled_LeavesAppRunning()
    {
        var options = NewOptions(killOnDisconnect: false);
        var sleeper = SpawnSleeper();
        try
        {
            var injector = new FixedPidInjector(options, sleeper.Id);
            var launcher = BuildLauncher(options, injector);
            var path = TempLog(options, "TestAccount");
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };

            await FireConnectAndWait(launcher, seat, path);
            Assert.Single(launcher.GetLaunchedProcesses(seat.Id));

            FireDisconnect(launcher, seat, path);

            Thread.Sleep(300);
            Assert.False(sleeper.HasExited,
                "With kill-on-disconnect disabled the app must keep running");
            Assert.Single(launcher.GetLaunchedProcesses(seat.Id));
        }
        finally
        {
            KillQuietly(sleeper);
            Cleanup(options);
        }
    }

    [Fact]
    public async Task DelayedLaunch_SessionIdChanged_PreventsLaunch()
    {
        // a461885 protection intact: a session replacement during the settle delay
        // must still suppress the launch (kill-enabled options must not disturb it).
        var options = NewOptions(killOnDisconnect: true, delayMs: 200);
        var path = TempLog(options, "TestAccount");
        var liveSeats = new Dictionary<Guid, (bool Exists, int SessionId)>();
        var injector = new RecordingInjector(options);
        var launcher = BuildLauncher(options, injector,
            id => liveSeats.TryGetValue(id, out var s) && s.Exists ? s.SessionId : (int?)null);
        try
        {
            var seat = new SeatInfo { AccountName = "TestAccount", SessionId = 1 };
            liveSeats[seat.Id] = (true, 1);

            launcher.ProcessSeat(seat, CancellationToken.None);
            File.AppendAllText(path, Line(Connect));
            launcher.ProcessSeat(seat, CancellationToken.None);

            liveSeats[seat.Id] = (true, 2); // session replaced during the delay

            await Task.Delay(600);

            Assert.Empty(injector.Launches);
        }
        finally
        {
            Cleanup(options);
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

    /// <summary>ProcessInjector override returning a predetermined PID.</summary>
    private sealed class FixedPidInjector : ProcessInjector
    {
        private readonly int _pid;
        public FixedPidInjector(MultiSeatOptions options, int pid)
            : base(NullLogger<ProcessInjector>.Instance, Options.Create(options),
                  new SessionLauncher(new TestLogger<SessionLauncher>(),
                      Options.Create(options), Mock.Of<IAccountManager>()))
        {
            _pid = pid;
        }

        public override Task<int> LaunchInSessionAsync(
            int sessionId, string accountName, string exePath,
            string? arguments = null, string? workingDir = null,
            CancellationToken ct = default, bool allowConsoleSession = false)
            => Task.FromResult(_pid);

        public override Task<int> LaunchApolloInSessionAsync(
            int sessionId, string accountName, string apolloExePath,
            string configPath, CancellationToken ct)
            => Task.FromResult(0);
    }

    private sealed class RecordingInjector : ProcessInjector
    {
        public List<int> Launches { get; } = new();
        public RecordingInjector(MultiSeatOptions options)
            : base(NullLogger<ProcessInjector>.Instance, Options.Create(options),
                  new SessionLauncher(new TestLogger<SessionLauncher>(),
                      Options.Create(options), Mock.Of<IAccountManager>()))
        { }

        public override Task<int> LaunchInSessionAsync(
            int sessionId, string accountName, string exePath,
            string? arguments = null, string? workingDir = null,
            CancellationToken ct = default, bool allowConsoleSession = false)
        {
            lock (Launches) Launches.Add(sessionId);
            return Task.FromResult(0);
        }

        public override Task<int> LaunchApolloInSessionAsync(
            int sessionId, string accountName, string apolloExePath,
            string configPath, CancellationToken ct)
            => Task.FromResult(0);
    }
}
