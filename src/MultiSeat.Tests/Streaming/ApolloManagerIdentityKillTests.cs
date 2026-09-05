using System.Collections.Concurrent;
using System.Diagnostics;
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
/// G6 regression: Apollo liveness is identity-aware (PID + StartedAt), but
/// KillForReconnect and Stop terminated by raw PID. If Apollo exited and Windows
/// recycled its PID onto an unrelated process, the kill landed on the stranger.
///
/// These drive the real KillForReconnect/Stop against real OS processes. A live
/// `ping` child stands in for Apollo; a recorded identity with a WRONG start time
/// for the same live PID is exactly the PID-reuse state (per the ProcessIdentity
/// invariant, same PID + different StartedAt = different instance) — deterministic,
/// with no need to wait for natural PID recycling. Every victim is a test-owned
/// child killed via its own held handle in finally; nothing else is ever touched.
/// </summary>
public class ApolloManagerIdentityKillTests
{
    [Fact]
    public void KillForReconnect_MatchingIdentity_TerminatesApollo()
    {
        // Characterization: the normal path must keep working — the recorded Apollo
        // instance is really that PID, so reconnect kills it.
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, TrueIdentityOf(victim), victim.Id);
        var seat = SeatFor(seatId, victim.Id);

        manager.KillForReconnect(seat);

        Assert.True(WaitForExit(victim, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void KillForReconnect_ReusedPid_Survives()
    {
        // The bug: this PID is alive but started later than the recorded Apollo —
        // Windows recycled the dead Apollo's PID. Killing it would murder a stranger.
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, StaleIdentityOf(victim), victim.Id);
        var seat = SeatFor(seatId, victim.Id);

        manager.KillForReconnect(seat);

        Assert.False(victim.HasExited);
    }

    [Fact]
    public void KillForReconnect_ExitedApollo_NoThrow()
    {
        // Idempotency: Apollo already gone (PID free) must stay a quiet no-op.
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, TrueIdentityOf(victim), victim.Id);
        KillOwned(victim);

        var ex = Record.Exception(() => manager.KillForReconnect(SeatFor(seatId, victim.Id)));

        Assert.Null(ex);
    }

    [Fact]
    public void Stop_MissingIdentityRecord_DoesNotKill()
    {
        // No instance record means no attributable identity. A raw-PID kill here is
        // PID-reuse roulette, so Stop must fail closed and leave the PID alone.
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        var seat = SeatFor(seatId, victim.Id); // no SeedInstance call

        manager.Stop(seat);

        Assert.False(victim.HasExited);
    }

    [Fact]
    public void Stop_ReusedPid_Survives()
    {
        // Same recycled-PID state through the Stop path: the record names a dead
        // Apollo, the live PID belongs to someone else now.
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, StaleIdentityOf(victim), victim.Id);

        manager.Stop(SeatFor(seatId, victim.Id));

        Assert.False(victim.HasExited);
    }

    [Fact]
    public void Stop_MatchingIdentity_TerminatesApollo()
    {
        // Characterization: normal Stop still terminates the recorded instance.
        using var victim = SpawnVictim();
        var (manager, seatId) = NewManagerWithSeat();
        SeedInstance(manager, seatId, TrueIdentityOf(victim), victim.Id);

        manager.Stop(SeatFor(seatId, victim.Id));

        Assert.True(WaitForExit(victim, TimeSpan.FromSeconds(5)));
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

    private static bool WaitForExit(Process proc, TimeSpan timeout)
    {
        try { return proc.WaitForExit((int)timeout.TotalMilliseconds); }
        catch { return proc.HasExited; }
    }

    /// <summary>Identity matching the live victim (what launch registration records).</summary>
    private static ProcessIdentity TrueIdentityOf(Process proc) =>
        new(proc.Id, ApolloManager.GetProcessStartTime(proc.Id)
            ?? throw new Xunit.Sdk.XunitException("Victim process must have a start time"));

    /// <summary>
    /// Identity for the same live PID but a different start time — exactly the state
    /// a recycled PID is in (mirrors the liveness tests' AddHours(-1) seam).
    /// </summary>
    private static ProcessIdentity StaleIdentityOf(Process proc) =>
        new(proc.Id, TrueIdentityOf(proc).StartedAt.AddHours(-1));

    private static (ApolloManager manager, Guid seatId) NewManagerWithSeat()
    {
        var options = new MultiSeatOptions
        {
            PortBase = 48100,
            ApolloExePath = @"C:\nonexistent\Apollo.exe",
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"test-kill-{Guid.NewGuid():N}")
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
            Mock.Of<IProcessTracker>(),
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

    private static SeatInfo SeatFor(Guid seatId, int streamingPid) => new()
    {
        Id = seatId,
        AccountName = "TestSeat",
        Status = SeatStatus.Ready,
        SessionId = 7,
        PortBase = 48100,
        StreamingProcessId = streamingPid
    };

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
