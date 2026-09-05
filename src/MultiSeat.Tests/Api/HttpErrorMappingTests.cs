using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Api;
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

namespace MultiSeat.Tests.Api;

/// <summary>
/// G14/F4: API error-mapping coherence for the lifecycle boundary, driven through REAL
/// endpoint lambdas over loopback HTTP (same harness shape as
/// <c>HttpStatusSemanticsTests</c>):
/// <list type="bullet">
/// <item>gate acquisition timeout → 503 Service Unavailable (contention, not a crash);</item>
/// <item>seat removed while entering the lifecycle boundary → 404 Not Found;</item>
/// <item>DELETE of an unknown account → 404 Not Found.</item>
/// </list>
/// </summary>
public class HttpErrorMappingTests
{
    // ── F4-A: gate timeout → 503 ────────────────────────────────────

    [Fact]
    public async Task ApolloStop_WhenGateTimesOut_Returns503()
    {
        // Deterministic contention: the test holds the seat's gate (as a stuck lifecycle
        // holder would), so the endpoint's AcquireAsync runs the full 30 s
        // DefaultAcquisitionTimeout and throws TimeoutException. No sleeps on the test
        // side — the gate itself is the barrier.
        var seatId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var (mgr, gate, streaming) = NewSeatManager();
        SeedLiveSeat(mgr, seatId);
        await using var host = await StartHost(
            s => RegisterSeatServices(s, mgr, gate), SeatEndpoints.Map);
        try
        {
            using var held = await gate.AcquireAsync(seatId, CancellationToken.None);

            var response = await host.Client.PostAsync(
                $"/api/seats/{seatId}/apollo/stop", EmptyBody());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(0, streaming.StopCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── F4-B: removed seat → 404 ────────────────────────────────────

    [Fact]
    public async Task DisplayReset_WhenSeatRemovedWhileWaitingForGate_Returns404()
    {
        // The seat is present for the endpoint pre-check and the manager pre-gate lookup,
        // then a teardown removes it while the reset waits for the gate (H2 ordering).
        // Holding the gate first makes this deterministic: everything before the gate
        // await is synchronous, so after a short arrival margin the request is guaranteed
        // parked at AcquireAsync before the removal — no timing dependence on the outcome.
        var seatId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var (mgr, gate, _) = NewSeatManager();
        SeedLiveSeat(mgr, seatId);
        await using var host = await StartHost(
            s => RegisterSeatServices(s, mgr, gate), SeatEndpoints.Map);
        try
        {
            using var held = await gate.AcquireAsync(seatId, CancellationToken.None);
            var requestTask = host.Client.PostAsync(
                $"/api/seats/{seatId}/display/reset", EmptyBody());

            await Task.Delay(500); // arrival margin: handler is parked at the gate by now
            RemoveSeat(mgr, seatId);
            held.Dispose();

            var response = await requestTask;
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("removed", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── F4-C: DELETE unknown account → 404 ──────────────────────────

    [Fact]
    public async Task DeleteAccount_WhenUnknown_Returns404()
    {
        // Real AccountManager (read-only construction: NetUserEnum + persisted-store read).
        // DeleteAccount on an unknown name throws before any Win32 mutation or disk write,
        // so this exercises the true production mapping with a name that cannot exist.
        var accounts = new AccountManager(
            NullLogger<AccountManager>.Instance,
            Options.Create(new MultiSeatOptions()));
        await using var host = await StartHost(
            s => s.AddSingleton<IAccountManager>(accounts), AccountEndpoints.Map);

        // Short enough for the 20-char account-name validation, random enough to
        // never be a managed account.
        var ghost = $"Gh{Guid.NewGuid():N}"[..14];
        var response = await host.Client.DeleteAsync($"/api/accounts/{ghost}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not managed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteAccount_WhenBackendFails_Stays400()
    {
        // The 404 is only for "unknown account": a real backend failure (NetUserDel error)
        // must stay 400, mirroring the G12 CreateAccount_WhenBackendFails_Stays400 guard.
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(m => m.DeleteAccount(It.IsAny<string>()))
            .Throws(new InvalidOperationException("NetUserDel failed for 'Ghost': error 5"));
        await using var host = await StartHost(
            s => s.AddSingleton(accounts.Object), AccountEndpoints.Map);

        var response = await host.Client.DeleteAsync("/api/accounts/Ghost01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WhenNameInvalid_Stays400()
    {
        // Validation still runs first and keeps its own status.
        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        await using var host = await StartHost(
            s => s.AddSingleton(accounts.Object), AccountEndpoints.Map);

        // NOTE: "../evil" cannot be used here — HttpClient normalizes dot-segments in
        // the URL path before sending, so it would never reach the route.
        var response = await host.Client.DeleteAsync("/api/accounts/Bad!Name");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Harness ──────────────────────────────────────────────────────

    private static StringContent EmptyBody() => new("", Encoding.UTF8, "application/json");

    /// <summary>
    /// Same DI note as <c>HttpStatusSemanticsTests.RegisterSeatServices</c>: every
    /// service-typed parameter appearing anywhere in the mapped group must be registered,
    /// even when the exercised endpoint never touches it. The gate is shared with the
    /// test so contention/removal can be arranged deterministically.
    /// </summary>
    private static void RegisterSeatServices(IServiceCollection s, SeatManager mgr, SeatLifecycleGate gate)
    {
        s.AddSingleton(mgr);
        s.AddSingleton(gate);
        s.AddSingleton(_ => new SeatPresetStore(
            NullLogger<SeatPresetStore>.Instance,
            Path.Combine(Path.GetTempPath(), $"multiseat-test-presets-{Guid.NewGuid():N}.json")));
        s.AddSingleton(Mock.Of<ISessionLauncher>());
    }

    private static (SeatManager mgr, SeatLifecycleGate gate, RecordingStreamingProvider streaming) NewSeatManager()
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-apollo-errmap-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };
        var gate = new SeatLifecycleGate();

        var configBuilder = new ApolloConfigBuilder(
            new TestLogger<ApolloConfigBuilder>(), Options.Create(options));
        var serverQuery = new ApolloServerQuery(new TestLogger<ApolloServerQuery>());
        var accounts = Mock.Of<IAccountManager>();
        var realLauncher = new SessionLauncher(
            new TestLogger<SessionLauncher>(), Options.Create(options), accounts);
        var processInjector = new ProcessInjector(
            new TestLogger<ProcessInjector>(), Options.Create(options), realLauncher);
        var display = new RecordingDisplayManager();
        var streaming = new RecordingStreamingProvider();
        var apolloManager = new ApolloManager(
            new TestLogger<ApolloManager>(), Options.Create(options), configBuilder,
            processInjector, serverQuery, Mock.Of<IProcessTracker>(), Mock.Of<IProcessMonitor>());

        var controllerManager = new ControllerManager(new TestLogger<ControllerManager>());
        var mgr = new SeatManager(
            NullLogger<SeatManager>.Instance,
            Options.Create(options),
            accounts,
            Mock.Of<ISessionLauncher>(),
            processInjector,
            display,
            streaming,
            apolloManager,
            new PortAllocator(),
            new FirewallManager(new TestLogger<FirewallManager>(), Options.Create(options)),
            new AudioRouter(new TestLogger<AudioRouter>(), Options.Create(options),
                new AudioDeviceEnumerator(new TestLogger<AudioDeviceEnumerator>()), processInjector),
            controllerManager,
            new InputRouter(new TestLogger<InputRouter>(), controllerManager),
            new InputHookManager(new TestLogger<InputHookManager>(), Options.Create(options)),
            new HidHideConfigurator(new TestLogger<HidHideConfigurator>(), Options.Create(options)),
            new OnConnectAppLauncher(new TestLogger<OnConnectAppLauncher>(), Options.Create(options),
                apolloManager, processInjector),
            Array.Empty<IEmulatorConfigSeeder>(),
            gate);

        return (mgr, gate, streaming);
    }

    private static void SeedLiveSeat(SeatManager mgr, Guid seatId)
    {
        var seats = (ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(seats.TryAdd(seatId, new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = SeatStatus.Ready,
            SessionId = 0
        }), "Test setup: seat seeding should succeed");
    }

    private static void RemoveSeat(SeatManager mgr, Guid seatId)
    {
        // Mirror teardown's TryRemove without running the teardown pipeline.
        var seats = (ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(seats.TryRemove(seatId, out _),
            "Test setup: seat should be registered before removal");
    }

    private static async Task<TestHost> StartHost(
        Action<IServiceCollection> register, Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        register(builder.Services);
        var app = builder.Build();
        map(app);
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return new TestHost(app);
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }

        public TestHost(WebApplication app)
        {
            _app = app;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class RecordingStreamingProvider : IStreamingProvider
    {
        private int _stopCount;
        public int StopCount => _stopCount;

        public Task<int> StartAsync(SeatInfo seat, CancellationToken ct) => Task.FromResult(1111);
        public void Stop(SeatInfo seat) => System.Threading.Interlocked.Increment(ref _stopCount);
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
