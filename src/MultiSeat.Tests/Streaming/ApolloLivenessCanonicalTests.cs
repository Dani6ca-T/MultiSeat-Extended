using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// G7 regression: canonicalize Apollo liveness on PID + StartedAt.
///
/// <see cref="ApolloManager.IsAlive(Guid)"/> already compared the registered identity via
/// <see cref="WindowsProcessTracker"/>, but two Apollo-specific liveness paths still used a
/// different discipline: <see cref="ApolloInstance.IsAlive"/> checked the raw PID only (so a
/// recycled PID counted as a live Apollo in <see cref="ApolloManager.RunningInstanceCount"/>),
/// and the process monitor attributed exits with a ±2 s fuzzy start-time window.
///
/// These drive the real liveness predicates against real OS processes. A live `ping` child
/// stands in for Apollo; a recorded identity with a WRONG start time for the same live PID
/// is exactly the PID-reuse state (per the ProcessIdentity invariant, same PID + different
/// StartedAt = different instance) — deterministic, with no need to wait for natural PID
/// recycling. Every victim is a test-owned child reaped via its own held handle in finally;
/// nothing else is ever touched.
///
/// Liveness is deliberately NOT readiness here: the victim is ping.exe, which never answers
/// serverinfo, yet a matching identity must still read alive (G4 owns readiness; G7 only
/// answers "is this still the process we launched?").
/// </summary>
public class ApolloLivenessCanonicalTests
{
    [Fact]
    public void Instance_MatchingIdentity_ReportsAlive()
    {
        // Characterization: the recorded Apollo instance really is that PID — alive.
        using var victim = SpawnVictim();
        var instance = MakeInstance(TrueIdentityOf(victim), victim.Id);

        Assert.True(instance.IsAlive);
    }

    [Fact]
    public void Instance_ReusedPid_ReportsDead()
    {
        // The bug: this PID is alive but started later than the recorded Apollo — Windows
        // recycled the dead Apollo's PID. A raw PID check answers "alive" (false positive);
        // the canonical identity check must answer dead.
        using var victim = SpawnVictim();
        var instance = MakeInstance(StaleIdentityOf(victim), victim.Id);

        Assert.False(instance.IsAlive);
    }

    [Fact]
    public void Instance_ExitedProcess_ReportsDead()
    {
        using var victim = SpawnVictim();
        var instance = MakeInstance(TrueIdentityOf(victim), victim.Id);
        KillOwned(victim);

        Assert.False(instance.IsAlive);
    }

    [Fact]
    public void Instance_MissingPid_ReportsDead()
    {
        var instance = MakeInstance(
            new ProcessIdentity(int.MaxValue, DateTimeOffset.UtcNow), int.MaxValue);

        Assert.False(instance.IsAlive);
    }

    [Fact]
    public void Manager_IsAlive_MatchingIdentity_ReportsAlive()
    {
        // No serverinfo probe is involved: the victim never serves, yet liveness is true.
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, TrueIdentityOf(victim), victim.Id);

        Assert.True(manager.IsAlive(seatId));
    }

    [Fact]
    public void Manager_IsAlive_ReusedPid_ReportsDead()
    {
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, StaleIdentityOf(victim), victim.Id);

        Assert.False(manager.IsAlive(seatId));
    }

    [Fact]
    public void Manager_IsAlive_ExitedApollo_ReportsDead()
    {
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, TrueIdentityOf(victim), victim.Id);
        KillOwned(victim);

        Assert.False(manager.IsAlive(seatId));
    }

    [Fact]
    public void RunningInstanceCount_CountsLiveApollo()
    {
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, TrueIdentityOf(victim), victim.Id);

        Assert.Equal(1, manager.RunningInstanceCount);
    }

    [Fact]
    public void RunningInstanceCount_ExcludesReusedPid()
    {
        // A stale record whose PID now names an unrelated live process must not count as
        // a running Apollo instance.
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, StaleIdentityOf(victim), victim.Id);

        Assert.Equal(0, manager.RunningInstanceCount);
    }

    [Fact]
    public void RunningInstanceCount_ExcludesDeadApollo()
    {
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, TrueIdentityOf(victim), victim.Id);
        KillOwned(victim);

        Assert.Equal(0, manager.RunningInstanceCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// A test-owned stand-in for the Apollo process: exits by itself after ~30 s
    /// (backstop), and is always reaped via its own held handle in finally
    /// (using declaration) so no stray survives a failure.
    /// </summary>
    private static Process SpawnVictim()
    {
        var proc = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
        Assert.NotNull(proc);
        return proc!;
    }

    private static void KillOwned(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill();
                proc.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Identity matching the live victim (what launch registration records).</summary>
    private static ProcessIdentity TrueIdentityOf(Process proc) =>
        new(proc.Id, ApolloManager.GetProcessStartTime(proc.Id)
            ?? throw new Xunit.Sdk.XunitException("Victim process must have a start time"));

    /// <summary>
    /// Identity for the same live PID but a different start time — exactly the state
    /// a recycled PID is in (mirrors the G6 tests' AddHours(-1) seam).
    /// </summary>
    private static ProcessIdentity StaleIdentityOf(Process proc) =>
        new(proc.Id, TrueIdentityOf(proc).StartedAt.AddHours(-1));

    private static ApolloInstance MakeInstance(ProcessIdentity identity, int processId) =>
        new(
            SeatId: Guid.NewGuid(),
            Identity: identity,
            ProcessId: processId,
            ConfigPath: @"C:\never\sunshine.conf",
            SessionId: 7,
            AccountName: "TestSeat",
            StartedAt: DateTimeOffset.UtcNow,
            RestartCount: 0);

    private static (ApolloManager manager, Guid seatId) NewManagerWithSeat()
    {
        var options = new MultiSeatOptions
        {
            PortBase = 48100,
            ApolloExePath = @"C:\nonexistent\Apollo.exe",
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"test-liveness-{Guid.NewGuid():N}")
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
            NullLogger<ProcessInjector>.Instance,
            Options.Create(options),
            sessionLauncher);

        var manager = new ApolloManager(
            new TestLogger<ApolloManager>(),
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            new WindowsProcessTracker(), // real tracker: canonical PID + start-time liveness
            Mock.Of<IProcessMonitor>());

        return (manager, Guid.NewGuid());
    }

    private static void SeedInstance(
        ApolloManager manager, Guid seatId, ProcessIdentity identity, int processId)
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
