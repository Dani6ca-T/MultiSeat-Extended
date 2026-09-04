using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Moq;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// Tests for the ApolloManager instance-record lifecycle as observed through the public
/// IStreamingProvider surface — in particular GetUptime.
///
/// Coverage is deliberately limited to what is reachable WITHOUT launching a real Apollo
/// process: the record-absent cases. Creating an instance record requires either a real
/// Windows session + real Apollo launch (StartAsync / successful RestartAsync) or a
/// test-only seam into the private _instances dictionary, and neither is introduced here
/// — see the checkpoint report for the full testability audit.
/// </summary>
public class ApolloManagerUptimeTests
{
    private readonly ApolloManager _manager;

    public ApolloManagerUptimeTests()
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
        var serverQuery = new ApolloServerQuery(
            new TestLogger<ApolloServerQuery>());

        var sessionLauncher = new SessionLauncher(
            new TestLogger<SessionLauncher>(),
            Options.Create(options),
            Mock.Of<IAccountManager>());
        var processInjector = new ProcessInjector(
            new TestLogger<ProcessInjector>(),
            Options.Create(options),
            sessionLauncher);

        _manager = new ApolloManager(
            new TestLogger<ApolloManager>(),
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());
    }

    private static SeatInfo NewSeat() => new()
    {
        AccountName = "TestSeat",
        SessionId = 0,
        StreamingProcessId = 0,
        PortBase = 48100
    };

    // ── GetUptime: record-absent cases ─────────────────────────────────

    [Fact]
    public void GetUptime_UnknownSeat_ReturnsNull()
    {
        // A seat that never had an instance record has no uptime to report.
        Assert.Null(_manager.GetUptime(Guid.NewGuid()));
    }

    [Fact]
    public void LifecycleNoOps_WithoutInstanceRecord_DoNotCreateUptime()
    {
        var seat = NewSeat();

        // KillForReconnect and Stop both no-op safely when no instance record exists
        // (no process to kill, nothing to unregister, nothing to remove).
        _manager.KillForReconnect(seat);
        _manager.Stop(seat);

        // The record is still absent — uptime stays null rather than being invented.
        Assert.Null(_manager.GetUptime(seat.Id));
    }

    [Fact]
    public async Task RestartAsync_WithoutInstanceRecord_FallsBackToStartAndFailsWithoutRecord()
    {
        var seat = NewSeat();

        // With no instance record, RestartAsync falls back to StartAsync. With Apollo not
        // installed that start fails cleanly — returns -1, launches nothing, throws nothing
        // — and, crucially, creates no instance record, so GetUptime stays null.
        var pid = await _manager.RestartAsync(seat, CancellationToken.None);

        Assert.Equal(-1, pid);
        Assert.Null(_manager.GetUptime(seat.Id));
    }
}