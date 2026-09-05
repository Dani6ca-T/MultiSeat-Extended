using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Moq;
using Xunit;

namespace MultiSeat.Tests.Monitoring;

/// <summary>
/// Regression tests for identity-aware Apollo liveness — the checkpoint that closes the
/// PID-reuse blind spot in the health-check restart decision.
///
/// Before the fix, both <see cref="ApolloManager.IsAlive"/> and SessionHealthCheck's Check 2
/// decided \"is Apollo running?\" with a raw <c>Process.GetProcessById(PID).HasExited</c>
/// lookup. If Apollo crashed and Windows recycled its PID onto a different, long-lived
/// process, that check answered \"alive\" forever: the seat never restarted and stayed broken.
/// The tracker had already recorded the launched Apollo's identity (PID + <b>start time</b>);
/// these tests prove the liveness decision now compares that identity against the OS.
///
/// Two layers are pinned:
///   1. <see cref="ApolloManager.IsAlive"/> — identity-aware, against a REAL
///      <see cref="WindowsProcessTracker"/> and the test host's own always-alive process.
///   2. <see cref="SessionHealthCheck.ApolloNeedsRestart"/> — the Check-2 decision seam that
///      drives the restart, mirroring the LaunchedAppHasExited pattern.
/// </summary>
public class ApolloIdentityLivenessTests
{
    // ── ApolloManager.IsAlive: identity-aware liveness (real OS identity) ──
    //
    // The test host process (Environment.ProcessId) stands in for the launched Apollo: it is
    // guaranteed to exist and stay alive for the whole test. Identity records are seeded into
    // the manager's private instance dictionary because creating one requires a real Apollo
    // launch; seeding is deterministic and touches no production seam.

    private static (ApolloManager manager, Guid seatId) NewManagerWithSeat()
    {
        var options = new MultiSeatOptions
        {
            PortBase = 48100,
            ApolloExePath = @"C:\nonexistent\Apollo.exe",
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}")
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

        var manager = new ApolloManager(
            new TestLogger<ApolloManager>(),
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            new WindowsProcessTracker(), // real tracker: IsAlive compares PID + OS start time
            Mock.Of<IProcessMonitor>());

        return (manager, Guid.NewGuid());
    }

    private static void SeedInstance(ApolloManager manager, Guid seatId, ProcessIdentity identity, int processId)
    {
        var instances = (ConcurrentDictionary<Guid, ApolloInstance>)typeof(ApolloManager)
            .GetField("_instances", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(manager)!;

        instances[seatId] = new ApolloInstance(
            SeatId: seatId,
            Identity: identity,
            ProcessId: processId,
            ConfigPath: @"C:\never\sunshine.conf",
            SessionId: 7,
            AccountName: "TestSeat",
            StartedAt: DateTimeOffset.UtcNow,
            RestartCount: 0);
    }

    [Fact]
    public void IsAlive_MatchingPidAndStartedAt_ReturnsTrue()
    {
        // The identity recorded at launch matches the OS process (same PID + same start time)
        // — the original Apollo is alive, so the health check must NOT restart it.
        var pid = Environment.ProcessId;
        var startedAt = ApolloManager.GetProcessStartTime(pid)
            ?? throw new Xunit.Sdk.XunitException("Test host process must have a start time");
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, new ProcessIdentity(pid, startedAt), processId: pid);

        Assert.True(manager.IsAlive(seatId));
    }

    [Fact]
    public void IsAlive_PidReused_SamePidDifferentStartedAt_ReportsDead()
    {
        // The core PID-reuse case: the OS process at this PID started at a DIFFERENT time than
        // the registered identity — Windows recycled the dead Apollo's PID onto another
        // process. A raw PID check would say "alive" forever and the seat would never recover;
        // the identity check must report the original Apollo dead so the restart fires.
        var pid = Environment.ProcessId;
        var realStart = ApolloManager.GetProcessStartTime(pid)
            ?? throw new Xunit.Sdk.XunitException("Test host process must have a start time");
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId,
            new ProcessIdentity(pid, realStart.AddHours(-1)), processId: pid);

