using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Emulators;
using MultiSeat.Service.Input;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Storage;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// Regression tests for the controller-reset stale-seat race (CONFIRMED #2 from the
/// stale-seat audit): <see cref="SeatManager.ResetController"/> was ungated, so a
/// teardown that acquires <see cref="SeatLifecycleGate"/> and removes the seat could
/// interleave between <c>DestroyController</c> and <c>CreateController</c>/
/// <c>AssignController</c>, leaving a real ViGEm virtual controller registered for a
/// dead seat and a stuck physical-XInput → orphan-ViGEm routing entry in
/// <see cref="InputRouter"/>.
///
/// The fix wraps the entire destroy/create/assign transaction in the per-seat lifecycle
/// gate and re-validates seat lifecycle state (status != TearingDown) AFTER the gate is
/// acquired. A <c>ResetController</c> that captures a seat before teardown and only then
/// enters the gate must see the post-teardown state and abort before any side effect.
/// </summary>
public class ResetControllerStaleSeatTests
{
    private static readonly Guid SeatA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ── Pure predicate (mirrors ResolutionChangeStillValid for SetResolution) ──

    [Theory]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    [InlineData(SeatStatus.Configuring)]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Connecting)]
    [InlineData(SeatStatus.Error)]
    public void ControllerResetStillValid_AllowsValidStates(SeatStatus status)
    {
        // ResetController is intentionally allowed for any non-tearing-down state — the
        // only invalidation is TearingDown, because by then the seat is no longer a
        // member of _seats and creating a new controller would orphan it.
        Assert.True(SeatManager.ControllerResetStillValid(status),
            $"Status {status} should be a valid precondition for ResetController");
    }

    [Fact]
    public void ControllerResetStillValid_RejectsTearingDown()
    {
        // TearingDown is the H2/H3 "removed" signal: a concurrent teardown removed the
        // seat from _seats while the request waited for the gate, so the captured object
        // now reads TearingDown. CreateController for such a seat would orphan the
        // virtual controller — nothing in _seats would ever tear it down.
        Assert.False(SeatManager.ControllerResetStillValid(SeatStatus.TearingDown));
    }

    // ── Race scenario tests ─────────────────────────────────────────────

    [Fact]
    public async Task ResetController_AfterTearingDown_ThrowsWithoutCreating()
    {
        // Case B: teardown first, then ResetController. The fix must reject the call
        // because the seat is no longer a member of _seats (the post-gate GetSeat
        // re-check returns null). The API endpoint maps a null seat to a 404 before
        // reaching the method; this test exercises the post-gate "seat gone" path
        // directly by skipping the endpoint wrapper. The method throws
        // InvalidOperationException, matching the F3 SetResolutionAsync pattern and
        // preserving the original "Seat not found." semantic surface (the endpoint
        // catches it and returns 400).
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);
            await mgr.TeardownSeatAsync(SeatA, CancellationToken.None);
            Assert.Null(mgr.GetSeat(SeatA));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgr.ResetController(SeatA));
            Assert.Equal("Seat was removed while resetting controller.", ex.Message);
            Assert.Equal(0, recorder.CreatedCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetController_OnSeatInTearingDownState_DoesNotCreateController()
    {
        // Pins the post-gate validation invariant: a seat that has been moved to
        // TearingDown status (the H2 "removed" signal — removal happened, status was
        // flipped, teardown is in progress) must not be allowed to create a controller.
        // We exercise this by flipping the seat to TearingDown before the call, which
        // models the race interleaving where ResetController is in the gate-held
        // critical section while teardown is removing the seat.
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);
            mgr.GetSeat(SeatA)!.TransitionTo(SeatStatus.TearingDown, NullLogger.Instance);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgr.ResetController(SeatA));
            Assert.Equal("Seat was removed while resetting controller.", ex.Message);
            Assert.Equal(0, recorder.CreatedCount);
            Assert.Empty(mgr.InputRouter.GetAssignments());
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetController_OnLiveReadySeat_CreatesNewController()
    {
        // Case C: valid path. ResetController on a Ready seat must still work end-to-end
        // after the fix (the gate is acquired and released, a new controller is created).
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);
            await mgr.ResetController(SeatA);

            Assert.Equal(1, recorder.CreatedCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetController_ConcurrentTeardown_TransactionIsSerialized()
    {
        // Pins the gate-boundary invariant: the entire destroy/create/assign transaction
        // is serialized with teardown by the per-seat lifecycle gate. The teardown must
        // block until the gate is released, and after the gate-serialized teardown no
        // controller entry can leak.
        var (mgr, recorder) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            // Hold the gate as if ResetController is in the middle of its transaction.
            // This is equivalent to the production post-fix ResetController body holding
            // the gate between GetSeat and the destroy/create/assign steps.
            using var t1Lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);

            // Start teardown. It must block on the gate because T1 holds it.
            var t2 = Task.Run(() => mgr.TeardownSeatAsync(SeatA, CancellationToken.None));

            // Confirm T2 is genuinely blocked.
            await Task.Delay(100);
            Assert.False(t2.IsCompleted, "Teardown completed while T1 still holds the gate");
            Assert.True(mgr.GetSeat(SeatA) is not null,
                "Seat was removed while T1 still holds the gate");

            // Release the gate; T2 can now proceed.
            t1Lease.Dispose();
            await t2;

            // After the gate-serialized teardown, the seat is gone and no controller
            // was created (because we never went through ResetController's body in this
            // test — we only held the gate to prove teardown was blocked).
            Assert.Null(mgr.GetSeat(SeatA));
            Assert.Equal(0, recorder.CreatedCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (SeatManager mgr, RecordingControllerManager recorder) BuildSeatManager()
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-reset-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe",
            EnableViGEmController = true,
            AutoAssignControllers = false
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
        var displayManager = new VirtualDisplayManager(
            new TestLogger<VirtualDisplayManager>(),
            Options.Create(options),
            processInjector);

        var apolloManager = new ApolloManager(
            new TestLogger<ApolloManager>(),
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());

        var portAllocator = new PortAllocator();
        var firewall = new FirewallManager(new TestLogger<FirewallManager>(), Options.Create(options));
        var audioDeviceEnumerator = new AudioDeviceEnumerator(new TestLogger<AudioDeviceEnumerator>());
        var audioRouter = new AudioRouter(
            new TestLogger<AudioRouter>(),
            Options.Create(options),
            audioDeviceEnumerator,
            processInjector);
        var recorder = new RecordingControllerManager(new TestLogger<ControllerManager>());
        var inputRouter = new InputRouter(new TestLogger<InputRouter>(), recorder);
        var inputHookManager = new InputHookManager(
            new TestLogger<InputHookManager>(), Options.Create(options));
        var hidHide = new HidHideConfigurator(new TestLogger<HidHideConfigurator>(), Options.Create(options));
        var onConnect = new OnConnectAppLauncher(
            new TestLogger<OnConnectAppLauncher>(),
            Options.Create(options),
            apolloManager,
            processInjector);
        var gate = new SeatLifecycleGate();

        var mgr = new SeatManager(
            new NullLogger<SeatManager>(),
            Options.Create(options),
            Mock.Of<IAccountManager>(),
            sessionLauncher,
            processInjector,
            displayManager,
            apolloManager, // also satisfies IStreamingProvider (ApolloManager : IStreamingProvider)
            apolloManager,
            portAllocator,
            firewall,
            audioRouter,
            recorder,
            inputRouter,
            inputHookManager,
            hidHide,
            onConnect,
            Array.Empty<IEmulatorConfigSeeder>(),
            gate);

        return (mgr, recorder);
    }

    private static void RegisterReadySeat(SeatManager mgr, Guid seatId)
    {
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = SeatStatus.Ready,
            SessionId = 0,
            ViGEmControllerIndex = -1
        };
        // Use the existing internal TryRegisterSeat seam.
        var seatsDict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        var ownershipLock = typeof(SeatManager)
            .GetField("_accountOwnershipLock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(SeatManager.TryRegisterSeat(seatsDict, ownershipLock, seat),
            "Test setup: seat registration should succeed");
    }

    /// <summary>
    /// Records every CreateController call so tests can assert the post-gate validation
    /// prevents the call. Bypasses the real ViGEm path (no driver on test machines).
    /// </summary>
    private sealed class RecordingControllerManager : ControllerManager
    {
        private int _createdCount;

        public RecordingControllerManager(ILogger<ControllerManager> logger) : base(logger) { }

        public int CreatedCount => _createdCount;

        public override int CreateController(SeatInfo seat)
        {
            // Bypass the real ViGEm path (no driver on test machines) and just record
            // the call. Return a deterministic index so the test can verify
            // ViGEmControllerIndex would have been set.
            System.Threading.Interlocked.Increment(ref _createdCount);
            return 1;
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
}