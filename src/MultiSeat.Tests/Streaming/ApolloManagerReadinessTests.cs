using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
/// G4 regression: a started Apollo process is not a ready Apollo server. StartAsync used
/// to report success (pid &gt; 0) as soon as the process existed, so a wedged-but-alive
/// Apollo read as healthy and no recovery ever fired for it.
///
/// These drive the real readiness wait on a real ApolloManager graph (same builder
/// pattern as the QueryHealth tests): liveness comes from a Moq IProcessTracker — the
/// established seam for OS process state — while servability is answered by real HTTP
/// against a loopback stub server (raw TCP, no URL reservations). No Apollo process,
/// no Win32 session, and no fake HTTP client is involved.
/// </summary>
public class ApolloManagerReadinessTests
{
    [Fact]
    public async Task AliveProcessWithNoServer_ReturnsFalse()
    {
        // The core invariant: process alive != servable. Nothing listens on the seat's
        // port (connection refused, fast), so the wait must exhaust its (short, test)
        // deadline and report not-ready rather than trusting the live process.
        var closedPort = ClosedLoopbackPort();
        var (manager, tracker) = BuildManager(alive: true);
        var seat = new SeatInfo
        {
            AccountName = "TestSeat",
            StreamingProcessId = 1234,
            PortBase = closedPort // OffsetGfeHttp is 0: the query goes here
        };

        var result = await manager.WaitForServerReadyAsync(
            seat, new ProcessIdentity(1234, DateTimeOffset.UtcNow),
            CancellationToken.None, deadline: TimeSpan.FromSeconds(2));

        Assert.False(result);
    }

    [Fact]
    public async Task AliveProcessWithAnsweringServer_ReturnsTrue()
    {
        // The success shape: a live process whose serverinfo answers is ready. Must
        // return on the first answering poll, not after the deadline.
        using var server = new StubServer();
        var (manager, tracker) = BuildManager(alive: true);
        var seat = new SeatInfo
        {
            AccountName = "TestSeat",
            StreamingProcessId = 1234,
            PortBase = server.Port
        };
        var sw = Stopwatch.StartNew();

        var result = await manager.WaitForServerReadyAsync(
            seat, new ProcessIdentity(1234, DateTimeOffset.UtcNow),
            CancellationToken.None, deadline: TimeSpan.FromSeconds(20));

        Assert.True(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task DeadProcess_ReturnsFalseImmediately()
    {
        // The liveness half of the invariant: a process that died while starting must
        // fail fast instead of waiting out the deadline querying a dead port.
        var closedPort = ClosedLoopbackPort();
        var (manager, tracker) = BuildManager(alive: false);
        var seat = new SeatInfo
        {
            AccountName = "TestSeat",
            StreamingProcessId = 1234,
            PortBase = closedPort
        };
        var sw = Stopwatch.StartNew();

        var result = await manager.WaitForServerReadyAsync(
            seat, new ProcessIdentity(1234, DateTimeOffset.UtcNow),
            CancellationToken.None, deadline: TimeSpan.FromSeconds(20));

        Assert.False(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledToken_Propagates()
    {
        // A pre-cancelled wait must throw rather than report either way: cancellation
        // is the caller's lifecycle signal (worker stop, teardown) and must not be
        // converted into a readiness verdict.
        using var server = new StubServer();
        var (manager, tracker) = BuildManager(alive: true);
        var seat = new SeatInfo
        {
            AccountName = "TestSeat",
            StreamingProcessId = 1234,
            PortBase = server.Port
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.WaitForServerReadyAsync(
                seat, new ProcessIdentity(1234, DateTimeOffset.UtcNow),
                cts.Token, deadline: TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public async Task CancelledMidWait_Propagates()
    {
        // Same, but canceled while the bounded wait is in flight (nothing listening, so
        // without cancellation this would run to its deadline).
        var closedPort = ClosedLoopbackPort();
        var (manager, tracker) = BuildManager(alive: true);
        var seat = new SeatInfo
        {
            AccountName = "TestSeat",
            StreamingProcessId = 1234,
            PortBase = closedPort
        };
        using var cts = new CancellationTokenSource(300);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.WaitForServerReadyAsync(
                seat, new ProcessIdentity(1234, DateTimeOffset.UtcNow),
                cts.Token, deadline: TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ReadyTimeout_MatchesStartupWindow()
    {
        // The deadline is not arbitrary: it is the codebase's own startup horizon. A
        // start that is not serving within ApolloStartupWindow is a startup failure by
        // the health check's own classification. Pin the coupling so the two horizons
        // cannot drift apart silently.
        Assert.Equal(SessionHealthCheck.ApolloStartupWindow, ApolloManager.ServerReadyTimeout);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (ApolloManager Manager, Mock<IProcessTracker> Tracker) BuildManager(bool alive)
    {
        var options = new MultiSeatOptions
        {
            PortBase = 48100,
            ApolloExePath = @"C:\nonexistent\Apollo.exe",
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"test-ready-{Guid.NewGuid():N}")
        };

        var configBuilder = new ApolloConfigBuilder(
            NullLogger<ApolloConfigBuilder>.Instance,
            Options.Create(options));
        var serverQuery = new ApolloServerQuery(NullLogger<ApolloServerQuery>.Instance);
        var sessionLauncher = new SessionLauncher(
            NullLogger<SessionLauncher>.Instance,
            Options.Create(options),
            Mock.Of<IAccountManager>());
        var processInjector = new ProcessInjector(
            NullLogger<ProcessInjector>.Instance,
            Options.Create(options),
            sessionLauncher);

        var tracker = new Mock<IProcessTracker>();
        tracker.Setup(t => t.IsAlive(It.IsAny<ProcessIdentity>())).Returns(alive);

        var manager = new ApolloManager(
            NullLogger<ApolloManager>.Instance,
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            tracker.Object,
            Mock.Of<IProcessMonitor>());

        return (manager, tracker);
    }

    /// <summary>
    /// A port on loopback that is guaranteed closed: borrowed from the OS and handed
    /// straight back, so connection attempts fail fast with "refused".
    /// </summary>
    private static int ClosedLoopbackPort()
    {
        using var server = new StubServer();
        return server.Port; // closed again by Dispose
    }

    /// <summary>
    /// Minimal real HTTP server on loopback: answers every connection with a canned
    /// Apollo serverinfo document, then closes. Raw TCP needs no URL reservations.
    /// </summary>
    private sealed class StubServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public int Port { get; }

        public StubServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                // Answer without reading the request first: both directions are a few
                // hundred bytes on loopback, so socket buffers guarantee progress with
                // no read/write rendezvous. (Reading first would trip CA2022 for an
                // exactness guarantee a stub drain does not need.)
                const string body =
                    "<root><hostname>stub</hostname><appversion>9.9</appversion>" +
                    "<state>SUNSHINE_SERVER_FREE</state><currentgame>0</currentgame></root>";
                var response =
                    "HTTP/1.1 200 OK\r\nContent-Type: text/xml\r\n" +
                    $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                    "Connection: close\r\n\r\n" + body;
                try
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _cts.Token);
                }
                catch { }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