        Assert.False(manager.IsAlive(seatId));
    }

    [Fact]
    public void IsAlive_MissingPid_ReportsDead()
    {
        // The process no longer exists at all — GetProcessById throws, tracker.IsAlive
        // translates that to "not alive", and the health check restarts.
        const int missingPid = int.MaxValue;
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId,
            new ProcessIdentity(missingPid, DateTimeOffset.UtcNow), processId: missingPid);

        Assert.False(manager.IsAlive(seatId));
    }

    [Fact]
    public void IsAlive_NoInstanceRecord_ReturnsFalse()
    {
        // A seat with no instance record (never started, or already stopped) has no Apollo.
        var (manager, seatId) = NewManagerWithSeat();
        Assert.False(manager.IsAlive(seatId));
    }

    [Fact]
    public void IsAlive_InstanceParkedAtProcessIdZero_ReturnsFalse()
    {
        // KillForReconnect parks the record at ProcessId 0 while preserving the old identity;
        // that record is not alive until a restart re-registers a new process.
        var pid = Environment.ProcessId;
        var startedAt = ApolloManager.GetProcessStartTime(pid)
            ?? throw new Xunit.Sdk.XunitException("Test host process must have a start time");
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, new ProcessIdentity(pid, startedAt), processId: 0);

        Assert.False(manager.IsAlive(seatId));
    }

    // ── SessionHealthCheck.ApolloNeedsRestart: the Check-2 restart decision ──
    //
    // Check 2 consults this seam with the provider's identity-aware IsAlive. The provider
    // reports "dead" exactly when the registered identity no longer matches the OS (tests
    // above), so these cases prove: identity mismatch → restart; identity match → no restart.

    private static SeatInfo SeatInState(SeatStatus status, int streamingPid) => new()
    {
        AccountName = "TestSeat",
        Status = status,
        StreamingProcessId = streamingPid
    };

    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    public void ApolloNeedsRestart_ProviderReportsDead_Restarts(SeatStatus status)
    {
        // Apollo identity no longer matches the OS (crashed, or PID recycled): the provider
        // says dead, so Check 2 must restart the seat's Apollo.
        Assert.True(SessionHealthCheck.ApolloNeedsRestart(
            SeatInState(status, streamingPid: 42), _ => false));
    }

    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    public void ApolloNeedsRestart_ProviderReportsAlive_DoesNotRestart(SeatStatus status)
    {
        // Apollo identity still matches the OS: the provider says alive, so Check 2 must
        // leave it alone (no double-launch).
        Assert.False(SessionHealthCheck.ApolloNeedsRestart(
            SeatInState(status, streamingPid: 42), _ => true));
    }

    [Fact]
    public void ApolloNeedsRestart_NoProcessRecorded_DoesNotRestart()
    {
        // A seat with PID 0 never has an Apollo to restart (mirrors the pre-existing guard —
        // Check 2 only acts when a process was actually started).
        Assert.False(SessionHealthCheck.ApolloNeedsRestart(
            SeatInState(SeatStatus.Ready, streamingPid: 0), _ => false));
    }

    [Theory]
    [InlineData(SeatStatus.Connecting)]
    [InlineData(SeatStatus.Error)]
    [InlineData(SeatStatus.Configuring)]
    public void ApolloNeedsRestart_NonOperationalState_DoesNotRestart(SeatStatus status)
    {
        // Recovery owns only Ready/Streaming seats. Connecting is mid-recovery (its own
        // reconnect path handles Apollo), Error is parked for manual action, Configuring is
        // mid-provision — none should get an independent auto-restart.
        Assert.False(SessionHealthCheck.ApolloNeedsRestart(
            SeatInState(status, streamingPid: 42), _ => false));
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
