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
using MultiSeat.Service;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Api;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Api;

/// <summary>
/// G12: capacity exhaustion and duplicate/conflict rejections must use their own statuses
/// instead of collapsing into 400 alongside validation errors:
/// <list type="bullet">
/// <item>no capacity remains (seat limit, port blocks) → 503 Service Unavailable;</item>
/// <item>request conflicts with existing state (account already has a seat, account already
/// exists/linked) → 409 Conflict;</item>
/// <item>bad input stays 400, missing resources stay 404.</item>
/// </list>
/// These drive the REAL endpoint lambdas over loopback HTTP: a minimal host maps the
/// production <c>SeatEndpoints</c>/<c>AccountEndpoints</c> with a real <see cref="SeatManager"/>
/// whose heavy subsystems are never reached on the rejected paths (every asserted rejection
/// throws before session/port/lifecycle work), or a mocked <see cref="IAccountManager"/>.
/// </summary>
public class HttpStatusSemanticsTests
{
    // ── POST /api/seats ─────────────────────────────────────────────

    [Fact]
    public async Task Provision_WhenSeatLimitReached_Returns503()
    {
        var mgr = NewSeatManager(maxSeats: 0, accountExists: true);
        await using var host = await StartHost(
            s => RegisterSeatServices(s, mgr), SeatEndpoints.Map);

        var response = await host.Client.PostAsync("/api/seats",
            JsonBody(new { accountName = "Seat01", width = 1920, height = 1080, fps = 60 }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Maximum seat count", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Provision_WhenAccountAlreadyHasSeat_Returns409()
    {
        var mgr = NewSeatManager(maxSeats: 4, accountExists: true);
        SeedLiveSeat(mgr, "DupSeat");
        await using var host = await StartHost(
            s => RegisterSeatServices(s, mgr), SeatEndpoints.Map);

        var response = await host.Client.PostAsync("/api/seats",
            JsonBody(new { accountName = "DupSeat", width = 1920, height = 1080, fps = 60 }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already has a seat", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Provision_WhenAccountMissing_Stays400()
    {
        // Regression guard: a request referencing a nonexistent account is still a client
        // error about the request itself, not capacity or conflict.
        var mgr = NewSeatManager(maxSeats: 4, accountExists: false);
        await using var host = await StartHost(
            s => RegisterSeatServices(s, mgr), SeatEndpoints.Map);

        var response = await host.Client.PostAsync("/api/seats",
            JsonBody(new { accountName = "Nobody", width = 1920, height = 1080, fps = 60 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Provision_WhenAccountNameInvalid_Stays400()
    {
        // Validation still runs first and keeps its own status.
        var mgr = NewSeatManager(maxSeats: 0, accountExists: true);
        await using var host = await StartHost(
            s => RegisterSeatServices(s, mgr), SeatEndpoints.Map);

        var response = await host.Client.PostAsync("/api/seats",
            JsonBody(new { accountName = "../evil", width = 1920, height = 1080, fps = 60 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── POST /api/accounts ──────────────────────────────────────────

    [Fact]
    public async Task CreateAccount_WhenDuplicate_Returns409()
    {
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(m => m.CreateAccount(It.IsAny<string>(), It.IsAny<string?>()))
            .Throws(new ResourceConflictException("Account 'Dup' already exists."));
        await using var host = await StartHost(
            s => s.AddSingleton(accounts.Object), AccountEndpoints.Map);

        var response = await host.Client.PostAsync("/api/accounts",
            JsonBody(new { username = "Dup", password = "x" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_WhenBackendFails_Stays400()
    {
        // Unrelated server failures must not be reclassified as conflicts.
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(m => m.CreateAccount(It.IsAny<string>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("NetUserAdd failed for 'Dup': error 5, param 0"));
        await using var host = await StartHost(
            s => s.AddSingleton(accounts.Object), AccountEndpoints.Map);

        var response = await host.Client.PostAsync("/api/accounts",
            JsonBody(new { username = "Dup", password = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LinkAccount_WhenAlreadyLinked_Returns409()
    {
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(m => m.LinkExistingAccount(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new ResourceConflictException("Account 'Dup' is already linked."));
        await using var host = await StartHost(
            s => s.AddSingleton(accounts.Object), AccountEndpoints.Map);

        var response = await host.Client.PostAsync("/api/accounts/link",
            JsonBody(new { username = "Dup", password = "x" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Allocator seam ──────────────────────────────────────────────

    [Fact]
    public void PortAllocator_WhenBlocksExhausted_ThrowsCapacityExhausted()
    {
        // The second capacity signal behind POST /api/seats: every block handed out, the
        // next provision has nowhere to go. Still an InvalidOperationException so existing
        // non-HTTP catchers behave identically.
        var allocator = new PortAllocator();
        for (var i = 0; i < Constants.MaxSeats; i++)
            allocator.Allocate();

        var ex = Assert.Throws<CapacityExhaustedException>(() => allocator.Allocate());

        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.Contains("No port blocks available", ex.Message);
    }

    [Fact]
    public void SemanticExceptions_RemainInvalidOperationExceptions()
    {
        // Backward-compatibility contract: any existing `catch (InvalidOperationException)`
        // (worker autostart, tests, tooling) keeps catching these exactly as before.
        Assert.IsAssignableFrom<InvalidOperationException>(new CapacityExhaustedException("x"));
        Assert.IsAssignableFrom<InvalidOperationException>(new ResourceConflictException("x"));
    }

    // ── Harness ─────────────────────────────────────────────────────

    private static StringContent JsonBody(object body) => new(
        JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>
    /// Minimal API infers parameter sources at route-build time: a complex type that is
    /// registered in DI resolves as a service, otherwise as a body — and a group allows
    /// only one body parameter. So every service-typed parameter appearing anywhere in the
    /// mapped group must be registered, even when the exercised endpoint never touches it.
    /// </summary>
    private static void RegisterSeatServices(IServiceCollection s, SeatManager mgr)
    {
        s.AddSingleton(mgr);
        s.AddSingleton(_ => new SeatPresetStore(
            NullLogger<SeatPresetStore>.Instance,
            Path.Combine(Path.GetTempPath(), $"multiseat-test-presets-{Guid.NewGuid():N}.json")));
        s.AddSingleton(Mock.Of<ISessionLauncher>());
        s.AddSingleton(new SeatLifecycleGate());
    }

    private static SeatManager NewSeatManager(int maxSeats, bool accountExists)
    {
        var options = new MultiSeatOptions { MaxSeats = maxSeats };
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(m => m.AccountExists(It.IsAny<string>())).Returns(accountExists);

        // Heavy subsystems are never reached on the asserted rejection paths (capacity,
        // missing account, and duplicate checks all throw before session/port/lifecycle
        // work), so only logger/options/accounts are real here.
        return new SeatManager(
            NullLogger<SeatManager>.Instance,
            Options.Create(options),
            accounts.Object,
            null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, Array.Empty<MultiSeat.Service.Emulators.IEmulatorConfigSeeder>(),
            null!);
    }

    private static void SeedLiveSeat(SeatManager mgr, string accountName)
    {
        // Same reflection-seeding precedent as the Apollo identity tests: plant one live
        // registry entry so the duplicate-account branch is reachable without provisioning.
        var seats = (ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        seats[Guid.NewGuid()] = new SeatInfo
        {
            AccountName = accountName,
            Status = SeatStatus.Ready
        };
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
}
